import type { ColDef } from "ag-grid-community";
import { RefreshCw, RotateCw, Trash2, TriangleAlert } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { NumberCell, TimestampCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { ChoiceFilter } from "@/apps/admin/components/LogbookFilters";
import { StatCards, type StatCard } from "@/apps/admin/components/StatCards";
import {
  BACKGROUND_JOB_STATES,
  canDeleteJob,
  canRequeue,
  DEFAULT_BACKGROUND_JOB_STATE,
  jobStateKey,
  useBackgroundJobsGridFetcher,
  useBackgroundJobsOverview,
  useDeleteBackgroundJob,
  useRequeueBackgroundJob,
  type BackgroundJobRow,
  type BackgroundJobState,
} from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

export const BACKGROUND_JOBS_GRID_ID = "platform.maintenance.backgroundJobs";

/* The Background jobs tab: Hangfire's counts per state (click one to show that state), its servers
 * and queues, and the jobs of one state in the grid with requeue and delete for those allowed. */
export function BackgroundJobsTab() {
  const { t } = useL();
  const f = useFormat();
  const canManage = useIsGranted("Platform.Administration.Maintenance.BackgroundJobs.Manage");
  const overview = useBackgroundJobsOverview();
  const [state, setState] = useState<BackgroundJobState>(DEFAULT_BACKGROUND_JOB_STATE);
  const [refreshKey, setRefreshKey] = useState(0);
  const [toRequeue, setToRequeue] = useState<BackgroundJobRow | undefined>();
  const [toDelete, setToDelete] = useState<BackgroundJobRow | undefined>();

  const fetchRows = useBackgroundJobsGridFetcher(state, refreshKey);

  const refresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
    void overview.refetch();
  }, [overview]);

  const requeue = useRequeueBackgroundJob({
    onSuccess: () => {
      setToRequeue(undefined);
      toast.success(t("Maintenance:JobRequeued"));
      refresh();
    },
    onError: (error) => toast.error(getApiErrorMessage(error, t)),
  });

  const remove = useDeleteBackgroundJob({
    onSuccess: () => {
      setToDelete(undefined);
      toast.success(t("Maintenance:JobDeleted"));
      refresh();
    },
    onError: (error) => toast.error(getApiErrorMessage(error, t)),
  });

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [{ id: "refresh", labelKey: "Maintenance:Refresh", icon: RefreshCw, onClick: refresh }],
    [refresh],
  );

  const rowActions = useCallback(
    (rows: BackgroundJobRow[]): GridRowAction[] => {
      if (rows.length !== 1 || !canManage) return [];
      const row = rows[0];
      return [
        ...(canRequeue(row.state)
          ? [{ id: "requeue", labelKey: "Maintenance:Requeue", icon: RotateCw, onClick: () => setToRequeue(row) }]
          : []),
        ...(canDeleteJob(row.state)
          ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => setToDelete(row) }]
          : []),
      ];
    },
    [canManage],
  );

  const data = overview.data;
  const cards = useMemo<StatCard[]>(() => {
    if (!data?.isAvailable) return [];
    const counts: Record<BackgroundJobState, number> = {
      Enqueued: data.enqueued ?? 0,
      Scheduled: data.scheduled ?? 0,
      Processing: data.processing ?? 0,
      Succeeded: data.succeeded ?? 0,
      Failed: data.failed ?? 0,
      Deleted: data.deleted ?? 0,
      Recurring: data.recurring ?? 0,
    };
    return [
      ...BACKGROUND_JOB_STATES.map((s) => ({
        id: s,
        label: t(jobStateKey(s)),
        value: f.number(counts[s]),
        active: s === state,
        tone: s === "Failed" && counts[s] > 0 ? ("destructive" as const) : ("default" as const),
        onClick: () => setState(s),
      })),
      { id: "servers", label: t("Maintenance:Servers"), value: f.number(data.servers?.length ?? 0) },
      { id: "queues", label: t("Maintenance:Queues"), value: (data.queues ?? []).map((q) => `${q.name} (${f.number(q.length)})`).join(", ") },
    ];
  }, [data, state, t, f]);

  const columnDefs = useMemo<ColDef<BackgroundJobRow>[]>(
    () => [
      { field: "id", headerName: t("Maintenance:JobId"), sortable: false, filter: false, minWidth: 110 },
      { field: "jobName", headerName: t("Maintenance:Job"), sortable: false, filter: false, flex: 1, minWidth: 180 },
      { field: "stateChangedAt", headerName: t("Maintenance:StateChangedAt"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170 },
      { field: "nextRunAt", headerName: t("Maintenance:NextRun"), cellRenderer: TimestampCell, sortable: false, filter: false, minWidth: 170 },
      { field: "reason", headerName: t("Maintenance:Reason"), sortable: false, filter: false, flex: 2, minWidth: 200 },
      { field: "exceptionMessage", headerName: t("Maintenance:Exception"), sortable: false, filter: false, flex: 2, minWidth: 200 },
      { field: "cron", headerName: t("Maintenance:Cron"), sortable: false, filter: false, minWidth: 110 },
      { field: "queue", headerName: t("Maintenance:Queue"), sortable: false, filter: false, minWidth: 100 },
      { field: "tenantId", headerName: t("Maintenance:Tenant"), sortable: false, filter: false, minWidth: 220, hide: true },
      { field: "exceptionType", headerName: t("Maintenance:Exception"), sortable: false, filter: false, minWidth: 180, hide: true },
      { field: "serverId", headerName: t("Maintenance:ServerId"), sortable: false, filter: false, minWidth: 160, hide: true },
      { field: "durationMilliseconds", headerName: t("Maintenance:JobDuration"), cellRenderer: NumberCell, type: "rightAligned", sortable: false, filter: false, minWidth: 110, hide: true },
    ],
    [t],
  );

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      {data && !data.isAvailable && (
        <div className="px-6 pb-3">
          <Alert variant="warning">
            <TriangleAlert aria-hidden="true" />
            <AlertDescription>{t("Maintenance:JobsUnavailable")}</AlertDescription>
          </Alert>
        </div>
      )}
      {overview.isError && (
        <div className="px-6 pb-3">
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(overview.error, t)}</AlertDescription>
          </Alert>
        </div>
      )}
      {cards.length > 0 && <StatCards cards={cards} className="px-6 pb-3" />}

      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<BackgroundJobRow>
          gridId={BACKGROUND_JOBS_GRID_ID}
          columnDefs={columnDefs}
          fetchRows={fetchRows}
          rowActions={rowActions}
          filters={
            <ChoiceFilter
              label={t("Maintenance:State")}
              value={state}
              options={BACKGROUND_JOB_STATES}
              optionLabel={(s) => t(jobStateKey(s))}
              onChange={setState}
            />
          }
        />
      </div>

      <ConfirmDialog
        open={toRequeue != null}
        onOpenChange={(open) => {
          if (!open) setToRequeue(undefined);
        }}
        title={t("Maintenance:RequeueJob")}
        description={t("Maintenance:RequeueJobQuestion").replace("{0}", toRequeue?.id ?? "")}
        confirmLabel={t("Maintenance:Requeue")}
        busyLabel={t("Maintenance:Requeueing")}
        onConfirm={() => requeue.mutateAsync(toRequeue!.id!).then(() => undefined, () => undefined)}
      />

      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(open) => {
          if (!open) setToDelete(undefined);
        }}
        title={t("Maintenance:DeleteJob")}
        description={t("Maintenance:DeleteJobQuestion").replace("{0}", toDelete?.id ?? "")}
        confirmLabel={t("Delete")}
        busyLabel={t("Maintenance:Deleting")}
        destructive
        onConfirm={() => remove.mutateAsync(toDelete!.id!).then(() => undefined, () => undefined)}
      />
    </div>
  );
}
