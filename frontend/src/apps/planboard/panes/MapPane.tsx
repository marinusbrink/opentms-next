import { useCallback, useMemo, useRef, useState, type DragEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { CircleDot, Flag, Maximize2, Plus } from "lucide-react";
import { toast } from "sonner";

import { planboardDragKind, readDragPayload, setDragPayload } from "@/apps/planboard/dnd";
import { usePaneFilters } from "@/apps/planboard/filters";
import { hintsConcern, PLANBOARD_RECORD_TYPES, usePlanboardLiveUpdates } from "@/apps/planboard/live-updates";
import { useOutsideWindowConfirm } from "@/apps/planboard/outside-window";
import { MapLegend } from "@/apps/planboard/panes/map/MapLegend";
import { allTripsFeatures, fitPoints, nearLegPoint, orderedStops, pointOf, routeFeatures, stopPoint, stopState, type StopState } from "@/apps/planboard/panes/map/model";
import { TripSummaryCard } from "@/apps/planboard/panes/map/TripSummaryCard";
import type { PlanboardPaneProps } from "@/apps/planboard/panes/registry";
import { PaneFilterControls } from "@/apps/planboard/panes/scheduler/PaneFilterControls";
import { usePlanboardSelection } from "@/apps/planboard/selection";
import { usePaneSettings } from "@/apps/planboard/settings-store";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { boundsOf, fitBounds, Layer, MapView, Marker, Popup, Source, tokenColor, type MapLayerMouseEvent, type MapRef } from "@/components/ui/map-view";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import {
  DEFAULT_UNPLANNED_NEAR_RADIUS_KM,
  MAX_MAP_TRIPS_DAYS,
  MAX_UNPLANNED_NEAR_RADIUS_KM,
  planboardMapKeys,
  usePlanboardMapTrips,
  usePlanboardTripMap,
  usePlanboardUnplannedNear,
  type PlanboardMapStopDto,
  type PlanboardNearLegDto,
} from "@/domains/planning-execution/planboard-map";
import { useAssignLegsToTrip } from "@/domains/planning-execution/planboard-scheduler";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Design 70, the Map pane (MapLibre through components/ui/map-view; this pane is its own lazy chunk). The detail layer
 * shows the focused trip: the stored route segments (toll segments in their own token colour, dashed straight lines
 * where no geometry is stored), numbered stop markers coloured by state, departure and arrival markers, popups with
 * window / ETA / actuals and the summary card. The all-trips layer draws the window's trips first → last (click selects
 * the trip); the unplanned-nearby layer shows legs near the focused trip (drag a marker onto the card, or "Add to trip").
 * Stop selection is two-way with Trip stops through the selection bus. A trip dragged onto the map (a Scheduler bar)
 * becomes the focused trip. Zero-emission zones are the second iteration. */

const PERMISSION = "PlanningExecution.TransportTrips";
const ALL_TRIPS_LAYER = "planboard-all-trips";
const RADII = [10, 25, 50, MAX_UNPLANNED_NEAR_RADIUS_KM];

interface MapPaneSettings {
  showAllTrips: boolean;
  showUnplannedNear: boolean;
  radiusKm: number;
}

const DEFAULT_MAP_SETTINGS: MapPaneSettings = { showAllTrips: true, showUnplannedNear: false, radiusKm: DEFAULT_UNPLANNED_NEAR_RADIUS_KM };

function sanitizeMapSettings(stored: Record<string, unknown>): Partial<MapPaneSettings> {
  const result: Partial<MapPaneSettings> = {};
  if (typeof stored.showAllTrips === "boolean") result.showAllTrips = stored.showAllTrips;
  if (typeof stored.showUnplannedNear === "boolean") result.showUnplannedNear = stored.showUnplannedNear;
  if (typeof stored.radiusKm === "number" && RADII.includes(stored.radiusKm)) result.radiusKm = stored.radiusKm;
  return result;
}

