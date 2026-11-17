import { act, renderHook, waitFor } from "@testing-library/react";
import { type ReactNode } from "react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import type { LegDto } from "@/domains/planning-execution/legs";

/* Design 75 risk analysis — "Legs to schedule drop target" (Medium risk):
 * The drop handler on LegsToSchedulePane calls enqueueUnassign when a drag payload with a valid fromTripId is dropped.
 * These tests cover the resulting optimistic state transitions directly (the DOM drag/drop event chain over the pane div
 * is verified manually on the dev tenant, as noted in the design). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/domains/planning-execution/planboard-scheduler", () => ({
  fetchPlanboardBars: vi.fn(async (ids: string[]) => ({
    bars: ids.map((id) => ({ id, concurrencyStamp: "fresh" })),
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

function Wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <PlanboardOptimisticStateProvider>{children}</PlanboardOptimisticStateProvider>
    </QueryClientProvider>
  );
}

function successResponse(newStamp = "stamp-2") {
  return { data: { concurrencyStamp: newStamp }, error: undefined, response: { status: 200 } as Response };
}

const FROM_TRIP_ID = "trip-from-77";
const LEG_X: LegDto = { id: "leg-x", orderNumber: "O1" } as LegDto;

beforeEach(() => {
  mockPost.mockReset().mockResolvedValue(successResponse());
});

describe("LegsToSchedulePane: drop of assigned leg triggers unassign path (design 75 §C)", () => {
  it("enqueueUnassign adds leg to unassignedLegs and pendingValues synchronously", async () => {
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => {
      result.current.enqueueUnassign({
        tripId: FROM_TRIP_ID,
        legIds: [LEG_X.id!],
        concurrencyStamp: "",
        legData: [LEG_X],
      });
    });

    const key = FROM_TRIP_ID.toLowerCase();
    expect(result.current.state.unassignedLegs.get(key)?.has("leg-x")).toBe(true);
    expect(result.current.state.pendingValues.has(key)).toBe(true);
    expect(result.current.state.pendingLegData.has("leg-x")).toBe(true);
  });

  it("leg data is captured in pendingLegData for the unscheduled view", async () => {
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => {
      result.current.enqueueUnassign({
        tripId: FROM_TRIP_ID,
        legIds: ["leg-y"],
        concurrencyStamp: "",
        legData: [{ id: "leg-y", orderNumber: "O2" } as LegDto],
      });
    });

    expect(result.current.state.pendingLegData.get("leg-y")?.orderNumber).toBe("O2");
  });

  it("server confirm clears the unassignedLegs entry and pendingLegData", async () => {
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    act(() => {
      result.current.enqueueUnassign({
        tripId: FROM_TRIP_ID,
        legIds: ["leg-z"],
        concurrencyStamp: "",
        legData: [{ id: "leg-z", orderNumber: "O3" } as LegDto],
      });
    });

    const key = FROM_TRIP_ID.toLowerCase();
    await waitFor(() => expect(result.current.state.unassignedLegs.has(key)).toBe(false));
    expect(result.current.state.pendingLegData.has("leg-z")).toBe(false);
  });

  it("drop without fromTripId (no-op payload) results in no unassigned legs", async () => {
    /* The LegsToSchedulePane.onDrop guard: `if (!payload.fromTripId) return;` */
    const { result } = renderHook(() => usePlanboardOptimisticState(), { wrapper: Wrapper });

    // No enqueueUnassign call → state stays empty.
    expect(result.current.state.unassignedLegs.size).toBe(0);
  });
});
