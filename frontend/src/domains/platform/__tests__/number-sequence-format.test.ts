import { describe, expect, it } from "vitest";

import {
  MAX_MINIMUM_DIGITS,
  MIN_MINIMUM_DIGITS,
  NO_PERIOD,
  NumberSequenceSchemes,
  formatNumber,
  periodOf,
  usesMonth,
  usesTwoDigitYear,
  usesYear,
  type NumberSequenceFormat,
  type NumberSequenceScheme,
} from "@/domains/platform/number-sequence-format";

/* The frontend mirror of NumberSequenceFormatter (backend/modules/platform/OpenTms.Platform.Domain.Shared/
 * NumberSequences/NumberSequenceFormatter.cs, design 49). Every expected value below is what the C# formatter returns
 * for the same input: Prefix + [YearPrefix + yyyy ("D4") | yy (Year % 100, "D2") + YearSuffix] + [MonthPrefix + MM
 * ("D2") + MonthSuffix] + counter.ToString().PadLeft(Math.Clamp(MinimumDigits, 1, 12), '0'); a null affix appends
 * nothing; an undefined scheme or a negative counter throws. Risk class: Critical (number generation: the formatter
 * table tests of the design's risk analysis); the backend's own table tests prove the C# side. */

const { FourDigitYear, TwoDigitYear, FourDigitYearMonth, TwoDigitYearMonth, CounterOnly } = NumberSequenceSchemes;

const format = (scheme: NumberSequenceScheme, rest: Partial<NumberSequenceFormat> = {}): NumberSequenceFormat => ({
  scheme,
  prefix: null,
  yearPrefix: null,
  yearSuffix: null,
  monthPrefix: null,
  monthSuffix: null,
  minimumDigits: 4,
  ...rest,
});

const MARCH_2026 = { year: 2026, month: 3 };
const ALL_AFFIXES = { prefix: "P", yearPrefix: "Y", yearSuffix: "y", monthPrefix: "M", monthSuffix: "m" };

describe("NumberSequenceSchemes", () => {
  it("has the backend enum's values, starting at 1, in OpenTMS's order", () => {
    expect(NumberSequenceSchemes).toEqual({ FourDigitYear: 1, TwoDigitYear: 2, FourDigitYearMonth: 3, TwoDigitYearMonth: 4, CounterOnly: 5 });
    expect([MIN_MINIMUM_DIGITS, MAX_MINIMUM_DIGITS, NO_PERIOD]).toEqual([1, 12, 0]);
  });

  it.each([
    [FourDigitYear, true, false, false],
    [TwoDigitYear, true, false, true],
    [FourDigitYearMonth, true, true, false],
    [TwoDigitYearMonth, true, true, true],
    [CounterOnly, false, false, false],
  ] as const)("scheme %i: uses year %s, month %s, two-digit year %s", (scheme, year, month, twoDigit) => {
    expect(usesYear(scheme)).toBe(year);
    expect(usesMonth(scheme)).toBe(month);
    expect(usesTwoDigitYear(scheme)).toBe(twoDigit);
  });
});

describe("formatNumber – every scheme with every affix set (affixes of an unused part are ignored)", () => {
  it.each([
    [FourDigitYear, "PY2026y0001"],
    [TwoDigitYear, "PY26y0001"],
    [FourDigitYearMonth, "PY2026yM03m0001"],
    [TwoDigitYearMonth, "PY26yM03m0001"],
    [CounterOnly, "P0001"],
  ] as const)("scheme %i → %s", (scheme, expected) => {
    expect(formatNumber(format(scheme, ALL_AFFIXES), MARCH_2026, 1)).toBe(expected);
  });
});

describe("formatNumber – every scheme without affixes", () => {
  it.each([
    [FourDigitYear, "20260007"],
    [TwoDigitYear, "260007"],
    [FourDigitYearMonth, "2026030007"],
    [TwoDigitYearMonth, "26030007"],
    [CounterOnly, "0007"],
  ] as const)("scheme %i → %s", (scheme, expected) => {
    expect(formatNumber(format(scheme), MARCH_2026, 7)).toBe(expected);
  });

  it("an empty affix appends nothing, as null does", () => {
    const empty = { prefix: "", yearPrefix: "", yearSuffix: "", monthPrefix: "", monthSuffix: "" };
    expect(formatNumber(format(TwoDigitYearMonth, empty), MARCH_2026, 7)).toBe(formatNumber(format(TwoDigitYearMonth), MARCH_2026, 7));
    expect(formatNumber(format(TwoDigitYearMonth, { prefix: undefined, yearPrefix: undefined }), MARCH_2026, 7)).toBe("26030007");
  });
});

