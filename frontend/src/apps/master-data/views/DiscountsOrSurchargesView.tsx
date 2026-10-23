import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { DiscountOrSurchargeEditor } from "@/apps/master-data/editors/DiscountOrSurchargeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  DIRECTIONS,
  SIDES,
  VALUE_KINDS,
  discountsOrSurcharges,
  formatDiscountOrSurchargeValue,
  keyOf,
  type DiscountOrSurchargeRow,
} from "@/domains/financial/reference-data";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 52: the Financial domain group of the Master data app, Pricing → Discounts and surcharges. The current value
 * is the one valid today, formatted by its kind. */
export function DiscountsOrSurchargesView() {
  const { t } = useL();
  const f = useFormat();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = discountsOrSurcharges.useGridFetcher(extra);
  const deleteOne = discountsOrSurcharges.useDelete();
  const bulk = discountsOrSurcharges.useBulkDelete();

  const columnDefs = useMemo<ColDef<DiscountOrSurchargeRow>[]>(() => {
    const label = (items: { value: number; key: string }[]) => (p: ValueFormatterParams<DiscountOrSurchargeRow>) => t(keyOf(items, p.value as number | undefined));
    return [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "direction", headerName: t("Financial:Direction"), flex: 1, minWidth: 110, valueFormatter: label(DIRECTIONS) },
      { field: "valueKind", headerName: t("Financial:ValueKind"), flex: 1, minWidth: 130, valueFormatter: label(VALUE_KINDS) },
      { field: "side", headerName: t("Financial:Side"), flex: 1, minWidth: 140, valueFormatter: label(SIDES) },
      {
        field: "currentValue",
        headerName: t("Financial:CurrentValue"),
        flex: 1,
        minWidth: 140,
        sortable: false,
        type: "rightAligned",
        cellClass: "tabular-nums",
        valueFormatter: (p: ValueFormatterParams<DiscountOrSurchargeRow>) =>
          formatDiscountOrSurchargeValue(f, t, p.data?.valueKind, p.value as number | null | undefined, p.data?.currentCurrencyCode),
      },
      { field: "regionCount", headerName: t("Financial:Regions"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ];
  }, [t, f]);

  return (
    <MasterDataListView<DiscountOrSurchargeRow>
      entityKey="DiscountOrSurcharge"
      resource="Financial"
      permission="Financial.DiscountsOrSurcharges"
      gridId="masterData.discountsOrSurcharges"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? row.code ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <DiscountOrSurchargeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
