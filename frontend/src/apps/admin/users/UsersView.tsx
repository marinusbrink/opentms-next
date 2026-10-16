import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { KeyRound, LockOpen, Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell, DateCell } from "@/components/ui/grid-cells";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import type {
  GridRequest,
  GridRowAction,
  GridSelectionDto,
  OpenTmsGridProps,
} from "@/components/ui/opentms-grid";
import { BulkDeleteUsersDialog } from "@/apps/admin/components/BulkDeleteUsersDialog";
import { ResetPasswordDialog } from "@/apps/admin/components/ResetPasswordDialog";
import { UserFormDialog } from "@/domains/platform/components/UserFormDialog";
import {
  useDeleteUser,
  useUnlockUser,
  useUsersGridFetcher,
  type UserRow,
} from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
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

type EditorState = { mode: "create" | "edit"; user?: UserRow; readOnly: boolean } | null;
type BulkTarget = { selection: GridSelectionDto; count: number } | null;

/* Users list (UI-PAGE-001): command bar, full-height grid, dialogs. Row actions live in the
 * context menu and double click opens the record (UI-GRID-007/008); bulk delete is a
 * selection command in the bar. */
export function UsersView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Users");
  const canCreate = useIsGranted("Platform.Administration.Users.Create");
  const canUpdate = useIsGranted("Platform.Administration.Users.Update");
  const canDelete = useIsGranted("Platform.Administration.Users.Delete");
  const canBulkDelete = useIsGranted("Platform.Administration.Users.BulkDelete");
  const canResetPassword = useIsGranted("Platform.Administration.Users.ResetPassword");
  const canUnlock = useIsGranted("Platform.Administration.Users.Unlock");

  const deleteUser = useDeleteUser();
  const unlockUser = useUnlockUser();

  const [refreshKey, setRefreshKey] = useState(0);
  const [editor, setEditor] = useState<EditorState>(null);
  const [resetPasswordUser, setResetPasswordUser] = useState<UserRow | undefined>();
  const [userToDelete, setUserToDelete] = useState<UserRow | undefined>();
  const [bulkTarget, setBulkTarget] = useState<BulkTarget>(null);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [lastFilteredCount, setLastFilteredCount] = useState(0);

  const baseUsersFetcher = useUsersGridFetcher();
  const fetchUsers = useCallback(
    async (req: GridRequest) => {
      const result = await baseUsersFetcher(req);
      setLastFilteredCount(result.filteredCount);
      return result;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseUsersFetcher, refreshKey],
  );

  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const selectionCount =
    selection.mode === "Explicit"
      ? selection.explicitIds.length
      : Math.max(0, lastFilteredCount - selection.excludedIds.length);

  const openRecord = useCallback(
    (row: UserRow) => {
      setEditor({ mode: "edit", user: row, readOnly: !canUpdate });
    },
    [canUpdate],
  );

  /* Unlock is reversible and leaves nothing to decide, so it runs without a confirm; the
   * outcome shows as a toast (UI-FB-002) and a failure as a toast too (UI-FB-004, list action). */
  const unlock = useCallback(
    async (row: UserRow) => {
      try {
        await unlockUser.mutateAsync(row.id!);
        toast.success(t("Administration:UserUnlocked"));
        invalidateGrid();
      } catch (err) {
        toast.error(getApiErrorMessage(err, t));
      }
    },
    [invalidateGrid, t, unlockUser],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [
      ...(canCreate
        ? [
            {
              id: "new-user",
              labelKey: "Administration:NewUser",
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create", readOnly: false }),
            } satisfies AppCommandBarCommand,
          ]
        : []),
      ...(canBulkDelete
        ? [
            {
              id: "bulk-delete-users",
              labelKey: "Administration:DeleteUser",
              selectionLabelKeys: {
                zero: "Administration:DeleteUser",
                one: "Administration:DeleteOneUser",
                many: "Administration:DeleteNUsers",
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
    (rows: UserRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      return [
        ...(canUpdate && single
          ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => openRecord(single) }]
          : []),
        ...(canResetPassword && single
          ? [
              {
                id: "reset-password",
                labelKey: "Administration:ResetPassword",
                icon: KeyRound,
                onClick: () => setResetPasswordUser(single),
              },
            ]
          : []),
        /* Only while a lockout is in force: it overrides the lockout duration. */
        ...(canUnlock && single?.isLockedOut
          ? [
              {
                id: "unlock",
                labelKey: "Administration:UnlockUser",
                icon: LockOpen,
                onClick: () => void unlock(single),
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
                onClick: () => {
                  if (rows.length === 1) {
                    setUserToDelete(rows[0]);
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
    [canBulkDelete, canDelete, canResetPassword, canUnlock, canUpdate, openRecord, unlock],
  );

  const columnDefs = useMemo<ColDef<UserRow>[]>(
    () => [
      {
        field: "userName",
        headerName: t("Administration:Username"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 140,
      },
      {
        field: "email",
        headerName: t("Administration:Email"),
        filter: "agTextColumnFilter",
        sortable: true,
        flex: 1,
        minWidth: 180,
      },
      {
        colId: "fullName",
        headerName: t("Administration:FullName"),
        valueGetter: (params) => {
          const row = params.data;
          if (!row) return "";
          return [row.name, row.surname].filter(Boolean).join(" ");
        },
        sortable: false,
        filter: false,
        flex: 1,
        minWidth: 140,
      },
      {
        field: "phoneNumber",
        headerName: t("Administration:PhoneNumber"),
        sortable: false,
        filter: false,
        minWidth: 140,
        hide: true,
      },
      {
        field: "roleNames",
        headerName: t("Administration:Roles"),
        valueGetter: (params) => (params.data?.roleNames ?? []).join(", "),
        sortable: false,
        filter: false,
        flex: 1,
        minWidth: 140,
      },
      {
        field: "isActive",
        headerName: t("Administration:Active"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 90,
      },
      {
        field: "isLockedOut",
        headerName: t("Administration:LockedOut"),
        cellRenderer: BoolCell,
        sortable: false,
        filter: false,
        minWidth: 100,
        hide: true,
      },
      {
        field: "creationTime",
        headerName: t("Administration:Created"),
        cellRenderer: DateCell,
        sortable: true,
        minWidth: 120,
      },
    ],
    [t],
  );

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} selectionCount={selectionCount} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<UserRow>
            gridId="platform.administration.users"
            columnDefs={columnDefs}
            fetchRows={fetchUsers}
            onSelectionChange={canBulkDelete ? (sel) => setSelection(sel) : undefined}
            onOpen={openRecord}
            rowActions={rowActions}
          />
        </Suspense>
      </div>

      {editor && (
        <UserFormDialog
          key={`${editor.mode}-${editor.user?.id ?? "new"}`}
          mode={editor.mode}
          user={editor.user}
          readOnly={editor.readOnly}
          open
          onClose={() => setEditor(null)}
          onSuccess={invalidateGrid}
        />
      )}

      {resetPasswordUser && (
        <ResetPasswordDialog
          userId={resetPasswordUser.id!}
          userName={resetPasswordUser.userName ?? ""}
          open
          onClose={() => setResetPasswordUser(undefined)}
        />
      )}

      <ConfirmDialog
        open={userToDelete != null}
        onOpenChange={(open) => {
          if (!open) setUserToDelete(undefined);
        }}
        title={t("Administration:DeleteUser")}
        description={`${t("Administration:DeleteUserQuestion").replace("{0}", userToDelete?.userName ?? "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={async () => {
          await deleteUser.mutateAsync(userToDelete!.id!);
          setUserToDelete(undefined);
          toast.success(t("Administration:UserDeleted"));
          invalidateGrid();
        }}
      />

      {bulkTarget && (
        <BulkDeleteUsersDialog
          selection={bulkTarget.selection}
          selectedCount={bulkTarget.count}
          open
          onClose={() => setBulkTarget(null)}
          onSuccess={invalidateGrid}
        />
      )}
    </div>
  );
}
