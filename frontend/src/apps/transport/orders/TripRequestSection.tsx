import { useEffect, useMemo, useRef, useState } from "react";
import { ExternalLink } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ComboField, NumberField, TextField, toOptions } from "@/apps/transport/components/fields";
import { CurrencyField } from "@/apps/transport/components/CurrencyField";
import { TripRequestWarningAlert } from "@/apps/transport/components/TripRequestWarning";
import { AddressField, ContactPersonField, RelationField } from "@/domains/master-data/components/RelationFields";
import { DriversField, VehicleCombinationField, VehiclesField } from "@/domains/master-data/components/ResourceFields";
import { defaultTripTypeIdOf, tripRequestForTypeChange, tripRequestWithDefaults, type EditorMode, type OrderValues, type TripRequestValues } from "@/apps/transport/orders/order-form";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import type { DriverOption, VehicleOption } from "@/domains/master-data/entities";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { transportTripTypes, useCachedTransportTripType, useDefaultTransportTripTypeId, type TransportTripTypeDto } from "@/domains/planning-execution/reference-data";
import { MAX_TRIP_DRIVERS, MAX_TRIP_VEHICLES, NO_TRIP_TYPE_PRESET, mergeOptions, optionsOfIds, tripTypePresetOf, type TripTypePreset } from "@/domains/planning-execution/trip-type-presets";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The Tevens trip tab of the order page (design 61 "Order page, legacy tab structure" 8; Q9): a checkbox "Create a trip with
 * this order", then trip type, departure/arrival (relation + address), planned departure, which freight items go on
 * the trip (a checkbox per item, default all: only their legs are put on the trip on Confirm, the others stay
 * unplanned for the Planboard; the trip's stops follow from its legs), and an Advanced disclosure for carrier,
 * vehicles or combination, drivers and the agreed purchase price. Ticking it prefills the empty fields from the trip
 * type's defaults and, for what the type leaves empty, from the items on the trip; choosing another type switches the
 * fields that still hold the previous type's preset (design 61 "Trip type defaults" 5). After Confirm the section is
 * read-only and shows the created trip as a link. A failed submit with an error in the request opens the tab
 * (UI-FORM-006). */

/** Relations with the Carrier role (design 43 flags). */
const CARRIER_ROLE = 2;

export interface TripRequestSectionProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  order: TransportOrderDto | undefined;
}

