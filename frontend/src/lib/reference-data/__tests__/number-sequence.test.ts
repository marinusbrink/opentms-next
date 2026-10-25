import { describe, expect, it } from "vitest";

import { formatNumber, numberingError } from "@/lib/reference-data/number-sequence";

/* The same table as the backend's NumberSequenceFormatTests (design 50). */
const october22 = new Date(2026, 9, 22);

describe("formatNumber (design 50 mirror)", () => {
  it.each([
    ["TO", true, true, "-", 5, 42, "TO-2026-10-00042"],
    ["TO", true, false, "-", 5, 42, "TO-2026-00042"],
    ["TO", false, false, "-", 5, 42, "TO-00042"],
    ["TO", false, false, "", 5, 42, "TO00042"],
    [null, true, false, "/", 3, 7, "2026/007"],
    [null, false, false, "", 3, 7, "007"],
    ["f", true, false, ".", 4, 12345, "F.2026.12345"],
    ["TR_", false, false, "", 10, 1, "TR_0000000001"],
  ] as const)("%s year=%s month=%s sep=%s digits=%s → %s", (prefix, includeYear, includeMonth, separator, digits, counter, expected) => {
    expect(formatNumber({ prefix, includeYear, includeMonth, separator, digits }, counter, october22)).toBe(expected);
  });
});

describe("numberingError", () => {
  it("accepts a valid format", () => {
    expect(numberingError({ prefix: "TO", includeYear: true, includeMonth: true, separator: "-", digits: 5 })).toBeUndefined();
  });

  it.each([
    [{ prefix: "TO 1", digits: 5 }, "MasterData:Numbering.InvalidPrefix"],
    [{ prefix: "12345678901234567", digits: 5 }, "MasterData:Numbering.InvalidPrefix"],
    [{ prefix: "TO", includeMonth: true, digits: 5 }, "MasterData:Numbering.MonthNeedsYear"],
    [{ prefix: "TO", separator: "#", digits: 5 }, "MasterData:Numbering.InvalidSeparator"],
    [{ prefix: "TO", digits: 2 }, "MasterData:Numbering.InvalidDigits"],
    [{ prefix: "TO", digits: 11 }, "MasterData:Numbering.InvalidDigits"],
  ])("refuses %o", (values, key) => {
    expect(numberingError(values)).toBe(key);
  });
});
