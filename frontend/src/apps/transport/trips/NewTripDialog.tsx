import { useEffect, useMemo, useRef, useState } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ComboField, SelectField, TextField, toOptions } from "@/apps/transport/components/fields";
import { defaultTripTypeIdOf } from "@/apps/transport/orders/order-form";
import { newTripCreateDtoOf, newTripValues, validateNewTrip, withTripDate, type NewTripValues } from "@/apps/transport/trips/new-trip-form";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { AddressField, ContactPersonField, RelationField } from "@/domains/master-data/components/RelationFields";
import { DriversField, VehicleCombinationField, VehiclesField } from "@/domains/master-data/components/ResourceFields";
import type { DriverOption, RelationOption, VehicleOption } from "@/domains/master-data/entities";
import { TRIP_EXECUTIONS, transportTripTypes, useDefaultTransportTripTypeId, type TransportTripTypeDto } from "@/domains/planning-execution/reference-data";
import { MAX_TRIP_DRIVERS, MAX_TRIP_VEHICLES, NO_TRIP_TYPE_PRESET, mergeOptions, optionsOfIds, switchTripTypePreset, tripTypePresetOf, type TripTypePreset } from "@/domains/planning-execution/trip-type-presets";
import { FIRST_LAST_STOP_RULES, STOP_COMPOSITION_MODES, useCreateTransportTrip } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormState } from "@/lib/forms/use-form-state";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* New trip (design 61 "Trip type defaults" 4, PO 2026-10-29: superseding "no New" on the Trips list): an editor dialog
 * with the trip's type (the default type preselected), trip date (today), planned departure (date and time), name,
 * execution, carrier and contact, combination or vehicles, drivers, departure and arrival, stop composition and the
 * first/last stop rule. Choosing a type presets every field it defines; another type switches the fields that still hold
 * the previous type's preset and keeps the ones the user changed (point 5). Saved through POST transport-trip, then the
 * trip page opens (legs are assigned there or on the Planboard). */

/** Relations with the Carrier role (design 43 flags). */
const CARRIER_ROLE = 2;

const sameRelation = (a: RelationOption | null, b: RelationOption | null) => (a?.id?.toLowerCase() ?? null) === (b?.id?.toLowerCase() ?? null);

export interface NewTripDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The trip was created (the list refreshes); the dialog then opens its page. */
  onCreated?: () => void;
}

