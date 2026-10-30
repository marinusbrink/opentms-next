import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { LogIn, MailPlus, Pencil, Plus, RotateCcw, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { DateTimeCell } from "@/components/ui/grid-cells";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusCell } from "@/components/ui/status-badge";
import type { GridRequest, GridRowAction, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { DeleteTenantDialog } from "@/apps/tenants/DeleteTenantDialog";
import { ImpersonateTenantDialog } from "@/domains/platform/components/ImpersonateTenantDialog";
import { TenantFormDialog } from "@/apps/tenants/TenantFormDialog";
import {
  TenantProvisioningStates,
  useResendTenantInvitation,
  useRetryTenantProvisioning,
  useTenantsGridFetcher,
  type TenantRow,
} from "@/domains/platform/tenants";
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

/** While a tenant is being provisioned the list refreshes itself, so the state moves without a click. */
const PENDING_REFRESH_MS = 5000;

type EditorState = { mode: "create" } | { mode: "edit"; tenant: TenantRow; readOnly: boolean } | null;

export function TenantsView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Tenants");
  const canCreate = useIsGranted("Platform.Tenants.Create");
  const canUpdate = useIsGranted("Platform.Tenants.Update");
  const canDelete = useIsGranted("Platform.Tenants.Delete");
  const canImpersonate = useIsGranted("Platform.Tenants.Impersonation");
  const canCreateEdition = useIsGranted("Platform.Editions.Create");

  const retryProvisioning = useRetryTenantProvisioning();
  const resendInvitation = useResendTenantInvitation();

  const [refreshKey, setRefreshKey] = useState(0);
  const [hasPending, setHasPending] = useState(false);
  const [editor, setEditor] = useState<EditorState>(null);
  const [tenantToDelete, setTenantToDelete] = useState<TenantRow | undefined>();
  const [tenantToEnter, setTenantToEnter] = useState<TenantRow | undefined>();

  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const baseFetcher = useTenantsGridFetcher();
  const fetchTenants = useCallback(
    async (req: GridRequest) => {
      const result = await baseFetcher(req);
      setHasPending(
        result.rows.some(
          (row) =>
            row.provisioningState === TenantProvisioningStates.Queued ||
            row.provisioningState === TenantProvisioningStates.Running,
        ),
      );
      return result;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetcher, refreshKey],
  );

  useEffect(() => {
    if (!hasPending) return;
    const timer = window.setTimeout(invalidateGrid, PENDING_REFRESH_MS);
    return () => window.clearTimeout(timer);
  }, [hasPending, refreshKey, invalidateGrid]);

  const openRecord = useCallback(
    (row: TenantRow) => setEditor({ mode: "edit", tenant: row, readOnly: !canUpdate }),
    [canUpdate],
  );

  const runRowCommand = useCallback(
    async (action: () => Promise<void>, successKey: string) => {
      try {
        await action();
        toast.success(t(successKey));
        invalidateGrid();
      } catch (err) {
        toast.error(getApiErrorMessage(err, t));
      }
    },
    [invalidateGrid, t],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () =>
      canCreate
        ? [
            {
              id: "new-tenant",
              labelKey: "Tenants:NewTenant",
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create" }),
            },
          ]
        : [],
    [canCreate],
  );

  const rowActions = useCallback(
    (rows: TenantRow[]): GridRowAction[] => {
      const row = rows.length === 1 ? rows[0] : undefined;
      if (!row) return [];
      const failed = row.provisioningState === TenantProvisioningStates.Failed;
      const ready = row.provisioningState === TenantProvisioningStates.Succeeded;
      return [
        ...(canUpdate
          ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => openRecord(row) }]
          : []),
        ...(canCreate && failed
          ? [
              {
                id: "retry-provisioning",
                labelKey: "Tenants:RetryProvisioning",
                icon: RotateCcw,
                onClick: () =>
                  void runRowCommand(() => retryProvisioning.mutateAsync(row.id!), "Tenants:ProvisioningRequeued"),
              },
            ]
          : []),
        ...(canUpdate && ready
          ? [
              {
                id: "resend-invitation",
                labelKey: "Tenants:ResendInvitation",
                icon: MailPlus,
                onClick: () =>
                  void runRowCommand(() => resendInvitation.mutateAsync(row.id!), "Tenants:InvitationQueued"),
              },
            ]
          : []),
        ...(canImpersonate
          ? [{ id: "impersonate", labelKey: "Impersonation:LogInAsEllipsis", icon: LogIn, onClick: () => setTenantToEnter(row) }]
          : []),
        ...(canDelete
          ? [
              {
                id: "delete",
                labelKey: "Delete",
                icon: Trash2,
                destructive: true,
                onClick: () => setTenantToDelete(row),
              },
            ]
          : []),
      ];
    },
    [canCreate, canDelete, canImpersonate, canUpdate, openRecord, resendInvitation, retryProvisioning, runRowCommand],
  );

  const columnDefs = useMemo<ColDef<TenantRow>[]>(
    () => [
      { field: "name", headerName: t("Tenants:Name"), sortable: true, filter: false, flex: 1, minWidth: 180 },
      { field: "editionName", headerName: t("Tenants:Edition"), sortable: false, filter: false, minWidth: 140 },
      {
        field: "provisioningState",
        headerName: t("Tenants:Status"),
        cellRenderer: StatusCell,
        cellRendererParams: { entity: "TenantProvisioning" },
        sortable: false,
        filter: false,
        minWidth: 130,
      },
      { field: "databaseName", headerName: t("Tenants:Database"), sortable: false, filter: false, minWidth: 220 },
      { field: "serverName", headerName: t("Tenants:Server"), sortable: false, filter: false, minWidth: 160 },
      {
        field: "creationTime",
        headerName: t("Tenants:Created"),
        cellRenderer: DateTimeCell,
        sortable: true,
        filter: false,
        minWidth: 150,
      },
    ],
    [t],
  );

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<TenantRow>
            gridId="platform.tenants"
            columnDefs={columnDefs}
            fetchRows={fetchTenants}
            onOpen={openRecord}
            rowActions={rowActions}
          />
        </Suspense>
      </div>

      {editor && (
        <TenantFormDialog
          key={editor.mode === "edit" ? editor.tenant.id : "new"}
          mode={editor.mode}
          tenant={editor.mode === "edit" ? editor.tenant : undefined}
          readOnly={editor.mode === "edit" && editor.readOnly}
          open
          onClose={() => setEditor(null)}
          onSuccess={invalidateGrid}
          canCreateEdition={canCreateEdition}
        />
      )}

      {tenantToEnter && (
        <ImpersonateTenantDialog tenant={tenantToEnter} open onClose={() => setTenantToEnter(undefined)} />
      )}

      <DeleteTenantDialog
        tenant={tenantToDelete}
        onClose={() => setTenantToDelete(undefined)}
        onDeleted={invalidateGrid}
      />
    </div>
  );
}
