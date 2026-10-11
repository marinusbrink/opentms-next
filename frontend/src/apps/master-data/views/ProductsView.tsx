import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { ProductEditor } from "@/apps/master-data/editors/ProductEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { products, type ProductRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function ProductsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = products.useGridFetcher(extra);
  const deleteOne = products.useDelete();
  const bulk = products.useBulkDelete();

  const columnDefs = useMemo<ColDef<ProductRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "isAdr", headerName: t("MasterData:IsAdr"), cellRenderer: BoolCell, flex: 1, minWidth: 80 },
      { field: "adrUnNumber", headerName: t("MasterData:AdrUnNumber"), flex: 1, minWidth: 110 },
      { field: "adrClass", headerName: t("MasterData:AdrClass"), flex: 1, minWidth: 90 },
      { field: "defaultWeightKg", headerName: t("MasterData:DefaultWeightKg"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110 },
      { field: "defaultLoadingMeters", headerName: t("MasterData:DefaultLoadingMeters"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 140 },
      { field: "defaultEpq", headerName: t("MasterData:DefaultEpq"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "adrPackingGroup", headerName: t("MasterData:AdrPackingGroup"), flex: 1, minWidth: 120, hide: true },
      { field: "defaultQuantity", headerName: t("MasterData:DefaultQuantity"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100, hide: true },
      { field: "defaultColli", headerName: t("MasterData:DefaultColli"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90, hide: true },
      { field: "defaultVolumeM3", headerName: t("MasterData:DefaultVolumeM3"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "defaultLengthCm", headerName: t("MasterData:DefaultLengthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "defaultWidthCm", headerName: t("MasterData:DefaultWidthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "defaultHeightCm", headerName: t("MasterData:DefaultHeightCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<ProductRow>
      entityKey="Product"
      permission="MasterData.Products"
      gridId="masterData.products"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <ProductEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
