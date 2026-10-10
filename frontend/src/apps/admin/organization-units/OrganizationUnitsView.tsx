import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, Plus, Trash2, TriangleAlert } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DateCell, NumberCell } from "@/components/ui/grid-cells";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridRequest, GridRowAction, GridTreeDataOptions } from "@/components/ui/opentms-grid";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { OrganizationUnitFormDialog } from "@/apps/admin/components/OrganizationUnitFormDialog";
import {
  useDeleteOrganizationUnit,
  useOrganizationUnitsGridFetcher,
  type OrganizationUnitRow,
} from "@/domains/platform/administration-organization-units";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* DEVIATION(constitution-4): no per-tenant feature flag. The Administration app carries the
 * bootstrap-deadlock exemption of design #8 (approved by the PO in issue #8) and design #22
 * extended it to a further admin capability; organization units are the same baseline
 * identity administration, gated by the Platform.Administration.OrganizationUnits.*
 * permissions. Gate 1 confirms the extension for this view.
 *
 * DEVIATION(UI-GRID-008): Delete lives in the context menu only. The task asks for single
 * units with their members, not a bulk delete, and a cascading delete of several subtrees
 * at once is not something to offer from a selection; so the grid has no selection column
 * and no selection command. Approved in the task as Marinus stated it. */

type EditorState =
  | { mode: "create"; parent?: OrganizationUnitRow; readOnly: false }
  | { mode: "edit"; unit: OrganizationUnitRow; readOnly: boolean }
  | null;

type ConflictState = { id: string; displayName: string; childCount: number; memberCount: number } | null;

/* Organization units list (UI-PAGE-001): command bar, a full-height tree grid, the dialogs.
 * The grid expands a unit into its sub-units; a search flattens the tree (the path column
 * says where a match sits). Rows open the editor on double click (UI-GRID-007); the actions
 * live in the context menu (UI-GRID-008). */
export function OrganizationUnitsView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.OrganizationUnits");
  const canCreate = useIsGranted("Platform.Administration.OrganizationUnits.Create");
  const canUpdate = useIsGranted("Platform.Administration.OrganizationUnits.Update");
  const canDelete = useIsGranted("Platform.Administration.OrganizationUnits.Delete");

  const deleteUnit = useDeleteOrganizationUnit();

  const [refreshKey, setRefreshKey] = useState(0);
  const [editor, setEditor] = useState<EditorState>(null);
  const [unitToDelete, setUnitToDelete] = useState<OrganizationUnitRow | undefined>();
  const [conflict, setConflict] = useState<ConflictState>(null);

  const baseFetcher = useOrganizationUnitsGridFetcher();
  const fetchUnits = useCallback(
    (req: GridRequest) => baseFetcher(req),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetcher, refreshKey],
  );
  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const openRecord = useCallback(
    (row: OrganizationUnitRow) => setEditor({ mode: "edit", unit: row, readOnly: !canUpdate }),
    [canUpdate],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () =>
      canCreate
        ? [
            {
              id: "new-organization-unit",
              labelKey: "Administration:NewOrganizationUnit",
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create", readOnly: false }),
            },
          ]
        : [],
    [canCreate],
  );

  const rowActions = useCallback(
    (rows: OrganizationUnitRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      if (!single) return [];
      return [
        ...(canUpdate
          ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => openRecord(single) }]
          : []),
        ...(canCreate
          ? [
              {
                id: "new-sub-unit",
                labelKey: "Administration:NewSubUnit",
                icon: Plus,
                onClick: () => setEditor({ mode: "create", parent: single, readOnly: false }),
              },
            ]
          : []),
        ...(canDelete
          ? [
              {
                id: "delete",
                labelKey: "Delete",
                icon: Trash2,
                destructive: true,
                onClick: () => setUnitToDelete(single),
              },
            ]
          : []),
      ];
    },
    [canCreate, canDelete, canUpdate, openRecord],
  );

  const treeData = useMemo<GridTreeDataOptions<OrganizationUnitRow>>(
    () => ({
      isGroup: (row) => row.hasChildren === true,
      getGroupKey: (row) => row.id ?? "",
      headerName: t("Administration:DisplayName"),
      field: "displayName",
    }),
    [t],
  );

  const columnDefs = useMemo<ColDef<OrganizationUnitRow>[]>(
    () => [
      {
        field: "memberCount",
        headerName: t("Administration:MemberCount"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        sortable: true,
        filter: false,
        minWidth: 110,
      },
      {
        field: "path",
        headerName: t("Administration:UnitPath"),
        sortable: true,
        filter: false,
        flex: 1,
        minWidth: 200,
        hide: true,
      },
      {
        field: "code",
        headerName: t("Administration:UnitCode"),
        sortable: false,
        filter: false,
        minWidth: 140,
        hide: true,
      },
      {
        field: "creationTime",
        headerName: t("Administration:Created"),
        cellRenderer: DateCell,
        sortable: true,
        filter: false,
        minWidth: 120,
      },
    ],
    [t],
  );

  const deleteSingle = async (force: boolean) => {
    const target = unitToDelete ?? (conflict ? { id: conflict.id, displayName: conflict.displayName } : undefined);
    if (!target?.id) return;
    const result = await deleteUnit.mutateAsync({ id: target.id, force });
    setUnitToDelete(undefined);
    if (result.status === "conflict") {
      setConflict({ id: target.id, ...result });
      return;
    }
    setConflict(null);
    toast.success(t("Administration:OrganizationUnitDeleted"));
    invalidateGrid();
  };

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<OrganizationUnitRow>
          gridId="platform.administration.organizationUnits"
          columnDefs={columnDefs}
          fetchRows={fetchUnits}
          treeData={treeData}
          onOpen={openRecord}
          rowActions={rowActions}
        />
      </div>

      {editor && (
        <OrganizationUnitFormDialog
          key={editor.mode === "edit" ? `edit-${editor.unit.id}` : `create-${editor.parent?.id ?? "root"}`}
          mode={editor.mode}
          unit={editor.mode === "edit" ? editor.unit : undefined}
          parent={editor.mode === "create" ? editor.parent : undefined}
          readOnly={editor.readOnly}
          open
          onClose={() => setEditor(null)}
          onSuccess={invalidateGrid}
        />
      )}

      <ConfirmDialog
        open={unitToDelete != null}
        onOpenChange={(open) => {
          if (!open) setUnitToDelete(undefined);
        }}
        title={t("Administration:DeleteOrganizationUnit")}
        description={`${t("Administration:DeleteOrganizationUnitQuestion").replace("{0}", unitToDelete?.displayName ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={() => deleteSingle(false)}
      />

      <ConfirmDialog
        open={conflict != null}
        onOpenChange={(open) => {
          if (!open) setConflict(null);
        }}
        title={t("Administration:DeleteOrganizationUnit")}
        description={`${t("Administration:DeleteOrganizationUnitQuestion").replace("{0}", conflict?.displayName ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={() => deleteSingle(true)}
      >
        <Alert variant="destructive">
          <TriangleAlert aria-hidden="true" />
          <AlertDescription>
            {t("Administration:OrganizationUnitInUseWarning")
              .replace("{0}", conflict?.displayName ?? "")
              .replace("{1}", String(conflict?.childCount ?? 0))
              .replace("{2}", String(conflict?.memberCount ?? 0))}
          </AlertDescription>
        </Alert>
      </ConfirmDialog>
    </div>
  );
}