const STOP_STATE_CLASSES: Record<StopState, string> = {
  done: "border-tone-success bg-tone-success-bg text-tone-success",
  "on-time": "border-tone-info bg-tone-info-bg text-tone-info",
  "at-risk": "border-tone-warning bg-tone-warning-bg text-tone-warning",
  late: "border-tone-danger bg-tone-danger-bg text-tone-danger",
};

const same = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

export default function MapPane(props: PlanboardPaneProps) {
  const canView = useIsGranted(PERMISSION);
  if (!canView) return <PermissionDenied />;
  return <MapPaneBody {...props} />;
}

function MapPaneBody({ instanceId }: PlanboardPaneProps) {
  const { t } = useL();
  const f = useFormat();
  const queryClient = useQueryClient();
  const selection = usePlanboardSelection();
  const canAssign = useIsGranted(`${PERMISSION}.AssignLegs`);
  const canSeeLegs = useIsGranted("PlanningExecution.Legs");
  const mapRef = useRef<MapRef | null>(null);

  const { settings, update } = usePaneSettings<MapPaneSettings>(instanceId, DEFAULT_MAP_SETTINGS, sanitizeMapSettings);
  const paneFilters = usePaneFilters(instanceId, { maxDays: MAX_MAP_TRIPS_DAYS });
  const { range, organizationUnitIds } = paneFilters.filters;
  const focusedTripId = selection.focusedTripId;

  const tripMap = usePlanboardTripMap(focusedTripId);
  const mapTrips = usePlanboardMapTrips(range, organizationUnitIds, settings.showAllTrips && paneFilters.isLoaded);
  const showNear = settings.showUnplannedNear && canSeeLegs;
  const near = usePlanboardUnplannedNear(focusedTripId, settings.radiusKm, range, showNear);

  const detail = focusedTripId && tripMap.data && same(tripMap.data.id, focusedTripId) ? tripMap.data : undefined;
  const stops = useMemo(() => orderedStops(detail), [detail]);
  const trips = useMemo(() => (settings.showAllTrips ? (mapTrips.data?.trips ?? []) : []), [settings.showAllTrips, mapTrips.data]);
  const nearLegs = useMemo(() => (showNear && detail ? (near.data?.legs ?? []) : []), [showNear, detail, near.data]);
  const selectedTrips = useMemo(() => new Set(selection.selectedTripIds.map((id) => id.toLowerCase())), [selection.selectedTripIds]);

  const route = useMemo(() => routeFeatures(detail), [detail]);
  const allTrips = useMemo(() => allTripsFeatures(trips, selectedTrips), [trips, selectedTrips]);
  const bounds = useMemo(() => boundsOf(fitPoints(detail, trips)), [detail, trips]);

  /* MapLibre paint cannot read CSS variables: the token values are read when painting (the brand is set at runtime). */
  const colors = {
    primary: tokenColor("--primary"),
    toll: tokenColor("--tone-warning-fg"),
    brand: tokenColor("--brand"),
    neutral: tokenColor("--tone-neutral-fg"),
  };

  /* ── Live updates ──────────────────────────────────────────────────────────────────────────────────────────── */

  const tripIdsOnMap = useMemo(() => trips.map((trip) => trip.id).filter((id): id is string => !!id), [trips]);
  const refetchTrips = mapTrips.refetch;
  const refetchDetail = tripMap.refetch;
  const refetchNear = near.refetch;
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.trip,
    predicate: (message) => same(message.targetId, focusedTripId) || (settings.showAllTrips && hintsConcern(message, { range, organizationUnitIds, knownIds: tripIdsOnMap })),
    onIds: (ids) => {
      if (focusedTripId && ids.some((id) => same(id, focusedTripId))) {
        void refetchDetail();
        if (showNear) void refetchNear();
      }
      if (settings.showAllTrips) void refetchTrips();
    },
    onResync: () => {
      if (focusedTripId) void refetchDetail();
      if (settings.showAllTrips) void refetchTrips();
      if (showNear && focusedTripId) void refetchNear();
    },
  });
  const nearIds = useMemo(() => nearLegs.map((leg) => leg.id).filter((id): id is string => !!id), [nearLegs]);
  usePlanboardLiveUpdates({
    targetType: PLANBOARD_RECORD_TYPES.leg,
    enabled: showNear && !!focusedTripId,
    filter: { range, knownIds: nearIds },
    onIds: () => void refetchNear(),
  });

  /* ── Assigning nearby legs ─────────────────────────────────────────────────────────────────────────────────── */

  const assignLegs = useAssignLegsToTrip();
  const [nearPopupId, setNearPopupId] = useState<string | null>(null);
  const tripTakesLegs = detail != null && (detail.status === 1 || detail.status === 2);
  const { run: runOutsideWindow, dialog: outsideWindowDialog } = useOutsideWindowConfirm();
  const assign = useCallback(
    async (legIds: string[]) => {
      if (!detail?.id) return;
      const tripId = detail.id;
      try {
        /* Legs outside the trip's window ask first and are retried with allowOutsideWindow (outside-window.tsx). */
        const result = await runOutsideWindow((allowOutsideWindow) => assignLegs.mutateAsync({ tripId, legIds, concurrencyStamp: detail.concurrencyStamp ?? "", allowOutsideWindow }));
        if (result.cancelled) return;
        toast.success(t("Planboard:Scheduler.LegsAssigned").replace("{0}", detail.number ?? ""));
        setNearPopupId(null);
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
      } finally {
        void queryClient.invalidateQueries({ queryKey: planboardMapKeys.trip(detail.id) });
        void queryClient.invalidateQueries({ queryKey: ["planning", "planboard", "map", "near"] });
      }
    },
    [detail, assignLegs, runOutsideWindow, t, queryClient],
  );

  /* A trip dragged here (a Scheduler bar) becomes the focused trip: the map shows its detail layer. */
  const onTripDragOver = (event: DragEvent<HTMLDivElement>) => {
    if (planboardDragKind(event.dataTransfer.types) !== "trip") return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
  };
  const onTripDrop = (event: DragEvent<HTMLDivElement>) => {
    const payload = readDragPayload(event.dataTransfer, ["trip"]);
    if (payload?.kind !== "trip") return;
    event.preventDefault();
    selection.setSelectedTripIds([payload.tripId]);
  };

  /* ── Interaction ───────────────────────────────────────────────────────────────────────────────────────────── */

  const onMapClick = useCallback(
    (event: MapLayerMouseEvent) => {
      const id = event.features?.find((feature) => feature.layer?.id === ALL_TRIPS_LAYER)?.properties?.id;
      if (typeof id === "string") selection.setSelectedTripIds([id]);
    },
    [selection],
  );
  const setCursor = (cursor: string) => {
    const canvas = mapRef.current?.getCanvas();
    if (canvas) canvas.style.cursor = cursor;
  };

  const selectedStop = stops.find((stop) => same(stop.id, selection.selectedStopId));
  const selectedStopPoint = selectedStop ? stopPoint(selectedStop) : null;
  const popupLeg = nearLegs.find((leg) => same(leg.id, nearPopupId));
  const popupLegPoint = popupLeg ? nearLegPoint(popupLeg) : null;
  const departure = pointOf(detail?.departurePoint);
  const arrival = pointOf(detail?.arrivalPoint);

  const error = (focusedTripId && tripMap.error) || (settings.showAllTrips && mapTrips.error) || (showNear && near.error) || null;
  const loading = (focusedTripId && tripMap.isFetching) || (settings.showAllTrips && mapTrips.isFetching) || (showNear && near.isFetching);
  const idPrefix = `map-${instanceId}`;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-3 border-b border-border px-2 py-2">
        <label htmlFor={`${idPrefix}-all`} className="flex items-center gap-1.5 text-label text-muted-foreground">
          <Checkbox id={`${idPrefix}-all`} checked={settings.showAllTrips} onCheckedChange={(checked) => update({ showAllTrips: checked === true })} />
          {t("Planboard:Map.AllTrips")}
        </label>
        {canSeeLegs && (
          <>
            <label htmlFor={`${idPrefix}-near`} className="flex items-center gap-1.5 text-label text-muted-foreground">
              <Checkbox id={`${idPrefix}-near`} checked={settings.showUnplannedNear} onCheckedChange={(checked) => update({ showUnplannedNear: checked === true })} />
              {t("Planboard:Map.UnplannedNear")}
            </label>
            {settings.showUnplannedNear && (
              <>
                <label htmlFor={`${idPrefix}-radius`} className="sr-only">
                  {t("Planboard:Map.RadiusKm")}
                </label>
                <Select
                  items={RADII.map((r) => ({ value: String(r), label: `${f.number(r)}` }))}
                  value={String(settings.radiusKm)}
                  onValueChange={(next) => next && update({ radiusKm: Number(next) })}
                >
                  <SelectTrigger id={`${idPrefix}-radius`} className="h-8 w-24 bg-background" aria-label={t("Planboard:Map.RadiusKm")}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {RADII.map((r) => (
                      <SelectItem key={r} value={String(r)}>
                        {f.number(r)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <span className="text-label text-muted-foreground">{t("Planboard:Map.RadiusKm")}</span>
              </>
            )}
          </>
        )}
        <Tooltip>
          <TooltipTrigger
            render={
              <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Planboard:Map.FitBounds")} disabled={!bounds} onClick={() => bounds && fitBounds(mapRef.current, bounds)}>
                <Maximize2 />
              </Button>
            }
          />
          <TooltipContent>{t("Planboard:Map.FitBounds")}</TooltipContent>
        </Tooltip>
        <PaneFilterControls idPrefix={idPrefix} paneFilters={paneFilters} />
        {loading && <Spinner aria-label={t("Planboard:Map.Loading")} />}
      </div>

      <div className="relative min-h-0 flex-1" onDragOver={onTripDragOver} onDrop={onTripDrop}>
        <MapView
          ref={mapRef}
          bounds={bounds}
          interactiveLayerIds={settings.showAllTrips ? [ALL_TRIPS_LAYER] : undefined}
          onClick={onMapClick}
          onMouseEnter={() => setCursor("pointer")}
          onMouseLeave={() => setCursor("")}
          aria-label={t("App:Planboard.Map")}
        >
          {settings.showAllTrips && (
            <Source id="planboard-all-trips-source" type="geojson" data={allTrips}>
              <Layer
                id={ALL_TRIPS_LAYER}
                type="line"
                paint={{
                  "line-color": ["case", ["get", "selected"], colors.brand, colors.neutral],
                  "line-width": ["case", ["get", "selected"], 3, 1.5],
                  "line-opacity": 0.7,
                }}
              />
            </Source>
          )}
          {detail && (
            <Source id="planboard-route-source" type="geojson" data={route}>
              <Layer id="planboard-route" type="line" filter={["==", ["get", "kind"], "route"]} layout={{ "line-cap": "round", "line-join": "round" }} paint={{ "line-color": colors.primary, "line-width": 4 }} />
              <Layer id="planboard-route-toll" type="line" filter={["==", ["get", "kind"], "toll"]} layout={{ "line-cap": "round", "line-join": "round" }} paint={{ "line-color": colors.toll, "line-width": 4 }} />
              <Layer id="planboard-route-straight" type="line" filter={["==", ["get", "kind"], "straight"]} paint={{ "line-color": colors.primary, "line-width": 2, "line-dasharray": [2, 2] }} />
            </Source>
          )}

          {departure && (
            <Marker longitude={departure.longitude} latitude={departure.latitude} anchor="bottom">
              <span role="img" aria-label={t("Planboard:Map.Departure")} title={t("Planboard:Map.Departure")} className="text-brand">
                <CircleDot className="size-5" aria-hidden="true" />
              </span>
            </Marker>
          )}
          {arrival && (
            <Marker longitude={arrival.longitude} latitude={arrival.latitude} anchor="bottom">
              <span role="img" aria-label={t("Planboard:Map.Arrival")} title={t("Planboard:Map.Arrival")} className="text-brand">
                <Flag className="size-5" aria-hidden="true" />
              </span>
            </Marker>
          )}

          {stops.map((stop) => {
            const point = stopPoint(stop);
            if (!point || !stop.id) return null;
            const state = stopState(stop);
            const selected = same(stop.id, selection.selectedStopId);
            return (
              <Marker
                key={stop.id}
                longitude={point.longitude}
                latitude={point.latitude}
                onClick={(e) => {
                  e.originalEvent.stopPropagation();
                  selection.setSelectedStopId(stop.id!);
                }}
              >
                <button
                  type="button"
                  aria-label={`${t("Planboard:Map.Stop")} ${stop.position}: ${stop.name ?? stop.city ?? ""} (${t(`Planboard:Map.StopState.${state}`)})`}
                  aria-pressed={selected}
                  data-state={state}
                  className={cn("flex size-6 items-center justify-center rounded-full border-2 text-label font-semibold tabular-nums shadow-sm", STOP_STATE_CLASSES[state], selected && "ring-2 ring-brand")}
                >
                  {stop.position}
                </button>
              </Marker>
            );
          })}

          {nearLegs.map((leg) => {
            const point = nearLegPoint(leg);
            if (!point || !leg.id) return null;
            return (
              <Marker
                key={leg.id}
                longitude={point.longitude}
                latitude={point.latitude}
                onClick={(e) => {
                  e.originalEvent.stopPropagation();
                  setNearPopupId(leg.id!);
                }}
              >
                <span
                  role="button"
                  tabIndex={0}
                  draggable={canAssign && tripTakesLegs}
                  aria-label={`${t("Planboard:Map.UnplannedLeg")} ${leg.originCity ?? ""} → ${leg.destinationCity ?? ""}`}
                  className="flex size-4 rotate-45 items-center justify-center border-2 border-tone-warning bg-tone-warning-bg shadow-sm"
                  onMouseDown={(e) => e.stopPropagation()}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setNearPopupId(leg.id!);
                    }
                  }}
                  onDragStart={(e) => setDragPayload(e.dataTransfer, { kind: "legs", legIds: [leg.id!], fromTripId: null })}
                />
              </Marker>
            );
          })}

          {selectedStop && selectedStopPoint && (
            <Popup longitude={selectedStopPoint.longitude} latitude={selectedStopPoint.latitude} offset={16} closeOnClick={false} onClose={() => selection.setSelectedStopId(null)}>
              <StopPopup stop={selectedStop} />
            </Popup>
          )}
          {popupLeg && popupLegPoint && (
            <Popup longitude={popupLegPoint.longitude} latitude={popupLegPoint.latitude} offset={12} closeOnClick={false} onClose={() => setNearPopupId(null)}>
              <NearLegPopup
                leg={popupLeg}
                customer={near.data?.names?.[popupLeg.customerRelationId ?? ""] ?? ""}
                onAssign={canAssign && tripTakesLegs ? () => void assign([popupLeg.id!]) : undefined}
                busy={assignLegs.isPending}
              />
            </Popup>
          )}
        </MapView>

        <div className="pointer-events-none absolute top-2 bottom-8 left-2 flex flex-col justify-between gap-2">
          <div className="flex min-h-0 flex-col gap-2 overflow-hidden">
          {detail && (
            <div className="pointer-events-auto">
              <TripSummaryCard map={detail} onDropLegs={canAssign && tripTakesLegs ? (legIds) => void assign(legIds) : undefined} busy={assignLegs.isPending} />
            </div>
          )}
          {!focusedTripId && (
            <p role="status" className="pointer-events-auto rounded-md border border-border bg-background px-2 py-1 text-label text-muted-foreground">
              {t("Planboard:Map.SelectTrip")}
            </p>
          )}
          {error && (
            <Alert variant="destructive" className="pointer-events-auto w-72">
              <AlertDescription>{getApiErrorMessage(error, t)}</AlertDescription>
            </Alert>
          )}
          {settings.showAllTrips && mapTrips.data?.truncated && (
            <Alert variant="warning" className="pointer-events-auto w-72">
              <AlertDescription>{t("Planboard:Map.TripsTruncated")}</AlertDescription>
            </Alert>
          )}
          {showNear && detail && near.data && (near.data.legs?.length ?? 0) === 0 && (
            <p role="status" className="pointer-events-auto rounded-md border border-border bg-background px-2 py-1 text-label text-muted-foreground">
              {t("Planboard:Map.NoUnplannedNear")}
            </p>
          )}
          </div>
          <MapLegend showAllTrips={settings.showAllTrips} showUnplannedNear={showNear} />
        </div>
      </div>
      {outsideWindowDialog}
    </div>
  );
}

