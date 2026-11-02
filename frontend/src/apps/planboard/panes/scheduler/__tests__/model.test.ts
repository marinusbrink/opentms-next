import { describe, expect, it } from "vitest";

import {
  barEventId,
  barInterval,
  barTone,
  DEFAULT_BAR_MS,
  DEFAULT_SCHEDULER_PAGE_SIZE,
  detectConflicts,
  hasConflicts,
  isReadOnlyStatus,
  mergeBars,
  movedBar,
  moveConflicts,
  pageResourceIds,
  pageTripIds,
  parseBarEventId,
  patchBar,
  planMove,
  presetRange,
  sanitizeSchedulerSettings,
  toSchedulerView,
  TRIP_STATUS,
  type BarData,
} from "@/apps/planboard/panes/scheduler/model";
import type { SchedulerBackgroundEvent, SchedulerEvent } from "@/components/ui/resource-scheduler-model";
import type { PlanboardBarDto, PlanboardBlockDto, PlanboardSchedulerDto } from "@/domains/planning-execution/planboard-scheduler";

/* The pure part of the Scheduler pane (design 70 "Scheduler"; design 68 D10 warns, never blocks): settings repair, the
 * mapping of a read per mode onto rows, bars and blocks, conflict detection (overlap on a row, overlap with a block,
 * cancelled bars excluded), the move planner, the optimistic patch and the merge of refetched bars. Risk class: Medium
 * (UI over the tested Scheduler endpoints; a wrong mapping shows a trip on the wrong vehicle). */

const at = (h: number, m = 0) => new Date(Date.UTC(2026, 10, 2, h, m)).toISOString();

const bar = (id: string, from: number, till: number, extra: Partial<PlanboardBarDto> = {}): PlanboardBarDto =>
  ({ id, number: `R-${id}`, status: TRIP_STATUS.Planned, plannedDeparture: at(from), plannedArrival: at(till), vehicleIds: [], driverIds: [], ...extra }) as PlanboardBarDto;

const dto = (extra: Partial<PlanboardSchedulerDto> = {}): PlanboardSchedulerDto =>
  ({
    rows: [
      { id: "V1", name: "Truck 1", code: "T1" },
      { id: "V2", name: "", code: "T2" },
      { id: "D1", name: "Driver 1" },
    ],
    bars: [],
    blocks: [],
    names: {},
    ...extra,
  }) as PlanboardSchedulerDto;

const title = (b: PlanboardBlockDto) => (b.unavailabilityId ? "Unavailable" : "Off");

describe("settings", () => {
  it("repairs stored settings field by field and drops what it does not know", () => {
    expect(sanitizeSchedulerSettings({ mode: "driver", zoom: "day", search: "x".repeat(200), barFields: ["drivers", "bogus", "number"], pageSize: 100 })).toEqual({
      mode: "driver",
      zoom: "day",
      search: "x".repeat(128),
      barFields: ["number", "drivers"],
      pageSize: 100,
    });
    expect(sanitizeSchedulerSettings({ mode: "bus", zoom: "week", search: 3, barFields: "number", pageSize: 51 })).toEqual({});
    expect(DEFAULT_SCHEDULER_PAGE_SIZE).toBe(50);
  });

  it("builds the preset ranges, capped at the 14-day Scheduler window", () => {
    expect(presetRange("2026-11-02", 1)).toEqual({ from: "2026-11-02", till: "2026-11-02" });
    expect(presetRange("2026-11-02", 7)).toEqual({ from: "2026-11-02", till: "2026-11-08" });
    expect(presetRange("2026-11-02", 30)).toEqual({ from: "2026-11-02", till: "2026-11-15" });
    expect(presetRange("2026-11-02", 0)).toEqual({ from: "2026-11-02", till: "2026-11-02" });
  });
});

