import type { PlanboardBlockDto } from "@/domains/planning-execution/planboard-scheduler";
import { WEEKDAYS, type ResourceWorkingPatternDto, type ResourceWorkingDayDto } from "@/domains/planning-execution/resource-availability";

/* The pure part of the Resources pane (design 70 "Resources"): the geometry of the compact availability strip, the
 * date + time fields of the unavailability dialog and the weekly working-pattern form. Times are local (the browser's
 * zone, like the rest of the Planboard until D-24 is decided). */

/** A stretch of the strip in percent of the window. */
export interface StripSegment {
  left: number;
  width: number;
}

/** Where an interval falls in the window, clipped; null when it lies outside. */
export function stripSegment(windowStart: number, windowEnd: number, from: string | number | Date, till: string | number | Date): StripSegment | null {
  const span = windowEnd - windowStart;
  if (span <= 0) return null;
  const s = Math.max(windowStart, new Date(from).getTime());
  const e = Math.min(windowEnd, new Date(till).getTime());
  if (Number.isNaN(s) || Number.isNaN(e) || e <= s) return null;
  return { left: ((s - windowStart) / span) * 100, width: Math.max(0.5, ((e - s) / span) * 100) };
}

/** Where a moment falls in the window in percent; null outside. */
export function stripPosition(windowStart: number, windowEnd: number, at: string | number | Date | null | undefined): number | null {
  if (at == null) return null;
  const time = new Date(at).getTime();
  if (Number.isNaN(time) || time < windowStart || time > windowEnd) return null;
  return ((time - windowStart) / (windowEnd - windowStart)) * 100;
}

export const isUnavailabilityBlock = (block: PlanboardBlockDto) => !!block.unavailabilityId;

const TIME = /^(\d{1,2}):?(\d{2})$/;

/** `H:mm`, `HHmm` or `HH:mm` → `HH:mm`; null when it is not a time of day. */
export function parseTime(text: string): string | null {
  const match = TIME.exec(text.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) return null;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}

const pad = (n: number) => String(n).padStart(2, "0");

/** An ISO instant → the local date (`YYYY-MM-DD`) and time (`HH:mm`). */
export function splitLocal(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return { date: null, time: "" };
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

/** A local date and time → an ISO instant; null when the date or the time is missing or invalid. */
export function joinLocal(date: string | null, time: string): string | null {
  if (!date) return null;
  const parsed = parseTime(time || "00:00");
  if (!parsed) return null;
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = parsed.split(":").map(Number);
  const result = new Date(y, m - 1, d, hh, mm);
  return Number.isNaN(result.getTime()) ? null : result.toISOString();
}

/** One weekday of the pattern form: off, or working from `start` to `end` (`HH:mm`). */
export interface PatternDay {
  weekday: number;
  works: boolean;
  start: string;
  end: string;
}

const shortTime = (value: string | null | undefined) => (value ? value.slice(0, 5) : "");

/** The pattern rows of a resource as the seven form rows (Monday first). */
export function patternToForm(patterns: readonly ResourceWorkingPatternDto[]): PatternDay[] {
  return WEEKDAYS.map((weekday) => {
    const row = patterns.find((p) => p.weekday === weekday);
    return row ? { weekday, works: true, start: shortTime(row.start), end: shortTime(row.end) } : { weekday, works: false, start: "", end: "" };
  });
}

/** The weekdays whose times are missing, invalid, or end before they start. */
export function patternErrors(days: readonly PatternDay[]): Set<number> {
  const errors = new Set<number>();
  for (const day of days) {
    if (!day.works) continue;
    const start = parseTime(day.start);
    const end = parseTime(day.end);
    if (!start || !end || end <= start) errors.add(day.weekday);
  }
  return errors;
}

/** The working days as the API takes them (`HH:mm:ss`); off days are left out. */
export function formToPattern(days: readonly PatternDay[]): ResourceWorkingDayDto[] {
  return days
    .filter((d) => d.works)
    .map((d) => ({ weekday: d.weekday, start: `${parseTime(d.start)}:00`, end: `${parseTime(d.end)}:00` }));
}
