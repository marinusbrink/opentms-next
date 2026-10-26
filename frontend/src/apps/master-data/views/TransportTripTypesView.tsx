import { useCallback, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { TransportTripTypeEditor } from "@/domains/planning-execution/components/TransportTripTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  TRIP_EXECUTIONS,
  transportTripTypes,
  useSetDefaultTransportTripType,
  type TransportTripTypeRow,
} from "@/domains/planning-execution/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 53: the Planning & Execution domain group of the Master data app, Trips → Transport trip types. */
export function TransportTripTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = transportTripTypes.useGridFetcher(extra);
  const deleteOne = transportTripTypes.useDelete();
  const bulk = transportTripTypes.useBulkDelete();
  const setDefault = useSetDefaultTransportTripType();
  const canUpdate = useIsGranted("PlanningExecution.TransportTripTypes.Update");

  const executionLabel = useCallback(
    (value: number | undefined) => t(TRIP_EXECUTIONS.find((e) => e.value === value)?.key ?? ""),
    [t],
  );

  const columnDefs = useMemo<ColDef<TransportTripTypeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "defaultExecution",
        headerName: t("PlanningExecution:DefaultExecution"),
        flex: 1,
        minWidth: 140,
        valueFormatter: (p: ValueFormatterParams<TransportTripTypeRow>) => executionLabel(p.value as number | undefined),
      },
      { field: "numberingExample", headerName: t("PlanningExecution:NumberingExample"), flex: 1, minWidth: 140 },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "sendToFleetManagementSystem", headerName: t("PlanningExecution:SendToFleetManagementSystem"), cellRenderer: BoolCell, flex: 1, minWidth: 120, hide: true },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t, executionLabel],
  );

  const extraRowActions = useCallback(
    (row: TransportTripTypeRow, refresh: () => void): GridRowAction[] =>
      canUpdate && !row.isDefault
        ? [
            {
              id: "set-default",
              labelKey: "MasterData:SetAsDefault",
              icon: Star,
              disabled: row.isArchived === true,
              disabledReasonKey: "MasterData:ArchivedCannotBeDefault",
              onClick: () => {
                setDefault.mutateAsync(row.id!).then(
                  () => {
                    toast.success(t("PlanningExecution:TransportTripTypeSaved"));
                    refresh();
                  },
                  (err: unknown) => toast.error(getApiErrorMessage(err, t)),
                );
              },
            },
          ]
        : [],
    [canUpdate, setDefault, t],
  );

  return (
    <MasterDataListView<TransportTripTypeRow>
      entityKey="TransportTripType"
      resource="PlanningExecution"
      permission="PlanningExecution.TransportTripTypes"
      gridId="masterData.transportTripTypes"
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
      extraRowActions={extraRowActions}
      renderEditor={(state, onClose, onSaved) => (
        <TransportTripTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
