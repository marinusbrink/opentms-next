import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { INTAKE_ALIAS_KINDS, fetchIntakeAliases, intakeEnumLabel, useDeleteIntakeAlias, type IntakeAlias } from "@/domains/orders/order-intakes";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The learned aliases (design 72 "Review": "Intake aliases is not a nav node in this run; the list is reachable from the
 * review page"): every text the review taught the resolver, with what it resolves to and how often it was used. Delete
 * (Orders.OrderIntakes.Resolve) makes the next intake with that text ask again. */
export function IntakeAliasesTab() {
  const { t } = useL();
  const canDelete = useIsGranted("Orders.OrderIntakes.Resolve");
  const remove = useDeleteIntakeAlias();
  const [refreshKey, setRefreshKey] = useState(0);
  const [toDelete, setToDelete] = useState<IntakeAlias | null>(null);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const fetchRows = useCallback((request: GridRequest) => fetchIntakeAliases(request), [refreshKey]);

  const columns = useMemo<ColDef<IntakeAlias>[]>(
    () => [
      { field: "kind", headerName: t("Orders:IntakeAliasKind"), minWidth: 130, valueFormatter: (p) => intakeEnumLabel(t, "IntakeAliasKind", INTAKE_ALIAS_KINDS, p.value as number) },
      { field: "normalizedText", headerName: t("Orders:IntakeAliasText"), flex: 2, minWidth: 200 },
      { field: "targetName", headerName: t("Orders:IntakeAliasTarget"), flex: 1, minWidth: 160, sortable: false },
      { field: "scopeRelationName", headerName: t("Orders:IntakeAliasScope"), flex: 1, minWidth: 160, sortable: false },
      { field: "useCount", headerName: t("Orders:IntakeAliasUseCount"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "creationTime", headerName: t("Orders:IntakeAliasCreated"), minWidth: 150, cellRenderer: DateTimeCell },
    ],
    [t],
  );

  return (
    <div className="flex h-full min-h-96 flex-col" data-slot="intake-aliases">
      <p className="mb-3 text-label text-muted-foreground">{t("Orders:IntakeAliasesExplanation")}</p>
      <div className="min-h-0 flex-1">
        <LazyOpenTmsGrid<IntakeAlias>
          gridId="orders.transport.intakeAliases"
          columnDefs={columns}
          fetchRows={fetchRows}
          rowActions={(rows) =>
            canDelete && rows[0] ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => setToDelete(rows[0]) }] : []
          }
        />
      </div>
      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(next) => !next && setToDelete(null)}
        title={t("Orders:DeleteIntakeAlias")}
        description={t("Orders:DeleteIntakeAliasQuestion").replace("{0}", toDelete?.normalizedText ?? "")}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={async () => {
          if (!toDelete?.id) return;
          await remove.mutateAsync(toDelete.id);
          setToDelete(null);
          toast.success(t("Orders:IntakeAliasDeleted"));
          setRefreshKey((k) => k + 1);
        }}
      />
    </div>
  );
}