describe("formatNumber – the design's examples and typical formats", () => {
  it.each([
    ["the design's example O-26-0001", format(TwoDigitYear, { prefix: "O-", yearSuffix: "-" }), MARCH_2026, 1, "O-26-0001"],
    ["an invoice per year", format(FourDigitYear, { prefix: "F", yearSuffix: "-", minimumDigits: 5 }), MARCH_2026, 42, "F2026-00042"],
    ["a trip per month with separators", format(FourDigitYearMonth, { prefix: "R", yearPrefix: "/", monthPrefix: "/", monthSuffix: "/" }), MARCH_2026, 3, "R/2026/03/0003"],
    ["a purchase order per month, two-digit year", format(TwoDigitYearMonth, { prefix: "PO-", monthPrefix: ".", monthSuffix: "-" }), { year: 2026, month: 11 }, 12, "PO-26.11-0012"],
    ["a plain counter with a prefix", format(CounterOnly, { prefix: "INV", minimumDigits: 6 }), MARCH_2026, 42, "INV000042"],
    ["affixes keep their spaces as typed", format(FourDigitYear, { prefix: "A ", yearSuffix: " " }), MARCH_2026, 1, "A 2026 0001"],
  ] as const)("%s", (_name, f, date, counter, expected) => {
    expect(formatNumber(f, date, counter)).toBe(expected);
  });
});

describe("formatNumber – year and month padding", () => {
  it.each([
    [2026, "26", "2026"],
    [2000, "00", "2000"],
    [2005, "05", "2005"],
    [2099, "99", "2099"],
    [1999, "99", "1999"],
    [999, "99", "0999"],
    [5, "05", "0005"],
    [9999, "99", "9999"],
  ] as const)("year %i → two digits %s, four digits %s", (year, two, four) => {
    expect(formatNumber(format(TwoDigitYear), { year, month: 1 }, 1)).toBe(`${two}0001`);
    expect(formatNumber(format(FourDigitYear), { year, month: 1 }, 1)).toBe(`${four}0001`);
  });

  it.each([
    [1, "01"],
    [9, "09"],
    [10, "10"],
    [12, "12"],
  ] as const)("month %i → %s", (month, mm) => {
    expect(formatNumber(format(FourDigitYearMonth), { year: 2026, month }, 1)).toBe(`2026${mm}0001`);
    expect(formatNumber(format(TwoDigitYearMonth), { year: 2026, month }, 1)).toBe(`26${mm}0001`);
  });
});

describe("formatNumber – minimum digits and the counter", () => {
  it.each([
    [1, 0, "0"],
    [1, 1, "1"],
    [1, 123, "123"],
    [4, 0, "0000"],
    [4, 1, "0001"],
    [4, 9999, "9999"],
    [4, 10000, "10000"],
    [4, 123456, "123456"],
    [6, 42, "000042"],
    [12, 1, "000000000001"],
    [12, 123456789012, "123456789012"],
    [12, 1234567890123, "1234567890123"],
    [4, Number.MAX_SAFE_INTEGER, "9007199254740991"],
  ] as const)("minimum digits %i, counter %i → %s (padded, never truncated)", (minimumDigits, counter, expected) => {
    expect(formatNumber(format(CounterOnly, { minimumDigits }), MARCH_2026, counter)).toBe(expected);
  });

  it.each([
    [0, "7"],
    [-3, "7"],
    [13, "000000000007"],
    [99, "000000000007"],
  ] as const)("minimum digits %i outside 1–12 is clamped as Math.Clamp does → %s", (minimumDigits, expected) => {
    expect(formatNumber(format(CounterOnly, { minimumDigits }), MARCH_2026, 7)).toBe(expected);
  });

  it("refuses a negative counter (ArgumentOutOfRangeException.ThrowIfNegative)", () => {
    expect(() => formatNumber(format(CounterOnly), MARCH_2026, -1)).toThrow(RangeError);
  });

  it.each([0, 6, -1, 2.5])("refuses the undefined scheme %s (EnsureDefined)", (scheme) => {
    expect(() => formatNumber(format(scheme as NumberSequenceScheme), MARCH_2026, 1)).toThrow(RangeError);
    expect(() => periodOf(scheme as NumberSequenceScheme, MARCH_2026)).toThrow(RangeError);
  });
});

describe("periodOf – the counter period of a date", () => {
  it.each([
    [FourDigitYear, 2026, 0],
    [TwoDigitYear, 2026, 0],
    [FourDigitYearMonth, 2026, 3],
    [TwoDigitYearMonth, 2026, 3],
    [CounterOnly, 0, 0],
  ] as const)("scheme %i → (%i, %i)", (scheme, periodYear, periodMonth) => {
    expect(periodOf(scheme, MARCH_2026)).toEqual({ periodYear, periodMonth });
  });

  it("a two-digit-year scheme keeps the full year as its period (2026 and 1926 are different periods)", () => {
    expect(periodOf(TwoDigitYear, { year: 1926, month: 3 })).not.toEqual(periodOf(TwoDigitYear, MARCH_2026));
  });
});