export function TripRequestSection({ form, mode, order }: TripRequestSectionProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const request = form.values.tripRequest;
  /* Honoured once on Confirm (design 58): afterwards the section is read-only and shows the created trip (Q9). */
  const locked = mode === "order" && order != null && order.status !== 1;
  const readOnly = form.readOnly || locked;
  const [advanced, setAdvanced] = useState(request.carrierRelation != null || request.vehicleIds.length > 0 || request.driverIds.length > 0 || !!request.agreedPurchaseAmount);
  /* The trip is outsourced when the request names a carrier (the server then creates it Subcontracted); without one it
   * runs on the own fleet with the type's execution, and its cost comes from the cost price per km (QP10). An agreed
   * purchase price is the carrier's price (the old screen's rule: only with a carrier that is not the own company), so
   * it waits for a carrier; a stored price without one stays editable so it can be cleared. */
  const outsourced = request.carrierRelation != null;
  const priceBlocked = !outsourced && !request.agreedPurchaseAmount.trim() && !request.agreedPurchaseCurrencyCode.trim();
  const [vehicles, setVehicles] = useState<VehicleOption[]>([]);
  const [drivers, setDrivers] = useState<DriverOption[]>([]);
  const typeLookup = transportTripTypes.useLookup();
  const typeOptions = useMemo(() => toOptions(typeLookup.data), [typeLookup.data]);
  const defaultType = useDefaultTransportTripTypeId(!readOnly);
  const defaultTypeId = defaultTripTypeIdOf(defaultType.data, (typeLookup.data ?? []).map((type) => type.id ?? "").filter(Boolean));

  const set = (change: Partial<OrderValues["tripRequest"]>) => {
    form.set("tripRequest", { ...request, ...change });
    form.blur("tripRequest");
  };

  /* The trip type's defaults (design 61 "Trip type defaults" 5). The detail of the chosen type, or before ticking of
   * the type ticking will choose, so a tick presets at once. `applied` is the preset the fields were last given: a
   * switch compares each field with it. A stored request counts as holding its type's preset; a type whose detail is
   * not loaded yet is applied when it arrives. */
  const detailTypeId = readOnly ? null : request.enabled ? request.transportTripTypeId : (request.transportTripTypeId ?? defaultTypeId);
  const typeDetail = transportTripTypes.useDetail(detailTypeId ?? undefined);
  const cachedType = useCachedTransportTripType();
  const applied = useRef<{ typeId: string | null; preset: TripTypePreset; settled: boolean; applyOnLoad: boolean }>({
    typeId: request.transportTripTypeId,
    preset: NO_TRIP_TYPE_PRESET,
    settled: false,
    applyOnLoad: false,
  });
  /* The preset's records show with their names; a preset carrier, vehicle or driver opens Advanced so it is seen. */
  const showPreset = (preset: TripTypePreset, next: TripRequestValues) => {
    if (preset.vehicles.length > 0) setVehicles((known) => mergeOptions(known, preset.vehicles));
    if (preset.drivers.length > 0) setDrivers((known) => mergeOptions(known, preset.drivers));
    if (next.carrierRelation != null || next.vehicleCombinationId != null || next.vehicleIds.length > 0 || next.driverIds.length > 0) setAdvanced(true);
  };
  /* Another type's preset; a switch that removes the carrier takes its agreed price with it, as clearing the carrier does. */
  const switchedType = (before: TripRequestValues, typeId: string | null, lines: OrderValues["lines"], previous: TripTypePreset, preset: TripTypePreset): TripRequestValues => {
    const next = tripRequestForTypeChange({ ...before, transportTripTypeId: typeId }, lines, mode, previous, preset);
    return next.carrierRelation == null && before.carrierRelation != null ? { ...next, agreedPurchaseAmount: "", agreedPurchaseCurrencyCode: "" } : next;
  };
  useEffect(() => {
    const current = applied.current;
    if (current.settled || !typeDetail.data || !detailTypeId || current.typeId !== detailTypeId) return;
    const preset = tripTypePresetOf(typeDetail.data);
    applied.current = { typeId: current.typeId, preset, settled: true, applyOnLoad: false };
    if (!current.applyOnLoad || !request.enabled) return;
    const next = switchedType(request, request.transportTripTypeId, form.values.lines, current.preset, preset);
    showPreset(preset, next);
    form.set("tripRequest", next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [typeDetail.data, detailTypeId]);

  const chooseType = (typeId: string | null) => {
    const previous = applied.current.preset;
    const cached = typeId ? cachedType(typeId) : undefined;
    if (!typeId || !cached) {
      applied.current = { typeId, preset: previous, settled: !typeId, applyOnLoad: typeId != null };
      return set({ transportTripTypeId: typeId });
    }
    const preset = tripTypePresetOf(cached);
    applied.current = { typeId, preset, settled: true, applyOnLoad: false };
    const next = switchedType(request, typeId, form.values.lines, previous, preset);
    showPreset(preset, next);
    form.set("tripRequest", next);
    form.blur("tripRequest");
  };
  /* Ticking "Create a trip" prefills the empty fields from the type and the order (design 61 "Tevens trip": easier than
   * OpenTMS; "Trip type defaults" 5: the type's departure/arrival win). */
  const enable = (enabled: boolean) => {
    if (!enabled) return set({ enabled: false });
    const typeId = request.transportTripTypeId ?? defaultTypeId;
    const cached = cachedType(typeId);
    const preset = cached ? tripTypePresetOf(cached) : null;
    applied.current = { typeId, preset: preset ?? NO_TRIP_TYPE_PRESET, settled: preset != null || typeId == null, applyOnLoad: preset == null && typeId != null };
    const next = tripRequestWithDefaults({ ...request, enabled: true }, form.values.lines, mode, defaultTypeId, preset);
    if (preset) showPreset(preset, next);
    set(next);
  };
  /* "Add new …" in every picker (UI-LOOK-004): the record its editor saved lands in the latest request. */
  const inPlace = useCreateInPlace();
  const setLatest = (change: (current: OrderValues["tripRequest"]) => Partial<OrderValues["tripRequest"]>) =>
    form.update((values) => ({ ...values, tripRequest: { ...values.tripRequest, ...change(values.tripRequest) } }));
  const requestForm = {
    values: request,
    set: (field: string, value: unknown) => (field === "transportTripTypeId" ? chooseType((value as string | null) ?? null) : set({ [field]: value })),
    update: () => undefined,
/* The section shows its one message above the fields (UI-FB-004: exactly once), not under each field. */
    error: () => undefined,
    blur: () => form.blur("tripRequest"),
    readOnly,
    id: (field: string) => form.id(`trip-${field}`),
  } as unknown as FormContext<OrderValues["tripRequest"]>;

  /* Which freight items go on the trip (PO 2026-10-26): a checkbox per item, default all; only their legs are put on
   * the trip on Confirm, the others stay unplanned for the Planboard. */
  const toggleItem = (key: string, included: boolean) =>
    set({ excludedLineKeys: included ? request.excludedLineKeys.filter((k) => k !== key) : [...request.excludedLineKeys, key] });

  return (
    <FormSection title={t("Orders:TripRequest")}>
      <div className="mb-3 flex items-center gap-4">
        <label className="flex items-center gap-2 text-label font-semibold text-brand" htmlFor={form.id("trip-enabled")}>
          <Checkbox id={form.id("trip-enabled")} checked={request.enabled} disabled={readOnly} onCheckedChange={(next) => enable(next === true)} />
          {t("Orders:TripRequestHint")}
        </label>
        {locked && order?.tripRequest?.tripId && (
          <Button type="button" variant="link" size="sm" className="ml-auto" onClick={() => openRecord("TransportTrip", order.tripRequest!.tripId!)}>
            <ExternalLink /> {t("Orders:Trip")} {order.tripRequest.tripNumber ?? ""}
          </Button>
        )}
      </div>
      {mode === "order" && <TripRequestWarningAlert request={order?.tripRequest} />}
      {form.error("tripRequest") && <p className="mb-2 text-label text-destructive">{form.error("tripRequest")}</p>}
      {request.enabled && (
        <div className="grid grid-cols-4 gap-x-4 gap-y-3" data-slot="trip-request-fields">
          <ComboField
            form={requestForm}
            field="transportTripTypeId"
            labelKey="Orders:TransportTripType"
            required
            options={typeOptions}
            {...inPlace.offer<TransportTripTypeDto>("TransportTripType", readOnly, (r) => {
              /* The new type's defaults preset the trip like a chosen type's (its editor returns the detail). */
              const previous = applied.current.preset;
              const preset = tripTypePresetOf(r.dto);
              applied.current = { typeId: r.id, preset, settled: true, applyOnLoad: false };
              setVehicles((known) => mergeOptions(known, preset.vehicles));
              setDrivers((known) => mergeOptions(known, preset.drivers));
              form.update((values) => ({ ...values, tripRequest: switchedType(values.tripRequest, r.id, values.lines, previous, preset) }));
            })}
          />
          {mode === "order" ? (
            <FormField label={t("Orders:PlannedDeparture")} htmlFor={form.id("trip-departureDate")} required floating="always">
              <DatePicker id={form.id("trip-departureDate")} value={request.departureDate} onChange={(next) => set({ departureDate: next })} required disabled={readOnly} />
            </FormField>
          ) : (
            <NumberField form={requestForm} field="departureDayOffset" labelKey="Orders:DayOffset" required />
          )}
          <TextField form={requestForm} field="departureTime" labelKey="Orders:DepartureTime" />
          <span />
          <RelationField id={form.id("trip-departureRelation")} label={t("Orders:Departure")} value={request.departureRelation} onChange={(next) => set({ departureRelation: next, departureAddressId: null })} required disabled={readOnly} />
          <AddressField id={form.id("trip-departureAddress")} label={`${t("Orders:Departure")}: ${t("Orders:Address")}`} relationId={request.departureRelation?.id} value={request.departureAddressId} onChange={(next) => set({ departureAddressId: next })} required disabled={readOnly} />
          <RelationField id={form.id("trip-arrivalRelation")} label={t("Orders:Arrival")} value={request.arrivalRelation} onChange={(next) => set({ arrivalRelation: next, arrivalAddressId: null })} required disabled={readOnly} />
          <AddressField id={form.id("trip-arrivalAddress")} label={`${t("Orders:Arrival")}: ${t("Orders:Address")}`} relationId={request.arrivalRelation?.id} value={request.arrivalAddressId} onChange={(next) => set({ arrivalAddressId: next })} required disabled={readOnly} />
          {form.values.lines.length > 0 && (
            <fieldset className="col-span-4 flex flex-col gap-1" data-slot="trip-request-items" aria-label={t("Orders:TripRequestItems")}>
              <legend className="mb-1 text-label font-medium text-muted-foreground">{t("Orders:TripRequestItems")}</legend>
              {form.values.lines.map((line, index) => {
                const first = line.stops[0];
                const last = line.stops.length > 1 ? line.stops.at(-1) : undefined;
                const route = [first?.relationName, last?.relationName].filter(Boolean).join(" → ");
                const id = form.id(`trip-item-${line.key}`);
                return (
                  <label key={line.key} className="flex h-8 items-center gap-2 text-sm" htmlFor={id}>
                    <Checkbox id={id} checked={!request.excludedLineKeys.includes(line.key)} disabled={readOnly} onCheckedChange={(next) => toggleItem(line.key, next === true)} />
                    {`${t("Orders:Line")} ${index + 1}`}
                    {route && <span className="text-muted-foreground">{route}</span>}
                  </label>
                );
              })}
              {/* None ticked (a stored empty selection, or every item unticked): the user chooses items or switches the trip off. */}
              {form.values.lines.every((line) => request.excludedLineKeys.includes(line.key)) && !form.error("tripRequest") && (
                <p className="text-label text-destructive" data-slot="trip-request-no-items">{t("Orders:TripRequestNoItems")}</p>
              )}
              <p className="text-label text-muted-foreground">{t("Orders:TripRequestItemsHint")}</p>
            </fieldset>
          )}
          <div className="col-span-4">
            <Button type="button" variant="link" size="sm" aria-expanded={advanced} onClick={() => setAdvanced((v) => !v)}>
              {t("Orders:Advanced")}
            </Button>
          </div>
          {advanced && (
            <>
              <RelationField
                id={form.id("trip-carrier")}
                label={t("Orders:Carrier")}
                value={request.carrierRelation}
                onChange={(next) => set({ carrierRelation: next, carrierContactPersonId: null, ...(next ? {} : { agreedPurchaseAmount: "", agreedPurchaseCurrencyCode: "" }) })}
                roles={CARRIER_ROLE}
                disabled={readOnly}
              />
              <ContactPersonField id={form.id("trip-carrierContact")} labelKey="PlanningExecution:CarrierContactPerson" relationId={request.carrierRelation?.id} value={request.carrierContactPersonId} onChange={(next) => set({ carrierContactPersonId: next })} disabled={readOnly} />
              <VehicleCombinationField
                id={form.id("trip-vehicleCombinationId")}
                label={t("Orders:VehicleCombination")}
                value={request.vehicleCombinationId}
                onChange={(next) => set({ vehicleCombinationId: next, ...(next ? { vehicleIds: [] } : {}) })}
                onCreated={(id) => setLatest(() => ({ vehicleCombinationId: id, vehicleIds: [] }))}
                stored={{ id: request.vehicleCombinationId, name: typeDetail.data?.defaultVehicleCombinationId === request.vehicleCombinationId ? typeDetail.data?.defaultVehicleCombinationName : null }}
                disabled={readOnly || request.vehicleIds.length > 0}
              />
              <VehiclesField
                id={form.id("trip-vehicles")}
                label={t("Orders:Vehicles")}
                value={optionsOfIds(request.vehicleIds, vehicles, (id) => ({ id, name: id, licencePlate: null, kind: 1, vehicleTypeName: null }))}
                onChange={(next) => {
                  setVehicles((known) => mergeOptions(known, next));
                  set({ vehicleIds: next.map((v) => v.id), vehicleCombinationId: next.length > 0 ? null : request.vehicleCombinationId });
                }}
                onAdd={(vehicle) => {
                  setVehicles((known) => mergeOptions(known, [vehicle]));
                  setLatest((current) => ({ vehicleIds: withId(current.vehicleIds, vehicle.id).slice(0, MAX_TRIP_VEHICLES), vehicleCombinationId: null }));
                }}
                max={MAX_TRIP_VEHICLES}
                disabled={readOnly || request.vehicleCombinationId != null}
              />
              <DriversField
                id={form.id("trip-drivers")}
                label={t("Orders:Drivers")}
                value={optionsOfIds(request.driverIds, drivers, (id) => ({ id, name: id, code: "" }))}
                onChange={(next) => {
                  setDrivers((known) => mergeOptions(known, next));
                  set({ driverIds: next.map((d) => d.id) });
                }}
                onAdd={(driver) => {
                  setDrivers((known) => mergeOptions(known, [driver]));
                  setLatest((current) => ({ driverIds: withId(current.driverIds, driver.id).slice(0, MAX_TRIP_DRIVERS) }));
                }}
                max={MAX_TRIP_DRIVERS}
                disabled={readOnly}
              />
              <NumberField
                form={requestForm}
                field="agreedPurchaseAmount"
                labelKey="Orders:AgreedPurchaseAmount"
                disabled={priceBlocked}
                hint={outsourced ? undefined : t("Orders:AgreedPurchaseNeedsCarrier")}
              />
              <CurrencyField id={form.id("trip-agreedPurchaseCurrencyCode")} label={t("Orders:CurrencyCode")} value={request.agreedPurchaseCurrencyCode} onChange={(next) => set({ agreedPurchaseCurrencyCode: next ?? "" })} disabled={readOnly || priceBlocked} />
            </>
          )}
        </div>
      )}
      {inPlace.dialog}
    </FormSection>
  );
}
