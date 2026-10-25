import { newLineKey } from "@/apps/master-data/components/validity-rows";
import {
  MAX_PRICE_LIST_BRACKETS,
  MAX_PRICE_LIST_COLUMNS,
  type PriceListBracketDto,
  type PriceListBracketInputDto,
  type PriceListColumnDto,
} from "@/domains/financial/price-lists";
import type { Formatter } from "@/lib/format";

/* The editor side of a price list's matrix (design 57): the columns are the horizontal brackets of the list (each an
 * inclusive upper bound, the last one open), a rule's brackets the vertical ones (the same shape, graded on the rule's
 * row quantity), and a cell the unit price of one bracket in one column. Pure helpers: the DTO shapes in and out and
 * the client-side checks the server repeats (ColumnsNotAscending, BracketsNotAscending, OpenRowRequired). */

export interface ColumnLine {
  key: string;
  /** Inclusive upper bound; null = the open (last) column. */
  upperBound: number | null;
}

export interface BracketLine {
  key: string;
  /** Inclusive upper bound; null = the open (last) row. */
  upperBound: number | null;
  /** One unit price per column, in column order; null = no price in that cell. */
  unitPrices: (number | null)[];
}

/** The open row or column, always present and always last. */
export const openLine = (): ColumnLine => ({ key: newLineKey(), upperBound: null });

export function columnsFromDto(columns: PriceListColumnDto[] | null | undefined): ColumnLine[] {
  const sorted = [...(columns ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const lines = sorted.map((c) => ({ key: newLineKey(), upperBound: c.upperBound ?? null }));
  return lines.length === 0 || lines[lines.length - 1].upperBound != null ? [...lines.filter((l) => l.upperBound != null), openLine()] : lines;
}

/** The upper bounds in order, the last null (PriceListCreateDto.Columns). */
export function columnsToDto(lines: ColumnLine[]): (number | null)[] {
  return lines.map((l) => l.upperBound);
}

/**
 * Strictly ascending bounds with the open one last and only there: undefined, else the position (1-based) of the
 * first offending line. Empty bounds before the open line count as offending too.
 */
export function boundsProblem(bounds: (number | null)[]): number | undefined {
  for (let i = 0; i < bounds.length; i++) {
    const bound = bounds[i];
    const last = i === bounds.length - 1;
    if (last) {
      if (bound != null) return i + 1;
      continue;
    }
    if (bound == null || !Number.isFinite(bound) || bound <= 0) return i + 1;
    if (i > 0 && bounds[i - 1] != null && bound <= bounds[i - 1]!) return i + 1;
  }
  return bounds.length === 0 ? 1 : undefined;
}

export function columnsError(lines: ColumnLine[]): string | undefined {
  if (lines.length > MAX_PRICE_LIST_COLUMNS) return "Financial:TooManyColumnsHint";
  return boundsProblem(columnsToDto(lines)) == null ? undefined : "Financial:ColumnsNotAscendingHint";
}

/** At most four decimals (numeric(18,4)). */
const fourDecimals = (value: number) => Math.abs(Math.round(value * 10000) - value * 10000) < 1e-6;

export function bracketsFromDto(brackets: PriceListBracketDto[] | null | undefined, columnCount: number): BracketLine[] {
  const sorted = [...(brackets ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const lines = sorted.map((b) => ({ key: newLineKey(), upperBound: b.upperBound ?? null, unitPrices: fitPrices(b.unitPrices ?? [], columnCount) }));
  return lines.length === 0 ? [newBracket(columnCount, null)] : lines;
}

export const newBracket = (columnCount: number, upperBound: number | null): BracketLine => ({ key: newLineKey(), upperBound, unitPrices: Array.from({ length: columnCount }, () => null) });

/** Pads or cuts a bracket's prices to the list's column count (a column added after the rule was saved). */
export function fitPrices(prices: (number | null)[], columnCount: number): (number | null)[] {
  return Array.from({ length: columnCount }, (_, i) => prices[i] ?? null);
}

export function bracketsToDto(lines: BracketLine[], columnCount: number): PriceListBracketInputDto[] {
  return lines.map((l) => ({ upperBound: l.upperBound, unitPrices: fitPrices(l.unitPrices, columnCount) }));
}

export function bracketsError(lines: BracketLine[]): string | undefined {
  if (lines.length === 0) return "Financial:OpenRowRequiredHint";
  if (lines.length > MAX_PRICE_LIST_BRACKETS) return "Financial:TooManyBracketsHint";
  if (boundsProblem(lines.map((l) => l.upperBound)) != null) return "Financial:BracketsNotAscendingHint";
  if (lines.some((l) => l.unitPrices.some((p) => p != null && (!Number.isFinite(p) || p < 0 || !fourDecimals(p))))) return "Financial:UnitPriceRangeHint";
  return undefined;
}

/** "≤ 1,000" for a bounded column, the "Above" text for the open one (the header of a ladder's price column). */
export function columnHeader(f: Formatter, t: (key: string) => string, column: ColumnLine): string {
  return column.upperBound == null ? t("Financial:Open") : `≤ ${f.number(column.upperBound, { maxDecimals: 4 })}`;
}

/** The index of the price column a bracket line stores for a ladder column (`unitPrices[i]`), as a grid field name. */
export const priceField = (index: number) => `price${index}`;

/** A bracket line flattened for the grid: `price0..n` next to the bound. */
export type BracketRow = BracketLine & Record<string, unknown>;

export function toBracketRow(line: BracketLine): BracketRow {
  const row: BracketRow = { ...line };
  line.unitPrices.forEach((price, i) => {
    row[priceField(i)] = price;
  });
  return row;
}

export function fromBracketRow(row: BracketRow, columnCount: number): BracketLine {
  return {
    key: row.key,
    upperBound: row.upperBound == null || row.upperBound === ("" as unknown) ? null : Number(row.upperBound),
    unitPrices: Array.from({ length: columnCount }, (_, i) => {
      const value = row[priceField(i)];
      return value == null || value === "" ? null : Number(value);
    }),
  };
}
