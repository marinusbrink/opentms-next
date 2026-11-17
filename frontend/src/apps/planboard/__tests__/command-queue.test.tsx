import { act, renderHook, waitFor } from "@testing-library/react";
import { type ReactNode } from "react";
import { describe, expect, it, vi, beforeEach } from "vitest";

/* Design 75 risk analysis — Command queue (Medium risk):
 * - Queue ordering: commands process in FIFO order per trip
 * - Server error rollback: on non-409 error the optimistic state is reversed and a toast is shown
 * - Stale stamp retry: on 409 the queue refetches the stamp and retries once; on second 409 it rolls back
 * - Live update during in-flight: pendingValues is set while a command is in-flight */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

/* fetchPlanboardBars is called on 409 to get a fresh stamp. */
vi.mock("@/domains/planning-execution/planboard-scheduler", () => ({
  fetchPlanboardBars: vi.fn(async (ids: string[]) => ({
    bars: ids.map((id) => ({ id, concurrencyStamp: "fresh-stamp" })),
    names: {},
  })),
}));

/* apiClient.POST is the only outbound call the queue makes. */
const mockPost = vi.fn();
vi.mock("@/lib/api/client", () => ({ apiClient: { POST: (...args: unknown[]) => mockPost(...args) } }));

/* QueryClient needed by the provider. */
vi.mock("@tanstack/react-query", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@tanstack/react-query")>();
  return {
    ...actual,
    useQueryClient: () => ({
      invalidateQueries: vi.fn(),
      setQueryData: vi.fn(),
      getQueryData: vi.fn(() => undefined),
    }),
  };
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PlanboardOptimisticStateProvider, usePlanboardOptimisticState } from "@/apps/planboard/optimistic-state";
import { toast } from "sonner";
import { fetchPlanboardBars } from "@/domains/planning-execution/planboard-scheduler";

function Wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <PlanboardOptimisticStateProvider>{children}</PlanboardOptimisticStateProvider>
    </QueryClientProvider>
  );
}

const TRIP_ID = "trip-1";
const LEG_IDS = ["leg-a", "leg-b"];
const STAMP = "stamp-1";

function successResponse(newStamp: string) {
  return { data: { concurrencyStamp: newStamp }, error: undefined, response: { status: 200 } as Response };
}
function conflictResponse() {
  return { data: undefined, error: { code: "", message: "concurrency" }, response: { status: 409 } as Response };
}
function serverErrorResponse() {
  return { data: undefined, error: { code: "SomeError", message: "fail" }, response: { status: 422 } as Response };
}

beforeEach(() => {
  mockPost.mockReset();
  vi.mocked(fetchPlanboardBars).mockClear();
  vi.mocked(toast.error).mockClear();
});

describe("command queue — assign", () => {
  it("sets pendingValues synchronously; clears assignedLegs after success but keeps pendingValues until clearPendingValue", async () => {
    mockPost.mockResolvedValue(successResponse("stamp-2"));
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(true);

    /* Design 75 §A step 3: server confirms → assignedLegs cleared; pendingValues stays until SignalR arrives. */
    await waitFor(() => expect(result.current.state.assignedLegs.has(TRIP_ID.toLowerCase())).toBe(false));
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(true);

    /* Simulate the SignalR clearPendingValue call. */
    act(() => result.current.clearPendingValue(TRIP_ID));
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(false);
  });

  it("retries with fresh stamp on 409 and succeeds", async () => {
    mockPost
      .mockResolvedValueOnce(conflictResponse())
      .mockResolvedValueOnce(successResponse("stamp-after-retry"));
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));

    await waitFor(() => expect(result.current.state.assignedLegs.has(TRIP_ID.toLowerCase())).toBe(false));
    expect(fetchPlanboardBars).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("rolls back and toasts on second 409", async () => {
    mockPost.mockResolvedValue(conflictResponse());
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Planboard:Optimistic.StaleStamp"));
    expect(result.current.state.assignedLegs.has(TRIP_ID.toLowerCase())).toBe(false);
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(false);
  });

  it("rolls back and toasts on server error", async () => {
    mockPost.mockResolvedValue(serverErrorResponse());
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(result.current.state.assignedLegs.has(TRIP_ID.toLowerCase())).toBe(false);
  });

  it("processes two commands for the same trip in order", async () => {
    const calls: string[] = [];
    mockPost.mockImplementation(async (_path: string, opts: { body: { legIds: string[] } }) => {
      calls.push(opts.body.legIds[0]);
      return successResponse("s");
    });

    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });
    act(() => {
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-1"], concurrencyStamp: STAMP });
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-2"], concurrencyStamp: STAMP });
    });

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2));
    expect(calls).toEqual(["leg-1", "leg-2"]);
  });
});

describe("command queue — unassign", () => {
  it("sets unassignedLegs and pendingValues synchronously", async () => {
    mockPost.mockResolvedValue(successResponse("stamp-2"));
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueUnassign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));

    const key = TRIP_ID.toLowerCase();
    expect(result.current.state.unassignedLegs.get(key)?.has(LEG_IDS[0].toLowerCase())).toBe(true);
    expect(result.current.state.pendingValues.has(key)).toBe(true);

    await waitFor(() => expect(result.current.state.unassignedLegs.has(key)).toBe(false));
  });
});

describe("clearPendingValue", () => {
  it("removes the trip from pendingValues", async () => {
    mockPost.mockImplementation(async () => new Promise(() => { /* never resolves */ }));
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: LEG_IDS, concurrencyStamp: STAMP }));
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(true);

    act(() => result.current.clearPendingValue(TRIP_ID));
    expect(result.current.state.pendingValues.has(TRIP_ID.toLowerCase())).toBe(false);
  });
});