function StopPopup({ stop }: { stop: PlanboardMapStopDto }) {
  const { t } = useL();
  const f = useFormat();
  const state = stopState(stop);
  const row = (label: string, value: string) =>
    value ? (
      <>
        <dt className="text-muted-foreground">{label}</dt>
        <dd className="tabular-nums">{value}</dd>
      </>
    ) : null;
  const windowText = stop.windowFrom || stop.windowTill ? `${f.dateTime(stop.windowFrom)} – ${f.time(stop.windowTill)}` : "";
  return (
    <div className="flex min-w-56 flex-col gap-1 text-label text-foreground">
      <p className="text-sm font-semibold">
        {stop.position}. {stop.name}
      </p>
      {stop.city && <p className="text-muted-foreground">{stop.city}</p>}
      <dl className="grid grid-cols-[auto_1fr] gap-x-2 gap-y-0.5">
        {row(t("Planboard:Map.Window"), windowText)}
        {row(t("Planboard:Map.PlannedArrival"), f.dateTime(stop.plannedArrival))}
        {row(t("Planboard:Map.EstimatedArrival"), f.dateTime(stop.estimatedArrival))}
        {row(t("Planboard:Map.ActualArrival"), f.dateTime(stop.actualArrival))}
        {row(t("Planboard:Map.ActualDeparture"), f.dateTime(stop.actualDeparture))}
        {row(t("Planboard:Map.Lateness"), (stop.latenessMinutes ?? 0) > 0 ? f.duration(stop.latenessMinutes) : "")}
      </dl>
      <p className={cn("w-fit rounded-full px-2 py-0.5 font-medium", STOP_STATE_CLASSES[state])}>{t(`Planboard:Map.StopState.${state}`)}</p>
    </div>
  );
}

function NearLegPopup({ leg, customer, onAssign, busy }: { leg: PlanboardNearLegDto; customer: string; onAssign?: () => void; busy: boolean }) {
  const { t } = useL();
  const f = useFormat();
  return (
    <div className="flex min-w-56 flex-col gap-1 text-label text-foreground">
      <p className="text-sm font-semibold">{customer}</p>
      <p>
        {leg.originName ?? ""} {leg.originCity ? `(${leg.originCity})` : ""} → {leg.destinationName ?? ""} {leg.destinationCity ? `(${leg.destinationCity})` : ""}
      </p>
      <p className="text-muted-foreground tabular-nums">
        {f.date(leg.originPlannedDate)} · {t("Planboard:Map.DistanceFromTripKm").replace("{0}", f.number(leg.distanceKm ?? 0, { maxDecimals: 1 }))}
      </p>
      {onAssign && (
        <Button type="button" size="sm" className="mt-1 w-fit" disabled={busy} onClick={onAssign}>
          <Plus />
          {t("Planboard:Map.AddToTrip")}
        </Button>
      )}
    </div>
  );
}
