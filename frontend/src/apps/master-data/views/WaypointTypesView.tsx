import { useCallback, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import { WaypointTypeEditor } from "@/apps/master-data/editors/WaypointTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { WAYPOINT_ACTIVITIES, waypointTypes, type WaypointTypeRow } from "@/domains/orders/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 51: the Orders domain group of the Master data app, Transport orders → Waypoint types. The Master Data names
 * (relation category, default relation) ship hidden and do not sort: they live in another module. */
export function WaypointTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = waypointTypes.useGridFetcher(extra);
  const deleteOne = waypointTypes.useDelete();
  const bulk = waypointTypes.useBulkDelete();

  const activityLabel = useCallback(
    (value: number | undefined) => t(WAYPOINT_ACTIVITIES.find((a) => a.value === value)?.key ?? ""),
    [t],
  );

  const columnDefs = useMemo<ColDef<WaypointTypeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "activity",
        headerName: t("Orders:Activity"),
        flex: 1,
        minWidth: 130,
        valueFormatter: (p: ValueFormatterParams<WaypointTypeRow>) => activityLabel(p.value as number | undefined),
      },
      { field: "successiveWaypointTypeName", headerName: t("Orders:SuccessiveWaypointType"), flex: 1, minWidth: 140 },
      { field: "defaultDurationMinutes", headerName: t("Orders:DefaultDurationMinutes"), flex: 1, minWidth: 120, type: "rightAligned" },
      { field: "relationCategoryName", headerName: t("Orders:RelationCategory"), flex: 1, minWidth: 140, hide: true, sortable: false },
      { field: "defaultRelationName", headerName: t("Orders:DefaultRelation"), flex: 1, minWidth: 140, hide: true, sortable: false },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t, activityLabel],
  );

  return (
    <MasterDataListView<WaypointTypeRow>
      entityKey="WaypointType"
      resource="Orders"
      permission="Orders.WaypointTypes"
      gridId="masterData.waypointTypes"
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
        <WaypointTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
