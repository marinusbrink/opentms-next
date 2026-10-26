import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ExternalLink, Pencil } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { StatusBadge } from "@/components/ui/status-badge";
import { ComboField, NumberField, TextField, toOptions } from "@/apps/transport/components/fields";
import { CurrencyField } from "@/apps/transport/components/CurrencyField";
import { LegDialog } from "@/apps/transport/components/LegDialog";
import { AddressField, ContactPersonField, RelationField } from "@/apps/transport/components/RelationFields";
import type { EditorMode, OrderValues } from "@/apps/transport/orders/order-form";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import { driverLookupPage, vehicleCombinations, vehicleLookupPage, type DriverOption, type VehicleDto, type VehicleOption } from "@/domains/master-data/entities";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { fetchLegs, legEndLabel, type LegDto } from "@/domains/planning-execution/legs";
import { transportTripTypes } from "@/domains/planning-execution/reference-data";
import { tripStatusName } from "@/domains/planning-execution/trips";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The Planning tab of the order editor (design 61): the legs of the order read from Planning (position, origin →
 * destination, planned dates, distance, duration, toll km, CO₂, trip number (a link), allocated cost/revenue) with the
 * editable leg fields (references, planned dates, time windows, remarks) in the shared LegDialog, and the Tevens trip section (Q9): a checkbox "Create a trip with this
 * order", trip type, departure/arrival (relation + address), planned departure, an Advanced disclosure for carrier,
 * vehicles or combination, drivers and the agreed purchase price; after Confirm the section is read-only and shows
 * the created trip as a link. Legs are never posted by the client (design 58). */

/** Relations with the Carrier role (design 43 flags). */
const CARRIER_ROLE = 2;

export interface PlanningTabProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  order: TransportOrderDto | undefined;
}

export function PlanningTab({ form, mode, order }: PlanningTabProps) {
  const { t } = useL();
  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {mode === "order" && order?.id && <LegsSection orderId={order.id} />}
      <TripRequestSection form={form} mode={mode} order={order} />
      {mode === "order" && !order?.id && <p className="text-label text-muted-foreground">{t("Orders:LegsAfterConfirm")}</p>}
    </div>
  );
}

