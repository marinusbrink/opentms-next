import { describe, expect, it } from "vitest";

import { formToPattern, joinLocal, parseTime, patternErrors, patternToForm, splitLocal, stripPosition, stripSegment } from "@/apps/planboard/panes/resources/model";
import { flagsFromWeekdays, weekdayFlag, weekdaysFromFlags, WEEKDAYS, type ResourceWorkingPatternDto } from "@/domains/planning-execution/resource-availability";

/* The pure part of the Resources pane (design 70 "Resources", design 68 §2): the weekly working-pattern form ↔ API
 * (0 = Monday … 6 = Sunday, `HH:mm:ss` on the wire), the recurrence weekday flags (Mon = 1 … Sun = 64, matching the
 * backend's AvailabilityWeekdays), time parsing, local date/time fields and the availability strip geometry.
 * Risk class: Medium. */

describe("weekday flags", () => {
  it("maps weekday i to 1 << i (Mon = 1 … Sun = 64) and back", () => {
    expect(WEEKDAYS.map(weekdayFlag)).toEqual([1, 2, 4, 8, 16, 32, 64]);
    expect(flagsFromWeekdays([0, 2, 4])).toBe(1 + 4 + 16);
    expect(flagsFromWeekdays([])).toBe(0);
    expect(flagsFromWeekdays([0, 1, 2, 3, 4, 5, 6])).toBe(127);
    expect(weekdaysFromFlags(1 + 4 + 16)).toEqual([0, 2, 4]);
    expect(weekdaysFromFlags(null)).toEqual([]);
    expect(weekdaysFromFlags(127 + 128)).toEqual([0, 1, 2, 3, 4, 5, 6]);
  });
});

describe("parseTime", () => {
  it.each([
    ["8:00", "08:00"],
    ["0800", "08:00"],
    ["08:00", "08:00"],
    [" 23:59 ", "23:59"],
    ["730", "07:30"],
  ])("reads %s as %s", (text, expected) => {
    expect(parseTime(text)).toBe(expected);
  });

  it.each(["24:00", "12:60", "8", "8:0", "ab:cd", "", "08:00:00"])("rejects %s", (text) => {
    expect(parseTime(text)).toBeNull();
  });
});

describe("pattern form ↔ API", () => {
  const patterns = [
    { resourceId: "D1", weekday: 0, start: "08:00:00", end: "17:00:00" },
    { resourceId: "D1", weekday: 4, start: "06:30:00", end: "14:00:00" },
  ] as ResourceWorkingPatternDto[];

  it("gives seven rows, Monday first, off where no pattern row exists", () => {
    const form = patternToForm(patterns);
    expect(form.map((d) => d.weekday)).toEqual([0, 1, 2, 3, 4, 5, 6]);
    expect(form[0]).toEqual({ weekday: 0, works: true, start: "08:00", end: "17:00" });
    expect(form[1]).toEqual({ weekday: 1, works: false, start: "", end: "" });
    expect(form[4]).toEqual({ weekday: 4, works: true, start: "06:30", end: "14:00" });
  });

  it("round-trips through the form, leaving off days out", () => {
    expect(formToPattern(patternToForm(patterns))).toEqual([
      { weekday: 0, start: "08:00:00", end: "17:00:00" },
      { weekday: 4, start: "06:30:00", end: "14:00:00" },
    ]);
    expect(formToPattern(patternToForm([]))).toEqual([]);
  });

  it("normalises typed times on the way out", () => {
    expect(formToPattern([{ weekday: 2, works: true, start: "7:00", end: "1530" }])).toEqual([{ weekday: 2, start: "07:00:00", end: "15:30:00" }]);
  });

  it("flags working days with missing, invalid or reversed times, never off days", () => {
    const errors = patternErrors([
      { weekday: 0, works: true, start: "08:00", end: "17:00" },
      { weekday: 1, works: true, start: "", end: "17:00" },
      { weekday: 2, works: true, start: "17:00", end: "08:00" },
      { weekday: 3, works: true, start: "08:00", end: "08:00" },
      { weekday: 4, works: true, start: "25:00", end: "26:00" },
      { weekday: 5, works: false, start: "x", end: "y" },
    ]);
    expect([...errors].sort()).toEqual([1, 2, 3, 4]);
  });
});

describe("local date and time fields", () => {
  it("splits an instant into local date and time and joins it back", () => {
    const instant = new Date(2026, 10, 2, 7, 45).toISOString();
    expect(splitLocal(instant)).toEqual({ date: "2026-11-02", time: "07:45" });
    expect(joinLocal("2026-11-02", "07:45")).toBe(instant);
    expect(joinLocal("2026-11-02", "")).toBe(new Date(2026, 10, 2, 0, 0).toISOString());
  });

  it("is empty or null for missing or invalid parts", () => {
    expect(splitLocal(null)).toEqual({ date: null, time: "" });
    expect(splitLocal("garbage")).toEqual({ date: null, time: "" });
    expect(joinLocal(null, "08:00")).toBeNull();
    expect(joinLocal("2026-11-02", "25:00")).toBeNull();
  });
});

describe("availability strip", () => {
  const start = 0;
  const end = 1000;

  it("clips an interval to the window in percent, with a minimum visible width", () => {
    expect(stripSegment(start, end, 250, 500)).toEqual({ left: 25, width: 25 });
    expect(stripSegment(start, end, -100, 100)).toEqual({ left: 0, width: 10 });
    expect(stripSegment(start, end, 999, 5000)).toEqual({ left: 99.9, width: 0.5 });
  });

  it("is null outside the window, for an empty window or an empty interval", () => {
    expect(stripSegment(start, end, 1000, 2000)).toBeNull();
    expect(stripSegment(start, end, 500, 500)).toBeNull();
    expect(stripSegment(5, 5, 0, 10)).toBeNull();
    expect(stripSegment(start, end, "x", 10)).toBeNull();
  });

  it("positions a moment, null outside", () => {
    expect(stripPosition(start, end, 500)).toBe(50);
    expect(stripPosition(start, end, 1001)).toBeNull();
    expect(stripPosition(start, end, null)).toBeNull();
  });
});
