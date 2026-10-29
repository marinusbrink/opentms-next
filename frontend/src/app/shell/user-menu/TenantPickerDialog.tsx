import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridSelectionDto } from "@/components/ui/opentms-grid";
import { fetchImpersonationTenants, type ImpersonationTenantRow } from "@/domains/platform/impersonation";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface TenantPickerDialogProps {
  open: boolean;
  onClose: () => void;
  /** The tenant chosen; the caller opens the "Log in as" user picker for it. */
  onPick: (tenant: ImpersonationTenantRow) => void;
}

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

/* "Log in to a tenant…" for a Customer Success Manager (PO 2026-10-29, docs/designs/63-user-menu.md): every tenant, in
 * a server-paged grid with its search box, because the list can be long. A click selects; "Log in as…" (the title's
 * verb, UI-DLG-013; the ellipsis says a step follows) or a double-click goes on to the existing user picker. */
export function TenantPickerDialog({ open, onClose, onPick }: TenantPickerDialogProps) {
  const { t } = useL();
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [rows, setRows] = useState<Map<string, ImpersonationTenantRow>>(new Map());

  const fetchTenants = useMemo(
    () => async (request: Parameters<typeof fetchImpersonationTenants>[0]) => {
      const response = await fetchImpersonationTenants(request);
      setRows((previous) => {
        const next = new Map(previous);
        for (const row of response.rows) if (row.id) next.set(row.id, row);
        return next;
      });
      return response;
    },
    [],
  );

  const selectedId = selection.mode === "Explicit" && selection.explicitIds.length === 1 ? selection.explicitIds[0] : undefined;
  const selected = selectedId ? rows.get(selectedId) : undefined;

  const columnDefs = useMemo<ColDef<ImpersonationTenantRow>[]>(
    () => [{ field: "name", headerName: t("UserMenu:TenantName"), sortable: true, filter: false, flex: 1, minWidth: 200 }],
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
      title={t("UserMenu:AllTenantsTitle")}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            {t("Cancel")}
          </Button>
          <Button onClick={() => selected && onPick(selected)} disabled={!selected}>
            {t("Impersonation:LogInAsEllipsis")}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t("UserMenu:AllTenantsExplanation")}</p>
        <div className="h-96 rounded-lg border bg-background">
          <LazyOpenTmsGrid<ImpersonationTenantRow>
            gridId="platform.userMenu.impersonationTenants"
            columnDefs={columnDefs}
            fetchRows={fetchTenants}
            onSelectionChange={setSelection}
            onOpen={(row) => onPick(row)}
            clickSelects
          />
        </div>
      </div>
    </EntityDialog>
  );
}