function LegsSection({ orderId }: { orderId: string }) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const [editing, setEditing] = useState<LegDto | null>(null);
  const fetchRows = useMemo(() => (request: Parameters<typeof fetchLegs>[0]) => fetchLegs(request, { orderId }), [orderId]);

  const columns = useMemo<ColDef<LegDto>[]>(
    () => [
      { field: "position", headerName: t("PlanningExecution:Position"), width: 80, minWidth: 70, cellRenderer: NumberCell, type: "rightAligned" },
      { colId: "origin", headerName: t("PlanningExecution:Origin"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.origin), sortable: false },
      { colId: "destination", headerName: t("PlanningExecution:Destination"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.destination), sortable: false },
      { colId: "originDate", headerName: t("PlanningExecution:PlannedDate"), minWidth: 110, valueGetter: (p) => p.data?.origin?.plannedDate, cellRenderer: DateCell, sortable: false },
      { colId: "destinationDate", headerName: `${t("PlanningExecution:PlannedDate")} (${t("PlanningExecution:Destination")})`, minWidth: 110, valueGetter: (p) => p.data?.destination?.plannedDate, cellRenderer: DateCell, sortable: false },
      { colId: "distance", headerName: `${t("PlanningExecution:Distance")} (km)`, minWidth: 100, type: "rightAligned", valueGetter: (p) => (p.data?.distanceM == null ? null : p.data.distanceM / 1000), valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 1 }), cellClass: "tabular-nums", sortable: false },
      { field: "durationS", headerName: t("Orders:DurationS"), minWidth: 100, type: "rightAligned", valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => (p.value == null ? "" : f.duration(Math.round(p.value / 60))), cellClass: "tabular-nums", sortable: false },
      { colId: "toll", headerName: `${t("PlanningExecution:TollDistance")} (km)`, minWidth: 100, type: "rightAligned", valueGetter: (p) => (p.data?.tollDistanceM == null ? null : p.data.tollDistanceM / 1000), valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 1 }), cellClass: "tabular-nums", sortable: false },
      { field: "estimatedCo2Kg", headerName: t("PlanningExecution:Co2Kg"), minWidth: 100, type: "rightAligned", valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 3 }), cellClass: "tabular-nums", sortable: false },
      { field: "tripNumber", headerName: t("PlanningExecution:Trip"), minWidth: 120, cellRenderer: (p: { value: string | null; data: LegDto | undefined }) => (p.value ? <span className="inline-flex items-center gap-1 text-brand">{p.value}<StatusBadge entity="TransportTrip" state={tripStatusName(p.data?.tripStatus)} /></span> : null), sortable: false },
      { field: "allocatedCostAmount", headerName: t("PlanningExecution:AllocatedCost"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { field: "allocatedRevenueAmount", headerName: t("PlanningExecution:AllocatedRevenue"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { colId: "originReference", headerName: `${t("PlanningExecution:Reference")} (${t("PlanningExecution:Origin")})`, minWidth: 120, valueGetter: (p) => p.data?.origin?.reference, hide: true, sortable: false },
      { colId: "destinationReference", headerName: `${t("PlanningExecution:Reference")} (${t("PlanningExecution:Destination")})`, minWidth: 120, valueGetter: (p) => p.data?.destination?.reference, hide: true, sortable: false },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 120, hide: true, sortable: false },
    ],
    [t, f],
  );

  return (
    <FormSection title={t("PlanningExecution:Legs")} className="flex min-h-64 flex-1 flex-col">
      <div className="min-h-0 flex-1" style={{ minHeight: "14rem" }}>
        <LazyOpenTmsGrid<LegDto>
          gridId="planning.transport.orderLegs"
          columnDefs={columns}
          fetchRows={fetchRows}
          onOpen={(row) => {
            if (row.tripId) openRecord("TransportTrip", row.tripId);
            else setEditing(row);
          }}
          rowActions={(rows) => [
            { id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditing(rows[0]) },
            ...(rows[0]?.tripId ? [{ id: "trip", labelKey: "PlanningExecution:OpenTrip", icon: ExternalLink, onClick: () => openRecord("TransportTrip", rows[0].tripId!) }] : []),
          ]}
        />
      </div>
      {editing && <LegDialog leg={editing} title={`${t("PlanningExecution:Leg")} ${editing.position ?? ""}`} idPrefix="leg" onClose={() => setEditing(null)} />}
    </FormSection>
  );
}

