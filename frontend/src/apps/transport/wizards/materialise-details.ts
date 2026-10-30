import type { MaterialisationOverrideDto } from "@/domains/orders/templates";
import type { TemplateDto } from "@/domains/orders/templates";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { numberError, numberToText, parseNumber } from "@/lib/forms/use-form-state";

/* The "Details" step of New from template / Copy order (design 61; design 58 build note 19): per generated order
 * (date × copy) the customer reference and per line the quantity and weight, prefilled from the source (the
 * template's lines, or the copied order's). The user's edits are kept per order; only the values that differ from
 * the source are sent as `overrides[]` (null keeps the source's value on the server), so an untouched order sends
 * nothing. An emptied quantity or weight keeps the source's value; an emptied customer reference clears it. */

export interface DetailSourceLine {
  id: string;
  position: number;
  label: string;
  quantity: number | null;
  weightKg: number | null;
}

export interface DetailSource {
  customerReference: string;
  lines: DetailSourceLine[];
}

export interface LineEdit {
  quantity?: string;
  weightKg?: string;
}

export interface OrderEdit {
  customerReference?: string;
  lines?: Record<string, LineEdit>;
}

/** The edits per generated order, keyed by `detailKey(date, copyIndex)`. */
export type DetailEdits = Record<string, OrderEdit>;

/** The details' source: the template's or the copied order's reference and lines (in position order; a line
 * without an id cannot be named in an override and is left out). */
export function detailSourceOf(record: Pick<TemplateDto, "customerReference" | "lines"> | Pick<TransportOrderDto, "customerReference" | "lines"> | undefined): DetailSource | undefined {
  if (!record) return undefined;
  const lines = [...(record.lines ?? [])]
    .filter((line) => line.id != null)
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
    .map((line, index) => ({
      id: line.id!,
      position: line.position ?? index + 1,
      label: [line.transportUnitName, line.productName].filter(Boolean).join(" · "),
      quantity: line.quantity ?? null,
      weightKg: line.weightKg ?? null,
    }));
  return { customerReference: record.customerReference ?? "", lines };
}

/** The key of one generated order; the copy index is 0-based, as the API takes it. */
export const detailKey = (date: string, copyIndex: number) => `${date}|${copyIndex}`;

/** The row a generated order's lines grid edits: the source line with the shown quantity and weight as text. */
export interface DetailLineRow {
  id: string;
  position: number;
  label: string;
  quantity: string;
  weightKg: string;
}

export function detailRows(source: DetailSource, edit: OrderEdit | undefined): DetailLineRow[] {
  return source.lines.map((line) => ({
    id: line.id,
    position: line.position,
    label: line.label,
    quantity: edit?.lines?.[line.id]?.quantity ?? numberToText(line.quantity),
    weightKg: edit?.lines?.[line.id]?.weightKg ?? numberToText(line.weightKg),
  }));
}

/** The shown customer reference of a generated order. */
export const detailReference = (source: DetailSource, edit: OrderEdit | undefined) => edit?.customerReference ?? source.customerReference;

/** The edit after the grid changed a generated order's rows (the text of each line as shown). */
export function withRows(edit: OrderEdit | undefined, rows: DetailLineRow[]): OrderEdit {
  const lines: Record<string, LineEdit> = { ...(edit?.lines ?? {}) };
  for (const row of rows) lines[row.id] = { quantity: row.quantity, weightKg: row.weightKg };
  return { ...edit, lines };
}

const changedNumber = (text: string | undefined, source: number | null): number | null => {
  if (text == null) return null;
  const n = parseNumber(text);
  if (n == null || Number.isNaN(n)) return null;
  return n === source ? null : n;
};

/** The override of one generated order, or null when nothing differs from the source. */
export function overrideOf(date: string, copyIndex: number, source: DetailSource, edit: OrderEdit | undefined): MaterialisationOverrideDto | null {
  if (!edit) return null;
  const reference = edit.customerReference != null && edit.customerReference.trim() !== source.customerReference.trim() ? edit.customerReference.trim() : null;
  const lines = source.lines
    .map((line) => {
      const quantity = changedNumber(edit.lines?.[line.id]?.quantity, line.quantity);
      const weightKg = changedNumber(edit.lines?.[line.id]?.weightKg, line.weightKg);
      return quantity == null && weightKg == null ? null : { lineId: line.id, quantity, weightKg };
    })
    .filter((line): line is NonNullable<typeof line> => line != null);
  if (reference == null && lines.length === 0) return null;
  return { date, copyIndex, customerReference: reference, lines };
}

/** The overrides of one date's orders (the wizard runs one call per date). */
export function overridesFor(date: string, copies: number, source: DetailSource, edits: DetailEdits): MaterialisationOverrideDto[] {
  const result: MaterialisationOverrideDto[] = [];
  for (let copyIndex = 0; copyIndex < copies; copyIndex++) {
    const override = overrideOf(date, copyIndex, source, edits[detailKey(date, copyIndex)]);
    if (override) result.push(override);
  }
  return result;
}

/** True when a generated order's edit holds a value that is no number of at least 0. */
export function hasInvalidValue(edit: OrderEdit | undefined): boolean {
  return Object.values(edit?.lines ?? {}).some((line) => numberError(line.quantity, { min: 0 }) != null || numberError(line.weightKg, { min: 0 }) != null);
}

/** The keys of the orders being created (dates × copies) whose details differ from the source. */
export function changedKeys(dates: string[], copies: number, source: DetailSource, edits: DetailEdits): string[] {
  return dates.flatMap((date) => Array.from({ length: copies }, (_, copyIndex) => detailKey(date, copyIndex))).filter((key) => {
    const [date, index] = key.split("|");
    return overrideOf(date, Number(index), source, edits[key]) != null;
  });
}
