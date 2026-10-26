import { describe, expect, it } from "vitest";

import { windowError, windowInput } from "@/apps/transport/components/leg-window";

/* A leg end's time window in the leg dialog (design 59 build note 14): HH:mm texts on the end's planned date, from
 * not after till, sent only when changed or when the date moved (the window moves with it), both emptied = clear.
 * Risk class: Medium. */

const at = (date: string, time: string) => {
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = time.split(":").map(Number);
  return new Date(y, m - 1, d, hh, mm).toISOString();
};

describe("windowError", () => {
  it("accepts empty, one end or an ordered window; refuses unreadable times and a window ending before it starts", () => {
    expect(windowError({ from: "", till: "" })).toBeUndefined();
    expect(windowError({ from: "08:00", till: "" })).toBeUndefined();
    expect(windowError({ from: "08:00", till: "08:00" })).toBeUndefined();
    expect(windowError({ from: "8:00", till: "17:30" })).toBeUndefined();
    expect(windowError({ from: "25:00", till: "" })).toBe("Orders:InvalidTime");
    expect(windowError({ from: "", till: "noon" })).toBe("Orders:InvalidTime");
    expect(windowError({ from: "17:00", till: "08:00" })).toBe("PlanningExecution:WindowEndsBeforeStart");
  });
});

describe("windowInput", () => {
  const initial = { from: "08:00", till: "12:00" };

  it("sends nothing when neither the window nor the date changed", () => {
    expect(windowInput(initial, { ...initial }, "2026-10-25", "2026-10-25")).toBeUndefined();
    expect(windowInput({ from: "", till: "" }, { from: "", till: "" }, "2026-10-25", "2026-10-26")).toBeUndefined();
  });

  it("sends the changed window as instants on the end's planned date", () => {
    expect(windowInput(initial, { from: "09:30", till: "12:00" }, "2026-10-25", "2026-10-25")).toEqual({ from: at("2026-10-25", "09:30"), till: at("2026-10-25", "12:00") });
    expect(windowInput({ from: "", till: "" }, { from: "", till: "16:00" }, "2026-10-25", "2026-10-25")).toEqual({ from: null, till: at("2026-10-25", "16:00") });
  });

  it("moves an unchanged window with a moved date, and clears a window whose times were emptied", () => {
    expect(windowInput(initial, { ...initial }, "2026-10-25", "2026-10-27")).toEqual({ from: at("2026-10-27", "08:00"), till: at("2026-10-27", "12:00") });
    expect(windowInput(initial, { from: "", till: " " }, "2026-10-25", "2026-10-25")).toEqual({ from: null, till: null });
  });
});
