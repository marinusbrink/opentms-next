import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, GripVertical, RefreshCw } from "lucide-react";
import { toast } from "sonner";

import { planboardDragKind, readDragPayload, setDragPayload, type PlanboardDragPayload } from "@/apps/planboard/dnd";
import { usePaneFilters } from "@/apps/planboard/filters";
import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { pulledTripIds } from "@/apps/planboard/order-drop-model";
import { useOrderDrop } from "@/apps/planboard/order-drop";
import { useOutsideWindowConfirm } from "@/apps/planboard/outside-window";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { BarContent } from "@/apps/planboard/panes/scheduler/BarContent";
import { CreateTripDialog } from "@/apps/planboard/panes/scheduler/CreateTripDialog";
import {
  blocksAt,
  DEFAULT_SCHEDULER_SETTINGS,
  hasConflicts,
  idsToRefetch,
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
  tripTypeChoice,
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
import type { SchedulerBackgroundEvent, SchedulerEvent } from "@/components/ui/resource-scheduler-model";
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
  type PlanboardCreateTripResultDto,
  type PlanboardBarDto,
  type PlanboardBlockDto,
  type PlanboardSchedulerDto,
  type PlanboardSchedulerQuery,
} from "@/domains/planning-execution/planboard-scheduler";
import { retryOnStaleStamp, useMoveLegsToTrip } from "@/domains/planning-execution/planboard-lists";
import { transportTripTypes } from "@/domains/planning-execution/reference-data";
import { unavailabilityReasonKey } from "@/domains/planning-execution/resource-availability";
import { TRANSPORT_TRIP_TARGET_TYPE } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { DerivedValueStatus } from "@/lib/realtime/record-updates";
import { derivedValueStatesKey, useDerivedValueStates } from "@/lib/realtime/use-record-updates";

/* Design 70, the Scheduler pane (several instances, each with its own settings under `planboard.pane.<instanceId>`):
 * a page of resource rows of the chosen mode against the pane's window (≤ 14 days), bars per trip coloured by status,
 * blocks as background, the conflict outline. A drag in time reschedules, a drag to another row changes the resource
 * (both: two commands in that order); a drop that creates a conflict or lands on a block asks first (D10: warn, never
 * block). Completed and cancelled trips are read-only. Click selects (selection bus), double-click opens the trip
 * (trip-peek.tsx), an empty stretch creates a trip. Drops from other panes (design 74 §10): legs or an order on a bar go on
 * that trip (outside its window after a confirmation, a stale stamp retried once), on an empty cell or a block (after the
 * warning) on a new trip there, the type picker asking when there is more than one type; a driver dropped on a bar is added,
 * on an empty stretch it creates a trip there (the create dialog) with that driver. A drop that cannot proceed says why. A
 * bar's grip drags the trip out (`DND_TRIP`, e.g. onto the map).
 *
 * After a command the bar is patched from the answer (the stamp; a move's times already are) and shows the indicator until
 * the calculation's notice arrives, which refetches it with the new values (design 74 §9, flow 2): a refetch before that would
 * read values known to be old. Live notices refetch only the bars that concern the pane and, debounced, the page for the KPI
 * strip; a notice during a command on that trip waits for its end; a reconnect or window focus refetches the page. */

/** How long after the last live bar refetch the page is refetched for the KPI strip. */
const KPI_REFRESH_DEBOUNCE_MS = 2000;

const PERMISSION = "PlanningExecution.TransportTrips";

/** The most trips whose calculation state the pane follows at once; the state read takes its ids in the URL. */
const MAX_FOLLOWED_CALCULATIONS = 100;

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
  /** Legs dropped on the cell: they go on the new trip. */
  legIds?: string[];
  /** An order dropped on the cell: its legs go on the new trip. */
  orderId?: string;
}

/** A drop on a block: the warning first, then as on an empty cell. */
interface PendingBlockDrop {
  rowId: string;
  blocks: SchedulerBackgroundEvent[];
  proceed: () => void;
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

