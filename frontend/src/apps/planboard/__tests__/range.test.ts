import { describe, expect, it } from "vitest";

import {
  addDays,
  clampRange,
  defaultBoardRange,
  isValidRange,
  MAX_BOARD_RANGE_DAYS,
  overlapsRange,
  parseIsoDate,
  rangeDays,
  restoredRange,
} from "@/apps/planboard/range";

/* The Planboard's date ranges (design 70, "Workspace" and "Persistence"): calendar dates, both ends inclusive, at most
 * 92 days, default today … today + 6; the clamp after a user edit, the restore rule on reload, and the overlap test the
 * live updates use. Risk class: Medium (UI over tested endpoints). */

const day = (y: number, m: number, d: number) => new Date(y, m - 1, d, 10, 30);

describe("parseIsoDate / addDays / rangeDays", () => {
  it("accepts real calendar dates only", () => {
    expect(parseIsoDate("2026-02-28")).not.toBeNull();
    expect(parseIsoDate("2028-02-29")).not.toBeNull();
    expect(parseIsoDate("2026-02-29")).toBeNull();
    expect(parseIsoDate("2026-13-01")).toBeNull();
    expect(parseIsoDate("2026-1-1")).toBeNull();
    expect(parseIsoDate("2026-01-01T00:00:00")).toBeNull();
    expect(parseIsoDate(20260101)).toBeNull();
    expect(parseIsoDate(null)).toBeNull();
  });

  it("adds days across month, year and daylight-saving boundaries", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2026-10-24", 7)).toBe("2026-10-31");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
    expect(addDays("not-a-date", 3)).toBe("not-a-date");
  });

  it("counts both ends (a single day is 1), also across a DST change", () => {
    expect(rangeDays({ from: "2026-11-02", till: "2026-11-02" })).toBe(1);
    expect(rangeDays({ from: "2026-10-20", till: "2026-10-30" })).toBe(11);
    expect(rangeDays({ from: "2026-03-25", till: "2026-04-03" })).toBe(10);
    expect(rangeDays({ from: "x", till: "2026-11-02" })).toBe(0);
  });
});

describe("defaultBoardRange / isValidRange", () => {
  it("defaults to today … today + 6", () => {
    expect(defaultBoardRange(day(2026, 10, 31))).toEqual({ from: "2026-10-31", till: "2026-11-06" });
  });

  it("allows 1 … 92 days with till not before from", () => {
    expect(isValidRange({ from: "2026-11-02", till: "2026-11-02" })).toBe(true);
    expect(isValidRange({ from: "2026-11-01", till: addDays("2026-11-01", MAX_BOARD_RANGE_DAYS - 1) })).toBe(true);
    expect(isValidRange({ from: "2026-11-01", till: addDays("2026-11-01", MAX_BOARD_RANGE_DAYS) })).toBe(false);
    expect(isValidRange({ from: "2026-11-03", till: "2026-11-02" })).toBe(false);
    expect(isValidRange({ from: "2026-11-03" })).toBe(false);
    expect(isValidRange(null)).toBe(false);
    expect(isValidRange("2026-11-03")).toBe(false);
    expect(isValidRange({ from: "2026-11-01", till: "2026-11-20" }, 14)).toBe(false);
  });
});

describe("clampRange", () => {
  it("leaves a valid range alone", () => {
    expect(clampRange({ from: "2026-11-02", till: "2026-11-08" })).toEqual({ from: "2026-11-02", till: "2026-11-08" });
  });

  it("moves the end the user did not touch when till falls before from", () => {
    expect(clampRange({ from: "2026-11-10", till: "2026-11-08" }, MAX_BOARD_RANGE_DAYS, "from")).toEqual({ from: "2026-11-10", till: "2026-11-10" });
    expect(clampRange({ from: "2026-11-10", till: "2026-11-08" }, MAX_BOARD_RANGE_DAYS, "till")).toEqual({ from: "2026-11-08", till: "2026-11-08" });
  });

  it("cuts a span longer than the maximum back from the untouched end", () => {
    expect(clampRange({ from: "2026-01-01", till: "2026-12-31" }, MAX_BOARD_RANGE_DAYS, "from")).toEqual({ from: "2026-01-01", till: addDays("2026-01-01", 91) });
    expect(clampRange({ from: "2026-01-01", till: "2026-12-31" }, MAX_BOARD_RANGE_DAYS, "till")).toEqual({ from: addDays("2026-12-31", -91), till: "2026-12-31" });
    expect(rangeDays(clampRange({ from: "2026-11-01", till: "2026-11-30" }, 14, "from"))).toBe(14);
  });

  it("repairs an invalid end from the other one", () => {
    expect(clampRange({ from: "nonsense", till: "2026-11-08" })).toEqual({ from: "2026-11-08", till: "2026-11-08" });
    expect(clampRange({ from: "2026-11-08", till: "" })).toEqual({ from: "2026-11-08", till: "2026-11-08" });
  });
});

describe("restoredRange", () => {
  const today = day(2026, 10, 31);

  it("keeps a stored range that still contains today", () => {
    expect(restoredRange({ from: "2026-10-25", till: "2026-11-05" }, today)).toEqual({ from: "2026-10-25", till: "2026-11-05" });
    expect(restoredRange({ from: "2026-10-31", till: "2026-10-31" }, today)).toEqual({ from: "2026-10-31", till: "2026-10-31" });
  });

  it("moves a range in the past or ahead to start today with the same length", () => {
    expect(restoredRange({ from: "2026-09-01", till: "2026-09-14" }, today)).toEqual({ from: "2026-10-31", till: "2026-11-13" });
    expect(restoredRange({ from: "2026-12-01", till: "2026-12-03" }, today)).toEqual({ from: "2026-10-31", till: "2026-11-02" });
  });

  it("caps the length at the maximum and falls back to the default length for garbage", () => {
    expect(rangeDays(restoredRange({ from: "2025-01-01", till: "2025-12-31" }, today))).toBe(MAX_BOARD_RANGE_DAYS);
    expect(restoredRange({ from: "x", till: "y" }, today)).toEqual({ from: "2026-10-31", till: "2026-11-06" });
  });
});

describe("overlapsRange", () => {
  const range = { from: "2026-11-02", till: "2026-11-08" };

  it("is inclusive at both ends", () => {
    expect(overlapsRange(range, "2026-11-08", "2026-11-12")).toBe(true);
    expect(overlapsRange(range, "2026-10-30", "2026-11-02")).toBe(true);
    expect(overlapsRange(range, "2026-11-09", "2026-11-12")).toBe(false);
    expect(overlapsRange(range, "2026-10-28", "2026-11-01")).toBe(false);
    expect(overlapsRange(range, "2026-10-01", "2026-12-01")).toBe(true);
  });

  it("treats a single known end as a one-day record and a record without dates as overlapping", () => {
    expect(overlapsRange(range, "2026-11-05", null)).toBe(true);
    expect(overlapsRange(range, null, "2026-11-10")).toBe(false);
    expect(overlapsRange(range, undefined, undefined)).toBe(true);
  });

  it("compares the date part of date-times", () => {
    expect(overlapsRange(range, "2026-11-08T23:30:00+01:00", "2026-11-09T02:00:00+01:00")).toBe(true);
    expect(overlapsRange(range, "2026-11-01T08:00:00Z", "2026-11-01T18:00:00Z")).toBe(false);
  });
});
