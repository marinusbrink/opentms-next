import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Skeleton } from "@/components/ui/skeleton";
import { RoleFormDialog } from "@/apps/admin/components/RoleFormDialog";
import { DeleteRoleConfirmDialog } from "@/apps/admin/components/DeleteRoleConfirmDialog";
import {
  useRolesGridFetcher,
  useDeleteRole,
  type RoleRow,
  type RoleDeleteResult,
} from "@/domains/platform/administration-roles";
import { useApplicationConfiguration } from "@/lib/abp/queries";
import { useL } from "@/lib/i18n/LocalizationProvider";
import type { OpenTmsGridProps, GridRequest } from "@/components/ui/opentms-grid";

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
  row: RoleRow;
  canUpdate: boolean;
  canDelete: boolean;
  onEdit: (row: RoleRow) => void;
  onDeleteResult: (result: RoleDeleteResult, row: RoleRow) => void;
  onDeleted: () => void;
}

function RowActions({ row, canUpdate, canDelete, onEdit, onDeleteResult, onDeleted }: RowActionsProps) {
  const { t } = useL();
  const deleteRole = useDeleteRole();
  const [popoverOpen, setPopoverOpen] = useState(false);
  const isStatic = row.isStatic ?? false;

  const handleDelete = async () => {
    try {
      const result = await deleteRole.mutateAsync({ id: row.id!, force: false });
      setPopoverOpen(false);
      if (result.status === "deleted") {
        onDeleted();
      } else {
        onDeleteResult(result, row);
      }
    } catch {
      setPopoverOpen(false);
    }
  };

  return (
    <div className="flex items-center gap-1">
      {canUpdate && (
        <Button
          size="icon-xs"
          variant="ghost"
          onClick={() => !isStatic && onEdit(row)}
          disabled={isStatic}
          title={t("Administration:EditRole")}
        >
          <Pencil />
        </Button>
      )}
      {canDelete && (
        <Popover open={popoverOpen} onOpenChange={setPopoverOpen}>
          <PopoverTrigger
            disabled={isStatic || deleteRole.isPending}
            title={t("Administration:DeleteRole")}
            className="inline-flex size-6 items-center justify-center rounded text-destructive hover:bg-muted disabled:opacity-50 disabled:pointer-events-none"
          >
            <Trash2 className="size-3" />
          </PopoverTrigger>
          <PopoverContent className="w-56" side="left">
            <p className="text-sm font-medium">
              {t("Administration:DeleteRole")} {row.name}?
            </p>
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="outline" onClick={() => setPopoverOpen(false)}>
                {t("Administration:Cancel")}
              </Button>
              <Button
                size="sm"
                variant="destructive"
                onClick={() => { void handleDelete(); }}
                disabled={deleteRole.isPending}
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

export function RolesView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Administration.Roles");
  const canCreate = useIsGranted("Platform.Administration.Roles.Create");
  const canUpdate = useIsGranted("Platform.Administration.Roles.Update");
  const canDelete = useIsGranted("Platform.Administration.Roles.Delete");

  const [refreshKey, setRefreshKey] = useState(0);
  const [roleFormOpen, setRoleFormOpen] = useState(false);
  const [roleFormMode, setRoleFormMode] = useState<"create" | "edit">("create");
  const [editingRole, setEditingRole] = useState<RoleRow | undefined>();
  const [conflictDialogOpen, setConflictDialogOpen] = useState(false);
  const [conflictRole, setConflictRole] = useState<{
    id: string;
    name: string;
    userCount: number;
  } | null>(null);

  const baseRolesFetcher = useRolesGridFetcher();
  const fetchRoles = useCallback(
    (req: GridRequest) => baseRolesFetcher(req),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseRolesFetcher, refreshKey],
  );

  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const columnDefs = useMemo<ColDef<RoleRow>[]>(
    () => [
      {
        field: "name",
        headerName: t("Administration:RoleName"),
        sortable: true,
        filter: false,
      },
      {
        field: "isDefault",
        headerName: t("Administration:IsDefault"),
        cellRenderer: (params: { value: boolean }) =>
          params.value ? (
            <span className="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs text-blue-800">
              {t("Administration:Yes")}
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">{t("Administration:No")}</span>
          ),
        sortable: false,
        filter: false,
        width: 110,
      },
      {
        field: "isPublic",
        headerName: t("Administration:IsPublic"),
        cellRenderer: (params: { value: boolean }) =>
          params.value ? (
            <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-800">
              {t("Administration:Yes")}
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">{t("Administration:No")}</span>
          ),
        sortable: false,
        filter: false,
        width: 100,
      },
      {
        field: "userCount",
        headerName: t("Administration:UserCount"),
        sortable: false,
        filter: false,
        width: 90,
      },
      {
        field: "isStatic",
        headerName: "",
        cellRenderer: (params: { value: boolean }) =>
          params.value ? (
            <span className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">
              {t("Administration:StaticRole")}
            </span>
          ) : null,
        sortable: false,
        filter: false,
        width: 110,
      },
      {
        colId: "__actions__",
        headerName: "",
        cellRenderer: (params: { data?: RoleRow }) =>
          params.data ? (
            <RowActions
              row={params.data}
              canUpdate={canUpdate}
              canDelete={canDelete}
              onEdit={(role) => {
                setEditingRole(role);
                setRoleFormMode("edit");
                setRoleFormOpen(true);
              }}
              onDeleteResult={(result, row) => {
                if (result.status === "conflict") {
                  setConflictRole({
                    id: row.id!,
                    name: result.roleName,
                    userCount: result.userCount,
                  });
                  setConflictDialogOpen(true);
                }
              }}
              onDeleted={invalidateGrid}
            />
          ) : null,
        width: 90,
        minWidth: 90,
        maxWidth: 90,
        sortable: false,
        filter: false,
        pinned: "right" as const,
        suppressMovable: true,
        resizable: false,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, canUpdate, canDelete, invalidateGrid],
  );

  if (!canView) {
    return (
      <div className="flex h-64 items-center justify-center rounded-lg border bg-background p-8 text-sm text-muted-foreground">
        {t("Administration:PermissionDenied")}
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="flex items-center">
        {canCreate && (
          <Button
            size="sm"
            onClick={() => {
              setRoleFormMode("create");
              setEditingRole(undefined);
              setRoleFormOpen(true);
            }}
          >
            + {t("Administration:NewRole")}
          </Button>
        )}
      </div>

      <div className="min-h-0 flex-1">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<RoleRow>
            gridId="platform.administration.roles"
            columnDefs={columnDefs}
            fetchRows={fetchRoles}
          />
        </Suspense>
      </div>

      <RoleFormDialog
        mode={roleFormMode}
        role={editingRole}
        open={roleFormOpen}
        onClose={() => {
          setRoleFormOpen(false);
          setEditingRole(undefined);
        }}
        onSuccess={invalidateGrid}
      />

      {conflictRole && (
        <DeleteRoleConfirmDialog
          roleId={conflictRole.id}
          roleName={conflictRole.name}
          userCount={conflictRole.userCount}
          open={conflictDialogOpen}
          onClose={() => {
            setConflictDialogOpen(false);
            setConflictRole(null);
          }}
          onSuccess={invalidateGrid}
        />
      )}
    </div>
  );
}
