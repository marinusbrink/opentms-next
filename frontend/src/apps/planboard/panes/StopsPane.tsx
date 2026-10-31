import { useCallback, useMemo, useRef, useState, type DragEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ColDef } from "ag-grid-community";
import { ArrowDown, ArrowUp, ClipboardCheck, GripVertical, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { DRAG_COLUMN, latenessMinutes, lower } from "@/apps/planboard/panes/lists/grid-helpers";
import { InlineTextCell, LatenessBadge, TripWarningsCell } from "@/apps/planboard/panes/lists/grid-support";
import { PaneMessage } from "@/apps/planboard/panes/lists/PaneMessage";
import { AddManualStopDialog, RecordActualsDialog } from "@/apps/planboard/panes/lists/stop-leg-dialogs";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell, DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridResponse, GridRowAction } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { waypointTypes } from "@/domains/orders/reference-data";
import { transportTripKeys, useRemoveStop, useTransportTrip, useTransportTripUpdates, useUpdateStop, type TransportTripDto, type TripStopDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 70, "Trip stops": the stops of the focused trip (the last trip selected on its own, P:Q16) from the trip itself:
 * position, activity, name, city, window, planned, estimated and actual times, a lateness badge (the stored estimate after
 * the window's end) and remarks. Drag a stop onto another to move it there (the existing stop move), edit the remarks in
 * place, record actuals, add or remove a manual stop. The trip's live notices refetch it. */

const GRID_ID = "planning.planboard.tripStops";
/** A stop dragged inside this pane (never leaves it, so it is not one of the board's payloads in dnd.ts). */
const DND_STOP = "application/x-opentms-stop";

/** The window of a stop: the earliest start and the earliest end of its legs' windows (the end the stop must meet). */
function stopWindow(stop: TripStopDto): { from: string | null; till: string | null } {
  const legs = stop.legs ?? [];
  const froms = legs.map((l) => l.windowFrom).filter((v): v is string => !!v).sort();
  const tills = legs.map((l) => l.windowTill).filter((v): v is string => !!v).sort();
  return { from: froms[0] ?? null, till: tills[0] ?? null };
}

export default function StopsPane(_props: PlanboardPaneProps) {
  const { t } = useL();
  const { focusedTripId } = usePlanboardSelection();
  const canView = useIsGranted("PlanningExecution.TransportTrips");
  const trip = useTransportTrip(focusedTripId ?? undefined);
  const queryClient = useQueryClient();

  useTransportTripUpdates([focusedTripId]);
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    predicate: (message) => focusedTripId != null && lower(message.targetId) === lower(focusedTripId),
    onIds: () => void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(focusedTripId ?? undefined) }),
    onResync: () => void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(focusedTripId ?? undefined) }),
    enabled: canView && focusedTripId != null,
  });

  if (!canView) return <PermissionDenied />;
  if (!focusedTripId) return <PaneMessage>{t("Planboard:Lists.SelectOneTripForStops")}</PaneMessage>;
  if (trip.isLoading) return <Skeleton className="m-2 h-full" />;
  if (trip.isError || !trip.data) return <PaneMessage tone="error">{trip.isError ? getApiErrorMessage(trip.error, t) : t("Planboard:Lists.TripNotFound")}</PaneMessage>;
  return <StopsGrid trip={trip.data} />;
}