describe("bars", () => {
  it("maps each status onto the trip badge's tone; completed and cancelled are read-only", () => {
    expect([1, 2, 3, 4, 99, null].map((s) => barTone(s))).toEqual(["neutral", "info", "success", "danger", "neutral", "neutral"]);
    expect([1, 2, 3, 4].map(isReadOnlyStatus)).toEqual([false, false, true, true]);
  });

  it("draws a bar without (or with an earlier) arrival one hour long, and none without departure", () => {
    const start = new Date(at(8)).getTime();
    expect(barInterval(bar("a", 8, 10))).toEqual({ start, end: new Date(at(10)).getTime() });
    expect(barInterval(bar("a", 8, 8, { plannedArrival: null }))).toEqual({ start, end: start + DEFAULT_BAR_MS });
    expect(barInterval(bar("a", 8, 7))).toEqual({ start, end: start + DEFAULT_BAR_MS });
    expect(barInterval(bar("a", 8, 9, { plannedDeparture: undefined }))).toBeNull();
    expect(barInterval(bar("a", 8, 9, { plannedDeparture: "garbage" }))).toBeNull();
  });

  it("round-trips the event id of a trip on a row", () => {
    expect(parseBarEventId(barEventId("t1", "r1"))).toEqual({ tripId: "t1", rowId: "r1" });
    expect(parseBarEventId("no-separator")).toBeNull();
    expect(parseBarEventId("::r1")).toBeNull();
  });
});

describe("toSchedulerView", () => {
  it("puts a bar on its vehicle rows in tractor and trailer mode, on its driver rows in driver mode", () => {
    const read = dto({ bars: [bar("t1", 8, 10, { vehicleIds: ["v1", "V2"], driverIds: ["D1"] })] });
    const vehicles = toSchedulerView(read, "tractor", title);
    expect(vehicles.events.map((e) => [e.id, e.resourceId])).toEqual([
      ["t1::V1", "V1"],
      ["t1::V2", "V2"],
    ]);
    expect(toSchedulerView(read, "trailer", title).events).toHaveLength(2);
    expect(toSchedulerView(read, "driver", title).events.map((e) => e.resourceId)).toEqual(["D1"]);
  });

  it("titles rows by name, else code; maps bar title, tone and data", () => {
    const view = toSchedulerView(dto({ bars: [bar("t1", 8, 10, { vehicleIds: ["V1"], status: TRIP_STATUS.InProgress })] }), "tractor", title);
    expect(view.resources).toEqual([
      { id: "V1", title: "Truck 1" },
      { id: "V2", title: "T2" },
      { id: "D1", title: "Driver 1" },
    ]);
    expect(view.events[0]).toMatchObject({ title: "R-t1", tone: "info", data: { tripId: "t1", rowId: "V1" } });
    expect(view.events[0].conflict).toBeUndefined();
  });

  it("skips bars without id, without departure, or on rows not on the page; a duplicate resource id yields one bar", () => {
    const view = toSchedulerView(
      dto({
        bars: [
          bar("", 8, 9, { vehicleIds: ["V1"] }),
          bar("t2", 8, 9, { vehicleIds: ["V1"], plannedDeparture: undefined }),
          bar("t3", 8, 9, { vehicleIds: ["V9"] }),
          bar("t4", 8, 9, { vehicleIds: ["V1", "v1"] }),
        ],
      }),
      "tractor",
      title,
    );
    expect(view.events.map((e) => e.id)).toEqual(["t4::V1"]);
  });

  it("maps blocks of rows on the page as background (unavailable vs outside working time)", () => {
    const view = toSchedulerView(
      dto({
        blocks: [
          { resourceId: "v1", from: at(6), till: at(7), unavailabilityId: "u1" },
          { resourceId: "V2", from: at(18), till: at(23) },
          { resourceId: "V9", from: at(6), till: at(7) },
          { resourceId: "V1", from: null, till: at(7) },
        ] as PlanboardBlockDto[],
      }),
      "tractor",
      title,
    );
    expect(view.backgroundEvents.map((b) => [b.resourceId, b.kind, b.title])).toEqual([
      ["V1", "unavailable", "Unavailable"],
      ["V2", "outside-working-time", "Off"],
    ]);
    expect(new Set(view.backgroundEvents.map((b) => b.id)).size).toBe(2);
  });

  it("marks conflicting bars", () => {
    const view = toSchedulerView(
      dto({
        bars: [bar("t1", 8, 10, { vehicleIds: ["V1"] }), bar("t2", 9, 11, { vehicleIds: ["V1"] }), bar("t3", 12, 13, { vehicleIds: ["V1"] })],
        blocks: [{ resourceId: "V2", from: at(0), till: at(23) }] as PlanboardBlockDto[],
      }),
      "tractor",
      title,
    );
    expect(view.events.filter((e) => e.conflict).map((e) => e.id)).toEqual(["t1::V1", "t2::V1"]);
  });

  it("is empty for no read", () => {
    expect(toSchedulerView(undefined, "tractor", title)).toEqual({ resources: [], events: [], backgroundEvents: [] });
  });
});

