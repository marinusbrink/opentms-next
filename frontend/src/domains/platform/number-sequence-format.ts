/* The frontend mirror of the backend's NumberSequenceFormatter (OpenTms.Platform.Domain.Shared/NumberSequences,
 * design 49): a pure function, used by the number-sequence editor for its live example. Keep both in step.
 * Number = Prefix + [YearPrefix + yyyy|yy + YearSuffix] + [MonthPrefix + MM + MonthSuffix] + counter padded to
 * MinimumDigits. Example: "O-" + "26" + "-" + "0001" = "O-26-0001". */

import type { components } from "@/lib/api/generated/schema";

export type NumberSequenceScheme = components["schemas"]["OpenTms.Platform.NumberSequences.NumberSequenceScheme"];

/** The scheme values as the backend enum defines them (OpenTMS EntityNumberCreationOption, same order). */
export const NumberSequenceSchemes = {
  FourDigitYear: 1,
  TwoDigitYear: 2,
  FourDigitYearMonth: 3,
  TwoDigitYearMonth: 4,
  CounterOnly: 5,
} as const satisfies Record<string, NumberSequenceScheme>;

/** PlatformMasterDataConsts.MinMinimumDigits / MaxMinimumDigits. */
export const MIN_MINIMUM_DIGITS = 1;
export const MAX_MINIMUM_DIGITS = 12;

/** PlatformMasterDataConsts.MaxAffixLength: prefix, year and month affixes. */
export const MAX_AFFIX_LENGTH = 16;

/** The period value stored for "no year" or "no month". */
export const NO_PERIOD = 0;

export interface NumberSequenceFormat {
  scheme: NumberSequenceScheme;
  prefix?: string | null;
  yearPrefix?: string | null;
  yearSuffix?: string | null;
  monthPrefix?: string | null;
  monthSuffix?: string | null;
  minimumDigits: number;
}

/** A calendar date as the record carries it: year and month (1–12). */
export interface PeriodDate {
  year: number;
  month: number;
}

const isDefined = (scheme: number): scheme is NumberSequenceScheme => scheme >= 1 && scheme <= 5 && Number.isInteger(scheme);

function ensureDefined(scheme: number): asserts scheme is NumberSequenceScheme {
  if (!isDefined(scheme)) {
    throw new RangeError(`Unknown number sequence scheme: ${scheme}`);
  }
}

export function usesYear(scheme: NumberSequenceScheme): boolean {
  return (
    scheme === NumberSequenceSchemes.FourDigitYear ||
    scheme === NumberSequenceSchemes.TwoDigitYear ||
    scheme === NumberSequenceSchemes.FourDigitYearMonth ||
    scheme === NumberSequenceSchemes.TwoDigitYearMonth
  );
}

export function usesMonth(scheme: NumberSequenceScheme): boolean {
  return scheme === NumberSequenceSchemes.FourDigitYearMonth || scheme === NumberSequenceSchemes.TwoDigitYearMonth;
}

export function usesTwoDigitYear(scheme: NumberSequenceScheme): boolean {
  return scheme === NumberSequenceSchemes.TwoDigitYear || scheme === NumberSequenceSchemes.TwoDigitYearMonth;
}

/** The counter period of a date: (year, 0) per year, (year, month) per month, (0, 0) never. */
export function periodOf(scheme: NumberSequenceScheme, date: PeriodDate): { periodYear: number; periodMonth: number } {
  ensureDefined(scheme);
  return {
    periodYear: usesYear(scheme) ? date.year : NO_PERIOD,
    periodMonth: usesMonth(scheme) ? date.month : NO_PERIOD,
  };
}

const pad = (value: number, length: number) => String(value).padStart(length, "0");

export function formatNumber(format: NumberSequenceFormat, date: PeriodDate, counter: number): string {
  ensureDefined(format.scheme);
  if (!Number.isInteger(counter) || counter < 0) {
    throw new RangeError(`The counter must be a non-negative integer: ${counter}`);
  }

  let number = format.prefix ?? "";

  if (usesYear(format.scheme)) {
    const year = usesTwoDigitYear(format.scheme) ? pad(date.year % 100, 2) : pad(date.year, 4);
    number += `${format.yearPrefix ?? ""}${year}${format.yearSuffix ?? ""}`;
  }

  if (usesMonth(format.scheme)) {
    number += `${format.monthPrefix ?? ""}${pad(date.month, 2)}${format.monthSuffix ?? ""}`;
  }

  const digits = Math.min(MAX_MINIMUM_DIGITS, Math.max(MIN_MINIMUM_DIGITS, Math.trunc(format.minimumDigits)));
  return number + pad(counter, digits);
}

/** Today's year and month in the user's calendar: the date the editor's example is drawn for (the backend uses its clock). */
export function today(): PeriodDate {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}
