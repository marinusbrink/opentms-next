import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { NumberCell } from "@/components/ui/grid-cells";
import { VehicleTypeEditor } from "@/apps/master-data/editors/VehicleTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { vehicleTypeKindKey, vehicleTypes, type VehicleTypeRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function VehicleTypesView() {
  const { t } = useL();
  const fetchRows = vehicleTypes.useGridFetcher();
  const deleteOne = vehicleTypes.useDelete();
  const bulk = vehicleTypes.useBulkDelete();

  const columnDefs = useMemo<ColDef<VehicleTypeRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "kind", headerName: t("MasterData:Kind"), flex: 1, minWidth: 120, valueFormatter: (p) => t(vehicleTypeKindKey(p.value)) },
      { field: "maxEpq", headerName: t("MasterData:MaxEpq"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "maxLoadingMeters", headerName: t("MasterData:MaxLoadingMeters"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 150 },
      { field: "defaultGrossVehicleWeightKg", headerName: t("MasterData:DefaultGrossVehicleWeightKg"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 130 },
      { field: "defaultLengthCm", headerName: t("MasterData:DefaultLengthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "defaultWidthCm", headerName: t("MasterData:DefaultWidthCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "defaultHeightCm", headerName: t("MasterData:DefaultHeightCm"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<VehicleTypeRow>
      entityKey="VehicleType"
      permission="MasterData.VehicleTypes"
      gridId="masterData.vehicleTypes"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      renderEditor={(state, onClose, onSaved) => (
        <VehicleTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
