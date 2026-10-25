import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* useRecordUpdates (design 49): watches while mounted, refetches the record's queries and the states on a push for a
 * watched record, ignores other records and types, refetches on reconnect, and unwatches on unmount. Risk: Medium. */

const listeners = new Set<(m: RecordUpdateMessage) => void>();
const reconnectListeners = new Set<() => void>();
const unwatch = vi.fn();
const watch = vi.fn((_type: string, _ids: readonly string[]) => unwatch);
vi.mock("@/lib/realtime/record-updates", () => ({
  recordUpdatesClient: () => ({
    watch,
    subscribe: (l: (m: RecordUpdateMessage) => void) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    onReconnected: (l: () => void) => {
      reconnectListeners.add(l);
      return () => reconnectListeners.delete(l);
    },
  }),
}));

import { useRecordUpdates } from "@/lib/realtime/use-record-updates";

let queryClient: QueryClient;
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
const push = (m: RecordUpdateMessage) => listeners.forEach((l) => l(m));

beforeEach(() => {
  queryClient = new QueryClient();
  listeners.clear();
  reconnectListeners.clear();
  watch.mockClear();
  unwatch.mockClear();
});

describe("useRecordUpdates", () => {
  it("watches the ids, refetches the watched record's queries on a push and unwatches on unmount", () => {
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const onUpdate = vi.fn();
    const { unmount } = renderHook(() => useRecordUpdates("PlanningExecution.TransportTrip", ["T1", null, "t2"], { onUpdate }), { wrapper });

    expect(watch).toHaveBeenCalledWith("PlanningExecution.TransportTrip", ["t1", "t2"]);

    push({ targetType: "PlanningExecution.TransportTrip", targetId: "T1", version: 2, status: 2 });
    expect(onUpdate).toHaveBeenCalledTimes(1);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["derivedValues", "PlanningExecution.TransportTrip"] });

    invalidate.mockClear();
    push({ targetType: "PlanningExecution.TransportTrip", targetId: "other", version: 1, status: 2 });
    push({ targetType: "Orders.TransportOrder", targetId: "t1", version: 1, status: 2 });
    expect(invalidate).not.toHaveBeenCalled();
    expect(onUpdate).toHaveBeenCalledTimes(1);

    reconnectListeners.forEach((l) => l());
    expect(invalidate).toHaveBeenCalled();

    unmount();
    expect(unwatch).toHaveBeenCalledTimes(1);
    expect(listeners.size).toBe(0);
  });

  it("refetches exactly the query keys the screen names", () => {
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    renderHook(() => useRecordUpdates("Orders.TransportOrder", ["o1"], { queryKeysFor: (id) => [["orders", "detail", id]] }), { wrapper });
    push({ targetType: "Orders.TransportOrder", targetId: "o1", version: 5, status: 2 });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["orders", "detail", "o1"] });
  });

  it("watches nothing without ids", () => {
    renderHook(() => useRecordUpdates("Orders.TransportOrder", []), { wrapper });
    expect(watch).not.toHaveBeenCalled();
  });
});
