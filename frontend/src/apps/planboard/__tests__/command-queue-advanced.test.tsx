import { act, renderHook, waitFor } from "@testing-library/react";
import { type ReactNode } from "react";
import { describe, expect, it, vi, beforeEach } from "vitest";

/* Design 75 risk analysis — Command queue advanced (High risk):
 * - 3-command stamp chain: each command uses the stamp returned by the previous one
 * - Partial queue rollback (cmd 2 of 3 fails): cmd 1 state is preserved; cmd 2+3 are rolled back
 * - processingTripIds: reactive set reflects which trips have in-flight commands */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("@/domains/planning-execution/planboard-scheduler", () => ({
  fetchPlanboardBars: vi.fn(async (ids: string[]) => ({
    bars: ids.map((id) => ({ id, concurrencyStamp: "fresh-stamp" })),
    names: {},
  })),
}));

const mockPost = vi.fn();
vi.mock("@/lib/api/client", () => ({ apiClient: { POST: (...args: unknown[]) => mockPost(...args) } }));

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

const TRIP_ID = "trip-adv";
const STAMP = "stamp-1";

function successResponse(newStamp: string) {
  return { data: { concurrencyStamp: newStamp }, error: undefined, response: { status: 200 } as Response };
}
function serverErrorResponse() {
  return { data: undefined, error: { code: "SomeError", message: "fail" }, response: { status: 422 } as Response };
}

beforeEach(() => {
  mockPost.mockReset();
  vi.mocked(fetchPlanboardBars).mockClear();
  vi.mocked(toast.error).mockClear();
});

describe("command queue — 3-command stamp chain (design 75 §B)", () => {
  it("each command uses the stamp returned by the previous command", async () => {
    const stampsSent: string[] = [];
    let call = 0;
    mockPost.mockImplementation(async (_path: string, opts: { body: { concurrencyStamp: string } }) => {
      stampsSent.push(opts.body.concurrencyStamp);
      call++;
      return successResponse(`stamp-${call + 1}`);
    });

    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => {
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-1"], concurrencyStamp: STAMP });
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-2"], concurrencyStamp: STAMP });
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-3"], concurrencyStamp: STAMP });
    });

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(3));

    // cmd1 must use the initial stamp; cmd2 must use cmd1's response stamp; cmd3 must use cmd2's.
    expect(stampsSent[0]).toBe("stamp-1");
    expect(stampsSent[1]).toBe("stamp-2");
    expect(stampsSent[2]).toBe("stamp-3");
  });
});

describe("command queue — partial queue rollback: cmd 2 of 3 fails (design 75 §B)", () => {
  it("cmd 1 confirmation is preserved; cmd 2 and cmd 3 are rolled back with one toast", async () => {
    mockPost
      .mockResolvedValueOnce(successResponse("stamp-2")) // cmd1 succeeds
      .mockResolvedValueOnce(serverErrorResponse());     // cmd2 fails → rollback queue

    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => {
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-1"], concurrencyStamp: STAMP });
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-2"], concurrencyStamp: STAMP });
      result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-3"], concurrencyStamp: STAMP });
    });

    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));

    // cmd3 must never have been sent — rollback drains the queue on cmd2 failure.
    expect(mockPost).toHaveBeenCalledTimes(2);

    const key = TRIP_ID.toLowerCase();
    // All optimistic state must be cleared (cmd1 confirmed by server; cmd2+3 rolled back).
    expect(result.current.state.assignedLegs.has(key)).toBe(false);
    expect(result.current.state.pendingValues.has(key)).toBe(false);
  });
});

describe("processingTripIds (design 75 §A)", () => {
  it("contains the trip id while its command queue is active", async () => {
    let resolvePost!: (v: ReturnType<typeof successResponse>) => void;
    mockPost.mockImplementation(() => new Promise<ReturnType<typeof successResponse>>((res) => { resolvePost = res; }));

    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => result.current.enqueueAssign({ tripId: TRIP_ID, legIds: ["leg-a"], concurrencyStamp: STAMP }));

    // The queue started processing — the trip id must be in processingTripIds.
    await waitFor(() => expect(result.current.processingTripIds.has(TRIP_ID.toLowerCase())).toBe(true));

    // A different trip is unaffected.
    expect(result.current.processingTripIds.has("trip-other")).toBe(false);

    // Complete the in-flight call.
    act(() => resolvePost(successResponse("stamp-2")));
    await waitFor(() => expect(result.current.processingTripIds.has(TRIP_ID.toLowerCase())).toBe(false));
  });
});
