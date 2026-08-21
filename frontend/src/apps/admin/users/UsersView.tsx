import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, KeyRound, Trash2, UserPlus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Skeleton } from "@/components/ui/skeleton";
import { AppCommandBar } from "@/components/ui/app-command-bar";
import { UserFormDialog } from "@/apps/admin/components/UserFormDialog";
import { ResetPasswordDialog } from "@/apps/admin/components/ResetPasswordDialog";
import { BulkDeleteUsersDialog } from "@/apps/admin/components/BulkDeleteUsersDialog";
import {
  useUsersGridFetcher,
  useDeleteUser,
  type UserRow,
} from "@/domains/platform/administration-users";
import { useApplicationConfiguration } from "@/lib/abp/queries";
import { useL } from "@/lib/i18n/LocalizationProvider";
import type { OpenTmsGridProps, GridRequest, GridSelectionDto } from "@/components/ui/opentms-grid";

function extractApiError(error: unknown): string {
  if (!error || typeof error !== "object") return String(error ?? "");
  const e = error as Record<string, unknown>;
  const abpMsg = (e["error"] as Record<string, unknown> | undefined)?.["message"];
  if (typeof abpMsg === "string") return abpMsg;
  if (typeof e["title"] === "string") return e["title"];
  if (typeof e["detail"] === "string") return e["detail"];
  return "";
}

const _OpenTmsGrid = lazy(() =>
  import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })),
);

// Typed generic wrapper around the lazy component
function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

function useIsGranted(policy: string): boolean {
  const { data } = useApplicationConfiguration();
  return (data?.auth?.grantedPolicies as Record<string, boolean> | undefined)?.[policy] === true;
}

interface RowActionsProps {
  row: UserRow;
  canUpdate: boolean;
  canDelete: boolean;
  canResetPassword: boolean;
  onEdit: (row: UserRow) => void;
  onResetPassword: (row: UserRow) => void;
  onDeleted: () => void;
}

function RowActions({ row, canUpdate, canDelete, canResetPassword, onEdit, onResetPassword, onDeleted }: RowActionsProps) {
  const { t } = useL();
  const deleteUser = useDeleteUser();
  const [popoverOpen, setPopoverOpen] = useState(false);
  const [deleteError, setDeleteError] = useState("");

  const handleDelete = async () => {
    setDeleteError("");
    try {
      await deleteUser.mutateAsync(row.id!);
      setPopoverOpen(false);
      onDeleted();
    } catch (err) {
      setDeleteError(extractApiError(err) || t("Administration:DeleteFailed"));
    }
  };

  return (
    <div className="flex items-center gap-1">
      {canUpdate && (
        <Button
          size="icon-xs"
          variant="ghost"
          onClick={() => onEdit(row)}
          title={t("Administration:EditUser")}
        >
          <Pencil />
        </Button>
      )}
      {canResetPassword && (
        <Button
          size="icon-xs"
          variant="ghost"
          onClick={() => onResetPassword(row)}
          title={t("Administration:ResetPassword")}
        >
          <KeyRound />
        </Button>
      )}
      {canDelete && (
        <Popover open={popoverOpen} onOpenChange={(open) => { setPopoverOpen(open); if (!open) setDeleteError(""); }}>
          <PopoverTrigger
            disabled={deleteUser.isPending}
            title={t("Administration:DeleteUser")}
            className="inline-flex size-6 items-center justify-center rounded text-destructive hover:bg-muted disabled:opacity-50"
          >
            <Trash2 className="size-3" />
          </PopoverTrigger>
          <PopoverContent className="w-56" side="left">
            <p className="text-sm font-medium">
              {t("Administration:DeleteUser")} {row.userName}?
            </p>
            {deleteError && (
              <p className="mt-1 text-xs text-destructive">{deleteError}</p>
            )}
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="outline" onClick={() => setPopoverOpen(false)}>
                {t("Administration:Cancel")}
              </Button>
              <Button
                size="sm"
                variant="destructive"
                onClick={() => { void handleDelete(); }}
                disabled={deleteUser.isPending}
              >
                {t("Administration:Delete")}
              </Button>
            </div>
          </PopoverContent>
        </Popover>
      )}
    </div>
  );
}