  /** Trips with a command in flight: their live notices wait for the command's end (`deferred` remembers them). */
  const inFlight = useRef(new Set<string>());
  const deferred = useRef(new Set<string>());

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

  /* The trips the pane follows the calculation of: those it ran a command on and those a Pending notice named. Bounded, not
   * every bar on the page; a trip leaves when its values are up to date (a failed one stays, with its warning). */
  const [followed, setFollowed] = useState<readonly string[]>([]);
  const follow = useCallback((ids: readonly string[]) => setFollowed((current) => [...new Set([...current, ...ids.map((id) => id.toLowerCase())])].slice(-MAX_FOLLOWED_CALCULATIONS)), []);
  const unfollow = useCallback((ids: readonly string[]) => {
    if (ids.length > 0) setFollowed((current) => current.filter((id) => !ids.includes(id)));
  }, []);
  const calculations = useDerivedValueStates(TRANSPORT_TRIP_TARGET_TYPE, followed);
  const calculationsRef = useRef(calculations.data);
  calculationsRef.current = calculations.data;

  /** After a command that recalculates: the bar takes the answer's stamp and shows the indicator until the notice arrives. */
  const afterCommand = useCallback(
    (tripId: string | null | undefined, stamp: string | null | undefined) => {
      if (!tripId) return;
      if (stamp) setData((dto) => patchBar(dto, tripId, (b) => ({ ...b, concurrencyStamp: stamp })));
      follow([tripId]);
      void queryClient.invalidateQueries({ queryKey: derivedValueStatesKey(TRANSPORT_TRIP_TARGET_TYPE) });
    },
    [setData, follow, queryClient],
  );

