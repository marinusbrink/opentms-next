import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import type {
  GridRequest,
  GridRowAction,
  GridSelectionDto,
  OpenTmsGridProps,
} from "@/components/ui/opentms-grid";
import { BulkDeleteRolesDialog } from "@/apps/admin/components/BulkDeleteRolesDialog";
import { RoleFormDialog } from "@/apps/admin/components/RoleFormDialog";
import {
  useDeleteRole,
  useRolesGridFetcher,
  type RoleRow,
} from "@/domains/platform/administration-roles";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

const _OpenTmsGrid = lazy(() =>
  import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })),
);

// Typed generic wrapper around the lazy component
function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

const EMPTY_SELECTION: GridSelectionDto = {
  mode: "Explicit",
  explicitIds: [],
  filterRequest: null,
  excludedIds: [],
};

type EditorState = { mode: "create" | "edit"; role?: RoleRow; readOnly: boolean } | null;
type BulkTarget = { selection: GridSelectionDto; count: number } | null;
type ConflictState = { id: string; name: string; userCount: number } | null;

export function RolesView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Roles");
  const canCreate = useIsGranted("Platform.Administration.Roles.Create");
  const canUpdate = useIsGranted("Platform.Administration.Roles.Update");
  const canDelete = useIsGranted("Platform.Administration.Roles.Delete");
  const canBulkDelete = useIsGranted("Platform.Administration.Roles.BulkDelete");

  const deleteRole = useDeleteRole();

  const [refreshKey, setRefreshKey] = useState(0);
  const [editor, setEditor] = useState<EditorState>(null);
  const [roleToDelete, setRoleToDelete] = useState<RoleRow | undefined>();
  const [conflict, setConflict] = useState<ConflictState>(null);
  const [bulkTarget, setBulkTarget] = useState<BulkTarget>(null);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [lastFilteredCount, setLastFilteredCount] = useState(0);

  const baseRolesFetcher = useRolesGridFetcher();
  const fetchRoles = useCallback(
    async (req: GridRequest) => {
      const result = await baseRolesFetcher(req);
      setLastFilteredCount(result.filteredCount);
      return result;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseRolesFetcher, refreshKey],
  );

  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const selectionCount =
    selection.mode === "Explicit"
      ? selection.explicitIds.length
      : Math.max(0, lastFilteredCount - selection.excludedIds.length);

  const openRecord = useCallback(
    (row: RoleRow) => {
      /* System roles are shown, never changed (business state, UI-PERM-005). */
      setEditor({ mode: "edit", role: row, readOnly: !canUpdate || row.isStatic === true });
    },
    [canUpdate],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [
      ...(canCreate
        ? [
            {
              id: "new-role",
              labelKey: "Administration:NewRole",
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create", readOnly: false }),
            } satisfies AppCommandBarCommand,
          ]
        : []),
      ...(canBulkDelete
        ? [
            {
              id: "bulk-delete-roles",
              labelKey: "Administration:DeleteRole",
              selectionLabelKeys: {
                zero: "Administration:DeleteRole",
                one: "Administration:DeleteOneRole",
                many: "Administration:DeleteNRoles",
              },
              icon: Trash2,
              variant: "destructive" as const,
              requiresSelection: true,
              disabled: bulkTarget != null,
              onClick: () => setBulkTarget({ selection, count: selectionCount }),
            } satisfies AppCommandBarCommand,
          ]
        : []),
    ],
    [canCreate, canBulkDelete, bulkTarget, selection, selectionCount],
  );

  const rowActions = useCallback(
    (rows: RoleRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      const isStatic = single?.isStatic === true;
      return [
        ...(canUpdate && single
          ? [
              {
                id: "edit",
                labelKey: "Edit",
                icon: Pencil,
                disabled: isStatic,
                disabledReasonKey: "Administration:SystemRoleReadOnly",
                onClick: () => openRecord(single),
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
                multi: canBulkDelete,
                disabled: isStatic,
                disabledReasonKey: "Administration:SystemRoleReadOnly",
                onClick: () => {
                  if (rows.length === 1) {
                    setRoleToDelete(rows[0]);
                  } else {
                    setBulkTarget({
                      selection: {
                        mode: "Explicit",
                        explicitIds: rows.map((r) => r.id!).filter(Boolean),
                        filterRequest: null,
                        excludedIds: [],
                      },
                      count: rows.length,
                    });
                  }
                },
              },
            ]
          : []),
      ];
    },
    [canBulkDelete, canDelete, canUpdate, openRecord],
  );

  const columnDefs = useMemo<ColDef<RoleRow>[]>(
    () => [
      {
        field: "name",
        headerName: t("Administration:RoleName"),
        sortable: true,
        filter: false,
        flex: 1,
        minWidth: 180,
      },
      {
        field: "isDefault",
        headerName: t("Administration:IsDefault"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 100,
      },
      {
        field: "isPublic",
        headerName: t("Administration:IsPublic"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 100,
      },
      {
        field: "userCount",
        headerName: t("Administration:UserCount"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        sortable: false,
        filter: false,
        minWidth: 100,
      },
      {
        field: "isStatic",
        headerName: t("Administration:StaticRole"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 110,
      },
    ],
    [t],
  );

  const deleteSingle = async (force: boolean) => {
    const target = roleToDelete ?? (conflict ? { id: conflict.id, name: conflict.name } : undefined);
    if (!target?.id) return;
    const result = await deleteRole.mutateAsync({ id: target.id, force });
    setRoleToDelete(undefined);
    if (result.status === "conflict") {
      setConflict({ id: target.id, name: result.roleName, userCount: result.userCount });
      return;
    }
    setConflict(null);
    toast.success(t("Administration:RoleDeleted"));
    invalidateGrid();
  };

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} selectionCount={selectionCount} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<RoleRow>
            gridId="platform.administration.roles"
            columnDefs={columnDefs}
            fetchRows={fetchRoles}
            onSelectionChange={canBulkDelete ? (sel) => setSelection(sel) : undefined}
            onOpen={openRecord}
            rowActions={rowActions}
          />
        </Suspense>
      </div>

      {editor && (
        <RoleFormDialog
          key={`${editor.mode}-${editor.role?.id ?? "new"}`}
          mode={editor.mode}
          role={editor.role}
          readOnly={editor.readOnly}
          open
          onClose={() => setEditor(null)}
          onSuccess={invalidateGrid}
        />
      )}

      <ConfirmDialog
        open={roleToDelete != null}
        onOpenChange={(open) => {
          if (!open) setRoleToDelete(undefined);
        }}
        title={t("Administration:DeleteRole")}
        description={`${t("Administration:DeleteRoleQuestion").replace("{0}", roleToDelete?.name ?? "")} ${t("ThisCannotBeUndone")}`}
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
        title={t("Administration:DeleteRole")}
        description={`${t("Administration:DeleteRoleQuestion").replace("{0}", conflict?.name ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={() => deleteSingle(true)}
      >
        <Alert variant="warning">
          <AlertDescription>
            {t("Administration:RoleHasUsersWarning")
              .replace("{0}", conflict?.name ?? "")
              .replace("{1}", String(conflict?.userCount ?? 0))}
          </AlertDescription>
        </Alert>
      </ConfirmDialog>

      {bulkTarget && (
        <BulkDeleteRolesDialog
          selection={bulkTarget.selection}
          selectionCount={bulkTarget.count}
          open
          onClose={() => setBulkTarget(null)}
          onSuccess={invalidateGrid}
        />
      )}
    </div>
  );
}
