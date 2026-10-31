import { renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { RecordUpdateHints, RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* Live updates of a Planboard pane (design 70, "Live updates"; design 67's channel): which notices concern a pane
 * (hintsConcern: known ids, removed, range, organisation units, resources, notices without hints), and the hook's
 * batching (250 ms, last notice per record wins, lower-case ids), resync on reconnect and on focus (throttled), and the
 * pending batch dropped on unmount. Risk class: Medium. */

const listeners = new Set<(m: RecordUpdateMessage) => void>();
const reconnectListeners = new Set<() => void>();
const unwatchType = vi.fn();
const watchType = vi.fn((_type: string) => unwatchType);
vi.mock("@/lib/realtime/record-updates", () => ({
  recordUpdatesClient: () => ({
    watchType,
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

import { hintsConcern, LIVE_UPDATE_BATCH_MS, PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";

const TRIP = PLANBOARD_RECORD_TYPES.trip;
const hints = (h: Partial<RecordUpdateHints> = {}): RecordUpdateHints => ({ from: "2026-11-03", till: "2026-11-04", organizationUnitId: "OU-1", resourceIds: ["V-1"], removed: false, ...h });
const msg = (targetId: string, h: RecordUpdateHints | null = hints(), version: number | null = 1): RecordUpdateMessage => ({ targetType: TRIP, targetId, version, status: null, hints: h });
const range = { from: "2026-11-02", till: "2026-11-08" };

describe("hintsConcern", () => {
  it("a record on screen always concerns the pane, even removed or outside the filter (case-insensitive)", () => {
    const known = new Set(["t1"]);
    expect(hintsConcern(msg("T1", hints({ removed: true })), { knownIds: known })).toBe(true);
    expect(hintsConcern(msg("t1", hints({ from: "2027-01-01", till: "2027-01-01" })), { range, knownIds: ["T1"] })).toBe(true);
  });

  it("a removed record off screen never does", () => {
    expect(hintsConcern(msg("t2", hints({ removed: true })), { knownIds: ["t1"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ removed: true })), {})).toBe(false);
  });

  it("a notice without hints does (refetching is the safe side)", () => {
    expect(hintsConcern(msg("t2", null), { range, organizationUnitIds: ["OU-9"], resourceIds: ["V-9"] })).toBe(true);
    expect(hintsConcern({ targetType: TRIP, targetId: "t2", version: 1, status: null }, { range })).toBe(true);
  });

  it("filters on the date range", () => {
    expect(hintsConcern(msg("t2"), { range })).toBe(true);
    expect(hintsConcern(msg("t2", hints({ from: "2026-11-09", till: "2026-11-10" })), { range })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ from: null, till: null })), { range })).toBe(true);
  });

  it("filters on organisation units (empty = all; a notice for no unit is skipped when units are chosen)", () => {
    expect(hintsConcern(msg("t2"), { organizationUnitIds: ["ou-1", "ou-2"] })).toBe(true);
    expect(hintsConcern(msg("t2"), { organizationUnitIds: ["OU-2"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ organizationUnitId: null })), { organizationUnitIds: ["OU-1"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ organizationUnitId: null })), { organizationUnitIds: [] })).toBe(true);
  });

  it("filters on the resources in the pane's rows", () => {
    expect(hintsConcern(msg("t2", hints({ resourceIds: ["V-2", "v-1"] })), { resourceIds: ["V-1"] })).toBe(true);
    expect(hintsConcern(msg("t2", hints({ resourceIds: ["V-2"] })), { resourceIds: ["V-1"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ resourceIds: [] })), { resourceIds: ["V-1"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ resourceIds: null })), { resourceIds: ["V-1"] })).toBe(false);
    expect(hintsConcern(msg("t2", hints({ resourceIds: null })), {})).toBe(true);
  });

  it("needs every criterion to pass", () => {
    const filter = { range, organizationUnitIds: ["OU-1"], resourceIds: ["V-1"] };
    expect(hintsConcern(msg("t2"), filter)).toBe(true);
    expect(hintsConcern(msg("t2", hints({ till: "2026-11-01", from: "2026-11-01" })), filter)).toBe(false);
  });
});

describe("usePlanboardLiveUpdates", () => {
  const push = (m: RecordUpdateMessage) => listeners.forEach((l) => l(m));

  beforeEach(() => {
    vi.useFakeTimers();
    listeners.clear();
    reconnectListeners.clear();
    watchType.mockClear();
    unwatchType.mockClear();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("hands over the matching ids of 250 ms as one batch, the last notice per record winning", () => {
    const onIds = vi.fn();
    renderHook(() => usePlanboardLiveUpdates({ targetType: TRIP, filter: { range }, onIds }));
    expect(watchType).toHaveBeenCalledWith(TRIP);

    push(msg("T1", hints(), 1));
    push(msg("t2", hints({ from: "2026-12-01", till: "2026-12-01" })));
    push(msg("t1", hints(), 2));
    push({ ...msg("t3"), targetType: PLANBOARD_RECORD_TYPES.leg });
    push(msg("T4"));
    expect(onIds).not.toHaveBeenCalled();

    vi.advanceTimersByTime(LIVE_UPDATE_BATCH_MS);
    expect(onIds).toHaveBeenCalledTimes(1);
    const [ids, messages] = onIds.mock.calls[0] as [string[], RecordUpdateMessage[]];
    expect(ids).toEqual(["t1", "t4"]);
    expect(messages.find((m) => m.targetId.toLowerCase() === "t1")?.version).toBe(2);

    push(msg("t5"));
    vi.advanceTimersByTime(LIVE_UPDATE_BATCH_MS);
    expect(onIds).toHaveBeenCalledTimes(2);
    expect(onIds.mock.calls[1][0]).toEqual(["t5"]);
  });

  it("uses the pane's own predicate instead of the filter", () => {
    const onIds = vi.fn();
    renderHook(() => usePlanboardLiveUpdates({ targetType: TRIP, filter: { range }, predicate: (m) => m.targetId === "keep", onIds }));
    push(msg("keep", hints({ removed: true })));
    push(msg("drop"));
    vi.advanceTimersByTime(LIVE_UPDATE_BATCH_MS);
    expect(onIds.mock.calls[0][0]).toEqual(["keep"]);
  });

  it("drops a pending batch on unmount and releases the watch", () => {
    const onIds = vi.fn();
    const { unmount } = renderHook(() => usePlanboardLiveUpdates({ targetType: TRIP, onIds }));
    push(msg("t1"));
    unmount();
    vi.advanceTimersByTime(LIVE_UPDATE_BATCH_MS * 2);
    expect(onIds).not.toHaveBeenCalled();
    expect(unwatchType).toHaveBeenCalledTimes(1);
  });

  it("watches nothing while disabled", () => {
    const onIds = vi.fn();
    renderHook(() => usePlanboardLiveUpdates({ targetType: TRIP, onIds, enabled: false }));
    expect(watchType).not.toHaveBeenCalled();
  });

  it("resyncs after a reconnect and when the window regains focus, at most once a second", () => {
    const onResync = vi.fn();
    renderHook(() => usePlanboardLiveUpdates({ targetType: TRIP, onIds: vi.fn(), onResync }));

    reconnectListeners.forEach((l) => l());
    expect(onResync).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(5000);
    window.dispatchEvent(new Event("focus"));
    document.dispatchEvent(new Event("visibilitychange"));
    expect(onResync).toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(1500);
    window.dispatchEvent(new Event("focus"));
    expect(onResync).toHaveBeenCalledTimes(3);
  });
});
