import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { TransportUnitEditor } from "@/apps/master-data/editors/TransportUnitEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { transportUnits, type TransportUnitRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function TransportUnitsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = transportUnits.useGridFetcher(extra);
  const deleteOne = transportUnits.useDelete();
  const bulk = transportUnits.useBulkDelete();

  const columnDefs = useMemo<ColDef<TransportUnitRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "sutcCode", headerName: t("MasterData:SutcCode"), flex: 1, minWidth: 100 },
      { field: "lengthCm", headerName: t("MasterData:LengthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "widthCm", headerName: t("MasterData:WidthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "heightCm", headerName: t("MasterData:HeightCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "weightKg", headerName: t("MasterData:WeightKg"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "loadingMeters", headerName: t("MasterData:LoadingMeters"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 140 },
      { field: "epqFactor", headerName: t("MasterData:EpqFactor"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "isExchangeable", headerName: t("MasterData:IsExchangeable"), cellRenderer: BoolCell, flex: 1, minWidth: 110 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<TransportUnitRow>
      entityKey="TransportUnit"
      permission="MasterData.TransportUnits"
      gridId="masterData.transportUnits"
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
        <TransportUnitEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
