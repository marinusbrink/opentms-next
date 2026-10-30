import { useMemo, useState, type FormEvent } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ArrowDown, ArrowUp, ClipboardCheck, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { BoolCell, DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { Input } from "@/components/ui/input";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Textarea } from "@/components/ui/textarea";
import { toOptions } from "@/apps/transport/components/fields";
import { joinDateTime, splitDateTime } from "@/apps/transport/orders/order-form";
import { AddressField, RelationField } from "@/domains/master-data/components/RelationFields";
import { WarningsCell, warningsOf } from "@/apps/transport/components/WarningsCell";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { RelationOption } from "@/domains/master-data/entities";
import { waypointTypes } from "@/domains/orders/reference-data";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { useAddManualStop, useRecordStopActuals, useRemoveStop, useUpdateStop, type TransportTripDto, type TripStopDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseInteger, parseNumber } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Stops tab of the trip editor (design 61): an OpenTmsGrid of the trip's stops (position, activity, relation,
 * address, planned/estimated/actual times, cumulative distance, legs count, warnings) with the row actions Move
 * up/down, Record actuals (dialog), Remove (manual only) and the command Add manual stop. The stops come with the
 * trip (design 59 `GET /{id}`), so the grid's fetcher serves them from the record; every change is a command. */

export interface TripStopsTabProps {
  trip: TransportTripDto;
  canManage: boolean;
}

export function TripStopsTab({ trip, canManage }: TripStopsTabProps) {
  const { t } = useL();
  const f = useFormat();
  const [actuals, setActuals] = useState<TripStopDto | null>(null);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const update = useUpdateStop();
  const remove = useRemoveStop();
  const stops = useMemo(() => [...(trip.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)), [trip.stops]);
  const waypointLookup = waypointTypes.useLookup();
  const waypointName = (id: string | undefined) => waypointLookup.data?.find((w) => w.id === id)?.name ?? "";

  const fetchRows = useMemo(
    () => async (_request: GridRequest): Promise<GridResponse<TripStopDto>> => ({ rows: stops, totalCount: stops.length, filteredCount: stops.length }),
    [stops],
  );

  const run = (action: Promise<unknown>, successKey: string) => {
    setError(null);
    action.then(() => toast.success(t(successKey))).catch((err: unknown) => setError(getApiErrorMessage(err, t)));
  };
  const move = (stop: TripStopDto, delta: number) => run(update.mutateAsync({ id: trip.id!, stopId: stop.id!, dto: { position: (stop.position ?? 1) + delta } }), "PlanningExecution:TransportTripSaved");

  const columns = useMemo<ColDef<TripStopDto>[]>(
    () => [
      { field: "position", headerName: t("PlanningExecution:Position"), width: 80, minWidth: 70, cellRenderer: NumberCell, type: "rightAligned" },
      { colId: "activity", headerName: t("Orders:Activity"), minWidth: 120, valueGetter: (p) => waypointName(p.data?.waypointTypeId), sortable: false },
      { colId: "relation", headerName: t("PlanningExecution:Relation"), flex: 1, minWidth: 160, valueGetter: (p) => p.data?.relationName ?? p.data?.name, sortable: false },
      { field: "city", headerName: t("MasterData:City"), minWidth: 120, sortable: false },
      { field: "plannedArrival", headerName: t("PlanningExecution:PlannedArrival"), minWidth: 140, cellRenderer: DateTimeCell, sortable: false },
      { field: "plannedDeparture", headerName: t("PlanningExecution:PlannedDeparture"), minWidth: 140, cellRenderer: DateTimeCell, sortable: false },
      { field: "estimatedArrival", headerName: t("PlanningExecution:EstimatedArrival"), minWidth: 140, cellRenderer: DateTimeCell, sortable: false, hide: true },
      { field: "actualArrival", headerName: t("PlanningExecution:ActualArrival"), minWidth: 140, cellRenderer: DateTimeCell, sortable: false },
      { field: "actualDeparture", headerName: t("PlanningExecution:ActualDeparture"), minWidth: 140, cellRenderer: DateTimeCell, sortable: false },
      { colId: "cumulative", headerName: `${t("PlanningExecution:CumulativeDistance")} (km)`, minWidth: 110, type: "rightAligned", valueGetter: (p) => (p.data?.plannedCumulativeDistanceM == null ? null : p.data.plannedCumulativeDistanceM / 1000), valueFormatter: (p: ValueFormatterParams<TripStopDto, number | null>) => f.number(p.value, { maxDecimals: 1 }), cellClass: "tabular-nums", sortable: false },
      { colId: "legs", headerName: t("PlanningExecution:Legs"), minWidth: 80, type: "rightAligned", valueGetter: (p) => (p.data?.legs ?? []).length, cellRenderer: NumberCell, sortable: false },
      { field: "isManual", headerName: t("PlanningExecution:ManualStop"), minWidth: 90, sortable: false, cellRenderer: BoolCell },
      { colId: "warnings", headerName: t("PlanningExecution:Warnings"), minWidth: 100, sortable: false, cellRenderer: (p: { data: TripStopDto | undefined }) => <WarningsCell value={warningsOf(trip.warnings, { stopId: p.data?.id })} /> },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 140, hide: true, sortable: false },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, trip.warnings, waypointLookup.data],
  );

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {canManage && (
        <div className="flex shrink-0 gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus /> {t("PlanningExecution:AddStop")}
          </Button>
        </div>
      )}
      <div className="min-h-64 flex-1">
        <LazyOpenTmsGrid<TripStopDto>
          gridId="planning.transport.tripStops"
          columnDefs={columns}
          fetchRows={fetchRows}
          rowActions={(rows) => {
            const stop = rows[0];
            const hasActuals = stop?.actualArrival != null || stop?.actualDeparture != null;
            return canManage
              ? [
                  { id: "up", labelKey: "Orders:MoveUp", icon: ArrowUp, disabled: (stop?.position ?? 1) <= 1 || hasActuals, disabledReasonKey: "OpenTms.PlanningExecution:StopHasActuals", onClick: () => move(stop, -1) },
                  { id: "down", labelKey: "Orders:MoveDown", icon: ArrowDown, disabled: (stop?.position ?? 0) >= stops.length || hasActuals, disabledReasonKey: "OpenTms.PlanningExecution:StopHasActuals", onClick: () => move(stop, 1) },
                  { id: "actuals", labelKey: "PlanningExecution:RecordActuals", icon: ClipboardCheck, onClick: () => setActuals(stop) },
                  { id: "remove", labelKey: "PlanningExecution:RemoveStop", icon: Trash2, destructive: true, disabled: stop?.isManual !== true, disabledReasonKey: "OpenTms.PlanningExecution:NotManualStop", onClick: () => run(remove.mutateAsync({ id: trip.id!, stopId: stop.id! }), "PlanningExecution:StopRemoved") },
                ]
              : [{ id: "actuals", labelKey: "PlanningExecution:RecordActuals", icon: ClipboardCheck, disabled: true, disabledReasonKey: "PermissionDenied", onClick: () => undefined }];
          }}
        />
      </div>
      {actuals && <RecordActualsDialog tripId={trip.id!} stop={actuals} onClose={() => setActuals(null)} />}
      {adding && <AddManualStopDialog tripId={trip.id!} stopCount={stops.length} onClose={() => setAdding(false)} />}
    </div>
  );
}