  /** Marks trips as under a command for its duration; notices that came meanwhile are handled at its end. */
  const whileInFlight = useCallback(
    async <T,>(tripIds: readonly string[], command: () => Promise<T>): Promise<T> => {
      const keys = tripIds.map((id) => id.toLowerCase());
      keys.forEach((key) => inFlight.current.add(key));
      try {
        return await command();
      } finally {
        keys.forEach((key) => inFlight.current.delete(key));
        const waiting = keys.filter((key) => deferred.current.delete(key));
        if (waiting.length > 0) void refreshBars(waiting).then(scheduleKpiRefresh);
      }
    },
    [refreshBars, scheduleKpiRefresh],
  );

  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    filter: { range, resourceIds, knownIds: tripIds },
    enabled: data != null,
    onIds: (_ids, messages) => {
      follow(messages.filter((m) => m.status === DerivedValueStatus.Pending && !m.hints?.removed).map((m) => m.targetId));
      unfollow(messages.filter((m) => m.status === DerivedValueStatus.UpToDate).map((m) => m.targetId.toLowerCase()));
      const refetch = idsToRefetch(messages, (id) => calculationsRef.current?.get(id)?.status === DerivedValueStatus.Pending);
      const wanted = refetch.filter((id) => {
        if (!inFlight.current.has(id)) return true;
        deferred.current.add(id);
        return false;
      });
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
  const moveLegs = useMoveLegsToTrip();
  const createTrip = useCreatePlanboardTrip();
  const { run: runOutsideWindow, dialog: outsideWindowDialog } = useOutsideWindowConfirm();
  const orderDrop = useOrderDrop();
  /* Only with the right to create: the type picker's question comes from it. */
  const tripTypes = transportTripTypes.useLookup(canCreate);

  const [pendingMove, setPendingMove] = useState<PendingMove | null>(null);
  const [pendingBlockDrop, setPendingBlockDrop] = useState<PendingBlockDrop | null>(null);
  const [createAt, setCreateAt] = useState<CreateAt | null>(null);

  const commitMove = useCallback(
    async (move: PlannedMove, bar: PlanboardBarDto) => {
      const mode = settings.mode;
      setData((dto) => patchBar(dto, move.tripId, (b) => movedBar(b, move, mode)));
      let stamp = bar.concurrencyStamp ?? "";
      await whileInFlight([move.tripId], async () => {
        try {
          if (move.timeChanged) {
            const change = await reschedule.mutateAsync({
              tripId: move.tripId,
              plannedDeparture: move.start.toISOString(),
              plannedArrival: bar.plannedArrival ? move.end.toISOString() : null,
              concurrencyStamp: stamp,
            });
            stamp = change.concurrencyStamp ?? stamp;
            afterCommand(move.tripId, stamp);
          }
          if (move.rowChanged) {
            const change = await changeResource.mutateAsync({ tripId: move.tripId, mode, fromResourceId: move.fromRowId, toResourceId: move.toRowId, concurrencyStamp: stamp });
            stamp = change.concurrencyStamp ?? stamp;
            afterCommand(move.tripId, stamp);
          }
          /* The rest of the server's truth (stop times, the tractor's default drivers, D11) comes with the calculation's notice. */
        } catch (error) {
          /* Back to the bar as it was; the refetch brings the server's state (the first command may have succeeded). */
          setData((dto) => patchBar(dto, move.tripId, () => bar));
          toast.error(getApiErrorMessage(error, t));
          await refreshBars([move.tripId]);
        }
      });
    },
    [settings.mode, setData, reschedule, changeResource, refreshBars, afterCommand, whileInFlight, t],
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

  /* Every kind a drop can handle is let through whatever the permissions: the browser shows no reason for a refused drag, and
   * the drop itself says why it cannot proceed. A trip's own grip has nothing to do here. */
  const acceptExternalDrag = useCallback((types: readonly string[]) => {
    const kind = planboardDragKind(types);
    return kind === "legs" || kind === "driver" || kind === "order";
  }, []);

  const onExternalDropMiss = useCallback(() => toast.error(t("Planboard:Scheduler.DropNotOnRow")), [t]);

  /** A trip created by a drop: nothing on screen to patch, so its bar is read; the trips an order's legs left change too. */
  const afterCreated = useCallback(
    (change: PlanboardCreateTripResultDto) => {
      afterCommand(change.id, change.concurrencyStamp);
      const pulled = pulledTripIds(change);
      void refreshBars([...(change.id ? [change.id] : []), ...pulled]);
      if (change.id) selection.setSelectedTripIds([change.id]);
    },
    [afterCommand, refreshBars, selection],
  );

  /** Legs or an order on a new trip at this cell; the type picker asks when there is more than one type. */
  const createWith = useCallback(
    async (at: CreateAt) => {
      const choice = tripTypeChoice(tripTypes.data);
      if (choice.ask) {
        setCreateAt(at);
        return;
      }
      const change = await createTrip.mutateAsync({ transportTripTypeId: choice.typeId, mode: settings.mode, resourceId: at.resourceId, plannedDeparture: at.start.toISOString(), legIds: at.legIds, orderId: at.orderId });
      if (at.orderId) orderDrop.report(change, null);
      else toast.success(t("Planboard:Scheduler.TripCreatedWithLegs"));
      afterCreated(change);
    },
    [tripTypes.data, createTrip, settings.mode, orderDrop, t, afterCreated],
  );

  const dropOnEmptyCell = useCallback(
    async (payload: PlanboardDragPayload, at: CreateAt) => {
      try {
        if (payload.kind === "driver") {
          /* A new trip there with this driver (mode Driver: on the row's driver, else on the row's vehicle, the driver added after). */
          setCreateAt({ ...at, driverId: payload.driverId });
        } else if (payload.kind === "legs") {
          /* Legs on a trip move to another trip; a new trip takes unplanned legs only. */
          if (payload.fromTripId) toast.error(t("Planboard:Scheduler.PlannedLegsNeedBar"));
          else await createWith({ ...at, legIds: payload.legIds });
        } else if (payload.kind === "order") {
          if (await orderDrop.prepareNewTrip(payload.orderId)) await createWith({ ...at, orderId: payload.orderId });
        }
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
      }
    },
    [createWith, orderDrop, t],
  );

  const dropOnBar = useCallback(
    async (payload: PlanboardDragPayload, bar: PlanboardBarDto & { id: string }) => {
      const tripId = bar.id;
      const number = bar.number ?? "";
      const currentStamp = () => barOf(tripId)?.concurrencyStamp ?? bar.concurrencyStamp;
      if (payload.kind === "driver" ? !canUpdate : !canAssign) {
        toast.error(t(payload.kind === "driver" ? "Planboard:Scheduler.NoPermissionToUpdate" : "Planboard:Scheduler.NoPermissionToAssign"));
        return;
      }
      if (payload.kind === "legs" && payload.fromTripId?.toLowerCase() === tripId.toLowerCase()) {
        toast.info(t("Planboard:Scheduler.LegsAlreadyOnTrip").replace("{0}", number));
        return;
      }
      try {
        if (payload.kind === "legs") {
          const { fromTripId, legIds } = payload;
          if (fromTripId) {
            /* From the Legs pane: the legs leave their trip. */
            const result = await whileInFlight([tripId, fromTripId], () => runOutsideWindow((allowOutsideWindow) => moveLegs.mutateAsync({ fromTripId, toTripId: tripId, legIds, allowOutsideWindow })));
            if (result.cancelled) return;
            toast.success(t("Planboard:Scheduler.LegsAssigned").replace("{0}", number));
            afterCommand(fromTripId, result.value.id?.toLowerCase() === fromTripId.toLowerCase() ? result.value.concurrencyStamp : null);
            afterCommand(tripId, result.value.id?.toLowerCase() === tripId.toLowerCase() ? result.value.concurrencyStamp : null);
          } else {
            /* Legs outside the trip's window ask first and are retried with allowOutsideWindow (outside-window.tsx). */
            const result = await whileInFlight([tripId], () =>
              runOutsideWindow((allowOutsideWindow) => retryOnStaleStamp(queryClient, tripId, currentStamp(), (concurrencyStamp) => assignLegs.mutateAsync({ tripId, legIds, concurrencyStamp, allowOutsideWindow }))),
            );
            if (result.cancelled) return;
            toast.success(t("Planboard:Scheduler.LegsAssigned").replace("{0}", number));
            afterCommand(tripId, result.value.concurrencyStamp);
          }
        } else if (payload.kind === "order") {
          const result = await whileInFlight([tripId], () => orderDrop.dropOnTrip({ orderId: payload.orderId, tripId, tripNumber: number, stamp: currentStamp() }));
          if (!result) return;
          afterCommand(tripId, result.concurrencyStamp);
          /* The trips the legs left: their stamps moved and the answer has not got them. */
          const pulled = pulledTripIds(result);
          if (pulled.length > 0) {
            follow(pulled);
            await refreshBars(pulled);
          }
        } else if (payload.kind === "driver") {
          const change = await whileInFlight([tripId], () => addDriver.mutateAsync({ tripId, driverId: payload.driverId, concurrencyStamp: currentStamp() ?? "" }));
          toast.success(t("Planboard:Scheduler.DriverAdded").replace("{0}", number));
          afterCommand(tripId, change.concurrencyStamp);
        }
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
        await refreshBars([tripId]);
      }
    },
    [barOf, canAssign, canUpdate, t, whileInFlight, runOutsideWindow, moveLegs, assignLegs, addDriver, queryClient, orderDrop, afterCommand, follow, refreshBars],
  );

  const onExternalDrop = useCallback(
    async (resourceId: string, start: Date, dataTransfer: DataTransfer, targetEventId: string | null) => {
      /* Read now: the browser empties the drag's data when this event ends. */
      const payload = readDragPayload(dataTransfer, ["legs", "driver", "order"]);
      if (!payload) {
        toast.error(t("Planboard:Scheduler.DropNotUnderstood"));
        return;
      }
      const target = targetEventId ? parseBarEventId(targetEventId) : null;
      if (target) {
        /* A bar on screen need not be in the cache of the current page (the previous page stays while the next loads): it is
         * read by id, and never falls through to creating a trip. */
        let bar = barOf(target.tripId);
        if (!bar) {
          try {
            bar = (await fetchPlanboardBars([target.tripId])).bars[0];
          } catch (error) {
            toast.error(getApiErrorMessage(error, t));
            return;
          }
        }
        if (!bar?.id) {
          toast.error(t("Planboard:Scheduler.TripGone"));
          void refetchPage();
          return;
        }
        if (isReadOnlyStatus(bar.status)) {
          toast.error(t("Planboard:Scheduler.TripReadOnly"));
          return;
        }
        await dropOnBar(payload, { ...bar, id: bar.id });
        return;
      }
      if (!canCreate) {
        toast.error(t(payload.kind === "driver" ? "Planboard:Scheduler.DropDriverOnBar" : "Planboard:Scheduler.NoPermissionToCreate"));
        return;
      }
      if (payload.kind !== "driver" && !canAssign) {
        toast.error(t("Planboard:Scheduler.NoPermissionToAssign"));
        return;
      }
      const at: CreateAt = { resourceId, resourceName: rowName(resourceId), start };
      const proceed = () => void dropOnEmptyCell(payload, at);
      const blocks = blocksAt(resourceId, start, view.backgroundEvents);
      if (blocks.length > 0) setPendingBlockDrop({ rowId: resourceId, blocks, proceed });
      else proceed();
    },
    [barOf, t, canCreate, canAssign, rowName, refetchPage, dropOnBar, dropOnEmptyCell, view.backgroundEvents],
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
          <BarContent data={event.data} fields={settings.barFields} names={names} mode={settings.mode} calculation={calculations.data?.get(event.data.tripId.toLowerCase())} />
        </span>
      ) : (
        event.title
      ),
    [settings.barFields, names, settings.mode, dragTripLabel, calculations.data],
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
        onExternalDropMiss={onExternalDropMiss}
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

      {pendingBlockDrop && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) setPendingBlockDrop(null);
          }}
          title={t("Planboard:Scheduler.ConfirmBlockTitle")}
          description={blockDescription(pendingBlockDrop, t, f, rowName)}
          confirmLabel={t("Planboard:Scheduler.CreateAnyway")}
          onConfirm={() => {
            const { proceed } = pendingBlockDrop;
            setPendingBlockDrop(null);
            proceed();
          }}
        />
      )}
      {outsideWindowDialog}
      {orderDrop.dialog}
      {createAt && (
        <CreateTripDialog
          mode={settings.mode}
          resourceId={createAt.resourceId}
          resourceName={createAt.resourceName}
          start={createAt.start}
          driverId={createAt.driverId}
          driverName={createAt.driverId ? (names[createAt.driverId] ?? names[createAt.driverId.toLowerCase()]) : undefined}
          legIds={createAt.legIds}
          orderId={createAt.orderId}
          onClose={() => setCreateAt(null)}
          onCreated={(change) => {
            if (createAt.orderId) orderDrop.report(change, null);
            afterCreated(change);
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

/** The warning of a drop on a block names the row and the block. */
function blockDescription(pending: PendingBlockDrop, t: (key: string) => string, f: ReturnType<typeof useFormat>, rowName: (id: string) => string): string {
  const row = rowName(pending.rowId);
  const parts = pending.blocks.map((block) => t("Planboard:Scheduler.ConflictBlock").replace("{0}", row).replace("{1}", block.title ?? "").replace("{2}", f.dateTime(block.start)).replace("{3}", f.dateTime(block.end)));
  return [...parts, t("Planboard:Scheduler.ConfirmBlockQuestion")].join(" ");
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
