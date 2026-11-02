import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ColDef, GridApi } from "ag-grid-community";
import { Archive, CircleCheck, CircleX, ListOrdered, Pencil, Play, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { usePaneFilters } from "@/apps/planboard/filters";
import { isCancelledOutcome, useOutsideWindowConfirm } from "@/apps/planboard/outside-window";
import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { lower, ROW_DROP_CLASS, useGridApiCapture, useRowDropTarget } from "@/apps/planboard/panes/lists/grid-helpers";
import { DialogEditCell, InlineTextCell, TripWarningsCell } from "@/apps/planboard/panes/lists/grid-support";
import { PaneFilterBar } from "@/apps/planboard/panes/lists/PaneFilterBar";
import { CompleteTripDialog, TripResourcesDialog, type TripResourceKind } from "@/apps/planboard/panes/lists/trip-dialogs";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { useOpenPlanboardTrip } from "@/apps/planboard/trip-peek";
import type { PlanboardDragPayload } from "@/apps/planboard/dnd";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { CalculatingIndicator } from "@/components/ui/calculating-indicator";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { AmountCell, DateCell, DateTimeCell, NumberCell, PercentCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { NewTripDialog } from "@/domains/planning-execution/components/NewTripDialog";
import {
  fetchPlanboardTrips,
  fetchPlanboardTripsByIds,
  PLANBOARD_TRIP_FIELD,
  useAddTripDriver,
  useAssignLegsToTrip,
  useMoveLegsToTrip,
  useOptimizeTripStops,
  useSetTripField,
  type PlanboardListFilters,
  type PlanboardTripField,
} from "@/domains/planning-execution/planboard-lists";
import {
  TRANSPORT_TRIP_TARGET_TYPE,
  tripStatusName,
  useArchiveTrip,
  useCancelTrip,
  useDeleteTrip,
  useStartTrip,
  type TransportTripRow,
  type TransportTripStatus,
} from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { DerivedValueStatus } from "@/lib/realtime/record-updates";
import { useDerivedValueStates, useRecordUpdates } from "@/lib/realtime/use-record-updates";

/* Design 70, "Transport trips": the trips of the board's range and organisation units (or the pane's own, "Use board
 * filter" off) in a server-side OpenTmsGrid. Inline edit of the name, remarks and driver instructions (the Planboard's
 * header-field command); carrier, vehicles and drivers through the pencil in their cells (the existing set commands).
 * Planboard commands on a row carry the row's concurrency stamp (no extra read of the trip). Row actions: open (the trip page in the Transport app, or the
 * read-only peek without it: trip-peek.tsx), edit, start,
 * complete (ATD and ATA), optimise stops (202, a toast), archive, cancel, delete. New trip in the command bar. A row is a
 * drop target for legs (assign, or move when they come from another trip) and for a driver (add). The selection feeds
 * the board's selection bus; live notices refetch only the rows they concern. */

const GRID_ID = "planning.planboard.transportTrips";
const STATUS = { Planned: 1, InProgress: 2, Completed: 3, Cancelled: 4 } as const satisfies Record<string, TransportTripStatus>;
const isOpen = (row: TransportTripRow | undefined) => row?.status === STATUS.Planned || row?.status === STATUS.InProgress;
const isClosed = (row: TransportTripRow | undefined) => row?.status === STATUS.Completed || row?.status === STATUS.Cancelled;
const RESYNC_DEBOUNCE_MS = 300;

type Confirm = { kind: "cancel" | "archive" | "delete"; row: TransportTripRow };

export default function TripsPane({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const f = useFormat();
  const openTrip = useOpenPlanboardTrip();
  const selection = usePlanboardSelection();
  const paneFilters = usePaneFilters(instanceId);

  const canView = useIsGranted("PlanningExecution.TransportTrips");
  const canCreate = useIsGranted("PlanningExecution.TransportTrips.Create");
  const canUpdate = useIsGranted("PlanningExecution.TransportTrips.Update");
  const canDelete = useIsGranted("PlanningExecution.TransportTrips.Delete");
  const canCancel = useIsGranted("PlanningExecution.TransportTrips.Cancel");
  const canArchive = useIsGranted("PlanningExecution.TransportTrips.Archive");
  const canStart = useIsGranted("PlanningExecution.TransportTrips.Start");
  const canComplete = useIsGranted("PlanningExecution.TransportTrips.Complete");
  const canAssign = useIsGranted("PlanningExecution.TransportTrips.AssignLegs");
  const canManageStops = useIsGranted("PlanningExecution.TransportTrips.ManageStops");

  const filters = useMemo<PlanboardListFilters>(
    () => ({ from: paneFilters.filters.range.from, till: paneFilters.filters.range.till, organizationUnitIds: paneFilters.filters.organizationUnitIds }),
    [paneFilters.filters],
  );
  const filtersRef = useRef(filters);
  filtersRef.current = filters;
  const filtersKey = JSON.stringify(filters);

  const grid = useGridApiCapture<TransportTripRow>();
  /** The rows the grid holds, by lower-case id (live updates decide with it; actions read it). */
  const rows = useRef(new Map<string, TransportTripRow>());
  const knownIds = useRef(new Set<string>());
  const [visibleIds, setVisibleIds] = useState<string[]>([]);
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [dialog, setDialog] = useState<{ kind: "new" } | { kind: "complete"; row: TransportTripRow } | { kind: "resources"; id: string; resource: TripResourceKind } | null>(null);
  const [confirm, setConfirm] = useState<Confirm | null>(null);

  const remember = useCallback((list: TransportTripRow[]) => {
    for (const row of list) {
      if (!row.id) continue;
      rows.current.set(lower(row.id), row);
      knownIds.current.add(lower(row.id));
    }
    setVisibleIds((prev) => {
      const next = [...new Set([...prev, ...list.map((row) => lower(row.id)).filter(Boolean)])].slice(-400);
      return next.length === prev.length && next.every((id, i) => id === prev[i]) ? prev : next;
    });
  }, []);

  const fetchRows = useCallback(
    async (request: GridRequest) => {
      if (request.startRow === 0) {
        rows.current.clear();
        knownIds.current.clear();
      }
      const page = await fetchPlanboardTrips(request, filtersRef.current);
      remember(page.rows);
      return page;
    },
    // A new filter or a reload gives the grid a new fetcher: it reloads from the top.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [filtersKey, reloadKey, remember],
  );

  /* Reloading the visible blocks keeps the scroll position; without the api (no rows yet) the grid reloads. */
  const resyncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const resync = useCallback(() => {
    if (resyncTimer.current) clearTimeout(resyncTimer.current);
    resyncTimer.current = setTimeout(() => {
      const api = grid.current();
      if (api) api.refreshServerSide({ purge: false });
      else setReloadKey((k) => k + 1);
    }, RESYNC_DEBOUNCE_MS);
  }, [grid]);
  useEffect(() => () => void (resyncTimer.current && clearTimeout(resyncTimer.current)), []);

  /** Refetches the given trips by id and patches their rows; a trip new to the filters reloads the visible blocks. */
  const refreshTrips = useCallback(
    async (ids: readonly (string | null | undefined)[]) => {
      const wanted = [...new Set(ids.filter((id): id is string => !!id).map(lower))];
      if (wanted.length === 0) return;
      let fresh: TransportTripRow[];
      try {
        fresh = await fetchPlanboardTripsByIds(wanted, filtersRef.current);
      } catch {
        /* The patch failed; the visible blocks are reloaded instead (their own failure path is the grid's). */
        resync();
        return;
      }
      const api = grid.current();
      if (!api) {
        setReloadKey((k) => k + 1);
        return;
      }
      const found = new Map(fresh.filter((row) => row.id).map((row) => [lower(row.id), row]));
      const update = fresh.filter((row) => knownIds.current.has(lower(row.id)));
      const remove = wanted.filter((id) => knownIds.current.has(id) && !found.has(id)).map((id) => rows.current.get(id) ?? ({ id } as TransportTripRow));
      const added = fresh.some((row) => !knownIds.current.has(lower(row.id)));
      remember(update);
      for (const row of remove) {
        rows.current.delete(lower(row.id));
        knownIds.current.delete(lower(row.id));
      }
      if (update.length > 0 || remove.length > 0) api.applyServerSideTransaction({ update, remove });
      if (added) resync();
    },
    [grid, remember, resync],
  );

  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    filter: { range: paneFilters.filters.range, organizationUnitIds: paneFilters.filters.organizationUnitIds, knownIds: knownIds.current },
    onIds: (ids) => void refreshTrips(ids),
    onResync: resync,
    enabled: canView,
  });

  /* Derived values (totals, CO₂, warnings) arrive per record (flow 2): show the indicator, patch the row when done. */
  const states = useDerivedValueStates(TRANSPORT_TRIP_TARGET_TYPE, visibleIds);
  useRecordUpdates(TRANSPORT_TRIP_TARGET_TYPE, visibleIds, { queryKeysFor: () => [], onUpdate: (message) => message.status !== DerivedValueStatus.Pending && void refreshTrips([message.targetId]) });

  /* The selection bus → the grid: a trip selected elsewhere (a Scheduler bar, the map) is selected here too. */
  useEffect(() => {
    const api = grid.current();
    if (!api) return;
    const wanted = new Set(selection.selectedTripIds.map(lower));
    const current = new Set(selectedIds.map(lower));
    if (wanted.size === current.size && [...wanted].every((id) => current.has(id))) return;
    const toChange: { node: Parameters<GridApi<TransportTripRow>["setNodesSelected"]>[0]["nodes"][number]; value: boolean }[] = [];
    api.forEachNode((node) => {
      const id = lower(node.data?.id);
      if (!id) return;
      const should = wanted.has(id);
      if (node.isSelected() !== should) toChange.push({ node, value: should });
    });
    const on = toChange.filter((c) => c.value).map((c) => c.node);
    const off = toChange.filter((c) => !c.value).map((c) => c.node);
    if (off.length) api.setNodesSelected({ nodes: off, newValue: false, source: "api" });
    if (on.length) api.setNodesSelected({ nodes: on, newValue: true, source: "api" });
    // Only a change on the bus moves the grid's selection.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selection.selectedTripIds, grid]);

  const onSelectionChange = useCallback(
    (next: GridSelectionDto) => {
      const ids = next.mode === "Explicit" ? next.explicitIds : [];
      setSelectedIds(ids);
      selection.setSelectedTripIds(ids);
    },
    [selection],
  );

  const { mutateAsync: setFieldAsync } = useSetTripField();
  const assignLegs = useAssignLegsToTrip();
  const moveLegs = useMoveLegsToTrip();
  const addDriver = useAddTripDriver();
  const optimize = useOptimizeTripStops();
  const startTrip = useStartTrip();
  const cancelTrip = useCancelTrip();
  const archiveTrip = useArchiveTrip();
  const deleteTrip = useDeleteTrip();

  const outsideWindow = useOutsideWindowConfirm();

  /** A list-level action: toast on failure (UI-FB-004), the toast and the row patch on success; nothing when the user
   * cancelled (a leg drop outside the trip's window they did not confirm). */
  const run = useCallback(
    async (action: () => Promise<unknown>, successKey: string, tripIds: (string | null | undefined)[]) => {
      try {
        if (isCancelledOutcome(await action())) return false;
        toast.success(t(successKey));
        void refreshTrips(tripIds);
        return true;
      } catch (err) {
        toast.error(getApiErrorMessage(err, t));
        return false;
      }
    },
    [t, refreshTrips],
  );

  const drop = useRowDropTarget({
    accept: [...(canAssign ? (["legs"] as const) : []), ...(canUpdate ? (["driver"] as const) : [])],
    /* Legs and drivers go onto planned or running trips only. */
    canDrop: (rowId) => isOpen(rows.current.get(lower(rowId))),
    onDrop: (rowId: string, payload: PlanboardDragPayload) => {
      const target = rows.current.get(lower(rowId));
      if (payload.kind === "legs") {
        if (payload.fromTripId && lower(payload.fromTripId) === lower(rowId)) return;
        /* A drop outside the target trip's window asks first and retries with allowOutsideWindow (outside-window.tsx). */
        if (payload.fromTripId) {
          const fromTripId = payload.fromTripId;
          void run(
            () => outsideWindow.run((allowOutsideWindow) => moveLegs.mutateAsync({ fromTripId, toTripId: rowId, legIds: payload.legIds, allowOutsideWindow })),
            "Planboard:Lists.LegsMoved",
            [rowId, fromTripId],
          );
        } else {
          void run(() => outsideWindow.run((allowOutsideWindow) => assignLegs.mutateAsync({ tripId: rowId, legIds: payload.legIds, allowOutsideWindow, concurrencyStamp: target?.concurrencyStamp })), "Planboard:Lists.LegsAssigned", [rowId]);
        }
      } else if (payload.kind === "driver") {
        void run(() => addDriver.mutateAsync({ tripId: rowId, driverId: payload.driverId, concurrencyStamp: target?.concurrencyStamp }), "Planboard:Lists.DriverAdded", [rowId]);
      }
    },
  });

  const calculating = useCallback((id: string | undefined) => (id ? states.data?.get(id.toLowerCase()) : undefined), [states.data]);

  const columns = useMemo<ColDef<TransportTripRow>[]>(() => {
    const derived = (field: keyof TransportTripRow, headerName: string, cell: (value: number | null | undefined) => unknown, extra: Partial<ColDef<TransportTripRow>> = {}): ColDef<TransportTripRow> => ({
      field,
      headerName,
      minWidth: 110,
      type: "rightAligned",
      sortable: false,
      cellRenderer: (p: { value: number | null | undefined; data: TransportTripRow | undefined }) => (
        <span className="inline-flex items-center justify-end gap-1">
          <CalculatingIndicator state={calculating(p.data?.id)} showLabel={false} />
          {cell(p.value) as never}
        </span>
      ),
      ...extra,
    });
    /* A header field edited in place (`set-field` with the row's stamp); the patched row ends the optimistic value. */
    const textColumn = (field: "name" | "remarks" | "driverInstructions", headerName: string, tripField: PlanboardTripField, maxLength: number, minWidth: number, extra: Partial<ColDef<TransportTripRow>> = {}): ColDef<TransportTripRow> => ({
      field,
      headerName,
      minWidth,
      cellRenderer: (p: { value: string | null | undefined; data: TransportTripRow | undefined }) => (
        <InlineTextCell<TransportTripRow>
          value={p.value}
          data={p.data}
          maxLength={maxLength}
          canEdit={(row) => canUpdate && !row.isArchived}
          onCommit={async (row, value) => {
            try {
              await setFieldAsync({ tripId: row.id!, field: tripField, value, concurrencyStamp: row.concurrencyStamp });
              void refreshTrips([row.id]);
            } catch (err) {
              toast.error(getApiErrorMessage(err, t));
              throw err;
            }
          }}
        />
      ),
      ...extra,
    });
    const resourceCell = (resource: TripResourceKind, text: (row: TransportTripRow) => string) => (p: { data: TransportTripRow | undefined }) =>
      p.data ? <DialogEditCell text={text(p.data)} canEdit={canUpdate && isOpen(p.data)} onEdit={() => setDialog({ kind: "resources", id: p.data!.id!, resource })} /> : null;
    return [
      { field: "number", headerName: t("PlanningExecution:Number"), minWidth: 120 },
      { field: "status", headerName: t("PlanningExecution:Status"), minWidth: 120, cellRenderer: (p: { value: TransportTripStatus | undefined }) => <StatusCell entity="TransportTrip" value={tripStatusName(p.value)} /> },
      { field: "tripDate", headerName: t("PlanningExecution:TripDate"), minWidth: 110, cellRenderer: DateCell },
      textColumn("name", t("PlanningExecution:Name"), PLANBOARD_TRIP_FIELD.Name, 128, 150),
      { field: "transportTripTypeName", headerName: t("PlanningExecution:TransportTripType"), minWidth: 130, sortable: false },
      { field: "carrierName", headerName: t("PlanningExecution:Carrier"), minWidth: 150, cellRenderer: resourceCell("carrier", (row) => row.carrierName ?? "") },
      { colId: "vehicles", headerName: t("PlanningExecution:Vehicles"), minWidth: 140, sortable: false, valueGetter: (p) => (p.data?.vehicles ?? []).join(", "), cellRenderer: resourceCell("vehicles", (row) => (row.vehicles ?? []).join(", ")) },
      { colId: "drivers", headerName: t("PlanningExecution:Drivers"), minWidth: 140, sortable: false, valueGetter: (p) => (p.data?.drivers ?? []).join(", "), cellRenderer: resourceCell("drivers", (row) => (row.drivers ?? []).join(", ")) },
      { field: "plannedDeparture", headerName: t("PlanningExecution:PlannedDeparture"), minWidth: 140, cellRenderer: DateTimeCell },
      { field: "plannedArrival", headerName: t("PlanningExecution:PlannedArrival"), minWidth: 140, cellRenderer: DateTimeCell },
      { colId: "customers", headerName: t("PlanningExecution:Customers"), minWidth: 150, sortable: false, valueGetter: (p) => (p.data?.customers ?? []).join(", ") },
      { colId: "orders", headerName: t("PlanningExecution:Orders"), minWidth: 140, sortable: false, valueGetter: (p) => (p.data?.orderNumbers ?? []).join(", ") },
      { colId: "route", headerName: t("PlanningExecution:Route"), minWidth: 160, sortable: false, valueGetter: (p) => [p.data?.firstStopCity, p.data?.lastStopCity].filter(Boolean).join(" → ") },
      { field: "stopCount", headerName: t("PlanningExecution:Stops"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "legCount", headerName: t("PlanningExecution:Legs"), minWidth: 80, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      derived("loadFactor", t("PlanningExecution:LoadFactor"), (value) => PercentCell({ value: value == null ? null : value * 100 })),
      derived("loadingMeters", t("PlanningExecution:LoadingMeters"), (value) => f.number(value, { maxDecimals: 2 })),
      derived("plannedDistanceM", `${t("PlanningExecution:Distance")} (km)`, (value) => (value == null ? "" : f.number(value / 1000, { maxDecimals: 1 }))),
      derived("co2Kg", t("PlanningExecution:Co2Kg"), (value) => NumberCell({ value })),
      derived("costAmount", t("PlanningExecution:CostAmount"), (value) => AmountCell({ value })),
      derived("revenueAmount", t("PlanningExecution:RevenueAmount"), (value) => AmountCell({ value })),
      { field: "warnings", headerName: t("PlanningExecution:Warnings"), minWidth: 100, sortable: false, cellRenderer: (p: { value: TransportTripRow["warnings"] }) => <TripWarningsCell value={p.value} /> },
      textColumn("remarks", t("PlanningExecution:Remarks"), PLANBOARD_TRIP_FIELD.Remarks, 2500, 180, { sortable: false }),
      textColumn("driverInstructions", t("PlanningExecution:DriverInstructions"), PLANBOARD_TRIP_FIELD.DriverInstructions, 2500, 180, { sortable: false }),
      { field: "actualDeparture", headerName: t("PlanningExecution:ActualDeparture"), minWidth: 140, hide: true, cellRenderer: DateTimeCell },
      { field: "actualArrival", headerName: t("PlanningExecution:ActualArrival"), minWidth: 140, hide: true, cellRenderer: DateTimeCell },
    ];
  }, [t, f, calculating, canUpdate, setFieldAsync, refreshTrips]);

  if (!canView) return <PermissionDenied />;

  const rowOf = (id: string | undefined) => (id ? rows.current.get(lower(id)) : undefined);
  const single = selectedIds.length === 1 ? rowOf(selectedIds[0]) : undefined;

  const rowActions = (list: TransportTripRow[]): GridRowAction[] => {
    const row = list[0];
    const id = row?.id;
    if (!row || !id) return [];
    return [
      { id: "open", labelKey: "PlanningExecution:OpenTrip", onClick: () => openTrip(id) },
      ...(canUpdate && isOpen(row)
        ? [
            {
              id: "resources",
              labelKey: "PlanningExecution:Resources",
              icon: Pencil,
              children: [
                { id: "carrier", labelKey: "PlanningExecution:Carrier", onClick: () => setDialog({ kind: "resources", id, resource: "carrier" }) },
                { id: "vehicles", labelKey: "PlanningExecution:Vehicles", onClick: () => setDialog({ kind: "resources", id, resource: "vehicles" }) },
                { id: "drivers", labelKey: "PlanningExecution:Drivers", onClick: () => setDialog({ kind: "resources", id, resource: "drivers" }) },
              ],
            },
          ]
        : []),
      ...(canStart ? [{ id: "start", labelKey: "PlanningExecution:Start", icon: Play, disabled: row.status !== STATUS.Planned, disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => void run(() => startTrip.mutateAsync({ id }), "PlanningExecution:TransportTripStarted", [id]) }] : []),
      ...(canComplete ? [{ id: "complete", labelKey: "PlanningExecution:Complete", icon: CircleCheck, disabled: !isOpen(row), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => setDialog({ kind: "complete", row }) }] : []),
      ...(canManageStops ? [{ id: "optimize", labelKey: "Planboard:Lists.OptimizeStops", icon: ListOrdered, disabled: !isOpen(row), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => void run(() => optimize.mutateAsync(id), "Planboard:Lists.OptimizeStopsQueued", []) }] : []),
      ...(canArchive ? [{ id: "archive", labelKey: "PlanningExecution:Archive", icon: Archive, disabled: !isClosed(row), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => setConfirm({ kind: "archive", row }) }] : []),
      ...(canCancel ? [{ id: "cancel", labelKey: "PlanningExecution:CancelTrip", icon: CircleX, destructive: true, disabled: !isOpen(row), disabledReasonKey: "OpenTms.PlanningExecution:InvalidTransition", onClick: () => setConfirm({ kind: "cancel", row }) }] : []),
      ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => setConfirm({ kind: "delete", row }) }] : []),
    ];
  };

  const commands: AppCommandBarCommand[] = [
    ...(canCreate ? [{ id: "new", labelKey: "PlanningExecution:NewTrip", icon: Plus, isPrimary: true, onClick: () => setDialog({ kind: "new" }) }] : []),
    ...(canComplete ? [{ id: "complete", labelKey: "PlanningExecution:Complete", icon: CircleCheck, requiresSelection: true, disabled: !isOpen(single), disabledReasonKey: "PlanningExecution:SelectOneOpenTrip", onClick: () => single && setDialog({ kind: "complete", row: single }) }] : []),
    ...(canCancel ? [{ id: "cancel", labelKey: "PlanningExecution:CancelTrip", icon: CircleX, variant: "destructive" as const, requiresSelection: true, disabled: !isOpen(single), disabledReasonKey: "PlanningExecution:SelectOneOpenTrip", onClick: () => single && setConfirm({ kind: "cancel", row: single }) }] : []),
    ...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive" as const, requiresSelection: true, disabled: !single, disabledReasonKey: "PlanningExecution:SelectOneTrip", onClick: () => single && setConfirm({ kind: "delete", row: single }) }] : []),
  ];

  const confirmText = (c: Confirm) => {
    const number = c.row.number ?? "";
    switch (c.kind) {
      case "cancel":
        return { title: t("PlanningExecution:CancelTrip"), description: `${t("PlanningExecution:CancelTripQuestion").replace("{0}", number)} ${t("ThisCannotBeUndone")}`, label: t("PlanningExecution:CancelTrip"), destructive: true };
      case "archive":
        return { title: t("PlanningExecution:Archive"), description: t("PlanningExecution:ArchiveTripQuestion").replace("{0}", number), label: t("PlanningExecution:Archive"), destructive: false };
      case "delete":
        return { title: t("PlanningExecution:DeleteTransportTrip"), description: `${t("PlanningExecution:DeleteTransportTripQuestion").replace("{0}", number)} ${t("ThisCannotBeUndone")}`, label: t("Delete"), destructive: true };
    }
  };

  const runConfirm = async (c: Confirm) => {
    const id = c.row.id!;
    setConfirm(null);
    if (c.kind === "cancel") await run(() => cancelTrip.mutateAsync(id), "PlanningExecution:TransportTripCancelled", [id]);
    else if (c.kind === "archive") await run(() => archiveTrip.mutateAsync(id), "PlanningExecution:TransportTripArchived", [id]);
    else if (await run(() => deleteTrip.mutateAsync(id), "PlanningExecution:TransportTripDeleted", [id])) {
      if (selection.focusedTripId && lower(selection.focusedTripId) === lower(id)) selection.setFocusedTripId(null);
    }
  };

  const confirmProps = confirm ? confirmText(confirm) : null;

  return (
    <div className="flex h-full min-h-0 flex-col" data-slot="planboard-trips-pane">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className={`min-h-0 flex-1 px-2 pb-2 ${ROW_DROP_CLASS}`} {...drop}>
        <LazyOpenTmsGrid<TransportTripRow>
          gridId={GRID_ID}
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={onSelectionChange}
          clickSelects
          onGridReady={grid.capture}
          onOpen={(row) => row.id && openTrip(row.id)}
          rowActions={rowActions}
          filters={<PaneFilterBar instanceId={instanceId} paneFilters={paneFilters} />}
        />
      </div>
      {dialog?.kind === "new" && canCreate && (
        <NewTripDialog
          open
          openAfterCreate={false}
          onOpenChange={(next) => !next && setDialog(null)}
          onCreated={(id) => {
            resync();
            if (id) selection.setSelectedTripIds([id]);
          }}
        />
      )}
      {dialog?.kind === "complete" && <CompleteTripDialog trip={dialog.row} onClose={() => setDialog(null)} onDone={() => void refreshTrips([dialog.row.id])} />}
      {dialog?.kind === "resources" && <TripResourcesDialog tripId={dialog.id} kind={dialog.resource} onClose={() => setDialog(null)} onDone={() => void refreshTrips([dialog.id])} />}
      {outsideWindow.dialog}
      {confirm && confirmProps && (
        <ConfirmDialog
          open
          onOpenChange={(next) => !next && setConfirm(null)}
          title={confirmProps.title}
          description={confirmProps.description}
          confirmLabel={confirmProps.label}
          destructive={confirmProps.destructive}
          onConfirm={() => runConfirm(confirm)}
        />
      )}
    </div>
  );
}
