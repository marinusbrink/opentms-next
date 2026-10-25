import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { TransportOrderTypeEditor } from "@/apps/master-data/editors/TransportOrderTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { transportOrderTypes, useSetDefaultTransportOrderType, type TransportOrderTypeRow } from "@/domains/orders/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 51: the Orders domain group of the Master data app, Transport orders → Transport order types. */
export function TransportOrderTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = transportOrderTypes.useGridFetcher(extra);
  const deleteOne = transportOrderTypes.useDelete();
  const bulk = transportOrderTypes.useBulkDelete();
  const setDefault = useSetDefaultTransportOrderType();
  const canUpdate = useIsGranted("Orders.TransportOrderTypes.Update");

  const columnDefs = useMemo<ColDef<TransportOrderTypeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "numberingExample", headerName: t("Orders:NumberingExample"), flex: 1, minWidth: 140, sortable: false },
      { field: "defaultWaypoints", headerName: t("Orders:DefaultWaypoints"), flex: 2, minWidth: 180, sortable: false },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: TransportOrderTypeRow, refresh: () => void): GridRowAction[] =>
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
                    toast.success(t("Orders:TransportOrderTypeSaved"));
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
    <MasterDataListView<TransportOrderTypeRow>
      entityKey="TransportOrderType"
      resource="Orders"
      permission="Orders.TransportOrderTypes"
      gridId="masterData.transportOrderTypes"
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
        <TransportOrderTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
