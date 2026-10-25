import { useMemo, useState, type FormEvent } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ExternalLink, Pencil } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { Input } from "@/components/ui/input";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { StatusBadge } from "@/components/ui/status-badge";
import { Textarea } from "@/components/ui/textarea";
import { ComboField, NumberField, TextField, toOptions } from "@/apps/transport/components/fields";
import { AddressField, ContactPersonField, RelationField } from "@/apps/transport/components/RelationFields";
import type { EditorMode, OrderValues } from "@/apps/transport/orders/order-form";
import { driverLookupPage, vehicleCombinations, vehicleLookupPage, type DriverOption, type VehicleOption } from "@/domains/master-data/entities";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { fetchLegs, legEndLabel, useUpdateLeg, type LegDto } from "@/domains/planning-execution/legs";
import { transportTripTypes } from "@/domains/planning-execution/reference-data";
import { tripStatusName } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The Planning tab of the order editor (design 61): the legs of the order read from Planning (position, origin →
 * destination, planned dates, distance, duration, toll km, CO₂, trip number (a link), allocated cost/revenue) with the
 * editable leg fields in an action dialog, and the Tevens trip section (Q9): a checkbox "Create a trip with this
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
      {editing && <LegDialog leg={editing} onClose={() => setEditing(null)} />}
    </FormSection>
  );
}

/** The editable leg fields (design 59: references, remarks, planned dates within the stop's window). */
function LegDialog({ leg, onClose }: { leg: LegDto; onClose: () => void }) {
  const { t } = useL();
  const update = useUpdateLeg();
  const [originReference, setOriginReference] = useState(leg.origin?.reference ?? "");
  const [destinationReference, setDestinationReference] = useState(leg.destination?.reference ?? "");
  const [originDate, setOriginDate] = useState<string | null>(leg.origin?.plannedDate ?? null);
  const [destinationDate, setDestinationDate] = useState<string | null>(leg.destination?.plannedDate ?? null);
  const [remarks, setRemarks] = useState(leg.remarks ?? "");
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      await update.mutateAsync({
        id: leg.id!,
        dto: { concurrencyStamp: leg.concurrencyStamp ?? "", originReference: originReference.trim() || null, destinationReference: destinationReference.trim() || null, originPlannedDate: originDate, destinationPlannedDate: destinationDate, remarks: remarks.trim() || null },
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
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={`${t("PlanningExecution:Leg")} ${leg.position ?? ""}`}
      isBusy={update.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={update.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="leg-form" disabled={update.isPending}>
            {update.isPending ? t("Saving") : t("Save")}
          </Button>
        </>
      }
    >
      <form id="leg-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={`${t("PlanningExecution:Reference")} (${t("PlanningExecution:Origin")})`} htmlFor="leg-origin-reference">
          <Input id="leg-origin-reference" value={originReference} maxLength={128} onChange={(e) => setOriginReference(e.target.value)} />
        </FormField>
        <FormField label={`${t("PlanningExecution:PlannedDate")} (${t("PlanningExecution:Origin")})`} htmlFor="leg-origin-date" floating="always">
          <DatePicker id="leg-origin-date" value={originDate} onChange={setOriginDate} />
        </FormField>
        <FormField label={`${t("PlanningExecution:Reference")} (${t("PlanningExecution:Destination")})`} htmlFor="leg-destination-reference">
          <Input id="leg-destination-reference" value={destinationReference} maxLength={128} onChange={(e) => setDestinationReference(e.target.value)} />
        </FormField>
        <FormField label={`${t("PlanningExecution:PlannedDate")} (${t("PlanningExecution:Destination")})`} htmlFor="leg-destination-date" floating="always">
          <DatePicker id="leg-destination-date" value={destinationDate} onChange={setDestinationDate} />
        </FormField>
        <FormField label={t("PlanningExecution:Remarks")} htmlFor="leg-remarks" floating="always">
          <Textarea id="leg-remarks" rows={3} value={remarks} maxLength={2500} onChange={(e) => setRemarks(e.target.value)} />
        </FormField>
      </form>
    </EntityDialog>
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
  const requestForm = {
    values: request,
    set: (field: string, value: unknown) => set({ [field]: value }),
    update: () => undefined,
    error: () => form.error("tripRequest"),
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
          <ComboField form={requestForm} field="transportTripTypeId" labelKey="Orders:TransportTripType" required options={typeOptions} />
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
              <RelationField id={form.id("trip-carrier")} label={t("Orders:Carrier")} value={request.carrierRelation} onChange={(next) => set({ carrierRelation: next, carrierContactPersonId: null })} roles={CARRIER_ROLE} disabled={readOnly} />
              <ContactPersonField id={form.id("trip-carrierContact")} labelKey="PlanningExecution:CarrierContactPerson" relationId={request.carrierRelation?.id} value={request.carrierContactPersonId} onChange={(next) => set({ carrierContactPersonId: next })} disabled={readOnly} />
              <ComboField form={requestForm} field="vehicleCombinationId" labelKey="Orders:VehicleCombination" options={combinationOptions} disabled={request.vehicleIds.length > 0} />
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
                />
              </FormField>
              <NumberField form={requestForm} field="agreedPurchaseAmount" labelKey="Orders:AgreedPurchaseAmount" />
              <TextField form={requestForm} field="agreedPurchaseCurrencyCode" labelKey="Orders:CurrencyCode" maxLength={3} upper />
            </>
          )}
        </div>
      )}
    </FormSection>
  );
}
