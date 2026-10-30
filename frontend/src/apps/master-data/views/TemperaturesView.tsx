import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { TemperatureEditor } from "@/domains/master-data/components/TemperatureEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { temperatures, type TemperatureRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function TemperaturesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = temperatures.useGridFetcher(extra);
  const deleteOne = temperatures.useDelete();
  const bulk = temperatures.useBulkDelete();

  const columnDefs = useMemo<ColDef<TemperatureRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "minCelsius", headerName: t("MasterData:MinCelsius"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "maxCelsius", headerName: t("MasterData:MaxCelsius"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<TemperatureRow>
      entityKey="Temperature"
      permission="MasterData.Temperatures"
      gridId="masterData.temperatures"
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
        <TemperatureEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
