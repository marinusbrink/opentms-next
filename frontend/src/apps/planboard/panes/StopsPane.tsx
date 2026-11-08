import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { toast } from "sonner";

import { PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { lower } from "@/apps/planboard/panes/lists/grid-helpers";
import { PaneMessage } from "@/apps/planboard/panes/lists/PaneMessage";
import { AddManualStopDialog, RecordActualsDialog } from "@/apps/planboard/panes/lists/stop-leg-dialogs";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { StopTimeline } from "@/apps/planboard/panes/stops/StopTimeline";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { waypointTypes } from "@/domains/orders/reference-data";
import { StopDetailDialog } from "@/domains/planning-execution/components/StopDetailDialog";
import { transportTripKeys, useRemoveStop, useTransportTrip, useTransportTripUpdates, useUpdateStop, type TransportTripDto, type TripStopDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 74 §2 (replacing the grid of design 70, "Trip stops"): the stops of the focused trip (the last trip selected on its
 * own, P:Q16) as a timeline with a "now" marker. The stops come with the trip and its live notices refetch it. Drag a stop
 * onto another to move it there (blocked once it has actual times), edit the remarks in place, record actuals, add or
 * remove a manual stop. Stop selection is two-way with the map (design 70 "Map"): a click selects the stop on the selection
 * bus, and the bus's stop is highlighted here and scrolled into view. A double click opens the detail window (§3). */

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
  return <StopsOfTrip trip={trip.data} />;
}

function StopsOfTrip({ trip }: { trip: TransportTripDto }) {
  const { t } = useL();
  const canManage = useIsGranted("PlanningExecution.TransportTrips.ManageStops");
  const update = useUpdateStop();
  const remove = useRemoveStop();
  const [actuals, setActuals] = useState<TripStopDto | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<TripStopDto | null>(null);
  const [detailStopId, setDetailStopId] = useState<string | null>(null);
  const waypointLookup = waypointTypes.useLookup();
  const { selectedStopId, setSelectedStopId } = usePlanboardSelection();
  const stops = useMemo(() => [...(trip.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)), [trip.stops]);
  const open = trip.status === 1 || trip.status === 2;
  const editable = canManage && open && !trip.isArchived;

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

  const moveTo = (stop: TripStopDto, position: number) =>
    void run(update.mutateAsync({ id: trip.id!, stopId: stop.id!, dto: { position } }), "Planboard:Lists.StopMoved").catch(() => undefined);

  const commands: AppCommandBarCommand[] = editable ? [{ id: "add-stop", labelKey: "PlanningExecution:AddStop", icon: Plus, isPrimary: true, onClick: () => setAdding(true) }] : [];
  /* The window shows the stop as the trip has it now: a live notice refreshes its read-only times, and it closes when the stop is gone. */
  const detailStop = detailStopId ? stops.find((stop) => stop.id === detailStopId) : undefined;

  return (
    <div className="flex h-full min-h-0 flex-col" data-slot="planboard-stops-pane">
      <AppCommandBar
        commands={commands}
        trailing={<span className="text-label text-muted-foreground">{[trip.number, trip.name].filter(Boolean).join(" · ")}</span>}
      />
      {stops.length === 0 ? (
        <div className="min-h-0 flex-1">
          <PaneMessage>{t("Planboard:Stops.NoStops")}</PaneMessage>
        </div>
      ) : (
        <StopTimeline
          trip={trip}
          stops={stops}
          editable={editable}
          activityName={(stop) => waypointLookup.data?.find((w) => w.id === stop.waypointTypeId)?.name ?? ""}
          selectedStopId={selectedStopId}
          onSelect={(stop) => setSelectedStopId(stop.id ?? null)}
          onOpen={(stop) => setDetailStopId(stop.id ?? null)}
          onMove={moveTo}
          onRecordActuals={setActuals}
          onRemove={setRemoving}
          onAddStop={() => setAdding(true)}
          /* An empty text clears the remarks (the command keeps them on null). */
          onCommitRemarks={(stop, value) => run(update.mutateAsync({ id: trip.id!, stopId: stop.id!, dto: { remarks: value ?? "" } }), "PlanningExecution:TransportTripSaved")}
        />
      )}
      {detailStop && <StopDetailDialog key={detailStop.id} tripId={trip.id!} stop={detailStop} canEdit={editable} onClose={() => setDetailStopId(null)} />}
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
