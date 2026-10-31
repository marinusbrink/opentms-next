import { useEffect, useMemo, useRef, type KeyboardEvent } from "react";
import type { ColDef } from "ag-grid-community";
import { X } from "lucide-react";

import { PaneMessage } from "@/apps/planboard/panes/lists/PaneMessage";
import { Button } from "@/components/ui/button";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusBadge } from "@/components/ui/status-badge";
import { tripStatusName, useTransportTrip, useTransportTripUpdates, type TransportTripDto, type TripStopDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The read-only trip peek (design 70 assumption 1): without the Transport app's trip page (`Apps.Transport.Trips` off)
 * opening a trip in any Planboard pane shows this side sheet over the right edge of the workspace instead: the header,
 * vehicles and drivers, the stops and the legs from `GET /api/planning/transport-trip/{id}`. Nothing can be changed
 * here; the panes keep their own commands. The trip's live notices refetch it. Esc and the close button close it. */

const STOPS_GRID_ID = "planning.planboard.tripPeek.stops";
const LEGS_GRID_ID = "planning.planboard.tripPeek.legs";
/** StopLegRole (design 59): the stop where the leg starts and where it ends. */
const ROLE = { Origin: 1, Destination: 2 } as const;

interface PeekLegRow {
  id: string;
  orderNumber: string | null;
  customerName: string | null;
  origin: string;
  destination: string;
  plannedDate: string | null;
  quantity: number | null;
  weightKg: number | null;
  loadingMeters: number | null;
  reference: string | null;
}

const stopLabel = (stop: TripStopDto) => [stop.relationName ?? stop.name, stop.city].filter(Boolean).join(", ");

/** The trip's legs, once each, with the stops they start and end at (exported for tests). */
export function peekLegs(trip: TransportTripDto | undefined): PeekLegRow[] {
  const byId = new Map<string, PeekLegRow>();
  const stops = [...(trip?.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  for (const stop of stops) {
    for (const leg of stop.legs ?? []) {
      if (!leg.legId) continue;
      const row = byId.get(leg.legId) ?? {
        id: leg.legId,
        orderNumber: leg.orderNumber ?? null,
        customerName: leg.customerName ?? null,
        origin: "",
        destination: "",
        plannedDate: null,
        quantity: leg.quantity ?? null,
        weightKg: leg.weightKg ?? null,
        loadingMeters: leg.loadingMeters ?? null,
        reference: leg.reference ?? null,
      };
      if (leg.role === ROLE.Origin) {
        row.origin = stopLabel(stop);
        row.plannedDate = leg.plannedDate ?? row.plannedDate;
      } else if (leg.role === ROLE.Destination) {
        row.destination = stopLabel(stop);
      }
      byId.set(leg.legId, row);
    }
  }
  return [...byId.values()];
}

export interface TripPeekSheetProps {
  tripId: string;
  onClose: () => void;
}

export function TripPeekSheet({ tripId, onClose }: TripPeekSheetProps) {
  const { t } = useL();
  const canView = useIsGranted("PlanningExecution.TransportTrips");
  const trip = useTransportTrip(canView ? tripId : undefined);
  useTransportTripUpdates([canView ? tripId : null]);
  const closeRef = useRef<HTMLButtonElement>(null);
  const titleId = `planboard-trip-peek-title-${tripId}`;

  /* Focus moves into the sheet when it opens (or shows another trip). */
  useEffect(() => closeRef.current?.focus(), [tripId]);

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key !== "Escape") return;
    event.stopPropagation();
    onClose();
  };

  const data = trip.data;
  let body;
  if (!canView) body = <PermissionDenied />;
  else if (trip.isLoading) body = <Skeleton className="m-3 h-full" />;
  else if (trip.isError || !data) body = <PaneMessage tone="error">{trip.isError ? getApiErrorMessage(trip.error, t) : t("Planboard:Lists.TripNotFound")}</PaneMessage>;
  else body = <TripPeekBody trip={data} />;

  return (
    <aside
      role="dialog"
      aria-modal="false"
      aria-labelledby={titleId}
      data-slot="planboard-trip-peek"
      onKeyDown={onKeyDown}
      className="absolute inset-y-0 right-0 z-40 flex w-full max-w-dialog-sm flex-col border-l border-border bg-background shadow-sm"
    >
      <header className="flex h-dialog-header shrink-0 items-center gap-2 border-b border-border px-3">
        <h2 id={titleId} className="min-w-0 flex-1 truncate text-title font-semibold">
          {t("Planboard:TripPeek.Title").replace("{0}", data?.number ?? "")}
        </h2>
        {data && <StatusBadge entity="TransportTrip" state={tripStatusName(data.status)} />}
        <Button ref={closeRef} type="button" variant="ghost" size="icon-sm" aria-label={t("Close")} title={t("Close")} onClick={onClose}>
          <X />
        </Button>
      </header>
      <div className="flex min-h-0 flex-1 flex-col">{body}</div>
    </aside>
  );
}

function TripPeekBody({ trip }: { trip: TransportTripDto }) {
  const { t } = useL();
  const f = useFormat();
  const none = t("Planboard:TripPeek.None");

  const stops = useMemo(() => [...(trip.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)), [trip.stops]);
  const legs = useMemo(() => peekLegs(trip), [trip]);

  const fetchStops = useMemo(
    () => async (_request: GridRequest): Promise<GridResponse<TripStopDto>> => ({ rows: stops, totalCount: stops.length, filteredCount: stops.length }),
    [stops],
  );
  const fetchLegs = useMemo(
    () => async (_request: GridRequest): Promise<GridResponse<PeekLegRow>> => ({ rows: legs, totalCount: legs.length, filteredCount: legs.length }),
    [legs],
  );

  const stopColumns = useMemo<ColDef<TripStopDto>[]>(
    () => [
      { field: "position", headerName: t("PlanningExecution:Position"), minWidth: 70, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { colId: "name", headerName: t("PlanningExecution:Relation"), minWidth: 150, sortable: false, valueGetter: (p) => p.data?.relationName ?? p.data?.name },
      { field: "city", headerName: t("MasterData:City"), minWidth: 110, sortable: false },
      { field: "plannedArrival", headerName: t("PlanningExecution:PlannedArrival"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "plannedDeparture", headerName: t("PlanningExecution:PlannedDeparture"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "actualArrival", headerName: t("PlanningExecution:ActualArrival"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "actualDeparture", headerName: t("PlanningExecution:ActualDeparture"), minWidth: 140, sortable: false, cellRenderer: DateTimeCell },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 150, sortable: false },
    ],
    [t],
  );
  const legColumns = useMemo<ColDef<PeekLegRow>[]>(
    () => [
      { field: "orderNumber", headerName: t("PlanningExecution:Order"), minWidth: 110, sortable: false },
      { field: "customerName", headerName: t("PlanningExecution:Customer"), minWidth: 140, sortable: false },
      { field: "origin", headerName: t("PlanningExecution:Origin"), minWidth: 150, sortable: false },
      { field: "destination", headerName: t("PlanningExecution:Destination"), minWidth: 150, sortable: false },
      { field: "plannedDate", headerName: t("PlanningExecution:Date"), minWidth: 110, sortable: false, valueFormatter: (p) => f.date(p.value as string | null) },
      { field: "quantity", headerName: t("PlanningExecution:Quantity"), minWidth: 90, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "weightKg", headerName: t("PlanningExecution:WeightKg"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "loadingMeters", headerName: t("PlanningExecution:LoadingMeters"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "reference", headerName: t("PlanningExecution:Reference"), minWidth: 120, sortable: false, hide: true },
    ],
    [t, f],
  );

  const vehicles = (trip.vehicles ?? []).map((v) => [v.name, v.licencePlate].filter(Boolean).join(" ")).filter(Boolean);
  const drivers = (trip.drivers ?? []).map((d) => d.name).filter(Boolean);
  const fact = (label: string, value: string | null | undefined) => (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 truncate tabular-nums">{value || none}</dd>
    </>
  );

  return (
    <>
      <p className="shrink-0 px-3 pt-2 text-label text-muted-foreground">{t("Planboard:TripPeek.ReadOnlyHint")}</p>
      <dl className="grid shrink-0 grid-cols-[auto_1fr] gap-x-3 gap-y-1 px-3 py-2 text-sm">
        {fact(t("PlanningExecution:Name"), trip.name)}
        {fact(t("PlanningExecution:TransportTripType"), trip.transportTripTypeName)}
        {fact(t("PlanningExecution:TripDate"), f.date(trip.tripDate))}
        {fact(t("PlanningExecution:PlannedDeparture"), f.dateTime(trip.plannedDeparture))}
        {fact(t("PlanningExecution:PlannedArrival"), f.dateTime(trip.plannedArrival))}
        {fact(t("PlanningExecution:Carrier"), trip.carrierName)}
        {fact(t("PlanningExecution:Vehicles"), trip.vehicleCombinationName ?? vehicles.join(", "))}
        {fact(t("PlanningExecution:Drivers"), drivers.join(", "))}
        {fact(t("PlanningExecution:Remarks"), trip.remarks)}
      </dl>
      <section className="flex min-h-0 flex-1 flex-col border-t border-border" aria-label={t("PlanningExecution:Stops")}>
        <h3 className="shrink-0 px-3 pt-2 text-sm font-semibold">{t("PlanningExecution:Stops")}</h3>
        {stops.length === 0 ? (
          <PaneMessage>{t("Planboard:TripPeek.NoStops")}</PaneMessage>
        ) : (
          <div className="min-h-0 flex-1 px-2 pb-2">
            <LazyOpenTmsGrid<TripStopDto> gridId={STOPS_GRID_ID} columnDefs={stopColumns} fetchRows={fetchStops} />
          </div>
        )}
      </section>
      <section className="flex min-h-0 flex-1 flex-col border-t border-border" aria-label={t("PlanningExecution:Legs")}>
        <h3 className="shrink-0 px-3 pt-2 text-sm font-semibold">{t("PlanningExecution:Legs")}</h3>
        {legs.length === 0 ? (
          <PaneMessage>{t("Planboard:TripPeek.NoLegs")}</PaneMessage>
        ) : (
          <div className="min-h-0 flex-1 px-2 pb-2">
            <LazyOpenTmsGrid<PeekLegRow> gridId={LEGS_GRID_ID} columnDefs={legColumns} fetchRows={fetchLegs} />
          </div>
        )}
      </section>
    </>
  );
}
