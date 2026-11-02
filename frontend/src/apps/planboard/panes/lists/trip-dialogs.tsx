import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { joinDateTime, splitDateTime } from "@/apps/planboard/panes/lists/grid-helpers";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ContactPersonField, RelationField, relationOptionOf } from "@/domains/master-data/components/RelationFields";
import { DriversField, VehicleCombinationField, VehiclesField } from "@/domains/master-data/components/ResourceFields";
import type { DriverOption, RelationOption, VehicleOption } from "@/domains/master-data/entities";
import { usePlanboardCompleteTrip } from "@/domains/planning-execution/planboard-lists";
import { MAX_TRIP_DRIVERS, MAX_TRIP_VEHICLES } from "@/domains/planning-execution/trip-type-presets";
import { useSetTripCarrier, useSetTripDrivers, useSetTripVehicles, useTransportTrip, type TransportTripDto, type TransportTripRow } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The dialogs of the trips pane (design 70 "Transport trips"): complete with the actual departure and arrival (the
 * Planboard's complete command, P:Q17, with the row's concurrency stamp) and the trip's carrier / vehicles / drivers (the
 * existing set commands of design 59). Remarks and driver instructions are edited inline in the pane. Errors show inside the dialog (UI-FB-004); success closes, confirms and hands over to the pane's refresh. */

/** Relations with the Carrier role (design 43 flags). */
const CARRIER_ROLE = 2;

function DialogFooter({ busy, onCancel, form, label, busyLabel }: { busy: boolean; onCancel: () => void; form: string; label: string; busyLabel: string }) {
  const { t } = useL();
  return (
    <>
      <Button type="button" variant="outline" onClick={onCancel} disabled={busy}>
        {t("Cancel")}
      </Button>
      <Button type="submit" form={form} disabled={busy}>
        {busy ? busyLabel : label}
      </Button>
    </>
  );
}

function ErrorAlert({ message, className }: { message: string | null; className?: string }) {
  if (!message) return null;
  return (
    <Alert variant="destructive" className={className}>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  );
}