describe("detectConflicts", () => {
  const ev = (id: string, row: string, from: number, till: number, status: number = TRIP_STATUS.Planned): SchedulerEvent<BarData> => ({
    id,
    resourceId: row,
    start: at(from),
    end: at(till),
    data: { bar: bar(id, from, till, { status: status as PlanboardBarDto["status"] }), tripId: id, rowId: row },
  });
  const block = (row: string, from: number, till: number): SchedulerBackgroundEvent => ({ id: `${row}-${from}`, resourceId: row, start: at(from), end: at(till), kind: "unavailable" });

  it("flags bars that overlap on the same row, not touching bars or bars on other rows", () => {
    const result = detectConflicts([ev("a", "R1", 8, 10), ev("b", "R1", 10, 12), ev("c", "R2", 9, 11), ev("d", "R1", 11, 13)], []);
    expect([...result].sort()).toEqual(["b", "d"]);
  });

  it("finds an overlap with a long bar that is not the direct predecessor", () => {
    const result = detectConflicts([ev("long", "R1", 6, 20), ev("x", "R1", 8, 9), ev("y", "R1", 12, 13)], []);
    expect([...result].sort()).toEqual(["long", "x", "y"]);
  });

  it("flags a bar over a block of its own row only", () => {
    const result = detectConflicts([ev("a", "R1", 8, 10), ev("b", "R2", 8, 10), ev("c", "R1", 12, 13)], [block("R1", 9, 11)]);
    expect([...result]).toEqual(["a"]);
  });

  it("leaves cancelled bars out", () => {
    const result = detectConflicts([ev("a", "R1", 8, 10), ev("x", "R1", 9, 11, TRIP_STATUS.Cancelled)], [block("R1", 8, 12)]);
    expect([...result]).toEqual(["a"]);
    expect(detectConflicts([ev("x", "R1", 9, 11, TRIP_STATUS.Cancelled)], [block("R1", 8, 12)]).size).toBe(0);
  });
});

describe("moveConflicts", () => {
  const events = toSchedulerView(
    dto({ bars: [bar("t1", 8, 10, { vehicleIds: ["V1"] }), bar("t2", 12, 14, { vehicleIds: ["V2"] }), bar("t3", 12, 14, { vehicleIds: ["V2"], status: TRIP_STATUS.Cancelled })] }),
    "tractor",
    title,
  ).events;
  const blocks: SchedulerBackgroundEvent[] = [{ id: "b", resourceId: "V2", start: at(16), end: at(18), kind: "outside-working-time" }];

  it("names the other live trips and the blocks the moved bar would overlap on the target row", () => {
    const c = moveConflicts("t1", "V2", new Date(at(13)), new Date(at(17)), events, blocks);
    expect(c.trips).toEqual(["R-t2"]);
    expect(c.blocks.map((b) => b.id)).toEqual(["b"]);
    expect(hasConflicts(c)).toBe(true);
  });

  it("ignores the bar itself (case-insensitive) and other rows", () => {
    const c = moveConflicts("T1", "V1", new Date(at(9)), new Date(at(11)), events, blocks);
    expect(hasConflicts(c)).toBe(false);
  });

  it("treats a zero-length move as one millisecond long", () => {
    expect(moveConflicts("t1", "V2", new Date(at(13)), new Date(at(13)), events, blocks).trips).toEqual(["R-t2"]);
  });
});