function TripRequestSection({ form, mode, order }: { form: FormContext<OrderValues>; mode: EditorMode; order: TransportOrderDto | undefined }) {
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
  const combinationLookup = vehicleCombinations.useLookup(advanced);
  const combinationOptions = useMemo(() => toOptions(combinationLookup.data), [combinationLookup.data]);
  const vehiclePage = useMemo(() => vehicleLookupPage(), []);

  const set = (change: Partial<OrderValues["tripRequest"]>) => {
    form.set("tripRequest", { ...request, ...change });
    form.blur("tripRequest");
  };
  /* "Add new …" in every picker (UI-LOOK-004): the record its editor saved lands in the latest request. */
  const inPlace = useCreateInPlace();
  const setLatest = (change: (current: OrderValues["tripRequest"]) => Partial<OrderValues["tripRequest"]>) =>
    form.update((values) => ({ ...values, tripRequest: { ...values.tripRequest, ...change(values.tripRequest) } }));
  const requestForm = {
    values: request,
    set: (field: string, value: unknown) => set({ [field]: value }),
    update: () => undefined,
/* The section shows its one message above the fields (UI-FB-004: exactly once), not under each field. */
    error: () => undefined,
    blur: () => form.blur("tripRequest"),
    readOnly,
    id: (field: string) => form.id(`trip-${field}`),
  } as unknown as FormContext<OrderValues["tripRequest"]>;

  return (
    <FormSection aria-label={t("Orders:TripRequest")} className="shrink-0">
      <div className="mb-3 flex items-center gap-4">
        <label className="flex items-center gap-2 text-label font-semibold text-brand" htmlFor={form.id("trip-enabled")}>
          <Checkbox id={form.id("trip-enabled")} checked={request.enabled} disabled={readOnly} onCheckedChange={(next) => set({ enabled: next === true })} />
          {t("Orders:TripRequestHint")}
        </label>
        {locked && order?.tripRequest?.tripId && (
          <Button type="button" variant="link" size="sm" className="ml-auto" onClick={() => openRecord("TransportTrip", order.tripRequest!.tripId!)}>
            <ExternalLink /> {t("Orders:Trip")} {order.tripRequest.tripNumber ?? ""}
          </Button>
        )}
      </div>
      {form.error("tripRequest") && <p className="mb-2 text-label text-destructive">{form.error("tripRequest")}</p>}
      {request.enabled && (
        <div className="grid grid-cols-4 gap-x-4 gap-y-3" data-slot="trip-request-fields">
          <ComboField
            form={requestForm}
            field="transportTripTypeId"
            labelKey="Orders:TransportTripType"
            required
            options={typeOptions}
            {...inPlace.offer("TransportTripType", readOnly, (r) => setLatest(() => ({ transportTripTypeId: r.id })))}
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
              <ComboField
                form={requestForm}
                field="vehicleCombinationId"
                labelKey="Orders:VehicleCombination"
                options={combinationOptions}
                disabled={request.vehicleIds.length > 0}
                {...inPlace.offer("VehicleCombination", readOnly || request.vehicleIds.length > 0, (r) => setLatest(() => ({ vehicleCombinationId: r.id, vehicleIds: [] })))}
              />
              <FormField label={t("Orders:Vehicles")} htmlFor={form.id("trip-vehicles")} floating="always">
                <EntityLookup<VehicleOption>
                  id={form.id("trip-vehicles")}
                  multiple
                  value={request.vehicleIds.map((id) => vehicles.find((v) => v.id === id) ?? { id, name: id, licencePlate: null, kind: 1, vehicleTypeName: null })}
                  onChange={(next) => {
                    setVehicles(next);
                    set({ vehicleIds: next.slice(0, 3).map((v) => v.id), vehicleCombinationId: next.length > 0 ? null : request.vehicleCombinationId });
                  }}
                  fetchPage={vehiclePage}
                  getLabel={(v) => v.name}
                  disabled={readOnly || request.vehicleCombinationId != null}
                  {...inPlace.offer<VehicleDto>("Vehicle", readOnly || request.vehicleCombinationId != null || request.vehicleIds.length >= 3, (r) => {
                    setVehicles((known) => [...known.filter((v) => v.id !== r.id), { id: r.id, name: r.name, licencePlate: r.dto.licencePlate ?? null, kind: r.dto.kind ?? 1, vehicleTypeName: r.dto.vehicleTypeName ?? null }]);
                    setLatest((current) => ({ vehicleIds: withId(current.vehicleIds, r.id).slice(0, 3), vehicleCombinationId: null }));
                  })}
                />
              </FormField>
              <FormField label={t("Orders:Drivers")} htmlFor={form.id("trip-drivers")} floating="always">
                <EntityLookup<DriverOption>
                  id={form.id("trip-drivers")}
                  multiple
                  value={request.driverIds.map((id) => drivers.find((d) => d.id === id) ?? { id, name: id, code: "" })}
                  onChange={(next) => {
                    setDrivers(next);
                    set({ driverIds: next.slice(0, 3).map((d) => d.id) });
                  }}
                  fetchPage={driverLookupPage}
                  getLabel={(d) => d.name}
                  disabled={readOnly}
                  {...inPlace.offer("Driver", readOnly || request.driverIds.length >= 3, (r) => {
                    setDrivers((known) => [...known.filter((d) => d.id !== r.id), { id: r.id, name: r.name, code: r.code ?? "" }]);
                    setLatest((current) => ({ driverIds: withId(current.driverIds, r.id).slice(0, 3) }));
                  })}
                />
              </FormField>
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
