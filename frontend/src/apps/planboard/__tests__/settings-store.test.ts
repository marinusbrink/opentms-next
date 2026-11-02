import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { parseStoredLayout, PANE_COMPONENT, toStoredLayout } from "@/apps/planboard/layout";
import {
  createDebouncedSaver,
  createLayoutRecordStore,
  createMemorySettingsStore,
  createPaneSettingsCache,
  isValidSettingsKey,
  paneSettingsKey,
  parseStoredBoardFilters,
  PLANBOARD_LAYOUT_KEY,
  SETTINGS_SAVE_DEBOUNCE_MS,
  type PlanboardSettingsStore,
} from "@/apps/planboard/settings-store";

/* Per-user Planboard settings (design 70, "Persistence"): the key rule of the grid-settings endpoint, the 1 s debounced
 * saver (last value wins, flush, cancel, failures reported once), the pane settings cache, the layout record that carries
 * the board filters, and the fallback of a corrupt or outdated stored layout. The server store over a fake fetch is in
 * settings-store-server.test.ts. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

function spyStore(): PlanboardSettingsStore & { saves: [string, unknown][]; loads: string[] } {
  const inner = createMemorySettingsStore();
  const saves: [string, unknown][] = [];
  const loads: string[] = [];
  return {
    saves,
    loads,
    load: (key) => {
      loads.push(key);
      return inner.load(key);
    },
    save: (key, value) => {
      saves.push([key, JSON.parse(JSON.stringify(value))]);
      return inner.save(key, value);
    },
    remove: (key) => inner.remove(key),
  };
}

describe("settings keys", () => {
  it("follows the grid-settings id rule", () => {
    expect(isValidSettingsKey(PLANBOARD_LAYOUT_KEY)).toBe(true);
    expect(isValidSettingsKey(paneSettingsKey("scheduler-ab12cd34"))).toBe(true);
    expect(isValidSettingsKey("planboard.pane.a b")).toBe(false);
    expect(isValidSettingsKey("planboard/pane")).toBe(false);
    expect(isValidSettingsKey("")).toBe(false);
    expect(isValidSettingsKey("x".repeat(101))).toBe(false);
    expect(isValidSettingsKey("x".repeat(100))).toBe(true);
  });
});

describe("createMemorySettingsStore", () => {
  it("round-trips through JSON (no shared references) and reads absent keys as null", async () => {
    const store = createMemorySettingsStore();
    const value = { a: [1, 2] };
    await store.save("k", value);
    value.a.push(3);
    expect(await store.load("k")).toEqual({ a: [1, 2] });
    expect(await store.load("other")).toBeNull();
    await store.remove("k");
    expect(await store.load("k")).toBeNull();
  });
});

describe("createDebouncedSaver", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("saves once per key after 1 s with the last value", async () => {
    const store = spyStore();
    const saver = createDebouncedSaver(store, vi.fn());
    saver.schedule("a", { v: 1 });
    vi.advanceTimersByTime(SETTINGS_SAVE_DEBOUNCE_MS - 1);
    saver.schedule("a", { v: 2 });
    saver.schedule("b", { v: 9 });
    vi.advanceTimersByTime(SETTINGS_SAVE_DEBOUNCE_MS - 1);
    expect(store.saves).toEqual([]);
    await vi.advanceTimersByTimeAsync(1);
    expect(store.saves).toEqual([
      ["a", { v: 2 }],
      ["b", { v: 9 }],
    ]);
  });

  it("flush writes what is pending at once, cancel and dispose drop it", async () => {
    const store = spyStore();
    const saver = createDebouncedSaver(store, vi.fn());
    saver.schedule("a", 1);
    saver.schedule("b", 2);
    saver.cancel("b");
    await saver.flush();
    expect(store.saves).toEqual([["a", 1]]);

    saver.schedule("c", 3);
    saver.dispose();
    await vi.advanceTimersByTimeAsync(SETTINGS_SAVE_DEBOUNCE_MS * 2);
    expect(store.saves).toEqual([["a", 1]]);
  });

  it("reports a failed save once, for a rejection and for a false result (UI-FB-005: never silent)", async () => {
    const onFailed = vi.fn();
    const error = new Error("413");
    const store: PlanboardSettingsStore = {
      load: async () => null,
      save: vi.fn().mockRejectedValueOnce(error).mockResolvedValueOnce(false),
      remove: async () => true,
    };
    const saver = createDebouncedSaver(store, onFailed);
    saver.schedule("a", 1);
    await saver.flush();
    saver.schedule("b", 2);
    await saver.flush();
    expect(onFailed.mock.calls).toEqual([
      ["a", error],
      ["b", null],
    ]);
  });
});

describe("createPaneSettingsCache", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("loads a key once, merges a patch made while loading over the stored value, and saves the merged record", async () => {
    const store = spyStore();
    await store.save("planboard.pane.p1", { mode: "driver", zoom: "day" });
    store.saves.length = 0;
    const saver = createDebouncedSaver(store, vi.fn());
    const cache = createPaneSettingsCache(store, saver);
    const listener = vi.fn();
    cache.subscribe("planboard.pane.p1", listener);

    expect(cache.get("planboard.pane.p1")).toEqual({ loaded: false, value: {} });
    const loading = cache.ensureLoaded("planboard.pane.p1");
    expect(cache.ensureLoaded("planboard.pane.p1")).toBe(loading);
    cache.patch("planboard.pane.p1", { zoom: "hour" });
    await loading;

    expect(store.loads).toEqual(["planboard.pane.p1"]);
    expect(cache.get("planboard.pane.p1")).toEqual({ loaded: true, value: { mode: "driver", zoom: "hour" } });
    expect(listener).toHaveBeenCalled();

    cache.patch("planboard.pane.p1", { search: "x" });
    await saver.flush();
    expect(store.saves.at(-1)).toEqual(["planboard.pane.p1", { mode: "driver", zoom: "hour", search: "x" }]);
  });

  it("an unreadable or non-object stored value counts as loaded with what is there", async () => {
    const failing: PlanboardSettingsStore = { load: async () => Promise.reject(new Error("500")), save: async () => true, remove: async () => true };
    const cache = createPaneSettingsCache(failing, createDebouncedSaver(failing, vi.fn()));
    await cache.ensureLoaded("k");
    expect(cache.get("k")).toEqual({ loaded: true, value: {} });

    const arrayStore = createMemorySettingsStore();
    await arrayStore.save("k", [1, 2]);
    const cache2 = createPaneSettingsCache(arrayStore, createDebouncedSaver(arrayStore, vi.fn()));
    await cache2.ensureLoaded("k");
    expect(cache2.get("k")).toEqual({ loaded: true, value: {} });
  });
});

describe("parseStoredBoardFilters", () => {
  it("reads a valid board part, dedupes units and drops non-string ones", () => {
    expect(parseStoredBoardFilters({ board: { range: { from: "2026-11-02", till: "2026-11-08", extra: 1 }, organizationUnitIds: ["a", "a", 3, "", "b"] } })).toEqual({
      range: { from: "2026-11-02", till: "2026-11-08" },
      organizationUnitIds: ["a", "b"],
    });
    expect(parseStoredBoardFilters({ board: { range: { from: "2026-11-02", till: "2026-11-08" } } })?.organizationUnitIds).toEqual([]);
  });

  it("is null without a board, with an invalid range, or a range over 92 days", () => {
    expect(parseStoredBoardFilters(null)).toBeNull();
    expect(parseStoredBoardFilters([])).toBeNull();
    expect(parseStoredBoardFilters({ version: 1 })).toBeNull();
    expect(parseStoredBoardFilters({ board: { range: { from: "2026-11-08", till: "2026-11-02" } } })).toBeNull();
    expect(parseStoredBoardFilters({ board: { range: { from: "2026-01-01", till: "2026-12-31" } } })).toBeNull();
  });
});

describe("createLayoutRecordStore", () => {
  const board = { range: { from: "2026-11-02", till: "2026-11-08" }, organizationUnitIds: ["ou1"] };

  it("adds the board filters to every layout save and leaves other keys alone", async () => {
    const inner = spyStore();
    const record = createLayoutRecordStore(inner);
    record.setBoard(board);
    await record.store.save(PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 1 }, board: { stale: true } });
    await record.store.save("planboard.pane.p1", { mode: "tractor" });
    expect(inner.saves).toEqual([
      [PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 1 }, board }],
      ["planboard.pane.p1", { mode: "tractor" }],
    ]);
  });

  it("shares the first read of the record between the board filters and the workspace (one request)", async () => {
    const inner = spyStore();
    await inner.save(PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 1 }, board });
    const record = createLayoutRecordStore(inner);
    const [stored, layout] = await Promise.all([record.loadBoard(), record.store.load(PLANBOARD_LAYOUT_KEY)]);
    expect(stored).toEqual(board);
    expect(layout).toMatchObject({ dockview: { x: 1 } });
    expect(inner.loads).toEqual([PLANBOARD_LAYOUT_KEY]);
    expect(record.currentLayout()).toEqual({ version: 1, dockview: { x: 1 } });
  });

  it("a board change before any layout is known cannot save yet (it would reset the layout); after the first layout save it can", async () => {
    const inner = spyStore();
    const record = createLayoutRecordStore(inner);
    await record.loadBoard();
    expect(record.setBoard(board)).toBe(false);
    expect(record.currentLayout()).toBeNull();

    await record.store.save(PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 2 } });
    expect(inner.saves.at(-1)).toEqual([PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 2 }, board }]);
    const next = { ...board, organizationUnitIds: ["ou2"] };
    expect(record.setBoard(next)).toBe(true);
    await record.store.save(PLANBOARD_LAYOUT_KEY, record.currentLayout());
    expect(inner.saves.at(-1)).toEqual([PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { x: 2 }, board: next }]);
  });

  it("a read after a save returns the saved record, not the one of the start; remove forgets it", async () => {
    const inner = spyStore();
    await inner.save(PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { old: true } });
    const record = createLayoutRecordStore(inner);
    await record.store.load(PLANBOARD_LAYOUT_KEY);
    record.setBoard(board);
    await record.store.save(PLANBOARD_LAYOUT_KEY, { version: 1, dockview: { new: true } });
    expect(await record.store.load(PLANBOARD_LAYOUT_KEY)).toEqual({ version: 1, dockview: { new: true }, board });

    await record.store.remove(PLANBOARD_LAYOUT_KEY);
    expect(record.currentLayout()).toBeNull();
    expect(await record.store.load(PLANBOARD_LAYOUT_KEY)).toBeNull();
  });
});

describe("parseStoredLayout (corrupt or outdated layouts fall back to the default)", () => {
  const panel = (id: string, type: string, extra: Record<string, unknown> = {}) => ({ id, contentComponent: PANE_COMPONENT, params: { type, instanceId: id }, ...extra });
  const dockview = (panels: Record<string, unknown>) => ({ grid: { root: { type: "branch", data: [] }, width: 100, height: 100, orientation: "HORIZONTAL" }, panels });
  const valid = dockview({ trips: panel("trips", "trips"), "scheduler-a": panel("scheduler-a", "scheduler"), "scheduler-b": panel("scheduler-b", "scheduler") });

  it("accepts a stored layout of this version (two Schedulers are fine)", () => {
    expect(parseStoredLayout(toStoredLayout(valid as never))).toBe(valid);
  });

  it.each([
    ["nothing", null],
    ["a string", "layout"],
    ["another version", { version: 2, dockview: valid }],
    ["no version", { dockview: valid }],
    ["no dockview", { version: 1 }],
    ["no grid root", { version: 1, dockview: { ...valid, grid: { width: 1 } } }],
    ["panels as an array", { version: 1, dockview: { ...valid, panels: [] } }],
    ["no panels", { version: 1, dockview: dockview({}) }],
    ["a panel id that differs from its key", { version: 1, dockview: dockview({ trips: panel("other", "trips") }) }],
    ["a foreign component", { version: 1, dockview: dockview({ trips: { ...panel("trips", "trips"), contentComponent: "x" } }) }],
    ["an unknown pane type", { version: 1, dockview: dockview({ gantt: panel("gantt", "gantt") }) }],
    ["an instance id that differs from the panel id", { version: 1, dockview: dockview({ trips: { ...panel("trips", "trips"), params: { type: "trips", instanceId: "x" } } }) }],
    ["a single-instance pane twice", { version: 1, dockview: dockview({ map: panel("map", "map"), map2: { ...panel("map2", "map") } }) }],
    ["a null panel", { version: 1, dockview: dockview({ trips: null }) }],
  ])("rejects %s", (_label, value) => {
    expect(parseStoredLayout(value)).toBeNull();
  });
});