describe("planMove / movedBar", () => {
  const b = bar("t1", 8, 10, { vehicleIds: ["V1", "V3"], driverIds: ["D1"] });

  it("is null when nothing changed, without id or without departure", () => {
    expect(planMove(b, "V1", "V1", new Date(at(8)), new Date(at(10)))).toBeNull();
    expect(planMove({ ...b, id: undefined }, "V1", "V2", new Date(at(8)), new Date(at(10)))).toBeNull();
    expect(planMove({ ...b, plannedDeparture: undefined }, "V1", "V2", new Date(at(8)), new Date(at(10)))).toBeNull();
  });

  it("tells a time change from a row change", () => {
    expect(planMove(b, "V1", "V1", new Date(at(9)), new Date(at(11)))).toMatchObject({ timeChanged: true, rowChanged: false });
    expect(planMove(b, "V1", "V2", new Date(at(8)), new Date(at(10)))).toMatchObject({ timeChanged: false, rowChanged: true, fromRowId: "V1", toRowId: "V2" });
    expect(planMove(b, "V1", "V1", new Date(at(8)), new Date(at(11)))).toMatchObject({ timeChanged: true });
  });

  it("patches times and replaces the source row in the mode's list", () => {
    const move = planMove(b, "v1", "V2", new Date(at(9)), new Date(at(11)))!;
    const moved = movedBar(b, move, "tractor");
    expect(moved.plannedDeparture).toBe(at(9));
    expect(moved.plannedArrival).toBe(at(11));
    expect(moved.vehicleIds).toEqual(["V2", "V3"]);
    expect(moved.driverIds).toEqual(["D1"]);

    const driverMove = planMove(b, "D1", "D2", new Date(at(8)), new Date(at(10)))!;
    const byDriver = movedBar(b, driverMove, "driver");
    expect(byDriver.driverIds).toEqual(["D2"]);
    expect(byDriver.plannedDeparture).toBe(b.plannedDeparture);
  });

  it("appends the target when the source row is not in the list and never duplicates", () => {
    const move = planMove(b, "V9", "V3", new Date(at(8)), new Date(at(10)))!;
    expect(movedBar(b, move, "tractor").vehicleIds).toEqual(["V1", "V3"]);
  });
});

describe("patchBar / mergeBars / page ids", () => {
  const read = dto({ bars: [bar("t1", 8, 10), bar("t2", 9, 11)], names: { a: "A" } });

  it("replaces one bar and keeps the read when the trip is not there", () => {
    const patched = patchBar(read, "T1", (x) => ({ ...x, number: "NEW" }));
    expect(patched.bars!.map((x) => x.number)).toEqual(["NEW", "R-t2"]);
    expect(patched).not.toBe(read);
    expect(patchBar(read, "t9", (x) => x)).toBe(read);
  });

  it("replaces the requested trips by what came back (absent = gone) and merges names", () => {
    const merged = mergeBars(read, ["T1", "t3"], [bar("t3", 1, 2), bar("", 1, 2)], { b: "B" });
    expect(merged.bars!.map((x) => x.id)).toEqual(["t2", "t3"]);
    expect(merged.names).toEqual({ a: "A", b: "B" });
  });

  it("lists the page's resource and trip ids", () => {
    expect(pageResourceIds(read)).toEqual(["V1", "V2", "D1"]);
    expect(pageTripIds(read)).toEqual(["t1", "t2"]);
    expect(pageResourceIds(undefined)).toEqual([]);
  });
});
