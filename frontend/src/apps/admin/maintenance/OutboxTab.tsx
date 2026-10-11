import type { ColDef } from "ag-grid-community";
import { RefreshCw, Send, TriangleAlert } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { BoolCell, NumberCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import {
  timeSpanToMinutes,
  useDispatchOutbox,
  useOutboxGridFetcher,
  useOutboxOverview,
  type OutboxRow,
} from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

export const OUTBOX_GRID_ID = "platform.maintenance.outbox";

/* The Outbox tab: one row per database outbox (the host sees them all, a tenant its own) with
 * what is waiting and what the dispatcher did last; "Dispatch now" per row for those allowed. */
export function OutboxTab() {
  const { t } = useL();
  const f = useFormat();
  const canDispatch = useIsGranted("Platform.Administration.Maintenance.Outbox.Dispatch");
  const overview = useOutboxOverview();
  const [refreshKey, setRefreshKey] = useState(0);

  const fetchRows = useOutboxGridFetcher(refreshKey);
  const refresh = useCallback(() => setRefreshKey((k) => k + 1), []);

  const dispatch = useDispatchOutbox({
    onSuccess: (result) => {
      toast.success(t("Maintenance:OutboxDispatched").replace("{0}", f.number(result.publishedCount)));
      refresh();
    },
    onError: (error) => toast.error(getApiErrorMessage(error, t)),
  });

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [{ id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: refresh }],
    [refresh],
  );

  const rowActions = useCallback(
    (rows: OutboxRow[]): GridRowAction[] =>
      rows.length === 1 && canDispatch && !rows[0].isSharedTier
        ? [
            {
              id: "dispatch",
              labelKey: "Maintenance:DispatchNow",
              icon: Send,
              disabled: dispatch.isPending,
              onClick: () => dispatch.mutate(rows[0].tenantId ?? null),
            },
          ]
        : [],
    [canDispatch, dispatch],
  );

  const instance = overview.data?.instanceName ?? "";
  const cards = useMemo<StatCard[]>(() => {
    const o = overview.data;
    if (!o) return [];
    return [
      { id: "dispatch", label: t("Maintenance:DispatchEnabled"), value: t(o.dispatchEnabled ? "Maintenance:DispatchOn" : "Maintenance:DispatchOff") },
      { id: "sweep", label: t("Maintenance:SweepPeriod"), value: f.duration(timeSpanToMinutes(o.sweepPeriod)) },
      { id: "batch", label: t("Maintenance:BatchSize"), value: f.number(o.batchSize) },
      { id: "instance", label: t("Maintenance:Instance"), value: instance },
    ];
  }, [overview.data, t, f, instance]);

  const columnDefs = useMemo<ColDef<OutboxRow>[]>(
    () => [
      { field: "databaseName", headerName: t("Maintenance:DatabaseName"), sortable: true, filter: false, flex: 1, minWidth: 160 },
      {
        colId: "role",
        headerName: t("Maintenance:Role"),
        valueGetter: (params) => (params.data ? t(params.data.isHost ? "Maintenance:RoleHost" : "Maintenance:RoleTenant") : ""),
        sortable: false,
        filter: false,
        minWidth: 90,
      },
      { field: "tenantName", headerName: t("Maintenance:Tenant"), sortable: true, filter: false, flex: 1, minWidth: 140 },
      { field: "pendingCount", headerName: t("Maintenance:Pending"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 100 },
      { field: "oldestPendingAt", headerName: t("Maintenance:OldestPending"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170 },
      { field: "lastRunAt", headerName: t("Maintenance:LastRun"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170 },
      { field: "lastPublishedCount", headerName: t("Maintenance:LastPublished"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 110 },
      {
        colId: "status",
        headerName: t("Maintenance:Status"),
        valueGetter: (params) => {
          const row = params.data;
          if (!row) return "";
          if (row.isSharedTier) return t("Maintenance:SharedTierOutbox");
          if (!row.isReachable) return t("Maintenance:Unreachable");
          return row.lastError ?? "";
        },
        sortable: false,
        filter: false,
        flex: 2,
        minWidth: 200,
      },
      { field: "lastErrorAt", headerName: t("Maintenance:LastErrorAt"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170, hide: true },
      { field: "sharedTenantCount", headerName: t("Maintenance:SharedTenants"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 110, hide: true },
      { field: "isReachable", headerName: t("Maintenance:Reachable"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 90, hide: true },
    ],
    [t],
  );

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      {cards.length > 0 && (
        <div className="grid gap-3 px-6 pb-3">
          <StatCards cards={cards} />
          <Alert variant="warning">
            <TriangleAlert aria-hidden="true" />
            <AlertDescription>{t("Maintenance:OutboxRegistryNotice").replace("{0}", instance)}</AlertDescription>
          </Alert>
        </div>
      )}

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<OutboxRow> gridId={OUTBOX_GRID_ID} columnDefs={columnDefs} fetchRows={fetchRows} rowActions={rowActions} />
      </div>
    </div>
  );
}
