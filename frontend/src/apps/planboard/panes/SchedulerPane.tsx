import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, GripVertical, RefreshCw } from "lucide-react";
import { toast } from "sonner";

import { planboardDragKind, readDragPayload, setDragPayload } from "@/apps/planboard/dnd";
import { usePaneFilters } from "@/apps/planboard/filters";
import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { useOutsideWindowConfirm } from "@/apps/planboard/outside-window";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { BarContent } from "@/apps/planboard/panes/scheduler/BarContent";
import { CreateTripDialog } from "@/apps/planboard/panes/scheduler/CreateTripDialog";
import {
  DEFAULT_SCHEDULER_SETTINGS,
  hasConflicts,
  isReadOnlyStatus,
  mergeBars,
  moveConflicts,
  movedBar,
  pageResourceIds,
  pageTripIds,
  parseBarEventId,
  patchBar,
  planMove,
  sanitizeSchedulerSettings,
  toSchedulerView,
  type BarData,
  type MoveConflicts,
  type PlannedMove,
  type SchedulerPaneSettings,
} from "@/apps/planboard/panes/scheduler/model";
import { SchedulerHeader } from "@/apps/planboard/panes/scheduler/SchedulerHeader";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { usePaneSettings } from "@/apps/planboard/settings-store";
import { useOpenPlanboardTrip } from "@/apps/planboard/trip-peek";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { LazyResourceScheduler } from "@/components/ui/resource-scheduler-lazy";
import type { SchedulerEvent } from "@/components/ui/resource-scheduler-model";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import {
  fetchPlanboardBars,
  isPlanboardResourceMode,
  MAX_SCHEDULER_DAYS,
  planboardSchedulerKeys,
  useAddTripDriver,
  useAssignLegsToTrip,
  useChangeTripResource,
  useCreatePlanboardTrip,
  usePlanboardScheduler,
  useRescheduleTrip,
  type PlanboardBarDto,
  type PlanboardBlockDto,
  type PlanboardSchedulerDto,
  type PlanboardSchedulerQuery,
} from "@/domains/planning-execution/planboard-scheduler";
import { unavailabilityReasonKey } from "@/domains/planning-execution/resource-availability";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 70, the Scheduler pane (several instances, each with its own settings under `planboard.pane.<instanceId>`):
 * a page of resource rows of the chosen mode against the pane's window (≤ 14 days), bars per trip coloured by status,
 * blocks as background, the conflict outline. A drag in time reschedules, a drag to another row changes the resource
 * (both: two commands in that order); a drop that creates a conflict or lands on a block asks first (D10: warn, never
 * block). Completed and cancelled trips are read-only. Click selects (selection bus), double-click opens the trip
 * (trip-peek.tsx), an empty stretch creates a trip; legs dropped on a row create a trip with them, on a bar they are
 * assigned (outside the trip's window after a confirmation); a driver dropped on a bar is added, on an empty stretch it
 * creates a trip there (the create dialog) with that driver. A bar's grip drags the trip out (`DND_TRIP`, e.g. onto the
 * map). Moves are optimistic and reconciled with the returned stamp; live notices refetch only the bars that concern the
 * pane and, debounced, the page for the KPI strip; a reconnect or window focus refetches the page. */

/** How long after the last live bar refetch the page is refetched for the KPI strip. */
const KPI_REFRESH_DEBOUNCE_MS = 2000;

const PERMISSION = "PlanningExecution.TransportTrips";

export default function SchedulerPane(props: PlanboardPaneProps) {
  const canView = useIsGranted(PERMISSION);
  if (!canView) return <PermissionDenied />;
  return <SchedulerPaneBody {...props} />;
}

interface PendingMove {
  move: PlannedMove;
  bar: PlanboardBarDto;
  conflicts: MoveConflicts;
}

interface CreateAt {
  resourceId: string;
  resourceName: string;
  start: Date;
  /** A driver dropped on the empty stretch: added to the new trip. */
  driverId?: string;
}

