import { describe, expect, it } from "vitest";

import {
  delayAtNextStop,
  lineState,
  markerState,
  nextStopIndex,
  nowPosition,
  stopBadge,
  stopHasActuals,
  stopOrderNumbers,
  stopTimes,
  stopWindow,
  timelinePosition,
  type NowPosition,
  type TimelineTimes,
} from "@/apps/planboard/panes/stops/model";
import type { TripStopDto } from "@/domains/planning-execution/trips";

/* The pure part of the Stops timeline (design 74 §2): where the trip is now, the tier of times a stop shows, its marker,
 * line, window and state badge. Risk class: Medium (UI flow on planning data; a wrong marker misleads the planner, it
 * changes no data). */

const at = (hour: number, minute = 0) => new Date(2026, 10, 10, hour, minute);
const iso = (hour: number, minute = 0) => at(hour, minute).toISOString();

const stop = (times: Partial<TimelineTimes> = {}): TimelineTimes => times as TimelineTimes;
/** A stop with planned times from hour to hour. */
const planned = (from: number, till: number) => stop({ plannedArrival: iso(from), plannedDeparture: iso(till) });

const THREE = [planned(8, 9), planned(11, 12), planned(14, 15)];

describe("nowPosition: nothing to place", () => {
  it("no stops is 'before'", () => {
    expect(nowPosition([], at(10))).toEqual({ kind: "before" });
  });

  it("before the first stop's arrival and no actuals is 'before'", () => {
    expect(nowPosition(THREE, at(7))).toEqual({ kind: "before" });
  });

  it("stops without any times are 'before'", () => {
    expect(nowPosition([stop(), stop()], at(10))).toEqual({ kind: "before" });
  });
});

describe("nowPosition: with actuals (they decide, even when the expected times have passed)", () => {
  it("an actual arrival without departure is 'at' that stop", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8), actualDeparture: iso(9) }, { ...THREE[1], actualArrival: iso(11) }, THREE[2]];
    expect(nowPosition(stops, at(20))).toEqual({ kind: "at", index: 1 });
  });

  it("the actual arrival holds on the first stop, also long after every expected time", () => {
    expect(nowPosition([{ ...THREE[0], actualArrival: iso(8) }, THREE[1], THREE[2]], at(23))).toEqual({ kind: "at", index: 0 });
  });

  it("an actual arrival wins over a clock before the first expected arrival", () => {
    expect(nowPosition([{ ...THREE[0], actualArrival: iso(7) }, THREE[1]], at(7, 30))).toEqual({ kind: "at", index: 0 });
  });

  it("after an actual departure, inside the next stop's expected stay, is 'at' the next stop", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8), actualDeparture: iso(9) }, THREE[1], THREE[2]];
    expect(nowPosition(stops, at(11, 30))).toEqual({ kind: "at", index: 1 });
  });

  it("after an actual departure, before the next arrival, is 'between' with the expected fraction", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8), actualDeparture: iso(9) }, THREE[1], THREE[2]];
    expect(nowPosition(stops, at(10))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0.5 });
  });

  it("after an actual departure, past the next stop's whole stay, stays 'between' with fraction 1", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8), actualDeparture: iso(9) }, THREE[1], THREE[2]];
    expect(nowPosition(stops, at(13))).toEqual({ kind: "between", from: 0, to: 1, fraction: 1 });
  });

  it("an actual departure from the last stop is 'after'", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8), actualDeparture: iso(9) }, { ...THREE[1], actualDeparture: iso(12) }];
    expect(nowPosition(stops, at(10))).toEqual({ kind: "after" });
  });

  it("the last stop with an actual counts, not the first", () => {
    const stops = [{ ...THREE[0], actualDeparture: iso(9) }, THREE[1], { ...THREE[2], actualArrival: iso(14) }];
    expect(nowPosition(stops, at(14, 30))).toEqual({ kind: "at", index: 2 });
  });
});