/** Record actuals (design 59 `RecordStopActual`): arrival, departure and the cumulative distance. */
export function RecordActualsDialog({ tripId, stop, onClose }: { tripId: string; stop: TripStopDto; onClose: () => void }) {
  const { t } = useL();
  const record = useRecordStopActuals();
  const arrival = splitDateTime(stop.actualArrival ?? stop.plannedArrival);
  const departure = splitDateTime(stop.actualDeparture ?? stop.plannedDeparture);
  const [arrivalDate, setArrivalDate] = useState<string | null>(arrival.date);
  const [arrivalTime, setArrivalTime] = useState(arrival.time);
  const [departureDate, setDepartureDate] = useState<string | null>(departure.date);
  const [departureTime, setDepartureTime] = useState(departure.time);
  const [distanceKm, setDistanceKm] = useState(stop.actualCumulativeDistanceM == null ? "" : String(Math.round(stop.actualCumulativeDistanceM / 1000)));
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const timeError = (time: string) => (submitted && time.trim() && !parseTimeOfDay(time) ? t("Orders:InvalidTime") : undefined);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (timeError(arrivalTime) || timeError(departureTime)) return;
    setError(null);
    const km = parseNumber(distanceKm);
    try {
      await record.mutateAsync({
        id: tripId,
        stopId: stop.id!,
        dto: { actualArrival: joinDateTime(arrivalDate, arrivalTime), actualDeparture: joinDateTime(departureDate, departureTime), cumulativeDistanceM: km == null || Number.isNaN(km) ? null : Math.round(km * 1000) },
      });
      toast.success(t("PlanningExecution:ActualsRecorded"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={`${t("PlanningExecution:RecordActuals")} ${stop.relationName ?? stop.name ?? ""}`}
      isBusy={record.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={record.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="actuals-form" disabled={record.isPending}>
            {record.isPending ? t("Saving") : t("PlanningExecution:RecordActuals")}
          </Button>
        </>
      }
    >
      <form id="actuals-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-2 gap-x-4 gap-y-3">
        {error && (
          <Alert variant="destructive" className="col-span-2">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("PlanningExecution:ActualArrival")} htmlFor="actuals-arrival-date" floating="always">
          <DatePicker id="actuals-arrival-date" value={arrivalDate} onChange={setArrivalDate} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="actuals-arrival-time" error={timeError(arrivalTime)}>
          <Input id="actuals-arrival-time" value={arrivalTime} onChange={(e) => setArrivalTime(e.target.value)} placeholder="HH:mm" />
        </FormField>
        <FormField label={t("PlanningExecution:ActualDeparture")} htmlFor="actuals-departure-date" floating="always">
          <DatePicker id="actuals-departure-date" value={departureDate} onChange={setDepartureDate} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="actuals-departure-time" error={timeError(departureTime)}>
          <Input id="actuals-departure-time" value={departureTime} onChange={(e) => setDepartureTime(e.target.value)} placeholder="HH:mm" />
        </FormField>
        <FormField label={`${t("PlanningExecution:CumulativeDistance")} (km)`} htmlFor="actuals-distance">
          <Input id="actuals-distance" inputMode="decimal" className="tabular-nums" value={distanceKm} onChange={(e) => setDistanceKm(e.target.value)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}

/** Add a manual stop (design 59 `AddManualStop`): waypoint type, relation, address, position, duration, remarks. */
function AddManualStopDialog({ tripId, stopCount, onClose }: { tripId: string; stopCount: number; onClose: () => void }) {
  const { t } = useL();
  const add = useAddManualStop();
  const inPlace = useCreateInPlace();
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const [waypointTypeId, setWaypointTypeId] = useState<string | null>(null);
  const [relation, setRelation] = useState<RelationOption | null>(null);
  const [addressId, setAddressId] = useState<string | null>(null);
  const [position, setPosition] = useState(String(stopCount + 1));
  const [duration, setDuration] = useState("");
  const [remarks, setRemarks] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (!waypointTypeId || !relation) return;
    setError(null);
    try {
      await add.mutateAsync({ id: tripId, dto: { waypointTypeId, relationId: relation.id, relationAddressId: addressId, position: parseInteger(position), plannedDurationMin: parseInteger(duration), remarks: remarks.trim() || null } });
      toast.success(t("PlanningExecution:StopAdded"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("PlanningExecution:AddStop")}
      isBusy={add.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={add.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="add-stop-form" disabled={add.isPending}>
            {add.isPending ? t("Saving") : t("PlanningExecution:AddStop")}
          </Button>
        </>
      }
    >
      <form id="add-stop-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Orders:StopType")} htmlFor="add-stop-type" required error={submitted && !waypointTypeId ? t("MasterData:Required") : undefined} floating="always">
          <ReferenceCombobox
            id="add-stop-type"
            value={waypointTypeId}
            onChange={setWaypointTypeId}
            options={waypointOptions}
            required
            aria-invalid={submitted && !waypointTypeId}
            {...inPlace.offer("WaypointType", add.isPending, (r) => setWaypointTypeId(r.id))}
          />
        </FormField>
        <RelationField id="add-stop-relation" label={t("PlanningExecution:Relation")} value={relation} onChange={(next) => { setRelation(next); setAddressId(null); }} required error={submitted && !relation ? t("MasterData:Required") : undefined} />
        <AddressField id="add-stop-address" label={t("Orders:Address")} relationId={relation?.id} value={addressId} onChange={setAddressId} />
        <FormField label={t("PlanningExecution:Position")} htmlFor="add-stop-position">
          <Input id="add-stop-position" inputMode="numeric" className="tabular-nums" value={position} onChange={(e) => setPosition(e.target.value)} />
        </FormField>
        <FormField label={t("Orders:PlannedDurationMin")} htmlFor="add-stop-duration">
          <Input id="add-stop-duration" inputMode="numeric" className="tabular-nums" value={duration} onChange={(e) => setDuration(e.target.value)} />
        </FormField>
        <FormField label={t("PlanningExecution:Remarks")} htmlFor="add-stop-remarks" floating="always">
          <Textarea id="add-stop-remarks" rows={2} value={remarks} maxLength={2500} onChange={(e) => setRemarks(e.target.value)} />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
