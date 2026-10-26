import type { ICellRendererParams } from "ag-grid-community";
import { Check } from "lucide-react";

import { formatLicencePlate, useFormat } from "@/lib/format";

/* One cell renderer per data type (UI-GRID-004). Features pick the renderer by the
 * DTO field's type and add `type: "rightAligned"` for numbers and amounts; the
 * renderers own the formatting (UI-FMT-001) and the tabular figures (UI-TOK-009). */

type CellParams<T = unknown> = Pick<ICellRendererParams<unknown, T>, "value">;

export function NumberCell({ value }: CellParams<number | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.number(value)}</span>;
}

export function AmountCell({ value }: CellParams<number | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.amount(value)}</span>;
}

/** A unit price (UI-FMT-006: 2 to 5 decimals, no currency symbol), e.g. a cost price per kilometre. */
export function UnitPriceCell({ value }: CellParams<number | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.unitPrice(value)}</span>;
}

export function DateCell({ value }: CellParams<string | Date | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.date(value)}</span>;
}

export function DateTimeCell({ value }: CellParams<string | Date | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.dateTime(value)}</span>;
}

/** A byte count (sizes of databases, tables, blobs) in the largest fitting unit. */
export function BytesCell({ value }: CellParams<number | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.bytes(value)}</span>;
}

/** A share given in percent (0..100). */
export function PercentCell({ value }: CellParams<number | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.percent(value)}</span>;
}

/** Technical timestamps (UI-FMT-003): logs, audit trails, API messages; culture-independent. */
export function TimestampCell({ value }: CellParams<string | Date | null | undefined>) {
  const f = useFormat();
  return <span className="tabular-nums">{f.timestamp(value)}</span>;
}

/** A check icon for true, nothing for false (never the words true / false). */
export function BoolCell({ value }: CellParams<boolean | null | undefined>) {
  if (!value) return null;
  return (
    <span className="flex h-full items-center justify-center" data-slot="bool-cell">
      <Check className="size-4" aria-hidden="true" />
    </span>
  );
}

/** A licence plate column (design 48): formatted per the row's registration country (`registrationCountryCode`). */
export function LicencePlateCell({ value, data }: Pick<ICellRendererParams<{ registrationCountryCode?: string | null } | undefined, string | null | undefined>, "value" | "data">) {
  return <span>{formatLicencePlate(value, data?.registrationCountryCode)}</span>;
}