export function NewTripDialog({ open, onOpenChange, onCreated }: NewTripDialogProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const create = useCreateTransportTrip();
  const busy = create.isPending;
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const [initial] = useState(() => newTripValues(toIsoDate(new Date())));
  const { form, isDirty, submit, setValues, reset } = useFormState<NewTripValues>(initial, validateNewTrip, false);
  const v = form.values;

  const typeLookup = transportTripTypes.useLookup(open);
  const typeOptions = useMemo(() => toOptions(typeLookup.data), [typeLookup.data]);
  const defaultType = useDefaultTransportTripTypeId(open);
  const defaultTypeId = typeLookup.data ? defaultTripTypeIdOf(defaultType.data, typeLookup.data.map((type) => type.id ?? "").filter(Boolean)) : null;
  const typeDetail = transportTripTypes.useDetail(v.transportTripTypeId ?? undefined);
  /* The preset the fields were last given (a switch compares each field with it) and the names of preset records. */
  const applied = useRef<{ typeId: string | null; preset: TripTypePreset }>({ typeId: null, preset: NO_TRIP_TYPE_PRESET });
  const [knownVehicles, setKnownVehicles] = useState<VehicleOption[]>([]);
  const [knownDrivers, setKnownDrivers] = useState<DriverOption[]>([]);
  const inPlace = useCreateInPlace();

  /* A preset the dialog applies on its own (the default type as it opens) leaves the form clean (UI-FORM-009). */
  const apply = (next: NewTripValues) => (isDirty ? setValues(next) : reset(next));

  useEffect(() => {
    if (!defaultTypeId || v.transportTripTypeId) return;
    apply({ ...v, transportTripTypeId: defaultTypeId });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [defaultTypeId]);

  useEffect(() => {
    const typeId = v.transportTripTypeId;
    if (!typeId || !typeDetail.data || applied.current.typeId === typeId) return;
    const preset = tripTypePresetOf(typeDetail.data);
    const next = switchTripTypePreset(v, applied.current.preset, preset);
    applied.current = { typeId, preset };
    setKnownVehicles((known) => mergeOptions(known, preset.vehicles));
    setKnownDrivers((known) => mergeOptions(known, preset.drivers));
    apply(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [typeDetail.data, v.transportTripTypeId]);

  const save = async () => {
    const values = submit(formRef.current);
    if (!values) return;
    setError(null);
    try {
      const created = await create.mutateAsync(newTripCreateDtoOf(values));
      toast.success(t("PlanningExecution:TransportTripSaved"));
      onOpenChange(false);
      onCreated?.();
      if (created?.id) openRecord("TransportTrip", created.id);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const executionItems = TRIP_EXECUTIONS.map((e) => ({ value: e.value, label: t(e.key) }));
  const stopCompositionItems = STOP_COMPOSITION_MODES.map((m) => ({ value: m.value, label: t(m.key) }));
  const firstLastStopItems = FIRST_LAST_STOP_RULES.map((r) => ({ value: r.value, label: t(r.key) }));

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="editor"
      size="md"
      title={t("PlanningExecution:NewTransportTrip")}
      isDirty={isDirty}
      isBusy={busy}
      commands={[{ id: "save", labelKey: busy ? "Saving" : "Save", icon: Save, isPrimary: true, onClick: () => void save() }]}
    >
      <form
        ref={formRef}
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          void save();
        }}
        className="flex flex-col gap-4"
        data-slot="new-trip-form"
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("PlanningExecution:TransportTrip")}>
            <div className="grid grid-cols-1 gap-3">
              <ComboField
                form={form}
                field="transportTripTypeId"
                labelKey="PlanningExecution:TransportTripType"
                required
                options={typeOptions}
                {...inPlace.offer<TransportTripTypeDto>("TransportTripType", busy, (r) => form.set("transportTripTypeId", r.id))}
              />
              <FormField label={t("PlanningExecution:TripDate")} htmlFor={form.id("tripDate")} required error={form.error("tripDate")} floating="always">
                <DatePicker id={form.id("tripDate")} value={v.tripDate} onChange={(next) => form.update((c) => withTripDate(c, next))} required disabled={busy} />
              </FormField>
              <TextField form={form} field="name" labelKey="PlanningExecution:Name" maxLength={128} />
              <SelectField form={form} field="execution" labelKey="PlanningExecution:Execution" items={executionItems} />
              <SelectField form={form} field="stopCompositionMode" labelKey="PlanningExecution:StopCompositionMode" items={stopCompositionItems} />
              <SelectField form={form} field="firstLastStopRule" labelKey="PlanningExecution:FirstLastStopRule" items={firstLastStopItems} />
            </div>
          </FormSection>
          <FormSection title={t("PlanningExecution:Resources")}>
            <div className="grid grid-cols-1 gap-3">
              <RelationField
                id={form.id("carrierRelation")}
                label={t("PlanningExecution:Carrier")}
                value={v.carrierRelation}
                onChange={(next) => form.update((c) => ({ ...c, carrierRelation: next, carrierContactPersonId: sameRelation(next, c.carrierRelation) ? c.carrierContactPersonId : null }))}
                roles={CARRIER_ROLE}
                disabled={busy}
              />
              <ContactPersonField
                id={form.id("carrierContactPersonId")}
                labelKey="PlanningExecution:ContactPerson"
                relationId={v.carrierRelation?.id}
                value={v.carrierContactPersonId}
                onChange={(next) => form.set("carrierContactPersonId", next)}
                hint={v.carrierRelation ? undefined : t("PlanningExecution:ContactPersonHint")}
                disabled={busy}
              />
              <VehicleCombinationField
                id={form.id("vehicleCombinationId")}
                label={t("PlanningExecution:VehicleCombination")}
                value={v.vehicleCombinationId}
                onChange={(next) => form.update((c) => ({ ...c, vehicleCombinationId: next, vehicleIds: next ? [] : c.vehicleIds }))}
                stored={{ id: typeDetail.data?.defaultVehicleCombinationId, name: typeDetail.data?.defaultVehicleCombinationName }}
                disabled={busy}
              />
              <VehiclesField
                id={form.id("vehicleIds")}
                label={t("PlanningExecution:Vehicles")}
                value={optionsOfIds(v.vehicleIds, knownVehicles, (id) => ({ id, name: id, licencePlate: null, kind: 1, vehicleTypeName: null }))}
                onChange={(next) => {
                  setKnownVehicles((known) => mergeOptions(known, next));
                  form.update((c) => ({ ...c, vehicleIds: next.map((vehicle) => vehicle.id), vehicleCombinationId: next.length > 0 ? null : c.vehicleCombinationId }));
                }}
                onAdd={(vehicle) => {
                  setKnownVehicles((known) => mergeOptions(known, [vehicle]));
                  form.update((c) => ({ ...c, vehicleIds: c.vehicleIds.includes(vehicle.id) ? c.vehicleIds : [...c.vehicleIds, vehicle.id].slice(0, MAX_TRIP_VEHICLES), vehicleCombinationId: null }));
                }}
                max={MAX_TRIP_VEHICLES}
                hint={t("PlanningExecution:VehiclesOrCombinationHint").replace("{0}", String(MAX_TRIP_VEHICLES))}
                disabled={busy}
              />
              <DriversField
                id={form.id("driverIds")}
                label={t("PlanningExecution:Drivers")}
                value={optionsOfIds(v.driverIds, knownDrivers, (id) => ({ id, name: id, code: "" }))}
                onChange={(next) => {
                  setKnownDrivers((known) => mergeOptions(known, next));
                  form.set("driverIds", next.map((driver) => driver.id));
                }}
                onAdd={(driver) => {
                  setKnownDrivers((known) => mergeOptions(known, [driver]));
                  form.update((c) => ({ ...c, driverIds: c.driverIds.includes(driver.id) ? c.driverIds : [...c.driverIds, driver.id].slice(0, MAX_TRIP_DRIVERS) }));
                }}
                max={MAX_TRIP_DRIVERS}
                hint={t("PlanningExecution:DriversHint").replace("{0}", String(MAX_TRIP_DRIVERS))}
                disabled={busy}
              />
            </div>
          </FormSection>
          <FormSection title={t("PlanningExecution:Departure")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <FormField label={t("PlanningExecution:PlannedDeparture")} htmlFor={form.id("departureDate")} required error={form.error("departureDate")} floating="always">
                <DatePicker id={form.id("departureDate")} value={v.departureDate} onChange={(next) => form.set("departureDate", next)} required disabled={busy} />
              </FormField>
              <TextField form={form} field="departureTime" labelKey="PlanningExecution:DepartureTime" required maxLength={5} />
              <RelationField
                id={form.id("departureRelation")}
                label={t("PlanningExecution:DepartureRelation")}
                value={v.departureRelation}
                onChange={(next) => form.update((c) => ({ ...c, departureRelation: next, departureAddressId: sameRelation(next, c.departureRelation) ? c.departureAddressId : null }))}
                disabled={busy}
                className="col-span-2"
              />
              <AddressField
                id={form.id("departureAddressId")}
                label={t("PlanningExecution:DepartureAddress")}
                relationId={v.departureRelation?.id}
                value={v.departureAddressId}
                onChange={(next) => form.set("departureAddressId", next)}
                hint={v.departureRelation ? undefined : t("PlanningExecution:AddressHint")}
                disabled={busy}
                className="col-span-2"
              />
            </div>
          </FormSection>
          <FormSection title={t("PlanningExecution:Arrival")}>
            <div className="grid grid-cols-1 gap-3">
              <RelationField
                id={form.id("arrivalRelation")}
                label={t("PlanningExecution:ArrivalRelation")}
                value={v.arrivalRelation}
                onChange={(next) => form.update((c) => ({ ...c, arrivalRelation: next, arrivalAddressId: sameRelation(next, c.arrivalRelation) ? c.arrivalAddressId : null }))}
                disabled={busy}
              />
              <AddressField
                id={form.id("arrivalAddressId")}
                label={t("PlanningExecution:ArrivalAddress")}
                relationId={v.arrivalRelation?.id}
                value={v.arrivalAddressId}
                onChange={(next) => form.set("arrivalAddressId", next)}
                hint={v.arrivalRelation ? undefined : t("PlanningExecution:AddressHint")}
                disabled={busy}
              />
            </div>
          </FormSection>
        </div>
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
