import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* useRecordTypeUpdates (design 67): watches the type while mounted and enabled, calls onUpdate only for notices of its
 * own type, calls onReconnect after a reconnect, keeps one subscription while the callbacks change, and releases the
 * watch on unmount. Risk class: Critical (Platform, design 67) — the frontend part is unit level by design. */

const listeners = new Set<(m: RecordUpdateMessage) => void>();
const reconnectListeners = new Set<() => void>();
const unwatchType = vi.fn();
const watchType = vi.fn((_type: string) => unwatchType);
vi.mock("@/lib/realtime/record-updates", () => ({
  recordUpdatesClient: () => ({
    watchType,
    watch: vi.fn(() => () => undefined),
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

import { useRecordTypeUpdates } from "@/lib/realtime/use-record-updates";

const push = (m: RecordUpdateMessage) => listeners.forEach((l) => l(m));
const TRIP = "PlanningExecution.TransportTrip";
const notice = (targetType: string, targetId: string): RecordUpdateMessage => ({ targetType, targetId, version: null, status: null, hints: null });

beforeEach(() => {
  listeners.clear();
  reconnectListeners.clear();
  watchType.mockClear();
  unwatchType.mockClear();
});

describe("useRecordTypeUpdates", () => {
  it("watches the type, passes on only notices of that type, and releases everything on unmount", () => {
    const onUpdate = vi.fn();
    const { unmount } = renderHook(() => useRecordTypeUpdates(TRIP, onUpdate));
    expect(watchType).toHaveBeenCalledWith(TRIP);

    push(notice(TRIP, "t1"));
    push(notice("PlanningExecution.Leg", "l1"));
    push(notice("Orders.TransportOrder", "o1"));
    expect(onUpdate.mock.calls.map(([m]) => m.targetId)).toEqual(["t1"]);

    unmount();
    expect(unwatchType).toHaveBeenCalledTimes(1);
    expect(listeners.size).toBe(0);
    expect(reconnectListeners.size).toBe(0);
  });

  it("watches nothing while disabled or without a type", () => {
    const onUpdate = vi.fn();
    renderHook(() => useRecordTypeUpdates(TRIP, onUpdate, false));
    renderHook(() => useRecordTypeUpdates("", onUpdate));
    expect(watchType).not.toHaveBeenCalled();
    push(notice(TRIP, "t1"));
    expect(onUpdate).not.toHaveBeenCalled();
  });

  it("starts watching when enabled and stops when disabled again", () => {
    const onUpdate = vi.fn();
    const { rerender } = renderHook(({ enabled }) => useRecordTypeUpdates(TRIP, onUpdate, enabled), { initialProps: { enabled: false } });
    expect(watchType).not.toHaveBeenCalled();
    rerender({ enabled: true });
    expect(watchType).toHaveBeenCalledTimes(1);
    rerender({ enabled: false });
    expect(unwatchType).toHaveBeenCalledTimes(1);
  });

  it("calls the latest callbacks without re-subscribing when they change", () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(({ cb }) => useRecordTypeUpdates(TRIP, cb), { initialProps: { cb: first } });
    rerender({ cb: second });
    expect(watchType).toHaveBeenCalledTimes(1);
    push(notice(TRIP, "t1"));
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });

  it("calls onReconnect after a reconnect so the pane refetches", () => {
    const onReconnect = vi.fn();
    renderHook(() => useRecordTypeUpdates(TRIP, vi.fn(), true, onReconnect));
    reconnectListeners.forEach((l) => l());
    expect(onReconnect).toHaveBeenCalledTimes(1);
  });

  it("re-watches when the type changes", () => {
    const { rerender } = renderHook(({ type }) => useRecordTypeUpdates(type, vi.fn()), { initialProps: { type: TRIP } });
    rerender({ type: "PlanningExecution.Leg" });
    expect(unwatchType).toHaveBeenCalledTimes(1);
    expect(watchType.mock.calls.map(([t]) => t)).toEqual([TRIP, "PlanningExecution.Leg"]);
  });
});
