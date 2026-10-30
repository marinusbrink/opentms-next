import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { TransportCharacteristicsEditor } from "@/domains/master-data/components/TransportCharacteristicsEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { transportCharacteristics, type TransportCharacteristicsRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function TransportCharacteristicsView() {
  const { t } = useL();
  const fetchRows = transportCharacteristics.useGridFetcher();
  const deleteOne = transportCharacteristics.useDelete();
  const bulk = transportCharacteristics.useBulkDelete();

  const columnDefs = useMemo<ColDef<TransportCharacteristicsRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 120 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "description", headerName: t("MasterData:Description"), flex: 3, minWidth: 200 },
    ],
    [t],
  );

  return (
    <MasterDataListView<TransportCharacteristicsRow>
      entityKey="TransportCharacteristics"
      permission="MasterData.TransportCharacteristics"
      gridId="masterData.transportCharacteristics"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.code ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      renderEditor={(state, onClose, onSaved) => (
        <TransportCharacteristicsEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
