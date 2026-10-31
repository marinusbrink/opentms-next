import { toIsoDate } from "@/lib/format";

/* Date ranges of the Planboard (design 70): ISO calendar dates (`YYYY-MM-DD`), both ends inclusive, no time or
 * timezone. The board range is at most 92 days and defaults to today … today + 6. */

export interface PlanboardRange {
  /** First day, `YYYY-MM-DD`. */
  from: string;
  /** Last day (inclusive), `YYYY-MM-DD`. */
  till: string;
}

export const MAX_BOARD_RANGE_DAYS = 92;
export const DEFAULT_BOARD_RANGE_DAYS = 7;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/** `YYYY-MM-DD` → a local Date at midnight, or null when the text is not a real calendar date. */
export function parseIsoDate(value: unknown): Date | null {
  if (typeof value !== "string" || !ISO_DATE.test(value)) return null;
  const [y, m, d] = value.split("-").map(Number);
  const date = new Date(y, m - 1, d);
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? date : null;
}

export function addDays(isoDate: string, days: number): string {
  const date = parseIsoDate(isoDate);
  if (!date) return isoDate;
  date.setDate(date.getDate() + days);
  return toIsoDate(date);
}

/** Number of days the range covers, both ends included (a single day is 1). */
export function rangeDays(range: PlanboardRange): number {
  const from = parseIsoDate(range.from);
  const till = parseIsoDate(range.till);
  if (!from || !till) return 0;
  return Math.round((till.getTime() - from.getTime()) / 86_400_000) + 1;
}

export function defaultBoardRange(today: Date = new Date()): PlanboardRange {
  const from = toIsoDate(today);
  return { from, till: addDays(from, DEFAULT_BOARD_RANGE_DAYS - 1) };
}

export function isValidRange(value: unknown, maxDays = MAX_BOARD_RANGE_DAYS): value is PlanboardRange {
  if (value == null || typeof value !== "object") return false;
  const range = value as Partial<PlanboardRange>;
  if (!parseIsoDate(range.from) || !parseIsoDate(range.till)) return false;
  const days = rangeDays(range as PlanboardRange);
  return days >= 1 && days <= maxDays;
}

/**
 * Makes a range valid after the user changed one end: `till` before `from` moves `till` to `from`; a span longer than
 * `maxDays` is cut back from the end the user did not touch (`changed`).
 */
export function clampRange(range: PlanboardRange, maxDays = MAX_BOARD_RANGE_DAYS, changed: "from" | "till" = "from"): PlanboardRange {
  let { from, till } = range;
  if (!parseIsoDate(from)) from = parseIsoDate(till) ? till : defaultBoardRange().from;
  if (!parseIsoDate(till)) till = from;
  if (till < from) {
    if (changed === "till") from = till;
    else till = from;
  }
  if (rangeDays({ from, till }) > maxDays) {
    if (changed === "till") from = addDays(till, -(maxDays - 1));
    else till = addDays(from, maxDays - 1);
  }
  return { from, till };
}

/** Whether a record's dates (either end may be unknown) overlap the range; a record without dates overlaps. */
export function overlapsRange(range: PlanboardRange, from: string | null | undefined, till: string | null | undefined): boolean {
  const start = from ?? till;
  const end = till ?? from;
  if (!start || !end) return true;
  return start.slice(0, 10) <= range.till && end.slice(0, 10) >= range.from;
}
