import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { PriceListEditor } from "@/apps/master-data/editors/PriceListEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { QUANTITY_SOURCES, RATE_SIDES, keyOf, priceLists, type PriceListRow, type QuantitySource, type RateSide } from "@/domains/financial/price-lists";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 57 §UI): the Financial domain group of the Master data app, Pricing → Price lists. Columns: code,
 * name, side, currency, basis, valid from/till, rules, archived. The side filter narrows to revenue or cost lists; the
 * rule count is not sortable (design 57 build note 4). A list in use is archived, not deleted (the server refuses). */
export function PriceListsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [side, setSide] = useState<RateSide | null>(null);
  const extra = useMemo(() => ({ includeArchived: showArchived, side: side ?? undefined }), [showArchived, side]);
  const fetchRows = priceLists.useGridFetcher(extra);
  const deleteOne = priceLists.useDelete();
  const bulk = priceLists.useBulkDelete();

  const sideItems = useMemo(() => [{ value: null as RateSide | null, label: t("Financial:AllSides") }, ...RATE_SIDES.map((s) => ({ value: s.value as RateSide | null, label: t(s.key) }))], [t]);

  const columnDefs = useMemo<ColDef<PriceListRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "side", headerName: t("Financial:RateSide"), flex: 1, minWidth: 100, valueFormatter: (p: ValueFormatterParams<PriceListRow>) => t(keyOf(RATE_SIDES, p.value as RateSide | undefined)) },
      { field: "currencyCode", headerName: t("Financial:Currency"), flex: 1, minWidth: 90 },
      { field: "basis", headerName: t("Financial:Basis"), flex: 1, minWidth: 130, valueFormatter: (p: ValueFormatterParams<PriceListRow>) => t(keyOf(QUANTITY_SOURCES, p.value as QuantitySource | undefined)) },
      { field: "validFrom", headerName: t("Financial:ValidFrom"), cellRenderer: DateCell, flex: 1, minWidth: 120 },
      { field: "validTill", headerName: t("Financial:ValidTill"), cellRenderer: DateCell, flex: 1, minWidth: 120 },
      { field: "ruleCount", headerName: t("Financial:RuleCount"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90, sortable: false },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<PriceListRow>
      entityKey="PriceList"
      resource="Financial"
      permission="Financial.PriceLists"
      gridId="masterData.priceLists"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? row.code ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <>
          <div className="w-44">
            <Select items={sideItems} value={side} onValueChange={(next) => setSide((next as RateSide | null) ?? null)}>
              <SelectTrigger aria-label={t("Financial:RateSide")} className="h-9 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {sideItems.map((item) => (
                  <SelectItem key={item.value ?? "all"} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
            {t("MasterData:ShowArchived")}
          </label>
        </>
      }
      renderEditor={(state, onClose, onSaved, openRecord) => (
        <PriceListEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} onOpenRecord={openRecord} />
      )}
    />
  );
}
