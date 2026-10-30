import { useCallback, useMemo } from "react";

import { Badge } from "@/components/ui/badge";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, EnumSelectField, TextAreaField, TextField, parseInteger } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import {
  NUMBERING_FIELDS,
  NumberingFields,
  numberingDtoOf,
  numberingValuesOf,
  validateNumbering,
  type NumberingFormValues,
} from "@/components/record-editor/numbering-fields";
import { AddressField, ContactPersonField, RelationField } from "@/domains/master-data/components/RelationFields";
import { DriversField, VehicleCombinationField, VehiclesField } from "@/domains/master-data/components/ResourceFields";
import type { DriverOption, RelationOption, VehicleOption } from "@/domains/master-data/entities";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import {
  TRIP_EXECUTIONS,
  transportTripTypes,
  type TransportTripTypeDto,
  type TripExecution,
} from "@/domains/planning-execution/reference-data";
import { MAX_TRIP_DRIVERS, MAX_TRIP_VEHICLES, mergeOptions, tripTypePresetOf } from "@/domains/planning-execution/trip-type-presets";
import { FIRST_LAST_STOP_RULES, STOP_COMPOSITION_MODES, type FirstLastStopRule, type StopCompositionMode } from "@/domains/planning-execution/trips";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values extends NumberingFormValues {
  code: string;
  name: string;
  defaultExecution: TripExecution;
  sendToFleetManagementSystem: boolean;
  isArchived: boolean;
  description: string;
  /* Defaults for new trips (design 61 "Trip type defaults" 1/3): the records with their names, so the pickers show them. */
  defaultCarrierRelation: RelationOption | null;
  defaultCarrierContactPersonId: string | null;
  defaultVehicleCombinationId: string | null;
  defaultVehicles: VehicleOption[];
  defaultDrivers: DriverOption[];
  defaultDepartureRelation: RelationOption | null;
  defaultDepartureAddressId: string | null;
  /** `HH:mm`, as typed (UI-FMT-004). */
  defaultDepartureTime: string;
  defaultArrivalRelation: RelationOption | null;
  defaultArrivalAddressId: string | null;
  defaultStopCompositionMode: StopCompositionMode;
  defaultFirstLastStopRule: FirstLastStopRule;
}

/** Relations with the Carrier role (design 43 flags). */
const CARRIER_ROLE = 2;

const DEFAULTS_FIELDS = [
  "defaultCarrierRelation",
  "defaultCarrierContactPersonId",
  "defaultVehicleCombinationId",
  "defaultVehicles",
  "defaultDrivers",
  "defaultDepartureRelation",
  "defaultDepartureAddressId",
  "defaultDepartureTime",
  "defaultArrivalRelation",
  "defaultArrivalAddressId",
  "defaultStopCompositionMode",
  "defaultFirstLastStopRule",
] as const;

const sameRelation = (a: RelationOption | null, b: RelationOption | null) => (a?.id?.toLowerCase() ?? null) === (b?.id?.toLowerCase() ?? null);

/* A new type starts as the seeded one does (design 53): TR, year, "-", five digits. */
const NEW_NUMBERING = { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 };

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: TransportTripTypeDto | undefined): Values => {
  const preset = tripTypePresetOf(dto);
  return {
    code: dto?.code ?? "",
    name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
    defaultExecution: dto?.defaultExecution ?? 1,
    sendToFleetManagementSystem: dto?.sendToFleetManagementSystem ?? true,
    isArchived: dto?.isArchived ?? false,
    description: dto?.description ?? "",
    ...numberingValuesOf(dto?.numbering, dto?.nextNumber, NEW_NUMBERING),
    defaultCarrierRelation: preset.carrierRelation,
    defaultCarrierContactPersonId: preset.carrierContactPersonId,
    defaultVehicleCombinationId: preset.vehicleCombinationId,
    defaultVehicles: preset.vehicles,
    defaultDrivers: preset.drivers,
    defaultDepartureRelation: preset.departureRelation,
    defaultDepartureAddressId: preset.departureAddressId,
    defaultDepartureTime: preset.departureTime,
    defaultArrivalRelation: preset.arrivalRelation,
    defaultArrivalAddressId: preset.arrivalAddressId,
    /* A new type starts as the server's defaults (MergeByLocationAndActivity, None). */
    defaultStopCompositionMode: preset.stopCompositionMode ?? 1,
    defaultFirstLastStopRule: preset.firstLastStopRule ?? 0,
  };
};

