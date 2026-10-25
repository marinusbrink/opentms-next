/* The numbering rule of design 50, mirrored from OpenTms.Platform.NumberSequences.NumberSequenceFormat so the editor's
 * preview matches what the server issues. Tested against the same table as NumberSequenceFormatTests. */

export interface NumberingValues {
  prefix?: string | null;
  includeYear?: boolean;
  includeMonth?: boolean;
  separator?: string | null;
  digits?: number;
}

export const NUMBERING_SEPARATORS = ["", "-", "/", "."] as const;
export const MIN_DIGITS = 3;
export const MAX_DIGITS = 10;
export const MAX_PREFIX_LENGTH = 16;

const PREFIX_SHAPE = /^[A-Za-z0-9_-]*$/;

/** The validation key of the first problem, or undefined when the numbering is valid. */
export function numberingError(values: NumberingValues): string | undefined {
  const prefix = values.prefix?.trim() ?? "";
  if (prefix.length > MAX_PREFIX_LENGTH || !PREFIX_SHAPE.test(prefix)) return "MasterData:Numbering.InvalidPrefix";
  if (values.includeMonth && !values.includeYear) return "MasterData:Numbering.MonthNeedsYear";
  const digits = values.digits ?? 0;
  if (!Number.isInteger(digits) || digits < MIN_DIGITS || digits > MAX_DIGITS) return "MasterData:Numbering.InvalidDigits";
  if (!(NUMBERING_SEPARATORS as readonly string[]).includes(values.separator ?? "")) return "MasterData:Numbering.InvalidSeparator";
  return undefined;
}

/** "TO-2026-10-00042" for counter 42 on 22 October 2026 with prefix TO, year, month, "-" and five digits. */
export function formatNumber(values: NumberingValues, counter: number, date: Date): string {
  const parts: string[] = [];
  const prefix = values.prefix?.trim().toUpperCase();
  if (prefix) parts.push(prefix);
  if (values.includeYear) parts.push(String(date.getFullYear()).padStart(4, "0"));
  if (values.includeYear && values.includeMonth) parts.push(String(date.getMonth() + 1).padStart(2, "0"));
  parts.push(String(counter).padStart(values.digits ?? 5, "0"));
  return parts.join(values.separator ?? "");
}