describe("nowPosition: without actuals (the expected times decide)", () => {
  it("inside a stay is 'at' that stop, both ends included", () => {
    expect(nowPosition(THREE, at(8))).toEqual({ kind: "at", index: 0 });
    expect(nowPosition(THREE, at(11, 30))).toEqual({ kind: "at", index: 1 });
    expect(nowPosition(THREE, at(12))).toEqual({ kind: "at", index: 1 });
  });

  it("between two stays is 'between' with the fraction of the expected drive", () => {
    expect(nowPosition(THREE, at(9, 30))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0.25 });
    expect(nowPosition(THREE, at(13))).toEqual({ kind: "between", from: 1, to: 2, fraction: 0.5 });
  });

  it("every expected time passed and nothing reported: the marker stays 'at' the last stop, the trip is not finished", () => {
    expect(nowPosition(THREE, at(23))).toEqual({ kind: "at", index: 2 });
  });

  it("a last stop with only an arrival, passed, stays 'at' it", () => {
    expect(nowPosition([planned(8, 9), stop({ plannedArrival: iso(11) })], at(23))).toEqual({ kind: "at", index: 1 });
  });

  it("the estimate wins over the plan", () => {
    const stops = [stop({ plannedArrival: iso(8), plannedDeparture: iso(9), estimatedArrival: iso(10), estimatedDeparture: iso(11) }), planned(12, 13)];
    expect(nowPosition(stops, at(9, 30))).toEqual({ kind: "before" });
    expect(nowPosition(stops, at(10, 30))).toEqual({ kind: "at", index: 0 });
  });

  it("a stop without a departure has no stay: it counts as passed from its arrival", () => {
    expect(nowPosition([stop({ plannedArrival: iso(8) }), planned(12, 13)], at(10))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0.5 });
  });
});

describe("nowPosition: fraction safety", () => {
  const departed = (departure: string) => stop({ actualDeparture: departure });

  it("clamps below 0: an actual departure later than the clock", () => {
    expect(nowPosition([departed(iso(10)), planned(12, 13)], at(9))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0 });
  });

  it("clamps above 1", () => {
    const position = nowPosition([departed(iso(9)), stop({ plannedArrival: iso(10) })], at(11));
    expect(position).toEqual({ kind: "between", from: 0, to: 1, fraction: 1 });
  });

  it("an unknown next arrival leaves the fraction at 0", () => {
    expect(nowPosition([departed(iso(9)), stop()], at(10))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0 });
  });

  it("an unreadable time counts as unknown", () => {
    expect(nowPosition([departed(iso(9)), stop({ plannedArrival: "not a date" })], at(10))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0 });
    expect(nowPosition([stop({ plannedArrival: "not a date" })], at(10))).toEqual({ kind: "before" });
  });

  it("inverted times (next arrival before the departure) give 0 before it and 1 from it", () => {
    const inverted = [departed(iso(12)), stop({ plannedArrival: iso(10) })];
    expect(nowPosition(inverted, at(9))).toEqual({ kind: "between", from: 0, to: 1, fraction: 0 });
    expect(nowPosition(inverted, at(10))).toEqual({ kind: "between", from: 0, to: 1, fraction: 1 });
    expect(nowPosition(inverted, at(11))).toEqual({ kind: "between", from: 0, to: 1, fraction: 1 });
  });

  it("an equal departure and arrival do not divide by zero", () => {
    const same = [departed(iso(10)), stop({ plannedArrival: iso(10) })];
    const position = nowPosition(same, at(9, 59)) as Extract<NowPosition, { kind: "between" }>;
    expect(position.fraction).toBe(0);
  });
});

describe("timelinePosition", () => {
  const inProgress = 2;
  const completed = 3;
  const cancelled = 4;

  it("a running trip shows the marker", () => {
    expect(timelinePosition(THREE, at(11, 30), inProgress)).toEqual({ position: { kind: "at", index: 1 }, showNow: true });
    expect(timelinePosition(THREE, at(7), undefined)).toEqual({ position: { kind: "before" }, showNow: true });
  });

  it("all expected times passed without actuals still shows the marker", () => {
    expect(timelinePosition(THREE, at(23), inProgress)).toEqual({ position: { kind: "at", index: 2 }, showNow: true });
  });

  it("an actual departure from the last stop has no marker", () => {
    expect(timelinePosition([{ ...THREE[0], actualDeparture: iso(9) }], at(10), inProgress)).toEqual({ position: { kind: "after" }, showNow: false });
  });

  it("a completed trip is past its end whatever its stops say", () => {
    expect(timelinePosition(THREE, at(7), completed)).toEqual({ position: { kind: "after" }, showNow: false });
    expect(timelinePosition([{ ...THREE[0], actualArrival: iso(8) }, THREE[1]], at(8), completed)).toEqual({ position: { kind: "after" }, showNow: false });
  });

  it("a cancelled trip has no marker and its expected times never pass", () => {
    expect(timelinePosition(THREE, at(23), cancelled)).toEqual({ position: { kind: "before" }, showNow: false });
  });

  it("a cancelled trip keeps the progress of its actuals", () => {
    const stops = [{ ...THREE[0], actualArrival: iso(8) }, THREE[1]];
    expect(timelinePosition(stops, at(23), cancelled)).toEqual({ position: { kind: "at", index: 0 }, showNow: false });
  });
});

