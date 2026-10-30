import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { TripAttributeDefinitionEditor } from "@/apps/master-data/editors/TripAttributeDefinitionEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { tripAttributeDefinitions, type TripAttributeDefinitionRow } from "@/domains/planning-execution/trip-attribute-definitions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 59): the Planning & Execution domain group of the Master data app, Trips → Trip attributes. */
export function TripAttributeDefinitionsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = tripAttributeDefinitions.useGridFetcher(extra);
  const deleteOne = tripAttributeDefinitions.useDelete();
  const bulk = tripAttributeDefinitions.useBulkDelete();

  const columnDefs = useMemo<ColDef<TripAttributeDefinitionRow>[]>(
    () => [
      { field: "key", headerName: t("PlanningExecution:Key"), flex: 1, minWidth: 140 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "position", headerName: t("PlanningExecution:Position"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<TripAttributeDefinitionRow>
      entityKey="TripAttributeDefinition"
      resource="PlanningExecution"
      permission="PlanningExecution.TripAttributeDefinitions"
      gridId="masterData.tripAttributeDefinitions"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? row.key ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <TripAttributeDefinitionEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