/** Complete (P:Q17): the actual departure and arrival, prefilled with the actual or else the planned times. */
export function CompleteTripDialog({ trip, onClose, onDone }: { trip: TransportTripRow; onClose: () => void; onDone: () => void }) {
  const { t } = useL();
  const complete = usePlanboardCompleteTrip();
  const departure = splitDateTime(trip.actualDeparture ?? trip.plannedDeparture);
  const arrival = splitDateTime(trip.actualArrival ?? trip.plannedArrival ?? new Date().toISOString());
  const [departureDate, setDepartureDate] = useState<string | null>(departure.date);
  const [departureTime, setDepartureTime] = useState(departure.time);
  const [arrivalDate, setArrivalDate] = useState<string | null>(arrival.date);
  const [arrivalTime, setArrivalTime] = useState(arrival.time);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const busy = complete.isPending;
  const required = t("MasterData:Required");
  const timeError = (time: string) => (!submitted ? undefined : !time.trim() ? required : !parseTimeOfDay(time) ? t("Orders:InvalidTime") : undefined);
  const dateError = (date: string | null) => (submitted && !date ? required : undefined);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    const actualDeparture = departureTime.trim() ? joinDateTime(departureDate, departureTime) : null;
    const actualArrival = arrivalTime.trim() ? joinDateTime(arrivalDate, arrivalTime) : null;
    if (!actualDeparture || !actualArrival) return;
    if (actualArrival < actualDeparture) {
      setError(t("Planboard:Lists.ArrivalBeforeDeparture"));
      return;
    }
    setError(null);
    try {
      await complete.mutateAsync({ tripId: trip.id!, concurrencyStamp: trip.concurrencyStamp, actualDeparture, actualArrival });
      toast.success(t("PlanningExecution:TransportTripCompleted"));
      onClose();
      onDone();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="sm"
      title={`${t("PlanningExecution:CompleteTrip")} ${trip.number ?? ""}`}
      isBusy={busy}
      footer={<DialogFooter busy={busy} onCancel={onClose} form="planboard-complete-trip" label={t("PlanningExecution:Complete")} busyLabel={t("Saving")} />}
    >
      <form id="planboard-complete-trip" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-2 gap-x-4 gap-y-3">
        <ErrorAlert message={error} className="col-span-2" />
        <p className="col-span-2 text-sm text-muted-foreground">{t("Planboard:Lists.CompleteTripHint")}</p>
        <FormField label={t("PlanningExecution:ActualDeparture")} htmlFor="planboard-complete-departure-date" required error={dateError(departureDate)} floating="always">
          <DatePicker id="planboard-complete-departure-date" value={departureDate} onChange={setDepartureDate} required disabled={busy} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="planboard-complete-departure-time" required error={timeError(departureTime)}>
          <Input id="planboard-complete-departure-time" value={departureTime} onChange={(e) => setDepartureTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
        </FormField>
        <FormField label={t("PlanningExecution:ActualArrival")} htmlFor="planboard-complete-arrival-date" required error={dateError(arrivalDate)} floating="always">
          <DatePicker id="planboard-complete-arrival-date" value={arrivalDate} onChange={setArrivalDate} required disabled={busy} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="planboard-complete-arrival-time" required error={timeError(arrivalTime)}>
          <Input id="planboard-complete-arrival-time" value={arrivalTime} onChange={(e) => setArrivalTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
        </FormField>
      </form>
    </EntityDialog>
  );
}

export type TripResourceKind = "carrier" | "vehicles" | "drivers";

/** Carrier, vehicles or drivers of a trip, over the trip's own commands (design 59); the trip is read for its ids. */
export function TripResourcesDialog({ tripId, kind, onClose, onDone }: { tripId: string; kind: TripResourceKind; onClose: () => void; onDone: () => void }) {
  const { t } = useL();
  const trip = useTransportTrip(tripId);
  const title = kind === "carrier" ? t("PlanningExecution:Carrier") : kind === "vehicles" ? t("PlanningExecution:Vehicles") : t("PlanningExecution:Drivers");
  if (!trip.data) {
    return (
      <EntityDialog open onOpenChange={(next) => !next && onClose()} variant="action" size="sm" title={title} footer={<Button type="button" variant="outline" onClick={onClose}>{t("Cancel")}</Button>}>
        {trip.isError ? <ErrorAlert message={getApiErrorMessage(trip.error, t)} /> : <Skeleton className="h-24 w-full" />}
      </EntityDialog>
    );
  }
  return <TripResourcesForm trip={trip.data} kind={kind} title={title} onClose={onClose} onDone={onDone} />;
}

function TripResourcesForm({ trip, kind, title, onClose, onDone }: { trip: TransportTripDto; kind: TripResourceKind; title: string; onClose: () => void; onDone: () => void }) {
  const { t } = useL();
  const setCarrier = useSetTripCarrier();
  const setVehicles = useSetTripVehicles();
  const setDrivers = useSetTripDrivers();
  const [carrier, setCarrierValue] = useState<RelationOption | null>(relationOptionOf(trip.carrierRelationId, trip.carrierName));
  const [contactId, setContactId] = useState<string | null>(trip.carrierContactPersonId ?? null);
  const [combinationId, setCombinationId] = useState<string | null>(trip.vehicleCombinationId ?? null);
  const [vehicles, setVehiclesValue] = useState<VehicleOption[]>((trip.vehicles ?? []).map((v) => ({ id: v.vehicleId ?? "", name: v.name ?? "", licencePlate: v.licencePlate ?? null, kind: 1, vehicleTypeName: null })));
  const [drivers, setDriversValue] = useState<DriverOption[]>((trip.drivers ?? []).map((d) => ({ id: d.driverId ?? "", name: d.name ?? "", code: "" })));
  const [error, setError] = useState<string | null>(null);
  const busy = setCarrier.isPending || setVehicles.isPending || setDrivers.isPending;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      if (kind === "carrier") await setCarrier.mutateAsync({ id: trip.id!, carrierRelationId: carrier?.id ?? null, carrierContactPersonId: carrier ? contactId : null });
      else if (kind === "vehicles") await setVehicles.mutateAsync({ id: trip.id!, vehicleCombinationId: combinationId, vehicleIds: combinationId ? [] : vehicles.slice(0, MAX_TRIP_VEHICLES).map((v) => v.id) });
      else await setDrivers.mutateAsync({ id: trip.id!, driverIds: drivers.slice(0, MAX_TRIP_DRIVERS).map((d) => d.id) });
      toast.success(t("PlanningExecution:TransportTripSaved"));
      onClose();
      onDone();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="sm"
      title={`${title} ${trip.number ?? ""}`}
      isBusy={busy}
      footer={<DialogFooter busy={busy} onCancel={onClose} form="planboard-trip-resources" label={t("Save")} busyLabel={t("Saving")} />}
    >
      <form id="planboard-trip-resources" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        <ErrorAlert message={error} />
        {kind === "carrier" && (
          <>
            <RelationField id="planboard-trip-carrier" label={t("PlanningExecution:Carrier")} value={carrier} onChange={(next) => { setCarrierValue(next); setContactId(null); }} roles={CARRIER_ROLE} disabled={busy} />
            <ContactPersonField id="planboard-trip-carrier-contact" labelKey="PlanningExecution:CarrierContactPerson" relationId={carrier?.id} value={contactId} onChange={setContactId} hint={carrier ? undefined : t("PlanningExecution:ContactPersonHint")} disabled={busy} />
          </>
        )}
        {kind === "vehicles" && (
          <>
            <VehicleCombinationField
              id="planboard-trip-combination"
              label={t("PlanningExecution:VehicleCombination")}
              value={combinationId}
              onChange={(next) => {
                setCombinationId(next);
                if (next) setVehiclesValue([]);
              }}
              stored={{ id: trip.vehicleCombinationId, name: trip.vehicleCombinationName }}
              disabled={busy}
              createDisabled={busy}
            />
            <VehiclesField
              id="planboard-trip-vehicles"
              label={t("PlanningExecution:Vehicles")}
              value={vehicles}
              onChange={(next) => {
                setVehiclesValue(next);
                if (next.length > 0) setCombinationId(null);
              }}
              onAdd={(vehicle) => {
                setVehiclesValue((current) => (current.some((v) => v.id === vehicle.id) ? current : [...current, vehicle].slice(0, MAX_TRIP_VEHICLES)));
                setCombinationId(null);
              }}
              max={MAX_TRIP_VEHICLES}
              hint={t("PlanningExecution:VehiclesOrCombinationHint").replace("{0}", String(MAX_TRIP_VEHICLES))}
              disabled={busy || combinationId != null}
              createDisabled={busy}
            />
          </>
        )}
        {kind === "drivers" && (
          <DriversField
            id="planboard-trip-drivers"
            label={t("PlanningExecution:Drivers")}
            value={drivers}
            onChange={setDriversValue}
            onAdd={(driver) => setDriversValue((current) => (current.some((d) => d.id === driver.id) ? current : [...current, driver].slice(0, MAX_TRIP_DRIVERS)))}
            max={MAX_TRIP_DRIVERS}
            hint={t("PlanningExecution:DriversHint").replace("{0}", String(MAX_TRIP_DRIVERS))}
            disabled={busy}
            createDisabled={busy}
          />
        )}
      </form>
    </EntityDialog>
  );
}