function StopsGrid({ trip }: { trip: TransportTripDto }) {
  const { t } = useL();
  const f = useFormat();
  const canManage = useIsGranted("PlanningExecution.TransportTrips.ManageStops");
  const update = useUpdateStop();
  const remove = useRemoveStop();
  const [actuals, setActuals] = useState<TripStopDto | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<TripStopDto | null>(null);
  const waypointLookup = waypointTypes.useLookup();
  const stops = useMemo(() => [...(trip.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)), [trip.stops]);
  const open = trip.status === 1 || trip.status === 2;
  const editable = canManage && open && !trip.isArchived;

  const fetchRows = useMemo(
    () => async (_request: GridRequest): Promise<GridResponse<TripStopDto>> => ({ rows: stops, totalCount: stops.length, filteredCount: stops.length }),
    [stops],
  );

  const run = useCallback(
    async (action: Promise<unknown>, successKey: string) => {
      try {
        await action;
        toast.success(t(successKey));
      } catch (err) {
        toast.error(getApiErrorMessage(err, t));
        throw err;
      }
    },
    [t],
  );

  const hasActuals = (stop: TripStopDto | undefined) => stop?.actualArrival != null || stop?.actualDeparture != null;
  const moveTo = (stop: TripStopDto, position: number) =>
    void run(update.mutateAsync({ id: trip.id!, stopId: stop.id!, dto: { position } }), "Planboard:Lists.StopMoved").catch(() => undefined);

  /* Reorder: a stop's handle onto another stop's row moves it to that position. */
  const dragged = useRef<string | null>(null);
  const rowIdOf = (target: EventTarget | null) => (target instanceof Element ? target.closest(".ag-row[row-id]")?.getAttribute("row-id") ?? null : null);
  const dropHandlers = {
    onDragOver: (event: DragEvent<HTMLDivElement>) => {
      if (!editable || !Array.from(event.dataTransfer.types).includes(DND_STOP) || !rowIdOf(event.target)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
    },
    onDrop: (event: DragEvent<HTMLDivElement>) => {
      const targetId = rowIdOf(event.target);
      const sourceId = event.dataTransfer.getData(DND_STOP) || dragged.current;
      dragged.current = null;
      if (!editable || !targetId || !sourceId || targetId === sourceId) return;
      event.preventDefault();
      const source = stops.find((s) => s.id === sourceId);
      const target = stops.find((s) => s.id === targetId);
      if (source && target?.position != null) moveTo(source, target.position);
    },
  };

  const columns = useMemo<ColDef<TripStopDto>[]>(
    () => [
      ...(editable
        ? [
            {
              ...DRAG_COLUMN,
              cellRenderer: (p: { data: TripStopDto | undefined }) =>
                p.data && !hasActuals(p.data) ? (
                  <span
                    draggable
                    role="button"
                    aria-label={t("Planboard:Lists.DragToReorder")}
                    title={t("Planboard:Lists.DragToReorder")}
                    className="inline-flex h-full cursor-grab items-center text-muted-foreground"
                    onDragStart={(event) => {
                      dragged.current = p.data!.id ?? null;
                      event.dataTransfer.setData(DND_STOP, p.data!.id ?? "");
                      event.dataTransfer.effectAllowed = "move";
                    }}
                  >
                    <GripVertical className="size-4" aria-hidden="true" />
                  </span>
                ) : null,
            } satisfies ColDef<TripStopDto>,
          ]
        : []),
      { field: "position", headerName: t("PlanningExecution:Position"), minWidth: 70, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { colId: "activity", headerName: t("Orders:Activity"), minWidth: 110, sortable: false, valueGetter: (p) => waypointLookup.data?.find((w) => w.id === p.data?.waypointTypeId)?.name ?? "" },
      { colId: "name", headerName: t("PlanningExecution:Relation"), minWidth: 150, sortable: false, valueGetter: (p) => p.data?.relationName ?? p.data?.name },
      { field: "city", headerName: t("MasterData:City"), minWidth: 110, sortable: false },
      {
        colId: "window",
        headerName: t("PlanningExecution:Window"),
        minWidth: 170,
        sortable: false,
        valueGetter: (p) => {
          const w = p.data ? stopWindow(p.data) : { from: null, till: null };
          return w.from || w.till ? `${w.from ? f.dateTime(w.from) : ""} – ${w.till ? f.dateTime(w.till) : ""}` : "";
        },
      },
      { field: "plannedArrival", headerName: t("PlanningExecution:PlannedArrival"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "plannedDeparture", headerName: t("PlanningExecution:PlannedDeparture"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "estimatedArrival", headerName: t("PlanningExecution:EstimatedArrival"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "actualArrival", headerName: t("PlanningExecution:ActualArrival"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "actualDeparture", headerName: t("PlanningExecution:ActualDeparture"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      {
        colId: "lateness",
        headerName: t("Planboard:Lists.Lateness"),
        minWidth: 110,
        sortable: false,
        cellRenderer: (p: { data: TripStopDto | undefined }) => {
          if (!p.data || hasActuals(p.data)) return null;
          const till = stopWindow(p.data).till;
          return <LatenessBadge minutes={latenessMinutes(p.data.estimatedArrival, till)} known={p.data.estimatedArrival != null && till != null} />;
        },
      },
      {
        field: "remarks",
        headerName: t("PlanningExecution:Remarks"),
        minWidth: 160,
        sortable: false,
        cellRenderer: (p: { value: string | null | undefined; data: TripStopDto | undefined }) => (
          <InlineTextCell<TripStopDto>
            value={p.value}
            data={p.data}
            maxLength={2500}
            canEdit={() => editable}
            /* An empty text clears the remarks (the command keeps them on null). */
            onCommit={(stop, value) => run(update.mutateAsync({ id: trip.id!, stopId: stop.id!, dto: { remarks: value ?? "" } }), "PlanningExecution:TransportTripSaved")}
          />
        ),
      },
      { field: "isManual", headerName: t("PlanningExecution:ManualStop"), minWidth: 90, hide: true, sortable: false, cellRenderer: BoolCell },
      { colId: "warnings", headerName: t("PlanningExecution:Warnings"), minWidth: 90, sortable: false, cellRenderer: (p: { data: TripStopDto | undefined }) => <TripWarningsCell value={(trip.warnings ?? []).filter((w) => w.stopId != null && w.stopId === p.data?.id)} /> },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, editable, waypointLookup.data, trip.id, trip.warnings, run],
  );

  const rowActions = (rows: TripStopDto[]): GridRowAction[] => {
    const stop = rows[0];
    if (!stop || !editable) return [];
    const blocked = hasActuals(stop);
    return [
      { id: "up", labelKey: "Orders:MoveUp", icon: ArrowUp, disabled: (stop.position ?? 1) <= 1 || blocked, disabledReasonKey: "OpenTms.PlanningExecution:StopHasActuals", onClick: () => moveTo(stop, (stop.position ?? 1) - 1) },
      { id: "down", labelKey: "Orders:MoveDown", icon: ArrowDown, disabled: (stop.position ?? 0) >= stops.length || blocked, disabledReasonKey: "OpenTms.PlanningExecution:StopHasActuals", onClick: () => moveTo(stop, (stop.position ?? 0) + 1) },
      { id: "actuals", labelKey: "PlanningExecution:RecordActuals", icon: ClipboardCheck, onClick: () => setActuals(stop) },
      { id: "remove", labelKey: "PlanningExecution:RemoveStop", icon: Trash2, destructive: true, disabled: stop.isManual !== true, disabledReasonKey: "OpenTms.PlanningExecution:NotManualStop", onClick: () => setRemoving(stop) },
    ];
  };

  const commands: AppCommandBarCommand[] = editable ? [{ id: "add-stop", labelKey: "PlanningExecution:AddStop", icon: Plus, isPrimary: true, onClick: () => setAdding(true) }] : [];

  return (
    <div className="flex h-full min-h-0 flex-col" data-slot="planboard-stops-pane">
      <AppCommandBar
        commands={commands}
        trailing={<span className="text-label text-muted-foreground">{[trip.number, trip.name].filter(Boolean).join(" · ")}</span>}
      />
      <div className="min-h-0 flex-1 px-2 pb-2" {...dropHandlers}>
        <LazyOpenTmsGrid<TripStopDto> gridId={GRID_ID} columnDefs={columns} fetchRows={fetchRows} rowActions={editable ? rowActions : undefined} />
      </div>
      {actuals && <RecordActualsDialog tripId={trip.id!} stop={actuals} onClose={() => setActuals(null)} />}
      {adding && <AddManualStopDialog tripId={trip.id!} stopCount={stops.length} onClose={() => setAdding(false)} />}
      {removing && (
        <ConfirmDialog
          open
          onOpenChange={(next) => !next && setRemoving(null)}
          title={t("PlanningExecution:RemoveStop")}
          description={t("Planboard:Lists.RemoveStopQuestion").replace("{0}", removing.relationName ?? removing.name ?? "")}
          confirmLabel={t("PlanningExecution:RemoveStop")}
          destructive
          onConfirm={async () => {
            const stop = removing;
            setRemoving(null);
            await run(remove.mutateAsync({ id: trip.id!, stopId: stop.id! }), "PlanningExecution:StopRemoved").catch(() => undefined);
          }}
        />
      )}
    </div>
  );
}
