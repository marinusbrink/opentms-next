import { useMemo, useState } from "react";
import { ExternalLink, Pencil } from "lucide-react";
import { toast } from "sonner";
import { useNavigate } from "@tanstack/react-router";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { TreeSelect, buildTree } from "@/components/ui/tree-select";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { DateField, NumberField, SelectField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { AddressField, ContactPersonField, RelationField, relationOptionOf } from "@/apps/transport/components/RelationFields";
import type { TripValues } from "@/apps/transport/trips/trip-form";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { driverLookupPage, vehicleCombinations, vehicleLookupPage, type DriverOption, type RelationOption, type VehicleDto, type VehicleOption } from "@/domains/master-data/entities";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { TRIP_EXECUTIONS } from "@/domains/planning-execution/reference-data";
import { FIRST_LAST_STOP_RULES, STOP_COMPOSITION_MODES, useSetTripCarrier, useSetTripDrivers, useSetTripVehicles, useTripAttributeDefinitions, type TransportTripDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The General tab of the trip editor (design 61): number, date, name, type, status, execution; carrier and contact,
 * vehicles or combination and drivers (read-only with a Planboard link unless the trip was created from an order and
 * is still Planned, then editable through their own commands in a small action dialog); departure/arrival, planned
 * and actual times, distances, remarks, driver instructions, attributes. */

const CARRIER_ROLE = 2;
const PLANNED = 1;

export interface TripGeneralTabProps {
  form: FormContext<TripValues>;
  trip: TransportTripDto;
  readOnly: boolean;
}

export function TripGeneralTab({ form, trip, readOnly }: TripGeneralTabProps) {
  const { t } = useL();
  const f = useFormat();
  const navigate = useNavigate();
  const units = useAllOrganizationUnits();
  const unitTree = useMemo(() => buildTree(units.data ?? [], (u) => u.id ?? "", (u) => u.parentId, (u) => u.displayName ?? ""), [units.data]);
  const definitions = useTripAttributeDefinitions();
  const [resourceDialog, setResourceDialog] = useState<"carrier" | "vehicles" | "drivers" | null>(null);
  /* Resources are the Planboard's (design 61); the Tevens-trip path edits them here while the trip is Planned. */
  const resourcesEditable = !readOnly && trip.createdFromOrderId != null && trip.status === PLANNED && !trip.isArchived;
  const inPlace = useCreateInPlace();

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-4">
        <FormSection title={t("PlanningExecution:TransportTrip")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("PlanningExecution:Number")} htmlFor={form.id("number")}>
              <Input id={form.id("number")} className="" value={trip.number ?? ""} readOnly disabled />
            </FormField>
            <DateField form={form} field="tripDate" labelKey="PlanningExecution:TripDate" required />
            <TextField form={form} field="name" labelKey="Orders:Name" maxLength={128} />
            <FormField label={t("PlanningExecution:TransportTripType")} htmlFor={form.id("type")}>
              <Input id={form.id("type")} className="" value={trip.transportTripTypeName ?? ""} readOnly disabled />
            </FormField>
            <SelectField form={form} field="execution" labelKey="PlanningExecution:Execution" items={TRIP_EXECUTIONS.map((e) => ({ value: e.value, label: t(e.key) }))} />
            <FormField label={t("PlanningExecution:OrganizationUnit")} htmlFor={form.id("organizationUnitId")} floating="always">
              <TreeSelect
                id={form.id("organizationUnitId")}
                nodes={unitTree}
                value={form.values.organizationUnitId}
                onChange={(next) => form.set("organizationUnitId", next)}
                disabled={form.readOnly}
                {...inPlace.offer("OrganizationUnit", form.readOnly, (r) => form.set("organizationUnitId", r.id))}
              />
            </FormField>
            <SelectField form={form} field="stopCompositionMode" labelKey="PlanningExecution:StopCompositionMode" items={STOP_COMPOSITION_MODES.map((m) => ({ value: m.value, label: t(m.key) }))} />
            <SelectField form={form} field="firstLastStopRule" labelKey="PlanningExecution:FirstLastStopRule" items={FIRST_LAST_STOP_RULES.map((r) => ({ value: r.value, label: t(r.key) }))} />
          </div>
        </FormSection>
        <FormSection title={t("PlanningExecution:Resources")}>
          <dl className="grid grid-cols-[auto_1fr_auto] items-center gap-x-4 gap-y-2 text-sm">
            <dt className="text-label text-muted-foreground">{t("PlanningExecution:Carrier")}</dt>
            <dd>{trip.carrierName ?? t("PlanningExecution:OwnCompany")}</dd>
            <dd>{resourcesEditable && <EditButton label={t("PlanningExecution:Carrier")} onClick={() => setResourceDialog("carrier")} />}</dd>
            <dt className="text-label text-muted-foreground">{t("PlanningExecution:Vehicles")}</dt>
            <dd>{trip.vehicleCombinationName ?? (trip.vehicles ?? []).map((v) => [v.name, v.licencePlate].filter(Boolean).join(" ")).join(", ")}</dd>
            <dd>{resourcesEditable && <EditButton label={t("PlanningExecution:Vehicles")} onClick={() => setResourceDialog("vehicles")} />}</dd>
            <dt className="text-label text-muted-foreground">{t("PlanningExecution:Drivers")}</dt>
            <dd>{(trip.drivers ?? []).map((d) => d.name).filter(Boolean).join(", ")}</dd>
            <dd>{resourcesEditable && <EditButton label={t("PlanningExecution:Drivers")} onClick={() => setResourceDialog("drivers")} />}</dd>
          </dl>
          {!resourcesEditable && (
            <Button type="button" variant="link" size="sm" className="mt-2 px-0" onClick={() => void navigate({ to: "/planboard" })}>
              <ExternalLink /> {t("PlanningExecution:OpenPlanboard")}
            </Button>
          )}
        </FormSection>
        <FormSection title={t("PlanningExecution:Departure")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <RelationField id={form.id("departureRelation")} label={t("PlanningExecution:Departure")} value={form.values.departureRelation} onChange={(next) => form.set("departureRelation", next)} disabled={form.readOnly} />
            <AddressField id={form.id("departureAddress")} label={t("Orders:Address")} relationId={form.values.departureRelation?.id} value={form.values.departureAddressId} onChange={(next) => form.set("departureAddressId", next)} disabled={form.readOnly} />
            <DateField form={form} field="plannedDepartureDate" labelKey="PlanningExecution:PlannedDeparture" required />
            <TextField form={form} field="plannedDepartureTime" labelKey="Orders:DepartureTime" />
            <FormField label={t("PlanningExecution:ActualDeparture")} htmlFor={form.id("actualDeparture")}>
              <Input id={form.id("actualDeparture")} className="tabular-nums" value={f.dateTime(trip.actualDeparture)} readOnly disabled />
            </FormField>
          </div>
        </FormSection>
        <FormSection title={t("PlanningExecution:Arrival")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <RelationField id={form.id("arrivalRelation")} label={t("PlanningExecution:Arrival")} value={form.values.arrivalRelation} onChange={(next) => form.set("arrivalRelation", next)} disabled={form.readOnly} />
            <AddressField id={form.id("arrivalAddress")} label={t("Orders:Address")} relationId={form.values.arrivalRelation?.id} value={form.values.arrivalAddressId} onChange={(next) => form.set("arrivalAddressId", next)} disabled={form.readOnly} />
            <DateField form={form} field="plannedArrivalDate" labelKey="PlanningExecution:PlannedArrival" />
            <TextField form={form} field="plannedArrivalTime" labelKey="Orders:ArrivalTime" />
            <FormField label={t("PlanningExecution:ActualArrival")} htmlFor={form.id("actualArrival")}>
              <Input id={form.id("actualArrival")} className="tabular-nums" value={f.dateTime(trip.actualArrival)} readOnly disabled />
            </FormField>
          </div>
        </FormSection>
        <FormSection title={t("PlanningExecution:Distances")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={`${t("PlanningExecution:PlannedDistance")} (km)`} htmlFor={form.id("plannedDistance")}>
              <Input id={form.id("plannedDistance")} className="tabular-nums" value={trip.plannedDistanceM == null ? "" : f.number(trip.plannedDistanceM / 1000, { maxDecimals: 1 })} readOnly disabled />
            </FormField>
            <NumberField form={form} field="actualDistanceKm" labelKey="PlanningExecution:ActualDistanceKm" />
            <FormField label={t("PlanningExecution:PlannedDuration")} htmlFor={form.id("plannedDuration")}>
              <Input id={form.id("plannedDuration")} className="tabular-nums" value={trip.plannedDurationS == null ? "" : f.duration(Math.round(trip.plannedDurationS / 60))} readOnly disabled />
            </FormField>
            <FormField label={t("PlanningExecution:LoadFactor")} htmlFor={form.id("loadFactor")}>
              <Input id={form.id("loadFactor")} className="tabular-nums" value={trip.loadFactor == null ? "" : f.percent(trip.loadFactor * 100)} readOnly disabled />
            </FormField>
          </div>
        </FormSection>
        <FormSection title={t("PlanningExecution:Remarks")}>
          <div className="grid grid-cols-1 gap-3">
            <TextAreaField form={form} field="remarks" labelKey="PlanningExecution:Remarks" maxLength={2500} />
            <TextAreaField form={form} field="driverInstructions" labelKey="PlanningExecution:DriverInstructions" maxLength={2500} />
          </div>
        </FormSection>
      </div>
      <AttributeFields idPrefix={form.id("trip")} title={t("PlanningExecution:Attributes")} definitions={definitions.data} value={form.values.attributes} onChange={(next) => form.set("attributes", next)} readOnly={form.readOnly} />
      {resourceDialog && <ResourceDialog kind={resourceDialog} trip={trip} onClose={() => setResourceDialog(null)} />}
      {inPlace.dialog}
    </div>
  );
}

function EditButton({ label, onClick }: { label: string; onClick: () => void }) {
  const { t } = useL();
  return (
    <Button type="button" variant="ghost" size="icon-sm" aria-label={`${t("Edit")} ${label}`} title={t("Edit")} onClick={onClick}>
      <Pencil />
    </Button>
  );
}

/** Set carrier / vehicles / drivers: one action dialog over the trip's own commands (design 59). */
function ResourceDialog({ kind, trip, onClose }: { kind: "carrier" | "vehicles" | "drivers"; trip: TransportTripDto; onClose: () => void }) {
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
  const combinations = vehicleCombinations.useLookup(kind === "vehicles");
  const combinationOptions = useMemo(() => toOptions(combinations.data), [combinations.data]);
  const vehiclePage = useMemo(() => vehicleLookupPage(), []);
  const busy = setCarrier.isPending || setVehicles.isPending || setDrivers.isPending;
  /* "Add new …" in every picker (UI-LOOK-004): the record its editor saved is picked (at most three vehicles / drivers). */
  const inPlace = useCreateInPlace();
  const title = kind === "carrier" ? t("PlanningExecution:Carrier") : kind === "vehicles" ? t("PlanningExecution:Vehicles") : t("PlanningExecution:Drivers");

  const save = async () => {
    setError(null);
    try {
      if (kind === "carrier") await setCarrier.mutateAsync({ id: trip.id!, carrierRelationId: carrier?.id ?? null, carrierContactPersonId: contactId });
      else if (kind === "vehicles") await setVehicles.mutateAsync({ id: trip.id!, vehicleCombinationId: combinationId, vehicleIds: combinationId ? [] : vehicles.slice(0, 3).map((v) => v.id) });
      else await setDrivers.mutateAsync({ id: trip.id!, driverIds: drivers.slice(0, 3).map((d) => d.id) });
      toast.success(t("PlanningExecution:TransportTripSaved"));
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
      title={title}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="button" onClick={() => void save()} disabled={busy}>
            {busy ? t("Saving") : t("Save")}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {kind === "carrier" && (
          <>
            <RelationField id="trip-carrier" label={t("PlanningExecution:Carrier")} value={carrier} onChange={(next) => { setCarrierValue(next); setContactId(null); }} roles={CARRIER_ROLE} />
            <ContactPersonField id="trip-carrier-contact" labelKey="PlanningExecution:CarrierContactPerson" relationId={carrier?.id} value={contactId} onChange={setContactId} />
          </>
        )}
        {kind === "vehicles" && (
          <>
            <FormField label={t("PlanningExecution:VehicleCombination")} htmlFor="trip-combination" floating="always">
              <ReferenceCombobox
                id="trip-combination"
                value={combinationId}
                onChange={(next) => {
                  setCombinationId(next);
                  if (next) setVehiclesValue([]);
                }}
                options={combinationOptions}
                {...inPlace.offer("VehicleCombination", busy, (r) => {
                  setCombinationId(r.id);
                  setVehiclesValue([]);
                })}
              />
            </FormField>
            <FormField label={t("PlanningExecution:Vehicles")} htmlFor="trip-vehicles" floating="always">
              <EntityLookup<VehicleOption>
                id="trip-vehicles"
                multiple
                value={vehicles}
                onChange={(next) => {
                  setVehiclesValue(next.slice(0, 3));
                  if (next.length > 0) setCombinationId(null);
                }}
                fetchPage={vehiclePage}
                getLabel={(v) => v.name}
                disabled={combinationId != null}
                {...inPlace.offer<VehicleDto>("Vehicle", busy || combinationId != null || vehicles.length >= 3, (r) => {
                  setVehiclesValue((current) => (current.some((v) => v.id === r.id) ? current : [...current, { id: r.id, name: r.name, licencePlate: r.dto.licencePlate ?? null, kind: r.dto.kind ?? 1, vehicleTypeName: r.dto.vehicleTypeName ?? null }].slice(0, 3)));
                  setCombinationId(null);
                })}
              />
            </FormField>
          </>
        )}
        {kind === "drivers" && (
          <FormField label={t("PlanningExecution:Drivers")} htmlFor="trip-drivers" floating="always">
            <EntityLookup<DriverOption>
              id="trip-drivers"
              multiple
              value={drivers}
              onChange={(next) => setDriversValue(next.slice(0, 3))}
              fetchPage={driverLookupPage}
              getLabel={(d) => d.name}
              {...inPlace.offer("Driver", busy || drivers.length >= 3, (r) =>
                setDriversValue((current) => (current.some((d) => d.id === r.id) ? current : [...current, { id: r.id, name: r.name, code: r.code ?? "" }].slice(0, 3))),
              )}
            />
          </FormField>
        )}
      </div>
      {inPlace.dialog}
    </EntityDialog>
  );
}
