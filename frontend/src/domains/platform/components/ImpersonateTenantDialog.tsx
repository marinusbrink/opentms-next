import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridRequest, GridSelectionDto } from "@/components/ui/opentms-grid";
import { switchSession } from "@/auth/session-switch";
import { fetchTenantUsers, useStartImpersonation, type TenantUserRow } from "@/domains/platform/impersonation";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface ImpersonateTenantDialogProps {
  /** The tenant to log in to: a row of the Tenants app or of the user menu's tenant list. */
  tenant: { id?: string | null; name?: string | null };
  open: boolean;
  onClose: () => void;
}

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

/* "Log in as" (PO 2026-10-14, docs/designs/tenants-and-editions.md): an action dialog (UI-DLG-002) with a picker
 * grid of the tenant's users. A click selects; the primary needs exactly one user; a double-click on a row chooses
 * it directly. The browser then leaves for the auth server, which switches the session. Shared by the Tenants app and
 * the user menu's "Log in to a tenant…" (docs/designs/63-user-menu.md). */
export function ImpersonateTenantDialog({ tenant, open, onClose }: ImpersonateTenantDialogProps) {
  const { t } = useL();
  const start = useStartImpersonation();
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [error, setError] = useState<string | null>(null);

  const fetchUsers = useCallback((req: GridRequest) => fetchTenantUsers(tenant.id!, req), [tenant.id]);

  const selectedId = selection.mode === "Explicit" && selection.explicitIds.length === 1 ? selection.explicitIds[0] : undefined;

  const logInAs = async (userId: string | undefined) => {
    if (!userId || start.isPending) return;
    setError(null);
    try {
      const url = await start.mutateAsync({ tenantId: tenant.id!, userId });
      switchSession(url, { id: tenant.id!, name: tenant.name ?? "" });
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const columnDefs = useMemo<ColDef<TenantUserRow>[]>(
    () => [
      { field: "userName", headerName: t("Impersonation:UserName"), sortable: true, filter: false, flex: 1, minWidth: 140 },
      {
        colId: "fullName",
        headerName: t("Impersonation:FullName"),
        valueGetter: (params) => [params.data?.name, params.data?.surname].filter(Boolean).join(" "),
        sortable: false,
        filter: false,
        flex: 1,
        minWidth: 160,
      },
      { field: "isActive", headerName: t("Impersonation:Active"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 90 },
    ],
    [t],
  );

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="md"
      title={t("Impersonation:Title").replace("{0}", tenant.name ?? "")}
      isBusy={start.isPending}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={start.isPending}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => void logInAs(selectedId)} disabled={selectedId == null || start.isPending}>
            {start.isPending ? t("Impersonation:LoggingIn") : t("Impersonation:LogInAs")}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t("Impersonation:Explanation")}</p>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <div className="h-96 rounded-lg border bg-background">
          <LazyOpenTmsGrid<TenantUserRow>
            gridId="platform.tenants.impersonationUsers"
            columnDefs={columnDefs}
            fetchRows={fetchUsers}
            onSelectionChange={setSelection}
            onOpen={(row) => void logInAs(row.id)}
            clickSelects
          />
        </div>
      </div>
    </EntityDialog>
  );
}
