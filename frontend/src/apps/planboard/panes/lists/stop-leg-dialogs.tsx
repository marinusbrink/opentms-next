import { useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { joinDateTime, splitDateTime } from "@/apps/planboard/panes/lists/grid-helpers";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Textarea } from "@/components/ui/textarea";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { AddressField, RelationField } from "@/domains/master-data/components/RelationFields";
import type { RelationOption } from "@/domains/master-data/entities";
import { parseTimeOfDay, waypointTypes } from "@/domains/orders/reference-data";
import { toOptions } from "@/domains/planning-execution/components/form-fields";
import { legEndLabel, useUpdateLeg, type LegDto } from "@/domains/planning-execution/legs";
import { useSplitUnplannedLeg } from "@/domains/planning-execution/planboard-lists";
import { useAddManualStop, useRecordStopActuals, type TripStopDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseInteger, parseNumber } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The dialogs of the Trip stops, Legs and Legs to schedule panes (design 70 "Panes"): record a stop's actual times, add a
 * manual stop (design 59), edit a leg's references and remarks (Planning fields only, D19) and split quantity off an
 * unplanned leg (design 68 §7). Errors show inside the dialog (UI-FB-004); success closes and confirms. */

function Footer({ busy, onCancel, form, label }: { busy: boolean; onCancel: () => void; form: string; label: string }) {
  const { t } = useL();
  return (
    <>
      <Button type="button" variant="outline" onClick={onCancel} disabled={busy}>
        {t("Cancel")}
      </Button>
      <Button type="submit" form={form} disabled={busy}>
        {busy ? t("Saving") : label}
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

/** Record actuals (design 59 `RecordStopActual`): arrival, departure and the cumulative distance of a stop. */
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
  const busy = record.isPending;
  const timeError = (time: string) => (submitted && time.trim() && !parseTimeOfDay(time) ? t("Orders:InvalidTime") : undefined);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (timeError(arrivalTime) || timeError(departureTime)) return;
    const km = parseNumber(distanceKm);
    setError(null);
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
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="sm"
      title={`${t("PlanningExecution:RecordActuals")} ${stop.relationName ?? stop.name ?? ""}`}
      isBusy={busy}
      footer={<Footer busy={busy} onCancel={onClose} form="planboard-stop-actuals" label={t("PlanningExecution:RecordActuals")} />}
    >
      <form id="planboard-stop-actuals" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-2 gap-x-4 gap-y-3">
        <ErrorAlert message={error} className="col-span-2" />
        <FormField label={t("PlanningExecution:ActualArrival")} htmlFor="planboard-actuals-arrival-date" floating="always">
          <DatePicker id="planboard-actuals-arrival-date" value={arrivalDate} onChange={setArrivalDate} disabled={busy} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="planboard-actuals-arrival-time" error={timeError(arrivalTime)}>
          <Input id="planboard-actuals-arrival-time" value={arrivalTime} onChange={(e) => setArrivalTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
        </FormField>
        <FormField label={t("PlanningExecution:ActualDeparture")} htmlFor="planboard-actuals-departure-date" floating="always">
          <DatePicker id="planboard-actuals-departure-date" value={departureDate} onChange={setDepartureDate} disabled={busy} />
        </FormField>
        <FormField label={t("Orders:Time")} htmlFor="planboard-actuals-departure-time" error={timeError(departureTime)}>
          <Input id="planboard-actuals-departure-time" value={departureTime} onChange={(e) => setDepartureTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
        </FormField>
        <FormField label={`${t("PlanningExecution:CumulativeDistance")} (km)`} htmlFor="planboard-actuals-distance">
          <Input id="planboard-actuals-distance" inputMode="decimal" className="tabular-nums" value={distanceKm} onChange={(e) => setDistanceKm(e.target.value)} disabled={busy} />
        </FormField>
      </form>
    </EntityDialog>
  );
}

/** Add a manual stop (design 59 `AddManualStop`): activity, relation, address, position, duration, remarks. */
export function AddManualStopDialog({ tripId, stopCount, onClose }: { tripId: string; stopCount: number; onClose: () => void }) {
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
  const busy = add.isPending;
  const required = t("MasterData:Required");

  const submit = async (event: FormEvent) => {
    event.preventDefault();
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
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="sm"
      title={t("PlanningExecution:AddStop")}
      isBusy={busy}
      footer={<Footer busy={busy} onCancel={onClose} form="planboard-add-stop" label={t("PlanningExecution:AddStop")} />}
    >
      <form id="planboard-add-stop" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        <ErrorAlert message={error} />
        <FormField label={t("Orders:StopType")} htmlFor="planboard-add-stop-type" required error={submitted && !waypointTypeId ? required : undefined} floating="always">
          <ReferenceCombobox
            id="planboard-add-stop-type"
            value={waypointTypeId}
            onChange={setWaypointTypeId}
            options={waypointOptions}
            required
            aria-invalid={submitted && !waypointTypeId}
            {...inPlace.offer("WaypointType", busy, (r) => setWaypointTypeId(r.id))}
          />
        </FormField>
        <RelationField
          id="planboard-add-stop-relation"
          label={t("PlanningExecution:Relation")}
          value={relation}
          onChange={(next) => {
            setRelation(next);
            setAddressId(null);
          }}
          required
          error={submitted && !relation ? required : undefined}
          disabled={busy}
        />
        <AddressField id="planboard-add-stop-address" label={t("Orders:Address")} relationId={relation?.id} value={addressId} onChange={setAddressId} disabled={busy} />
        <FormField label={t("PlanningExecution:Position")} htmlFor="planboard-add-stop-position">
          <Input id="planboard-add-stop-position" inputMode="numeric" className="tabular-nums" value={position} onChange={(e) => setPosition(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t("Orders:PlannedDurationMin")} htmlFor="planboard-add-stop-duration">
          <Input id="planboard-add-stop-duration" inputMode="numeric" className="tabular-nums" value={duration} onChange={(e) => setDuration(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t("PlanningExecution:Remarks")} htmlFor="planboard-add-stop-remarks" floating="always">
          <Textarea id="planboard-add-stop-remarks" rows={2} value={remarks} maxLength={2500} onChange={(e) => setRemarks(e.target.value)} disabled={busy} />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}

/** References and remarks of a leg (the Planning fields only, D19; dates and windows stay as they are). */
export function LegReferencesDialog({ leg, onClose }: { leg: LegDto; onClose: () => void }) {
  const { t } = useL();
  const update = useUpdateLeg();
  const [originReference, setOriginReference] = useState(leg.origin?.reference ?? "");
  const [destinationReference, setDestinationReference] = useState(leg.destination?.reference ?? "");
  const [remarks, setRemarks] = useState(leg.remarks ?? "");
  const [error, setError] = useState<string | null>(null);
  const busy = update.isPending;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      await update.mutateAsync({
        id: leg.id!,
        dto: { concurrencyStamp: leg.concurrencyStamp ?? "", originReference: originReference.trim() || null, destinationReference: destinationReference.trim() || null, remarks: remarks.trim() || null },
      });
      toast.success(t("PlanningExecution:LegSaved"));
      onClose();
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
      title={`${t("Planboard:Lists.EditLeg")} ${legEndLabel(leg.origin)} → ${legEndLabel(leg.destination)}`}
      isBusy={busy}
      footer={<Footer busy={busy} onCancel={onClose} form="planboard-leg-references" label={t("Save")} />}
    >
      <form id="planboard-leg-references" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        <ErrorAlert message={error} />
        <FormField label={t("Planboard:Lists.OriginReference")} htmlFor="planboard-leg-origin-reference">
          <Input id="planboard-leg-origin-reference" maxLength={128} value={originReference} onChange={(e) => setOriginReference(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t("Planboard:Lists.DestinationReference")} htmlFor="planboard-leg-destination-reference">
          <Input id="planboard-leg-destination-reference" maxLength={128} value={destinationReference} onChange={(e) => setDestinationReference(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t("PlanningExecution:Remarks")} htmlFor="planboard-leg-remarks" floating="always">
          <Textarea id="planboard-leg-remarks" rows={3} maxLength={2500} value={remarks} onChange={(e) => setRemarks(e.target.value)} disabled={busy} />
        </FormField>
      </form>
    </EntityDialog>
  );
}

/** Split quantity off an unplanned leg (design 68 §7): the leg keeps the rest, a new unplanned leg carries the quantity. */
export function SplitLegLineDialog({ leg, onClose, onDone }: { leg: LegDto; onClose: () => void; onDone: () => void }) {
  const { t } = useL();
  const f = useFormat();
  const split = useSplitUnplannedLeg();
  const total = leg.quantity ?? 0;
  const [quantity, setQuantity] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const busy = split.isPending;
  const parsed = parseNumber(quantity);
  const invalid = parsed == null || Number.isNaN(parsed) || parsed <= 0 || parsed >= total;
  const rangeError = submitted && invalid ? t("PlanningExecution:SplitQuantityRange").replace("{0}", f.number(total)) : undefined;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (invalid || parsed == null) return;
    setError(null);
    try {
      await split.mutateAsync({ legId: leg.id!, quantity: parsed });
      toast.success(t("PlanningExecution:QuantitySplit"));
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
      title={`${t("PlanningExecution:SplitLine")} ${leg.orderNumber ?? ""}`}
      isBusy={busy}
      footer={<Footer busy={busy} onCancel={onClose} form="planboard-split-line" label={t("PlanningExecution:Split")} />}
    >
      <form id="planboard-split-line" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        <ErrorAlert message={error} />
        <FormField label={t("PlanningExecution:SplitQuantity")} htmlFor="planboard-split-quantity" required error={rangeError} hint={t("PlanningExecution:SplitQuantityHint")}>
          <Input id="planboard-split-quantity" inputMode="decimal" className="tabular-nums" value={quantity} onChange={(e) => setQuantity(e.target.value)} disabled={busy} autoFocus />
        </FormField>
      </form>
    </EntityDialog>
  );
}
