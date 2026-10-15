import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { VehicleCombinationEditor } from "@/apps/master-data/editors/VehicleCombinationEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  useArchiveVehicleCombination,
  useUnarchiveVehicleCombination,
  vehicleCombinations,
  type VehicleCombinationRow,
} from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Vehicle combinations list (design 48, UI design): Name, Members, Motorised vehicle; Archived hidden.
 * Show archived as the filter; Archive / Unarchive as confirmed row actions (archiving frees the vehicles). */

export function VehicleCombinationsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = vehicleCombinations.useGridFetcher(extra);
  const deleteOne = vehicleCombinations.useDelete();
  const bulk = vehicleCombinations.useBulkDelete();
  const archive = useArchiveVehicleCombination();
  const unarchive = useUnarchiveVehicleCombination();
  const canUpdate = useIsGranted("MasterData.VehicleCombinations.Update");

  const [archiveTarget, setArchiveTarget] = useState<{ row: VehicleCombinationRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<VehicleCombinationRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "members", headerName: t("MasterData:Members"), flex: 3, minWidth: 220, sortable: false },
      { field: "motorisedVehicleName", headerName: t("MasterData:MotorisedVehicle"), flex: 2, minWidth: 160, sortable: false },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: VehicleCombinationRow, refresh: () => void): GridRowAction[] =>
      canUpdate
        ? [
            {
              id: "archive",
              labelKey: row.isArchived ? "MasterData:Unarchive" : "MasterData:Archive",
              icon: row.isArchived ? ArchiveRestore : Archive,
              onClick: () => setArchiveTarget({ row, refresh }),
            },
          ]
        : [],
    [canUpdate],
  );

  const toggleArchive = async () => {
    if (!archiveTarget?.row.id) return;
    const { row, refresh } = archiveTarget;
    setBusy(true);
    try {
      await (row.isArchived ? unarchive : archive).mutateAsync(row.id!);
      setArchiveTarget(null);
      toast.success(t(row.isArchived ? "MasterData:VehicleCombinationUnarchived" : "MasterData:VehicleCombinationArchived"));
      refresh();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  const targetArchived = archiveTarget?.row.isArchived === true;

  return (
    <>
      <MasterDataListView<VehicleCombinationRow>
        entityKey="VehicleCombination"
        permission="MasterData.VehicleCombinations"
        gridId="masterData.vehicleCombinations"
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
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved) => (
          <VehicleCombinationEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "MasterData:UnarchiveVehicleCombination" : "MasterData:ArchiveVehicleCombination")}
        description={t(targetArchived ? "MasterData:UnarchiveVehicleCombinationQuestion" : "MasterData:ArchiveVehicleCombinationQuestion").replace("{0}", archiveTarget?.row.name ?? "")}
        confirmLabel={t(targetArchived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