export function UsersView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Users");
  const canCreate = useIsGranted("Platform.Administration.Users.Create");
  const canUpdate = useIsGranted("Platform.Administration.Users.Update");
  const canDelete = useIsGranted("Platform.Administration.Users.Delete");
  const canBulkDelete = useIsGranted("Platform.Administration.Users.BulkDelete");
  const canResetPassword = useIsGranted("Platform.Administration.Users.ResetPassword");

  const [refreshKey, setRefreshKey] = useState(0);
  const [userFormOpen, setUserFormOpen] = useState(false);
  const [userFormMode, setUserFormMode] = useState<"create" | "edit">("create");
  const [editingUser, setEditingUser] = useState<UserRow | undefined>();
  const [resetPasswordOpen, setResetPasswordOpen] = useState(false);
  const [resetPasswordUser, setResetPasswordUser] = useState<UserRow | undefined>();
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [selection, setSelection] = useState<GridSelectionDto>({
    mode: "Explicit",
    explicitIds: [],
    filterRequest: null,
    excludedIds: [],
  });
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

  const commands = useMemo(
    () => [
      ...(canCreate
        ? [
            {
              id: "new-user",
              labelKey: "Administration:NewUser",
              icon: UserPlus,
              variant: "default" as const,
              requiresSelection: false,
              onClick: () => {
                setUserFormMode("create");
                setEditingUser(undefined);
                setUserFormOpen(true);
              },
            },
          ]
        : []),
      ...(canBulkDelete
        ? [
            {
              id: "bulk-delete-users",
              labelKey: "Administration:BulkDelete",
              icon: Trash2,
              variant: "destructive" as const,
              requiresSelection: true,
              disabled: bulkDeleteOpen,
              onClick: () => setBulkDeleteOpen(true),
            },
          ]
        : []),
    ],
    [canCreate, canBulkDelete, bulkDeleteOpen],
  );

  const columnDefs = useMemo<ColDef<UserRow>[]>(
    () => [
      {
        field: "userName",
        headerName: t("Administration:Username"),
        filter: "agTextColumnFilter",
        sortable: true,
      },
      {
        field: "email",
        headerName: t("Administration:Email"),
        filter: "agTextColumnFilter",
        sortable: true,
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
      },
      {
        field: "roleNames",
        headerName: t("Administration:Roles"),
        valueGetter: (params) => (params.data?.roleNames ?? []).join(", "),
        sortable: false,
        filter: false,
      },
      {
        field: "isActive",
        headerName: t("Administration:Active"),
        cellRenderer: (params: { value: boolean }) =>
          params.value ? (
            <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-800">
              {t("Administration:Yes")}
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-800">
              {t("Administration:No")}
            </span>
          ),
        sortable: false,
        filter: false,
        width: 90,
      },
      {
        field: "creationTime",
        headerName: t("Administration:Created"),
        valueFormatter: (params) =>
          params.value
            ? new Intl.DateTimeFormat(undefined, { dateStyle: "short" }).format(
                new Date(params.value as string),
              )
            : "",
        sortable: true,
      },
      {
        colId: "__actions__",
        headerName: "",
        cellRenderer: (params: { data?: UserRow }) =>
          params.data ? (
            <RowActions
              row={params.data}
              canUpdate={canUpdate}
              canDelete={canDelete}
              canResetPassword={canResetPassword}
              onEdit={(row) => {
                setEditingUser(row);
                setUserFormMode("edit");
                setUserFormOpen(true);
              }}
              onResetPassword={(row) => {
                setResetPasswordUser(row);
                setResetPasswordOpen(true);
              }}
              onDeleted={invalidateGrid}
            />
          ) : null,
        width: 120,
        minWidth: 120,
        maxWidth: 120,
        sortable: false,
        filter: false,
        pinned: "right" as const,
        suppressMovable: true,
        resizable: false,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, canUpdate, canDelete, canResetPassword, invalidateGrid],
  );

  if (!canView) {
    return (
      <div className="flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground">
        {t("Administration:PermissionDenied")}
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} selectionCount={selectionCount} />

      <div className="min-h-0 flex-1">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<UserRow>
            gridId="platform.administration.users"
            columnDefs={columnDefs}
            fetchRows={fetchUsers}
            onSelectionChange={canBulkDelete ? (sel) => setSelection(sel) : undefined}
          />
        </Suspense>
      </div>

      <UserFormDialog
        mode={userFormMode}
        user={editingUser}
        open={userFormOpen}
        onClose={() => setUserFormOpen(false)}
        onSuccess={invalidateGrid}
      />

      {resetPasswordUser && (
        <ResetPasswordDialog
          userId={resetPasswordUser.id!}
          userName={resetPasswordUser.userName ?? ""}
          open={resetPasswordOpen}
          onClose={() => {
            setResetPasswordOpen(false);
            setResetPasswordUser(undefined);
          }}
        />
      )}

      <BulkDeleteUsersDialog
        selection={selection}
        selectedCount={selectionCount}
        open={bulkDeleteOpen}
        onClose={() => setBulkDeleteOpen(false)}
        onSuccess={invalidateGrid}
      />
    </div>
  );
}
