import type { ColDef } from "ag-grid-community";
import { FileSearch, RefreshCw } from "lucide-react";
import { useCallback, useMemo, useState } from "react";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { BoolCell, BytesCell, NumberCell, PercentCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { DatabaseDetailDialog } from "@/apps/admin/components/DatabaseDetailDialog";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import { useDatabasesGridFetcher, useDatabasesSummary, type DatabaseRow } from "@/domains/platform/administration-maintenance";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

export const DATABASES_GRID_ID = "platform.maintenance.databases";

/* The Databases tab: every database the host knows with the server's figures (UI-PAGE-001 with a
 * summary strip above the grid), and the per-database detail on open. */
export function DatabasesTab() {
  const { t } = useL();
  const f = useFormat();
  const summary = useDatabasesSummary();
  const [refreshKey, setRefreshKey] = useState(0);
  const [openKey, setOpenKey] = useState<string | undefined>();

  const fetchRows = useDatabasesGridFetcher(refreshKey);

  const refresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
    void summary.refetch();
  }, [summary]);

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [{ id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: refresh }],
    [refresh],
  );

  const openRecord = useCallback((row: DatabaseRow) => setOpenKey(row.key ?? undefined), []);

  const rowActions = useCallback(
    (rows: DatabaseRow[]): GridRowAction[] =>
      rows.length === 1 ? [{ id: "open", labelKey: "Open", icon: FileSearch, onClick: () => openRecord(rows[0]) }] : [],
    [openRecord],
  );

  const cards = useMemo<StatCard[]>(() => {
    const s = summary.data;
    if (!s) return [];
    return [
      { id: "databases", label: t("Maintenance:DatabaseCount"), value: f.number(s.databaseCount) },
      { id: "tenant-databases", label: t("Maintenance:TenantDatabases"), value: f.number(s.tenantDatabaseCount) },
      { id: "shared", label: t("Maintenance:SharedTenants"), value: f.number(s.sharedTenantCount) },
      { id: "size", label: t("Maintenance:TotalSize"), value: f.bytes(s.totalSizeBytes) },
      ...(s.hostBlobStorage
        ? [
            {
              id: "blobs",
              label: t("Maintenance:BlobStorageHost"),
              value: `${f.bytes(s.hostBlobStorage.contentBytes)} (${f.number(s.hostBlobStorage.blobCount)})`,
            },
          ]
        : []),
      ...(s.unreachableCount
        ? [{ id: "unreachable", label: t("Maintenance:Unreachable"), value: f.number(s.unreachableCount), tone: "destructive" as const }]
        : []),
    ];
  }, [summary.data, t, f]);

  const columnDefs = useMemo<ColDef<DatabaseRow>[]>(
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
      { field: "serverName", headerName: t("Maintenance:Server"), sortable: true, filter: false, minWidth: 150 },
      { field: "sizeBytes", headerName: t("Maintenance:Size"), cellRenderer: BytesCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110 },
      { field: "activeConnections", headerName: t("Maintenance:Connections"), cellRenderer: NumberCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110 },
      {
        colId: "cacheHitRatio",
        headerName: t("Maintenance:CacheHitRatio"),
        valueGetter: (params) => (params.data?.cacheHitRatio == null ? null : params.data.cacheHitRatio * 100),
        cellRenderer: PercentCell,
        type: "rightAligned",
        sortable: true,
        filter: false,
        minWidth: 100,
      },
      { field: "transactionsCommitted", headerName: t("Maintenance:Commits"), cellRenderer: NumberCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110 },
      { field: "transactionsRolledBack", headerName: t("Maintenance:Rollbacks"), cellRenderer: NumberCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110 },
      { field: "deadlocks", headerName: t("Maintenance:Deadlocks"), cellRenderer: NumberCell, type: "rightAligned", sortable: true, filter: false, minWidth: 100 },
      { field: "sharedTenantCount", headerName: t("Maintenance:SharedTenants"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 110, hide: true },
      { field: "rowsReturned", headerName: t("Maintenance:RowsReturned"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "rowsFetched", headerName: t("Maintenance:RowsFetched"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "rowsInserted", headerName: t("Maintenance:RowsInserted"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "rowsUpdated", headerName: t("Maintenance:RowsUpdated"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "rowsDeleted", headerName: t("Maintenance:RowsDeleted"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "tempBytes", headerName: t("Maintenance:TempBytes"), cellRenderer: BytesCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110, hide: true },
      { field: "statisticsResetAt", headerName: t("Maintenance:StatisticsReset"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170, hide: true },
      { field: "isReachable", headerName: t("Maintenance:Reachable"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 90, hide: true },
    ],
    [t],
  );

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      {cards.length > 0 && <StatCards cards={cards} className="px-6 pb-3" />}

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<DatabaseRow>
          gridId={DATABASES_GRID_ID}
          columnDefs={columnDefs}
          fetchRows={fetchRows}
          onOpen={openRecord}
          rowActions={rowActions}
        />
      </div>

      {openKey && <DatabaseDetailDialog key={openKey} databaseKey={openKey} open onClose={() => setOpenKey(undefined)} />}
    </div>
  );
}
