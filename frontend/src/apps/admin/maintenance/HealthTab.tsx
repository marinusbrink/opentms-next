import type { CellClassRules, ColDef } from "ag-grid-community";
import { FileSearch, RefreshCw, TriangleAlert } from "lucide-react";
import { useCallback, useMemo, useState } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { BoolCell, BytesCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { InstanceDetailDialog } from "@/apps/admin/components/InstanceDetailDialog";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import {
  healthStatusKey,
  instanceStatusKey,
  timeSpanToSeconds,
  useHealthReport,
  useInstancesGridFetcher,
  useInstancesOverview,
  type ApplicationInstanceRow,
} from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

export const INSTANCES_GRID_ID = "platform.maintenance.instances";

/* Status colours by tone token (UI-TOK-005). */
const statusClassRules: CellClassRules<ApplicationInstanceRow> = {
  "text-destructive": (params) => params.data?.status === "Unresponsive",
  "text-muted-foreground": (params) => params.data?.status === "Stopped",
};
const healthClassRules: CellClassRules<ApplicationInstanceRow> = {
  "text-destructive": (params) => params.data?.healthStatus === "Unhealthy",
  "text-tone-warning": (params) => params.data?.healthStatus === "Degraded",
};

function toneOf(status: string | null | undefined): StatCard["tone"] {
  if (status === "Unhealthy") return "destructive";
  if (status === "Degraded") return "warning";
  return "default";
}

/* The Health tab: the instance registry (every instance of every role: born, alive, dying) as a
 * grid, with this instance's live health in the cards. Opening a row shows that instance's last
 * health result and process facts. */
export function HealthTab() {
  const { t } = useL();
  const f = useFormat();
  const report = useHealthReport();
  const overview = useInstancesOverview();
  const [refreshKey, setRefreshKey] = useState(0);
  const [openId, setOpenId] = useState<string | undefined>();

  const fetchRows = useInstancesGridFetcher(refreshKey);

  const refresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
    void report.refetch();
    void overview.refetch();
  }, [report, overview]);

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [{ id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: refresh }],
    [refresh],
  );

  const openRecord = useCallback((row: ApplicationInstanceRow) => setOpenId(row.id ?? undefined), []);

  const rowActions = useCallback(
    (rows: ApplicationInstanceRow[]): GridRowAction[] =>
      rows.length === 1 ? [{ id: "open", labelKey: "Open", icon: FileSearch, onClick: () => openRecord(rows[0]) }] : [],
    [openRecord],
  );

  const data = report.data;
  const fleet = overview.data;
  const silence = f.durationSeconds(timeSpanToSeconds(fleet?.unresponsiveAfter));

  const cards = useMemo<StatCard[]>(() => {
    const cards: StatCard[] = [];
    if (data) {
      cards.push({ id: "status", label: t("Maintenance:Status"), value: t(healthStatusKey(data.status)), tone: toneOf(data.status) });
    }
    if (fleet) {
      cards.push(
        { id: "running", label: t("Maintenance:Instance.Running"), value: f.number(fleet.running) },
        {
          id: "unresponsive",
          label: t("Maintenance:Instance.Unresponsive"),
          value: f.number(fleet.unresponsive),
          tone: fleet.unresponsive ? "destructive" : "default",
        },
        { id: "stopped", label: t("Maintenance:Instance.Status.Stopped"), value: f.number(fleet.stopped) },
        { id: "heartbeat", label: t("Maintenance:Instance.HeartbeatPeriod"), value: f.durationSeconds(timeSpanToSeconds(fleet.heartbeatPeriod)) },
      );
    }
    return cards;
  }, [data, fleet, t, f]);

  const columnDefs = useMemo<ColDef<ApplicationInstanceRow>[]>(
    () => [
      {
        colId: "status",
        field: "status",
        headerName: t("Maintenance:Instance.Status"),
        valueGetter: (params) => (params.data ? t(instanceStatusKey(params.data.status)) : ""),
        cellClassRules: statusClassRules,
        sortable: true,
        filter: false,
        minWidth: 120,
      },
      { field: "role", headerName: t("Maintenance:Instance.Role"), sortable: true, filter: false, minWidth: 90 },
      { field: "machineName", headerName: t("Maintenance:Machine"), sortable: true, filter: false, flex: 1, minWidth: 150 },
      { field: "isCurrent", headerName: t("Maintenance:Instance.Current"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 110 },
      {
        colId: "healthStatus",
        field: "healthStatus",
        headerName: t("Maintenance:Health"),
        valueGetter: (params) => (params.data?.healthStatus ? t(healthStatusKey(params.data.healthStatus)) : ""),
        cellClassRules: healthClassRules,
        sortable: false,
        filter: false,
        minWidth: 100,
      },
      { field: "version", headerName: t("Maintenance:Version"), sortable: true, filter: false, flex: 1, minWidth: 160 },
      { field: "environment", headerName: t("Maintenance:Environment"), sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "startedAt", headerName: t("Maintenance:StartedAt"), cellRenderer: TimestampCell, sortable: true, filter: false, minWidth: 170 },
      { field: "lastHeartbeatAt", headerName: t("Maintenance:Instance.LastHeartbeat"), cellRenderer: TimestampCell, sortable: true, filter: false, minWidth: 170 },
      { field: "stoppedAt", headerName: t("Maintenance:Instance.StoppedAt"), cellRenderer: TimestampCell, sortable: true, filter: false, minWidth: 170 },
      {
        colId: "uptimeSeconds",
        headerName: t("Maintenance:Uptime"),
        valueGetter: (params) => (params.data ? f.durationSeconds(params.data.uptimeSeconds) : ""),
        sortable: false,
        filter: false,
        minWidth: 100,
      },
      { field: "workingSetBytes", headerName: t("Maintenance:WorkingSet"), cellRenderer: BytesCell, type: "rightAligned", sortable: true, filter: false, minWidth: 110, hide: true },
      { field: "managedMemoryBytes", headerName: t("Maintenance:ManagedMemory"), cellRenderer: BytesCell, type: "rightAligned", sortable: false, filter: false, minWidth: 120, hide: true },
      { field: "outboxDispatchEnabled", headerName: t("Maintenance:OutboxDispatcher"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 110, hide: true },
      { field: "processId", headerName: t("Maintenance:Instance.ProcessId"), sortable: false, filter: false, minWidth: 100, hide: true },
    ],
    [t, f],
  );

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="grid gap-3 px-6 pb-3">
        {(report.isError || overview.isError) && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(report.error ?? overview.error, t)}</AlertDescription>
          </Alert>
        )}
        {cards.length > 0 && <StatCards cards={cards} />}
        {fleet && !fleet.registryEnabled && (
          <Alert variant="warning">
            <TriangleAlert aria-hidden="true" />
            <AlertDescription>{t("Maintenance:Instance.RegistryOff")}</AlertDescription>
          </Alert>
        )}
        {fleet && <p className="text-label text-muted-foreground">{t("Maintenance:Instance.Notice").replace("{0}", silence)}</p>}
      </div>

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<ApplicationInstanceRow>
          gridId={INSTANCES_GRID_ID}
          columnDefs={columnDefs}
          fetchRows={fetchRows}
          onOpen={openRecord}
          rowActions={rowActions}
        />
      </div>

      {openId && <InstanceDetailDialog key={openId} instanceId={openId} open onClose={() => setOpenId(undefined)} />}
    </div>
  );
}