export function TransportTripTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = transportTripTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = transportTripTypes.useCreate();
  const update = transportTripTypes.useUpdate();
  const remove = transportTripTypes.useDelete();
  const canDelete = useIsGranted("PlanningExecution.TransportTripTypes.Delete");
  const minimumNextNumber = Number(detail.data?.nextNumber ?? 1);

  const validate = useCallback(
    (v: Values): FieldErrors<Values> => {
      const errors: FieldErrors<Values> = { ...validateNumbering(v, minimumNextNumber) };
      if (!v.code.trim()) errors.code = "MasterData:Required";
      if (!v.name.trim()) errors.name = "MasterData:Required";
      if (v.isArchived && detail.data?.isDefault) errors.isArchived = "MasterData:ArchivedCannotBeDefault";
      if (v.defaultDepartureTime.trim() && !parseTimeOfDay(v.defaultDepartureTime)) errors.defaultDepartureTime = "Orders:InvalidTime";
      return errors;
    },
    [minimumNextNumber, detail.data?.isDefault],
  );

  const save = useCallback(
    async (v: Values, dto: TransportTripTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        defaultExecution: v.defaultExecution,
        sendToFleetManagementSystem: v.sendToFleetManagementSystem,
        numbering: numberingDtoOf(v),
        nextNumber: parseInteger(v.nextNumber),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
        /* A contact needs its carrier, an address its relation, and a combination excludes vehicles (the pickers keep it
         * so; the server refuses otherwise: ContactWithoutCarrier, AddressWithoutRelation, VehicleSourceConflict). */
        defaultCarrierRelationId: v.defaultCarrierRelation?.id ?? null,
        defaultCarrierContactPersonId: v.defaultCarrierRelation ? v.defaultCarrierContactPersonId : null,
        defaultVehicleCombinationId: v.defaultVehicleCombinationId,
        defaultVehicleIds: v.defaultVehicleCombinationId ? [] : v.defaultVehicles.map((vehicle) => vehicle.id),
        defaultDriverIds: v.defaultDrivers.map((driver) => driver.id),
        defaultDepartureRelationId: v.defaultDepartureRelation?.id ?? null,
        defaultDepartureAddressId: v.defaultDepartureRelation ? v.defaultDepartureAddressId : null,
        defaultDepartureTime: v.defaultDepartureTime.trim() ? parseTimeOfDay(v.defaultDepartureTime) : null,
        defaultArrivalRelationId: v.defaultArrivalRelation?.id ?? null,
        defaultArrivalAddressId: v.defaultArrivalRelation ? v.defaultArrivalAddressId : null,
        defaultStopCompositionMode: v.defaultStopCompositionMode,
        defaultFirstLastStopRule: v.defaultFirstLastStopRule,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const executionItems = TRIP_EXECUTIONS.map((e) => ({ value: e.value, label: t(e.key) }));
  const stopCompositionItems = STOP_COMPOSITION_MODES.map((m) => ({ value: m.value, label: t(m.key) }));
  const firstLastStopItems = FIRST_LAST_STOP_RULES.map((r) => ({ value: r.value, label: t(r.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "defaultExecution", "sendToFleetManagementSystem", "isArchived", "description"],
      render: (form) => (
        <FormSection title={t("PlanningExecution:TransportTripType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("PlanningExecution:DefaultExecution")} htmlFor={form.id("defaultExecution")} required floating="always">
              <Select
                items={executionItems}
                value={form.values.defaultExecution}
                onValueChange={(next) => form.set("defaultExecution", (next as TripExecution | null) ?? 1)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("defaultExecution")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {executionItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <CheckboxField form={form} field="sendToFleetManagementSystem" labelKey="PlanningExecution:SendToFleetManagementSystem" />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    {
      /* Design 61 "Trip type defaults" 3: what a new trip of this type gets where it leaves a field empty. The pickers
       * are the trip page's (UI-LOOK-004/005 inside them); a combination and vehicles exclude each other, choosing one
       * clears the other; clearing (or replacing) a carrier clears its contact, a relation its address. */
      value: "defaults",
      labelKey: "PlanningExecution:DefaultsForNewTrips",
      fields: [...DEFAULTS_FIELDS],
      render: (form) => {
        const v = form.values;
        return (
          <div className="flex flex-col gap-4">
            <p className="text-label text-muted-foreground">{t("PlanningExecution:DefaultsForNewTripsHint")}</p>
            <FormSection title={t("PlanningExecution:Resources")}>
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <RelationField
                  id={form.id("defaultCarrierRelation")}
                  label={t("PlanningExecution:Carrier")}
                  value={v.defaultCarrierRelation}
                  onChange={(next) => form.update((c) => ({ ...c, defaultCarrierRelation: next, defaultCarrierContactPersonId: sameRelation(next, c.defaultCarrierRelation) ? c.defaultCarrierContactPersonId : null }))}
                  roles={CARRIER_ROLE}
                  disabled={form.readOnly}
                />
                <ContactPersonField
                  id={form.id("defaultCarrierContactPersonId")}
                  labelKey="PlanningExecution:ContactPerson"
                  relationId={v.defaultCarrierRelation?.id}
                  value={v.defaultCarrierContactPersonId}
                  onChange={(next) => form.set("defaultCarrierContactPersonId", next)}
                  hint={v.defaultCarrierRelation ? undefined : t("PlanningExecution:ContactPersonHint")}
                  disabled={form.readOnly}
                />
                <VehicleCombinationField
                  id={form.id("defaultVehicleCombinationId")}
                  label={t("PlanningExecution:VehicleCombination")}
                  value={v.defaultVehicleCombinationId}
                  onChange={(next) => form.update((c) => ({ ...c, defaultVehicleCombinationId: next, defaultVehicles: next ? [] : c.defaultVehicles }))}
                  stored={{ id: detail.data?.defaultVehicleCombinationId, name: detail.data?.defaultVehicleCombinationName }}
                  disabled={form.readOnly}
                />
                <VehiclesField
                  id={form.id("defaultVehicles")}
                  label={t("PlanningExecution:Vehicles")}
                  value={v.defaultVehicles}
                  onChange={(next) => form.update((c) => ({ ...c, defaultVehicles: next, defaultVehicleCombinationId: next.length > 0 ? null : c.defaultVehicleCombinationId }))}
                  onAdd={(vehicle) => form.update((c) => ({ ...c, defaultVehicles: mergeOptions(c.defaultVehicles, [vehicle]).slice(0, MAX_TRIP_VEHICLES), defaultVehicleCombinationId: null }))}
                  max={MAX_TRIP_VEHICLES}
                  hint={t("PlanningExecution:VehiclesOrCombinationHint").replace("{0}", String(MAX_TRIP_VEHICLES))}
                  disabled={form.readOnly}
                />
                <DriversField
                  id={form.id("defaultDrivers")}
                  label={t("PlanningExecution:Drivers")}
                  value={v.defaultDrivers}
                  onChange={(next) => form.set("defaultDrivers", next)}
                  onAdd={(driver) => form.update((c) => ({ ...c, defaultDrivers: mergeOptions(c.defaultDrivers, [driver]).slice(0, MAX_TRIP_DRIVERS) }))}
                  max={MAX_TRIP_DRIVERS}
                  hint={t("PlanningExecution:DriversHint").replace("{0}", String(MAX_TRIP_DRIVERS))}
                  disabled={form.readOnly}
                />
              </div>
            </FormSection>
            <FormSection title={t("PlanningExecution:Departure")}>
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <RelationField
                  id={form.id("defaultDepartureRelation")}
                  label={t("PlanningExecution:DepartureRelation")}
                  value={v.defaultDepartureRelation}
                  onChange={(next) => form.update((c) => ({ ...c, defaultDepartureRelation: next, defaultDepartureAddressId: sameRelation(next, c.defaultDepartureRelation) ? c.defaultDepartureAddressId : null }))}
                  disabled={form.readOnly}
                />
                {/* No default-address preselect: a stored type with a relation and no address must open clean (UI-FORM-009). */}
                <AddressField
                  id={form.id("defaultDepartureAddressId")}
                  label={t("PlanningExecution:DepartureAddress")}
                  relationId={v.defaultDepartureRelation?.id}
                  value={v.defaultDepartureAddressId}
                  onChange={(next) => form.set("defaultDepartureAddressId", next)}
                  preselectDefault={false}
                  hint={v.defaultDepartureRelation ? undefined : t("PlanningExecution:AddressHint")}
                  disabled={form.readOnly}
                />
                <TextField form={form} field="defaultDepartureTime" labelKey="PlanningExecution:DepartureTime" maxLength={5} hintKey="PlanningExecution:DepartureTimeHint" />
              </div>
            </FormSection>
            <FormSection title={t("PlanningExecution:Arrival")}>
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <RelationField
                  id={form.id("defaultArrivalRelation")}
                  label={t("PlanningExecution:ArrivalRelation")}
                  value={v.defaultArrivalRelation}
                  onChange={(next) => form.update((c) => ({ ...c, defaultArrivalRelation: next, defaultArrivalAddressId: sameRelation(next, c.defaultArrivalRelation) ? c.defaultArrivalAddressId : null }))}
                  disabled={form.readOnly}
                />
                <AddressField
                  id={form.id("defaultArrivalAddressId")}
                  label={t("PlanningExecution:ArrivalAddress")}
                  relationId={v.defaultArrivalRelation?.id}
                  value={v.defaultArrivalAddressId}
                  onChange={(next) => form.set("defaultArrivalAddressId", next)}
                  preselectDefault={false}
                  hint={v.defaultArrivalRelation ? undefined : t("PlanningExecution:AddressHint")}
                  disabled={form.readOnly}
                />
              </div>
            </FormSection>
            <FormSection title={t("PlanningExecution:Stops")}>
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <EnumSelectField form={form} field="defaultStopCompositionMode" labelKey="PlanningExecution:StopCompositionMode" items={stopCompositionItems} required />
                <EnumSelectField form={form} field="defaultFirstLastStopRule" labelKey="PlanningExecution:FirstLastStopRule" items={firstLastStopItems} required />
              </div>
            </FormSection>
          </div>
        );
      },
    },
    {
      value: "numbering",
      labelKey: "MasterData:Numbering",
      fields: [...NUMBERING_FIELDS],
      render: (form) => <NumberingFields form={form} />,
    },
  ];

  return (
    <MasterDataEditor<Values, TransportTripTypeDto>
      entityKey="TransportTripType"
      resource="PlanningExecution"
      queryEntity={transportTripTypes.entity}
      size="md"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => resolveLocalizedName(dto.names, culture) || dto.code || ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      badges={detail.data?.isDefault ? <Badge variant="secondary">{t("MasterData:IsDefault")}</Badge> : undefined}
      deleteRecord={mode === "edit" && canDelete && !detail.data?.isDefault ? remove : undefined}
    />
  );
}