describe("nextStopIndex", () => {
  it("follows the position", () => {
    expect(nextStopIndex({ kind: "before" }, 3)).toBe(0);
    expect(nextStopIndex({ kind: "before" }, 0)).toBeNull();
    expect(nextStopIndex({ kind: "at", index: 0 }, 3)).toBe(1);
    expect(nextStopIndex({ kind: "at", index: 2 }, 3)).toBeNull();
    expect(nextStopIndex({ kind: "between", from: 0, to: 1, fraction: 0.5 }, 3)).toBe(1);
    expect(nextStopIndex({ kind: "after" }, 3)).toBeNull();
  });
});

describe("delayAtNextStop", () => {
  const next = (estimated: string | null, plan: string | null) => stop({ estimatedArrival: estimated, plannedArrival: plan });

  it("is expected against planned at the next stop, in minutes", () => {
    expect(delayAtNextStop([THREE[0], next(iso(11, 25), iso(11))], { kind: "at", index: 0 })).toBe(25);
    expect(delayAtNextStop([THREE[0], next(iso(11, 25), iso(11))], { kind: "between", from: 0, to: 1, fraction: 0.5 })).toBe(25);
    expect(delayAtNextStop([next(iso(8, 10), iso(8))], { kind: "before" })).toBe(10);
  });

  it("is negative when early", () => {
    expect(delayAtNextStop([THREE[0], next(iso(10, 45), iso(11))], { kind: "at", index: 0 })).toBe(-15);
  });

  it("is zero on time", () => {
    expect(delayAtNextStop([THREE[0], next(iso(11), iso(11))], { kind: "at", index: 0 })).toBe(0);
  });

  it("is unknown without an estimate or a plan", () => {
    expect(delayAtNextStop([THREE[0], next(null, iso(11))], { kind: "at", index: 0 })).toBeNull();
    expect(delayAtNextStop([THREE[0], next(iso(11), null)], { kind: "at", index: 0 })).toBeNull();
  });

  it("is unknown at the last stop and past the end", () => {
    expect(delayAtNextStop([THREE[0], next(iso(11, 25), iso(11))], { kind: "at", index: 1 })).toBeNull();
    expect(delayAtNextStop([THREE[0], next(iso(11, 25), iso(11))], { kind: "after" })).toBeNull();
    expect(delayAtNextStop([], { kind: "before" })).toBeNull();
  });
});

describe("stopTimes: one tier per stop", () => {
  const all = { plannedArrival: "pa", plannedDeparture: "pd", estimatedArrival: "ea", estimatedDeparture: "ed" };

  it("actual wins, and does not mix with lower tiers", () => {
    expect(stopTimes(stop({ ...all, actualArrival: "aa" }))).toEqual({ tier: "actual", arrival: "aa", departure: null });
    expect(stopTimes(stop({ ...all, actualDeparture: "ad" }))).toEqual({ tier: "actual", arrival: null, departure: "ad" });
    expect(stopTimes(stop({ ...all, actualArrival: "aa", actualDeparture: "ad" }))).toEqual({ tier: "actual", arrival: "aa", departure: "ad" });
  });

  it("then the estimate", () => {
    expect(stopTimes(stop(all))).toEqual({ tier: "estimated", arrival: "ea", departure: "ed" });
    expect(stopTimes(stop({ plannedArrival: "pa", plannedDeparture: "pd", estimatedArrival: "ea" }))).toEqual({ tier: "estimated", arrival: "ea", departure: null });
  });

  it("then the plan; nothing at all is the planned tier without times", () => {
    expect(stopTimes(stop({ plannedArrival: "pa", plannedDeparture: "pd" }))).toEqual({ tier: "planned", arrival: "pa", departure: "pd" });
    expect(stopTimes(stop())).toEqual({ tier: "planned", arrival: null, departure: null });
  });
});

describe("markerState", () => {
  const states = (position: NowPosition) => [0, 1, 2].map((index) => markerState(index, position));

  it("marks the stop the trip is at or going to as next", () => {
    expect(states({ kind: "before" })).toEqual(["next", "later", "later"]);
    expect(states({ kind: "at", index: 1 })).toEqual(["done", "next", "later"]);
    expect(states({ kind: "between", from: 0, to: 1, fraction: 0.5 })).toEqual(["done", "next", "later"]);
    expect(states({ kind: "between", from: 1, to: 2, fraction: 0.5 })).toEqual(["done", "done", "next"]);
    expect(states({ kind: "after" })).toEqual(["done", "done", "done"]);
  });
});

