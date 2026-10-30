import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { PencilLine } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { Skeleton } from "@/components/ui/skeleton";
import {
  changedKeys,
  detailKey,
  detailReference,
  detailRows,
  hasInvalidValue,
  withRows,
  type DetailEdits,
  type DetailLineRow,
  type DetailSource,
} from "@/apps/transport/wizards/materialise-details";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The per-order part of the wizard's Details step (design 61): the orders being created (date × copy) as a list on
 * the left, with a mark on the ones whose details differ from the source; on the right the chosen order's customer
 * reference and its lines in a compact LinesGrid (UI-GRID-014) with the quantity and the weight editable, prefilled
 * from the source. Lines are neither added nor removed here: the order takes the source's lines. */

export interface MaterialiseDetailsProps {
  dates: string[];
  copies: number;
  /** The source's reference and lines; undefined while it loads. */
  source: DetailSource | undefined;
  edits: DetailEdits;
  onEditsChange: (edits: DetailEdits) => void;
}

export function MaterialiseDetails({ dates, copies, source, edits, onEditsChange }: MaterialiseDetailsProps) {
  const { t } = useL();
  const f = useFormat();
  const orders = useMemo(() => dates.flatMap((date) => Array.from({ length: copies }, (_, copyIndex) => ({ key: detailKey(date, copyIndex), date, copyIndex }))), [dates, copies]);
  const [chosen, setChosen] = useState<string | null>(null);
  const current = orders.find((o) => o.key === chosen) ?? orders[0];
  const changed = useMemo(() => new Set(source ? changedKeys(dates, copies, source, edits) : []), [dates, copies, source, edits]);

  const columns = useMemo<ColDef<DetailLineRow>[]>(
    () => [
      { field: "position", headerName: t("Orders:Position"), width: 90, minWidth: 80, type: "rightAligned" },
      { field: "label", headerName: t("Orders:Line"), flex: 1, minWidth: 160 },
      { field: "quantity", headerName: t("Orders:Quantity"), width: 130, minWidth: 110, editable: true, type: "rightAligned" },
      { field: "weightKg", headerName: t("Orders:WeightKg"), width: 140, minWidth: 110, editable: true, type: "rightAligned" },
    ],
    [t],
  );

  if (!source) return <Skeleton className="h-48 w-full" />;
  if (!current) return null;
  const edit = edits[current.key];
  const orderLabel = (o: { date: string; copyIndex: number }) => `${f.date(o.date)} · ${t("Orders:CopyN").replace("{0}", String(o.copyIndex + 1))}`;

  return (
    <FormSection title={t("Orders:DetailsPerOrder")}>
      <div className="grid grid-cols-[16rem_minmax(0,1fr)] gap-4" data-slot="materialise-details">
        <ul className="max-h-72 overflow-y-auto rounded-md border border-border-section" aria-label={t("Orders:OrdersToCreate")}>
          {orders.map((o) => (
            <li key={o.key}>
              <button
                type="button"
                data-key={o.key}
                aria-pressed={o.key === current.key}
                onClick={() => setChosen(o.key)}
                className={cn("flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-sm tabular-nums hover:bg-muted", o.key === current.key && "bg-muted font-medium")}
              >
                <span>{orderLabel(o)}</span>
                {changed.has(o.key) && (
                  <span data-slot="detail-changed" className="inline-flex items-center text-muted-foreground">
                    <PencilLine className="size-3.5" aria-hidden="true" />
                    <span className="sr-only">{t("Orders:DetailsChanged")}</span>
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>
        <div className="flex min-w-0 flex-col gap-3" data-slot="materialise-detail" data-key={current.key}>
          <p className="text-label font-medium text-muted-foreground">{orderLabel(current)}</p>
          <FormField label={t("Orders:CustomerReference")} htmlFor="materialise-detail-reference" floating="always" className="max-w-md">
            <Input
              id="materialise-detail-reference"
              value={detailReference(source, edit)}
              maxLength={128}
              onChange={(e) => onEditsChange({ ...edits, [current.key]: { ...edit, customerReference: e.target.value } })}
            />
          </FormField>
          <div className="h-56">
            <LazyLinesGrid<DetailLineRow>
              rows={detailRows(source, edit)}
              onRowsChange={(rows) => onEditsChange({ ...edits, [current.key]: withRows(edit, rows) })}
              getRowId={(row) => row.id}
              columnDefs={columns}
              canRemoveRow={() => false}
              emptyText={t("Orders:NoLines")}
            />
          </div>
          {hasInvalidValue(edit) && (
            <Alert variant="destructive">
              <AlertDescription>{t("Orders:DetailsInvalidNumber")}</AlertDescription>
            </Alert>
          )}
        </div>
      </div>
      <p className="mt-3 text-label text-muted-foreground">{t("Orders:MaterialiseDetailsHint")}</p>
    </FormSection>
  );
}
