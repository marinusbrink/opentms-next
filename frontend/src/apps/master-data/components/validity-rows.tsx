import type { ICellRendererParams } from "ag-grid-community";
import { Lock } from "lucide-react";

import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The validity rows of the Financial master data (design 52, §3.4): a VAT category's rates and a discount or
 * surcharge's values. A row valid from today or earlier has taken effect and is fixed: read-only in the LinesGrid with
 * a lock icon and a tooltip, never removable; only future rows are edited. The server checks the same rule
 * (Financial:FixedValidityRowChanged). Shared by the VatPercentage and DiscountOrSurcharge editors. */

export interface ValidityLine {
  key: string;
  validFrom: string | null;
  /** Set from the server's `isFixed`: the row has taken effect. A new row is never fixed. */
  isFixed: boolean;
}

let lineCounter = 0;
export const newLineKey = () => `new-${++lineCounter}`;

/** Today as a calendar date (yyyy-MM-dd) in the browser's zone, the zone the user enters dates in. */
export function todayIso(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** The DatePicker cell editor may hand back a Date or an ISO string; the API takes a date-only string. */
export function dateOnly(value: string | Date | null | undefined): string | null {
  if (value == null || value === "") return null;
  if (value instanceof Date) return todayIso(value);
  return value.slice(0, 10);
}

/**
 * The validity rule on the lines (the localization key of the first problem, or undefined): at least one row, each
 * with a date, each date once, and on an existing record every row that is not fixed must start after today
 * (a new row dated today or earlier would change history).
 */
export function validityError(lines: ValidityLine[], mode: "create" | "edit", today: string = todayIso()): string | undefined {
  if (lines.length === 0) return "Financial:ValidityRowRequiredHint";
  if (lines.some((l) => !dateOnly(l.validFrom))) return "MasterData:Required";
  const dates = lines.map((l) => dateOnly(l.validFrom));
  if (new Set(dates).size !== dates.length) return "Financial:DuplicateValidFromHint";
  if (mode === "edit" && lines.some((l) => !l.isFixed && (dateOnly(l.validFrom) ?? "") <= today)) return "Financial:FutureDateRequiredHint";
  return undefined;
}

/** Lines in date order, the way the editor shows and sends them. */
export const byDate = <T extends ValidityLine>(lines: T[]) => [...lines].sort((a, b) => (dateOnly(a.validFrom) ?? "").localeCompare(dateOnly(b.validFrom) ?? ""));

/** The Valid from cell: the date, with a lock and its tooltip on a fixed row. */
export function ValidFromCell({ value, data, tooltipKey }: Pick<ICellRendererParams<ValidityLine, string | null>, "value" | "data"> & { tooltipKey: string }) {
  const { t } = useL();
  const f = useFormat();
  const tooltip = t(tooltipKey);
  return (
    <span className="inline-flex items-center gap-1.5 tabular-nums" title={data?.isFixed ? tooltip : undefined}>
      {data?.isFixed && <Lock className="size-3.5 text-muted-foreground" aria-label={tooltip} role="img" data-fixed-row />}
      <span>{f.date(value)}</span>
    </span>
  );
}