describe("lineState", () => {
  const lines = (position: NowPosition) => [0, 1, 2].map((index) => lineState(index, position));

  it("nothing is driven before the start", () => {
    expect(lines({ kind: "before" })).toEqual([{ upper: false, lower: false }, { upper: false, lower: false }, { upper: false, lower: false }]);
  });

  it("at a stop the line up to it is driven, below it not", () => {
    expect(lines({ kind: "at", index: 1 })).toEqual([{ upper: true, lower: true }, { upper: true, lower: false }, { upper: false, lower: false }]);
  });

  it("between two stops the line is driven up to and below `from`", () => {
    expect(lines({ kind: "between", from: 1, to: 2, fraction: 0.5 })).toEqual([{ upper: true, lower: true }, { upper: true, lower: true }, { upper: false, lower: false }]);
  });

  it("past the end all of it is driven", () => {
    expect(lines({ kind: "after" })).toEqual([{ upper: true, lower: true }, { upper: true, lower: true }, { upper: true, lower: true }]);
  });
});

type Legs = Pick<TripStopDto, "legs">;
const withLegs = (...legs: Record<string, unknown>[]) => ({ legs } as unknown as Legs);

describe("stopWindow", () => {
  it("is the earliest start and the earliest end of the legs' windows", () => {
    expect(stopWindow(withLegs({ windowFrom: iso(9), windowTill: iso(17) }, { windowFrom: iso(8), windowTill: iso(12) }))).toEqual({ from: iso(8), till: iso(12) });
  });

  it("skips legs without a value and is empty without legs", () => {
    expect(stopWindow(withLegs({ windowFrom: null, windowTill: iso(12) }, { windowFrom: iso(8) }))).toEqual({ from: iso(8), till: iso(12) });
    expect(stopWindow(withLegs())).toEqual({ from: null, till: null });
    expect(stopWindow({ legs: null } as unknown as Legs)).toEqual({ from: null, till: null });
  });
});

describe("stopBadge", () => {
  const badge = (fields: Record<string, unknown>, windowTill: string | null = iso(12)) => stopBadge({ legs: [{ windowTill }], ...fields } as never);

  it("none once the stop is done", () => {
    expect(badge({ estimatedArrival: iso(13), actualArrival: iso(13) })).toBeNull();
    expect(badge({ estimatedArrival: iso(13), actualDeparture: iso(13) })).toBeNull();
  });

  it("none without a window or without an estimate", () => {
    expect(badge({ estimatedArrival: iso(11) }, null)).toBeNull();
    expect(badge({ estimatedArrival: null })).toBeNull();
    expect(stopBadge({ estimatedArrival: iso(11), legs: [] } as never)).toBeNull();
  });

  it("on time with room to spare", () => {
    expect(badge({ estimatedArrival: iso(10) })).toEqual({ state: "on-time", delayMinutes: null });
  });

  it("at risk within thirty minutes of the window's end, the boundaries included", () => {
    expect(badge({ estimatedArrival: iso(11, 45) })).toEqual({ state: "at-risk", delayMinutes: null });
    expect(badge({ estimatedArrival: iso(11, 30) })).toEqual({ state: "at-risk", delayMinutes: null });
    expect(badge({ estimatedArrival: iso(12) })).toEqual({ state: "at-risk", delayMinutes: null });
    expect(badge({ estimatedArrival: iso(11, 29) })).toEqual({ state: "on-time", delayMinutes: null });
  });

  it("late past the window's end, with the minutes", () => {
    expect(badge({ estimatedArrival: iso(12, 45) })).toEqual({ state: "late", delayMinutes: 45 });
  });

  it("measures against the earliest end of the legs", () => {
    expect(stopBadge({ estimatedArrival: iso(12, 10), legs: [{ windowTill: iso(17) }, { windowTill: iso(12) }] } as never)).toEqual({ state: "late", delayMinutes: 10 });
  });
});

describe("stopOrderNumbers and stopHasActuals", () => {
  it("lists each order once, in the legs' order", () => {
    expect(stopOrderNumbers(withLegs({ orderNumber: "O-2" }, { orderNumber: "O-1" }, { orderNumber: "O-2" }, { orderNumber: null }))).toEqual(["O-2", "O-1"]);
    expect(stopOrderNumbers({ legs: null } as unknown as Legs)).toEqual([]);
  });

  it("an arrival or a departure is an actual; no stop has none", () => {
    expect(stopHasActuals({ actualArrival: iso(8) })).toBe(true);
    expect(stopHasActuals({ actualDeparture: iso(8) })).toBe(true);
    expect(stopHasActuals({})).toBe(false);
    expect(stopHasActuals(undefined)).toBe(false);
  });
});
