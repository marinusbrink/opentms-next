import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  RELATION_ROLES,
  relationCategories,
  relationRoleKeys,
  relations,
  useArchiveRelation,
  useUnarchiveRelation,
  type RelationListExtra,
  type RelationRow,
} from "@/domains/master-data/entities";
import { RELATIONS_FEATURE } from "@/domains/master-data/shared";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Relations list (design 43, UI design): the shared list view behind MasterData.Relations with
 * the Role, Category and Show archived filters; Archive / Unarchive as confirmed row actions that
 * refresh the grid (no optimistic update). Columns the server cannot sort on are not sortable. */

export function RelationsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [role, setRole] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const extra = useMemo<RelationListExtra>(
    () => ({ includeArchived: showArchived, roles: role ? Number(role) : undefined, categoryId: categoryId ?? undefined }),
    [showArchived, role, categoryId],
  );
  const fetchRows = relations.useGridFetcher(extra);
  const deleteOne = relations.useDelete();
  const bulk = relations.useBulkDelete();
  const archive = useArchiveRelation();
  const unarchive = useUnarchiveRelation();
  const canUpdate = useIsGranted("MasterData.Relations.Update");
  const categoryLookup = relationCategories.useLookup();
  const categoryOptions = useMemo(() => (categoryLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: c.name ?? "" })), [categoryLookup.data]);
  const roleOptions = useMemo(
    () => [{ value: null, label: t("MasterData:AllRoles") }, ...RELATION_ROLES.map((r) => ({ value: String(r.value), label: t(r.key) }))],
    [t],
  );

  const [archiveTarget, setArchiveTarget] = useState<{ row: RelationRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<RelationRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 110 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "roles",
        headerName: t("MasterData:Roles"),
        flex: 2,
        minWidth: 160,
        sortable: false,
        valueFormatter: (p) => relationRoleKeys(p.value as number | undefined).map((key) => t(key)).join(", "),
      },
      { field: "city", headerName: t("MasterData:City"), flex: 1, minWidth: 130 },
      { field: "countryCode", headerName: t("MasterData:Country"), flex: 1, minWidth: 90, sortable: false },
      { field: "vatNumber", headerName: t("MasterData:VatNumber"), flex: 1, minWidth: 140 },
      { field: "phone", headerName: t("MasterData:Phone"), flex: 1, minWidth: 130 },
      { field: "email", headerName: t("MasterData:Email"), flex: 2, minWidth: 180 },
      { field: "chamberOfCommerceNumber", headerName: t("MasterData:ChamberOfCommerceNumber"), flex: 1, minWidth: 140, hide: true },
      {
        field: "categoryNames",
        headerName: t("MasterData:Categories"),
        flex: 2,
        minWidth: 160,
        hide: true,
        sortable: false,
        valueFormatter: (p) => ((p.value as string[] | null | undefined) ?? []).join(", "),
      },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
      { field: "isBlocked", headerName: t("MasterData:IsBlocked"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: RelationRow, refresh: () => void): GridRowAction[] =>
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
      toast.success(t(row.isArchived ? "MasterData:RelationUnarchived" : "MasterData:RelationArchived"));
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
      <MasterDataListView<RelationRow>
        entityKey="Relation"
        permission="MasterData.Relations"
        feature={RELATIONS_FEATURE}
        gridId="masterData.relations"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={(row) => row.name ?? ""}
        deleteOne={deleteOne}
        bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
        filters={
          <>
            <div className="w-48">
              <Select items={roleOptions} value={role} onValueChange={(next) => setRole((next as string | null) ?? null)}>
                <SelectTrigger id="relations-role-filter" className="h-9 w-full bg-background" aria-label={t("MasterData:Role")}>
                  <SelectValue placeholder={t("MasterData:Role")} />
                </SelectTrigger>
                <SelectContent>
                  {roleOptions.map((item) => (
                    <SelectItem key={item.value ?? "all"} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="w-64">
              <ReferenceCombobox id="relations-category-filter" value={categoryId} onChange={setCategoryId} options={categoryOptions} placeholder={t("MasterData:RelationCategory")} />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
              {t("MasterData:ShowArchived")}
            </label>
          </>
        }
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved) => (
          <RelationEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "MasterData:UnarchiveRelation" : "MasterData:ArchiveRelation")}
        description={t(targetArchived ? "MasterData:UnarchiveRelationQuestion" : "MasterData:ArchiveRelationQuestion").replace("{0}", archiveTarget?.row.name ?? "")}
        confirmLabel={t(targetArchived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
