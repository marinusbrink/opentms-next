import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { DriverEditor } from "@/apps/master-data/editors/DriverEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  EMPLOYER_ROLES,
  drivers,
  relationLookupPage,
  useArchiveDriver,
  useUnarchiveDriver,
  type DriverListExtra,
  type DriverRow,
  type RelationOption,
} from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Drivers list (design 43, UI design): the shared list view behind MasterData.Relations with the
 * Employer (EntityLookup) and Show archived filters; Qualifications expiring as a NumberCell with the
 * hint as header tooltip; Archive / Unarchive as confirmed row actions that refresh the grid. */

export function DriversView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [employer, setEmployer] = useState<RelationOption | null>(null);
  const extra = useMemo<DriverListExtra>(() => ({ includeArchived: showArchived, employerRelationId: employer?.id }), [showArchived, employer]);
  const fetchRows = drivers.useGridFetcher(extra);
  const deleteOne = drivers.useDelete();
  const bulk = drivers.useBulkDelete();
  const archive = useArchiveDriver();
  const unarchive = useUnarchiveDriver();
  const canUpdate = useIsGranted("MasterData.Drivers.Update");
  const employerPage = useMemo(() => relationLookupPage(EMPLOYER_ROLES), []);

  const [archiveTarget, setArchiveTarget] = useState<{ row: DriverRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<DriverRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 110 },
      { field: "fullName", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "employerName", headerName: t("MasterData:Employer"), flex: 2, minWidth: 160, sortable: false },
      { field: "mobile", headerName: t("MasterData:Mobile"), flex: 1, minWidth: 130 },
      { field: "email", headerName: t("MasterData:Email"), flex: 2, minWidth: 180 },
      {
        field: "qualificationsExpiring",
        headerName: t("MasterData:QualificationsExpiring"),
        headerTooltip: t("MasterData:QualificationsExpiringHint"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        flex: 1,
        minWidth: 110,
        sortable: false,
      },
      { field: "hasUser", headerName: t("MasterData:UserAccount"), cellRenderer: BoolCell, flex: 1, minWidth: 110, sortable: false, hide: true },
      { field: "isAnonymised", headerName: t("MasterData:Anonymised"), cellRenderer: BoolCell, flex: 1, minWidth: 110, sortable: false, hide: true },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: DriverRow, refresh: () => void): GridRowAction[] =>
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
      toast.success(t(row.isArchived ? "MasterData:DriverUnarchived" : "MasterData:DriverArchived"));
      refresh();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  const targetArchived = archiveTarget?.row.isArchived === true;
  const rowName = (row: DriverRow) => row.fullName ?? [row.firstName, row.lastName].filter(Boolean).join(" ");

  return (
    <>
      <MasterDataListView<DriverRow>
        entityKey="Driver"
        permission="MasterData.Drivers"
        gridId="masterData.drivers"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={rowName}
        deleteOne={deleteOne}
        bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
        filters={
          <>
            <div className="w-64">
              <EntityLookup<RelationOption>
                id="drivers-employer-filter"
                value={employer}
                onChange={setEmployer}
                fetchPage={employerPage}
                getLabel={(r) => r.name}
                placeholder={t("MasterData:Employer")}
              />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
              {t("MasterData:ShowArchived")}
            </label>
          </>
        }
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved) => (
          <DriverEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "MasterData:UnarchiveDriver" : "MasterData:ArchiveDriver")}
        description={t(targetArchived ? "MasterData:UnarchiveDriverQuestion" : "MasterData:ArchiveDriverQuestion").replace("{0}", archiveTarget ? rowName(archiveTarget.row) : "")}
        confirmLabel={t(targetArchived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