function SchedulerPaneBody({ instanceId, initial }: PlanboardPaneProps) {
  const { t } = useL();
  const f = useFormat();
  const queryClient = useQueryClient();
  const openTrip = useOpenPlanboardTrip();
  const selection = usePlanboardSelection();
  const canUpdate = useIsGranted(`${PERMISSION}.Update`);
  const canCreate = useIsGranted(`${PERMISSION}.Create`);
  const canAssign = useIsGranted(`${PERMISSION}.AssignLegs`);

  const initialMode = initial?.mode;
  const defaults = useMemo<SchedulerPaneSettings>(
    () => ({ ...DEFAULT_SCHEDULER_SETTINGS, ...(isPlanboardResourceMode(initialMode) ? { mode: initialMode } : {}) }),
    [initialMode],
  );
  const { settings, update, isLoaded } = usePaneSettings<SchedulerPaneSettings>(instanceId, defaults, sanitizeSchedulerSettings);
  const paneFilters = usePaneFilters(instanceId, { maxDays: MAX_SCHEDULER_DAYS });
  const { range, organizationUnitIds } = paneFilters.filters;

  const [page, setPage] = useState(0);
  const pageKey = `${settings.mode}|${range.from}|${range.till}|${organizationUnitIds.join(",")}|${settings.search}|${settings.pageSize}`;
  const [pageFor, setPageFor] = useState(pageKey);
  if (pageFor !== pageKey) {
    /* Another mode, window, filter or search starts at the first page again. */
    setPageFor(pageKey);
    setPage(0);
  }

  const query: PlanboardSchedulerQuery = useMemo(
    () => ({ mode: settings.mode, range, organizationUnitIds, search: settings.search, skip: page * settings.pageSize, take: settings.pageSize }),
    [settings.mode, range, organizationUnitIds, settings.search, page, settings.pageSize],
  );
  const queryKey = useMemo(() => planboardSchedulerKeys.page(query), [query]);
  const scheduler = usePlanboardScheduler(query, isLoaded && paneFilters.isLoaded);
  const data = scheduler.data;

  const blockTitle = useCallback(
    (block: PlanboardBlockDto) => (block.unavailabilityId ? t(unavailabilityReasonKey(block.reason)) : t("Planboard:Scheduler.OutsideWorkingTime")),
    [t],
  );
  const view = useMemo(() => toSchedulerView(data, settings.mode, blockTitle), [data, settings.mode, blockTitle]);
  const names = useMemo(() => data?.names ?? {}, [data?.names]);
  const rowName = useCallback((rowId: string) => view.resources.find((r) => r.id === rowId)?.title ?? "", [view.resources]);

  const selectedTrips = useMemo(() => new Set(selection.selectedTripIds.map((id) => id.toLowerCase())), [selection.selectedTripIds]);
  const selectedEventIds = useMemo(() => view.events.filter((e) => e.data && selectedTrips.has(e.data.tripId.toLowerCase())).map((e) => e.id), [view.events, selectedTrips]);

  /* ── Cache patches ─────────────────────────────────────────────────────────────────────────────────────────── */

  const setData = useCallback(
    (patch: (dto: PlanboardSchedulerDto) => PlanboardSchedulerDto) => queryClient.setQueryData<PlanboardSchedulerDto>(queryKey, (dto) => (dto ? patch(dto) : dto)),
    [queryClient, queryKey],
  );
  const barOf = useCallback(
    (tripId: string) => queryClient.getQueryData<PlanboardSchedulerDto>(queryKey)?.bars?.find((b) => b.id?.toLowerCase() === tripId.toLowerCase()),
    [queryClient, queryKey],
  );

  /** Trips with a command in flight: their live notices wait for the command's own refetch. */
  const inFlight = useRef(new Set<string>());

  const refreshBars = useCallback(
    async (ids: readonly string[]) => {
      if (ids.length === 0) return;
      try {
        const fresh = await fetchPlanboardBars(ids);
        setData((dto) => mergeBars(dto, ids, fresh.bars, fresh.names));
      } catch (error) {
        console.error({ event: "PlanboardSchedulerRefreshFailed", instanceId });
        toast.error(getApiErrorMessage(error, t), { id: `planboard-scheduler-refresh-${instanceId}` });
      }
    },
    [setData, instanceId, t],
  );

  /* ── Live updates ──────────────────────────────────────────────────────────────────────────────────────────── */

  const resourceIds = useMemo(() => pageResourceIds(data), [data]);
  const tripIds = useMemo(() => pageTripIds(data), [data]);
  const refetchPage = scheduler.refetch;
  /* The KPI strip comes with the page: after live bar refetches the page is refetched once things are quiet (2 s). */
  const kpiTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scheduleKpiRefresh = useCallback(() => {
    if (kpiTimer.current) clearTimeout(kpiTimer.current);
    kpiTimer.current = setTimeout(() => {
      kpiTimer.current = null;
      void refetchPage();
    }, KPI_REFRESH_DEBOUNCE_MS);
  }, [refetchPage]);
  useEffect(() => () => void (kpiTimer.current && clearTimeout(kpiTimer.current)), []);
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    filter: { range, resourceIds, knownIds: tripIds },
    enabled: data != null,
    onIds: (ids) => {
      const wanted = ids.filter((id) => !inFlight.current.has(id.toLowerCase()));
      if (wanted.length === 0) return;
      void refreshBars(wanted).then(scheduleKpiRefresh);
    },
    onResync: () => void refetchPage(),
  });
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.resourceAvailability,
    filter: { range, resourceIds },
    enabled: data != null,
    onIds: () => void refetchPage(),
  });

  /* ── Commands ──────────────────────────────────────────────────────────────────────────────────────────────── */

  const reschedule = useRescheduleTrip();
  const changeResource = useChangeTripResource();
  const addDriver = useAddTripDriver();
  const assignLegs = useAssignLegsToTrip();
  const createTrip = useCreatePlanboardTrip();
  const { run: runOutsideWindow, dialog: outsideWindowDialog } = useOutsideWindowConfirm();

  const [pendingMove, setPendingMove] = useState<PendingMove | null>(null);
  const [createAt, setCreateAt] = useState<CreateAt | null>(null);

  const commitMove = useCallback(
    async (move: PlannedMove, bar: PlanboardBarDto) => {
      const key = move.tripId.toLowerCase();
      inFlight.current.add(key);
      const mode = settings.mode;
      setData((dto) => patchBar(dto, move.tripId, (b) => movedBar(b, move, mode)));
      let stamp = bar.concurrencyStamp ?? "";
      try {
        if (move.timeChanged) {
          const change = await reschedule.mutateAsync({
            tripId: move.tripId,
            plannedDeparture: move.start.toISOString(),
            plannedArrival: bar.plannedArrival ? move.end.toISOString() : null,
            concurrencyStamp: stamp,
          });
          stamp = change.concurrencyStamp ?? stamp;
          setData((dto) => patchBar(dto, move.tripId, (b) => ({ ...b, concurrencyStamp: stamp })));
        }
        if (move.rowChanged) {
          const change = await changeResource.mutateAsync({ tripId: move.tripId, mode, fromResourceId: move.fromRowId, toResourceId: move.toRowId, concurrencyStamp: stamp });
          stamp = change.concurrencyStamp ?? stamp;
          setData((dto) => patchBar(dto, move.tripId, (b) => ({ ...b, concurrencyStamp: stamp })));
        }
      } catch (error) {
        /* Back to the bar as it was; the refetch below brings the server's state (the first command may have succeeded). */
        setData((dto) => patchBar(dto, move.tripId, () => bar));
        toast.error(getApiErrorMessage(error, t));
      } finally {
        inFlight.current.delete(key);
        /* The server's truth: stop times, the tractor's default drivers (D11), the stamp. */
        await refreshBars([move.tripId]);
      }
    },
    [settings.mode, setData, reschedule, changeResource, refreshBars, t],
  );

  const onEventMove = useCallback(
    (eventId: string, start: Date, end: Date, fromResourceId: string, toResourceId: string) => {
      const parsed = parseBarEventId(eventId);
      if (!parsed) return;
      const bar = barOf(parsed.tripId);
      if (!bar || !canUpdate || isReadOnlyStatus(bar.status)) return;
      const move = planMove(bar, fromResourceId, toResourceId, start, end);
      if (!move) return;
      const conflicts = moveConflicts(move.tripId, move.toRowId, move.start, move.end, view.events, view.backgroundEvents);
      if (hasConflicts(conflicts)) setPendingMove({ move, bar, conflicts });
      else void commitMove(move, bar);
    },
    [barOf, canUpdate, view.events, view.backgroundEvents, commitMove],
  );

  const onEventClick = useCallback(
    (eventId: string, jsEvent: MouseEvent) => {
      const parsed = parseBarEventId(eventId);
      if (!parsed) return;
      if (jsEvent.ctrlKey || jsEvent.metaKey || jsEvent.shiftKey) {
        const current = selection.selectedTripIds;
        const has = current.some((id) => id.toLowerCase() === parsed.tripId.toLowerCase());
        selection.setSelectedTripIds(has ? current.filter((id) => id.toLowerCase() !== parsed.tripId.toLowerCase()) : [...current, parsed.tripId]);
      } else {
        selection.setSelectedTripIds([parsed.tripId]);
      }
    },
    [selection],
  );

  const onEventDoubleClick = useCallback(
    (eventId: string) => {
      const parsed = parseBarEventId(eventId);
      if (parsed) openTrip(parsed.tripId);
    },
    [openTrip],
  );

  const onSelectEmpty = useCallback(
    (resourceId: string, start: Date) => {
      if (!canCreate) return;
      setCreateAt({ resourceId, resourceName: rowName(resourceId), start });
    },
    [canCreate, rowName],
  );

  const acceptExternalDrag = useCallback(
    (types: readonly string[]) => {
      const kind = planboardDragKind(types);
      if (kind === "legs") return canCreate || canAssign;
      if (kind === "driver") return canUpdate || canCreate;
      return false;
    },
    [canCreate, canAssign, canUpdate],
  );

  const onExternalDrop = useCallback(
    async (resourceId: string, start: Date, dataTransfer: DataTransfer, targetEventId: string | null) => {
      const payload = readDragPayload(dataTransfer, ["legs", "driver"]);
      if (!payload) return;
      const target = targetEventId ? parseBarEventId(targetEventId) : null;
      const bar = target ? barOf(target.tripId) : undefined;
      if (bar?.id && isReadOnlyStatus(bar.status)) {
        toast.error(t("Planboard:Scheduler.TripReadOnly"));
        return;
      }
      try {
        if (payload.kind === "legs") {
          if (bar?.id) {
            if (!canAssign) return;
            const tripId = bar.id;
            /* Legs outside the trip's window ask first and are retried with allowOutsideWindow (outside-window.tsx). */
            const result = await runOutsideWindow((allowOutsideWindow) =>
              assignLegs.mutateAsync({ tripId, legIds: payload.legIds, concurrencyStamp: barOf(tripId)?.concurrencyStamp ?? bar.concurrencyStamp ?? "", allowOutsideWindow }),
            );
            if (result.cancelled) return;
            toast.success(t("Planboard:Scheduler.LegsAssigned").replace("{0}", bar.number ?? ""));
            await refreshBars([tripId]);
          } else {
            if (!canCreate) return;
            const change = await createTrip.mutateAsync({ mode: settings.mode, resourceId, plannedDeparture: start.toISOString(), legIds: payload.legIds });
            toast.success(t("Planboard:Scheduler.TripCreatedWithLegs"));
            if (change.id) await refreshBars([change.id]);
          }
        } else if (payload.kind === "driver") {
          if (!bar?.id) {
            /* An empty stretch: a new trip there with this driver (mode Driver: on the row's driver, else on the row's
             * vehicle, the driver added after). Without the create permission the driver goes on a bar only. */
            if (!canCreate) {
              toast.error(t("Planboard:Scheduler.DropDriverOnBar"));
              return;
            }
            setCreateAt({ resourceId, resourceName: rowName(resourceId), start, driverId: payload.driverId });
            return;
          }
          if (!canUpdate) return;
          await addDriver.mutateAsync({ tripId: bar.id, driverId: payload.driverId, concurrencyStamp: bar.concurrencyStamp ?? "" });
          toast.success(t("Planboard:Scheduler.DriverAdded").replace("{0}", bar.number ?? ""));
          await refreshBars([bar.id]);
        }
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
        if (bar?.id) await refreshBars([bar.id]);
      }
    },
    [barOf, t, canAssign, canCreate, canUpdate, assignLegs, createTrip, addDriver, settings.mode, refreshBars, runOutsideWindow, rowName],
  );

  const readOnly = useCallback((event: SchedulerEvent<BarData>) => !canUpdate || isReadOnlyStatus(event.data?.bar.status), [canUpdate]);
  const dragTripLabel = t("Planboard:Scheduler.DragTrip");
  const eventContent = useCallback(
    (event: SchedulerEvent<BarData>) =>
      event.data ? (
        <span className="flex min-w-0 items-center gap-0.5">
          {/* The grip drags the trip out of the Scheduler (HTML5 drag and drop, DND_TRIP); stopping the pointer events
              keeps FullCalendar from starting its own move of the bar. */}
          <span
            draggable
            role="button"
            aria-label={dragTripLabel}
            title={dragTripLabel}
            className="inline-flex shrink-0 cursor-grab items-center opacity-70"
            onPointerDown={(e) => e.stopPropagation()}
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
            onDragStart={(e) => {
              e.stopPropagation();
              setDragPayload(e.dataTransfer, { kind: "trip", tripId: event.data!.tripId });
            }}
          >
            <GripVertical className="size-3" aria-hidden="true" />
          </span>
          <BarContent data={event.data} fields={settings.barFields} names={names} mode={settings.mode} />
        </span>
      ) : (
        event.title
      ),
    [settings.barFields, names, settings.mode, dragTripLabel],
  );
  const resourceLabel = useCallback(
    (resource: { id: string; title: string }) => {
      const row = data?.rows?.find((r) => r.id === resource.id);
      return (
        <span className="flex min-w-0 flex-col leading-tight">
          <span className="truncate font-medium">{resource.title}</span>
          {(row?.code || row?.typeName) && <span className="truncate text-label text-muted-foreground">{[row.code !== resource.title ? row.code : null, row.typeName].filter(Boolean).join(" · ")}</span>}
        </span>
      );
    },
    [data?.rows],
  );

  /* ── Render ────────────────────────────────────────────────────────────────────────────────────────────────── */

  const idPrefix = `scheduler-${instanceId}`;
  const total = Number(data?.totalRows ?? 0);
  const firstRow = total === 0 ? 0 : page * settings.pageSize + 1;
  const lastRow = Math.min(total, (page + 1) * settings.pageSize);

  let body: ReactNode;
  if (scheduler.isError && !data) {
    body = (
      <div className="p-3">
        <Alert variant="destructive">
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{getApiErrorMessage(scheduler.error, t)}</span>
            <Button type="button" size="sm" variant="outline" onClick={() => void scheduler.refetch()}>
              <RefreshCw />
              {t("Planboard:Scheduler.Retry")}
            </Button>
          </AlertDescription>
        </Alert>
      </div>
    );
  } else if (!data) {
    body = <Skeleton className="m-2 h-full" />;
  } else if (view.resources.length === 0) {
    body = (
      <div role="status" className="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">
        {t(settings.search ? "Planboard:Scheduler.NoRowsForSearch" : "Planboard:Scheduler.NoRows")}
      </div>
    );
  } else {
    body = (
      <LazyResourceScheduler<BarData>
        resources={view.resources}
        events={view.events}
        backgroundEvents={view.backgroundEvents}
        range={range}
        zoom={settings.zoom}
        onEventMove={onEventMove}
        onEventClick={onEventClick}
        onEventDoubleClick={onEventDoubleClick}
        onSelectEmpty={canCreate ? onSelectEmpty : undefined}
        onExternalDrop={(resourceId, start, dt, targetEventId) => void onExternalDrop(resourceId, start, dt, targetEventId)}
        acceptExternalDrag={acceptExternalDrag}
        eventContent={eventContent}
        readOnly={readOnly}
        resourceLabel={resourceLabel}
        resourceAreaHeader={t(`Planboard:Scheduler.RowsHeader.${settings.mode}`)}
        selectedEventIds={selectedEventIds}
        aria-label={`${t("App:Planboard.Scheduler")} ${f.date(range.from)}`}
      />
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <SchedulerHeader idPrefix={idPrefix} settings={settings} update={update} paneFilters={paneFilters} kpis={data?.kpis} />
      {data?.barsTruncated && (
        <Alert variant="warning" className="mx-2 mt-2">
          <AlertDescription>{t("Planboard:Scheduler.BarsTruncated")}</AlertDescription>
        </Alert>
      )}
      <div className="relative min-h-0 flex-1">{body}</div>
      {total > 0 && (
        <div className="flex items-center justify-end gap-1 border-t border-border px-2 py-1 text-label text-muted-foreground">
          <span className="tabular-nums" aria-live="polite">
            {t("Planboard:Scheduler.RowsOf").replace("{0}", f.number(firstRow)).replace("{1}", f.number(lastRow)).replace("{2}", f.number(total))}
          </span>
          <PagerButton label={t("Planboard:Scheduler.PreviousPage")} disabled={page === 0} onClick={() => setPage((p) => Math.max(0, p - 1))}>
            <ChevronLeft />
          </PagerButton>
          <PagerButton label={t("Planboard:Scheduler.NextPage")} disabled={lastRow >= total} onClick={() => setPage((p) => p + 1)}>
            <ChevronRight />
          </PagerButton>
        </div>
      )}

      {pendingMove && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) setPendingMove(null);
          }}
          title={t("Planboard:Scheduler.ConfirmConflictTitle").replace("{0}", pendingMove.bar.number ?? "")}
          description={conflictDescription(pendingMove, t, f, rowName)}
          confirmLabel={t("Planboard:Scheduler.MoveAnyway")}
          onConfirm={() => {
            const { move, bar } = pendingMove;
            setPendingMove(null);
            void commitMove(move, bar);
          }}
        />
      )}

      {outsideWindowDialog}
      {createAt && (
        <CreateTripDialog
          mode={settings.mode}
          resourceId={createAt.resourceId}
          resourceName={createAt.resourceName}
          start={createAt.start}
          driverId={createAt.driverId}
          driverName={createAt.driverId ? (names[createAt.driverId] ?? names[createAt.driverId.toLowerCase()]) : undefined}
          onClose={() => setCreateAt(null)}
          onCreated={(change) => {
            if (change.id) {
              void refreshBars([change.id]);
              selection.setSelectedTripIds([change.id]);
            }
          }}
        />
      )}
    </div>
  );
}

function PagerButton({ label, disabled, onClick, children }: { label: string; disabled: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button type="button" variant="ghost" size="icon-xs" aria-label={label} disabled={disabled} onClick={onClick}>
            {children}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

/** The confirmation names what the drop collides with: the other trips on the row and the blocks. */
function conflictDescription(pending: PendingMove, t: (key: string) => string, f: ReturnType<typeof useFormat>, rowName: (id: string) => string): string {
  const parts: string[] = [];
  const row = rowName(pending.move.toRowId);
  if (pending.conflicts.trips.length > 0) {
    parts.push(t("Planboard:Scheduler.ConflictTrips").replace("{0}", row).replace("{1}", pending.conflicts.trips.join(", ")));
  }
  for (const block of pending.conflicts.blocks) {
    parts.push(t("Planboard:Scheduler.ConflictBlock").replace("{0}", row).replace("{1}", block.title ?? "").replace("{2}", f.dateTime(block.start)).replace("{3}", f.dateTime(block.end)));
  }
  parts.push(t("Planboard:Scheduler.ConflictQuestion"));
  return parts.join(" ");
}
