import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { parseDateInput } from "@/lib/format";
import { numberError } from "@/lib/forms/use-form-state";
import type { EditorMode } from "@/apps/transport/orders/order-form";

/* Paste from Excel on the order page (design 61 "Order page, revised" 3): rows copied from a spreadsheet arrive as
 * tab-separated text, one row per line, the columns in the fast entry row's order (From, To, Date, Time, Transport
 * unit, Quantity, Weight, Reference; a template's Date column is the day offset). A date cell may carry the time
 * ("26-10-2026 08:00"); it is used when the Time cell is empty. Relations and units are matched on code or name
 * (exact, case-insensitive); a cell without exactly one match is marked in the preview and stays empty in the item. */

export const PASTE_COLUMNS = ["from", "to", "date", "time", "unit", "quantity", "weight", "reference"] as const;
export type PasteColumn = (typeof PASTE_COLUMNS)[number];

/** At most this many rows are taken from one paste (the order's 200 lines, design 56). */
export const MAX_PASTED_ROWS = 200;

export type PastedRow = Record<PasteColumn, string>;

/** Whether pasted text holds spreadsheet rows (a tab or a second line) rather than a value for one field. */
export function looksLikeRows(text: string): boolean {
  const trimmed = text.replace(/[\r\n]+$/, "");
  return trimmed.includes("\t") || /\r?\n/.test(trimmed);
}

/** The pasted text as rows of cells; empty lines are dropped, missing cells are empty. */
export function parsePastedRows(text: string): PastedRow[] {
  return text
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0)
    .slice(0, MAX_PASTED_ROWS)
    .map((line) => {
      const cells = line.split("\t").map((cell) => cell.trim().replace(/^"(.*)"$/, "$1").trim());
      return Object.fromEntries(PASTE_COLUMNS.map((column, index) => [column, cells[index] ?? ""])) as PastedRow;
    });
}

export type MatchStatus = "empty" | "matched" | "unmatched" | "ambiguous";

export interface Match<T> {
  text: string;
  value: T | null;
  status: MatchStatus;
}

/** Exactly one candidate whose code or name equals the text (case-insensitive) is a match; several are ambiguous. */
export function matchByCodeOrName<T extends { code?: string | null; name?: string | null }>(text: string, candidates: readonly T[]): Match<T> {
  const needle = text.trim().toLowerCase();
  if (!needle) return { text, value: null, status: "empty" };
  const hits = candidates.filter((c) => (c.code ?? "").trim().toLowerCase() === needle || (c.name ?? "").trim().toLowerCase() === needle);
  const unique = hits.filter((hit, index) => hits.indexOf(hit) === index);
  if (unique.length === 1) return { text, value: unique[0], status: "matched" };
  return { text, value: null, status: unique.length > 1 ? "ambiguous" : "unmatched" };
}

export interface ParsedValue {
  text: string;
  value: string | null;
  ok: boolean;
}

function isoDateOf(year: number, month: number, day: number): string | null {
  const check = new Date(year, month - 1, day);
  if (check.getFullYear() !== year || check.getMonth() !== month - 1 || check.getDate() !== day) return null;
  return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

/** A date cell: `YYYY-MM-DD` or the user's locale pattern (`26-10-2026`), optionally followed by a time. */
export function parseDateCell(text: string, locale: string): { date: ParsedValue; time: string | null } {
  const trimmed = text.trim();
  if (!trimmed) return { date: { text, value: null, ok: true }, time: null };
  const [datePart, timePart] = trimmed.split(/\s+/, 2);
  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(datePart);
  const value = iso ? isoDateOf(Number(iso[1]), Number(iso[2]), Number(iso[3])) : parseDateInput(datePart, locale);
  const time = timePart ? parseTimeOfDay(timePart.slice(0, 5)) : null;
  return { date: { text, value, ok: value != null }, time: time ? time.slice(0, 5) : null };
}

/** One pasted row checked cell by cell (relations and units matched by the caller, they need the server). */
export interface CheckedRow<TRelation, TUnit> {
  from: Match<TRelation>;
  to: Match<TRelation>;
  date: ParsedValue;
  dayOffset: ParsedValue;
  time: ParsedValue;
  unit: Match<TUnit>;
  quantity: ParsedValue;
  weight: ParsedValue;
  reference: string;
}

export function checkRow<TRelation, TUnit>(
  row: PastedRow,
  mode: EditorMode,
  locale: string,
  relationMatch: (text: string) => Match<TRelation>,
  unitMatch: (text: string) => Match<TUnit>,
): CheckedRow<TRelation, TUnit> {
  const dateCell = mode === "order" ? parseDateCell(row.date, locale) : { date: { text: row.date, value: null, ok: true }, time: null };
  const typedTime = row.time.trim() ? parseTimeOfDay(row.time) : null;
  const number = (text: string, integer = false): ParsedValue => ({ text, value: text.trim() || null, ok: !numberError(text, integer ? { integer: true } : { min: 0 }) });
  return {
    from: relationMatch(row.from),
    to: relationMatch(row.to),
    date: dateCell.date,
    dayOffset: mode === "template" ? number(row.date, true) : { text: "", value: null, ok: true },
    time: { text: row.time, value: typedTime ? typedTime.slice(0, 5) : dateCell.time, ok: !row.time.trim() || typedTime != null },
    unit: unitMatch(row.unit),
    quantity: number(row.quantity),
    weight: number(row.weight),
    reference: row.reference.slice(0, 128),
  };
}

/** The cells the preview marks: a typed relation or unit that matched nothing (or several), a bad date, time or number. */
export function problemCells(row: CheckedRow<unknown, unknown>): PasteColumn[] {
  const problems: PasteColumn[] = [];
  const unmatched = (m: Match<unknown>) => m.status === "unmatched" || m.status === "ambiguous";
  if (unmatched(row.from)) problems.push("from");
  if (unmatched(row.to)) problems.push("to");
  if (!row.date.ok || !row.dayOffset.ok) problems.push("date");
  if (!row.time.ok) problems.push("time");
  if (unmatched(row.unit)) problems.push("unit");
  if (!row.quantity.ok) problems.push("quantity");
  if (!row.weight.ok) problems.push("weight");
  return problems;
}
