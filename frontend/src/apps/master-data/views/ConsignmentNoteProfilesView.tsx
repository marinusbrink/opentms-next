import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { ConsignmentNoteProfileEditor } from "@/apps/master-data/editors/ConsignmentNoteProfileEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { consignmentNoteProfiles, useSetDefaultConsignmentNoteProfile, type ConsignmentNoteProfileRow } from "@/domains/orders/consignment-note-profiles";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 58 "CMR"): the Orders domain group of the Master data app, Transport orders → CMR profiles. One
 * profile is the tenant's default; "Set as default" is a row action on the others (as the transport order types). */
export function ConsignmentNoteProfilesView() {
  const { t } = useL();
  const canUpdate = useIsGranted("Orders.ConsignmentNoteProfiles.Update");
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = consignmentNoteProfiles.useGridFetcher(extra);
  const deleteOne = consignmentNoteProfiles.useDelete();
  const bulk = consignmentNoteProfiles.useBulkDelete();
  const setDefault = useSetDefaultConsignmentNoteProfile();

  const columnDefs = useMemo<ColDef<ConsignmentNoteProfileRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "copies", headerName: t("Orders:Copies"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "showBorders", headerName: t("Orders:ShowBorders"), cellRenderer: BoolCell, flex: 1, minWidth: 110, sortable: false },
      { field: "showQrCode", headerName: t("Orders:ShowQrCode"), cellRenderer: BoolCell, flex: 1, minWidth: 110, sortable: false },
      { field: "groupByLeg", headerName: t("Orders:GroupByLeg"), cellRenderer: BoolCell, flex: 1, minWidth: 110, sortable: false },
      { field: "reportDefinitionName", headerName: t("Orders:ReportDefinition"), flex: 2, minWidth: 160, sortable: false },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: ConsignmentNoteProfileRow, refresh: () => void): GridRowAction[] =>
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
                    toast.success(t("Orders:ConsignmentNoteProfileSaved"));
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
    <MasterDataListView<ConsignmentNoteProfileRow>
      entityKey="ConsignmentNoteProfile"
      resource="Orders"
      permission="Orders.ConsignmentNoteProfiles"
      gridId="masterData.consignmentNoteProfiles"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      deleteDisabledReasonKey={(row) => (row.isDefault ? "Orders:DefaultProfileCannotBeDeleted" : undefined)}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      extraRowActions={extraRowActions}
      renderEditor={(state, onClose, onSaved) => (
        <ConsignmentNoteProfileEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
