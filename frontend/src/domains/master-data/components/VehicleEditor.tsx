import { useCallback, useMemo, useState } from "react";
import type { ColDef, ICellRendererParams } from "ag-grid-community";
import { useQueryClient } from "@tanstack/react-query";
import { Archive, ArchiveRestore, Plus } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { MultiSelect } from "@/components/ui/multi-select";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCanCreateInPlace, useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseNumber } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors, type FormContext } from "@/components/record-editor/MasterDataEditor";
import { RelationEditor } from "@/domains/master-data/components/RelationEditor";
import { TransportCharacteristicsEditor } from "@/domains/master-data/components/TransportCharacteristicsEditor";
import {
  EMPLOYER_ROLES,
  EURO_CLASSES,
  FUEL_TYPES,
  countries,
  driverLookupPage,
  masterDataVehicleTypeDefaultsKey,
  relationLookupPage,
  transportCharacteristicLookupPage,
  useArchiveVehicle,
  useDefaultCountry,
  useOwnCompany,
  useUnarchiveVehicle,
  useVehicleTypeDefaults,
  vehicleTypeKindKey,
  vehicleTypes,
  vehicles,
  fetchVehicleTypeDefaults,
  type DriverDto,
  type DriverOption,
  type EuroClass,
  type FuelType,
  type RelationDto,
  type RelationOption,
  type TransportCharacteristicOption,
  type TransportCharacteristicsDto,
  type VehicleCharacteristicDto,
  type VehicleDto,
  type VehicleTypeDefaultsDto,
} from "@/domains/master-data/entities";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { formatLicencePlate, isLicencePlateShape, useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The vehicle editor (design 48, UI design): `lg`, five tabs. General with the sections Vehicle (name, type,
 * archived; choosing a type on a new vehicle fills the empty dimension fields from `type-defaults`),
 * Registration, Ownership (owner and home depot as EntityLookups over the relations with the right roles, with
 * "Add new relation…"; organisation units) and Remarks; Dimensions and capacity with fuel type and Euro class;
 * Characteristics as one grid in which the type's rows are read-only with a "from type" badge and the vehicle's
 * own rows are removable; Drivers and identification with the default drivers and the external ids; then Record
 * identification. Kind and Archived are badges after the title. */

/** Relations a vehicle may be stationed at: Depot or OwnCompany (design 48). */
const HOME_DEPOT_ROLES = 8 | 16;
const ORIGIN_TYPE = 1;
const ORIGIN_VEHICLE = 2;
const MAX_DEFAULT_DRIVERS = 5;
const VIN_SHAPE = /^[A-HJ-NPR-Z0-9]{17}$/;

interface CharacteristicRow {
  id: string;
  code: string;
  name: string;
  origin: number;
}

interface DriverLine {
  key: string;
  driverId: string;
  name: string;
  sortOrder: number;
}

interface ExternalIdRow {
  key: string;
  id: string | null;
  source: string;
  identifier: string;
}

interface Values {
  name: string;
  vehicleTypeId: string | null;
  isArchived: boolean;
  registrationCountryId: string | null;
  licencePlate: string;
  vin: string;
  owner: RelationOption | null;
  homeDepot: RelationOption | null;
  organizationUnitIds: string[];
  description: string;
  lengthCm: string;
  widthCm: string;
  heightCm: string;
  emptyWeightKg: string;
  grossVehicleWeightKg: string;
  maxPayloadKg: string;
  maxEpq: string;
  maxLoadingMeters: string;
  /** QP10: the vehicle's own cost price per kilometre; empty = its type's (the server's cascade). */
  costPricePerKm: string;
  fuelType: FuelType | null;
  euroClass: EuroClass | null;
  /** The vehicle's own characteristics; the type's are shown beside them, not stored here. */
  ownCharacteristics: CharacteristicRow[];
  defaultDrivers: DriverLine[];
  externalIdentifiers: ExternalIdRow[];
}

const NUMBER_FIELDS = ["lengthCm", "widthCm", "heightCm", "emptyWeightKg", "grossVehicleWeightKg", "maxPayloadKg", "maxEpq", "maxLoadingMeters", "costPricePerKm"] as const;
type NumberFieldName = (typeof NUMBER_FIELDS)[number];

/** The dimension fields a type fills on a new vehicle (design 48): the type has no empty weight or payload. */
const TYPE_DEFAULT_FIELDS: { field: NumberFieldName; pick: (d: VehicleTypeDefaultsDto) => number | null | undefined }[] = [
  { field: "lengthCm", pick: (d) => d.lengthCm },
  { field: "widthCm", pick: (d) => d.widthCm },
  { field: "heightCm", pick: (d) => d.heightCm },
  { field: "grossVehicleWeightKg", pick: (d) => d.grossVehicleWeightKg },
  { field: "maxEpq", pick: (d) => d.maxEpq },
  { field: "maxLoadingMeters", pick: (d) => d.maxLoadingMeters },
];

let rowCounter = 0;

const toOption = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

const initialValuesFor =
  (defaults: { owner: RelationOption | null; countryId: string | null; name: string | undefined }) =>
  (dto: VehicleDto | undefined): Values => ({
    name: dto?.name ?? defaults.name ?? "",
    vehicleTypeId: dto?.vehicleTypeId ?? null,
    isArchived: dto?.isArchived ?? false,
    registrationCountryId: dto ? (dto.registrationCountryId ?? null) : defaults.countryId,
    licencePlate: formatLicencePlate(dto?.licencePlate, dto?.registrationCountryCode),
    vin: dto?.vin ?? "",
    owner: dto ? toOption(dto.ownerRelationId, dto.ownerName) : defaults.owner,
    homeDepot: toOption(dto?.homeDepotRelationId, dto?.homeDepotName),
    organizationUnitIds: dto?.organizationUnitIds ?? [],
    description: dto?.description ?? "",
    lengthCm: numberToText(dto?.lengthCm),
    widthCm: numberToText(dto?.widthCm),
    heightCm: numberToText(dto?.heightCm),
    emptyWeightKg: numberToText(dto?.emptyWeightKg),
    grossVehicleWeightKg: numberToText(dto?.grossVehicleWeightKg),
    maxPayloadKg: numberToText(dto?.maxPayloadKg),
    maxEpq: numberToText(dto?.maxEpq),
    maxLoadingMeters: numberToText(dto?.maxLoadingMeters),
    costPricePerKm: numberToText(dto?.costPricePerKm),
    fuelType: dto?.fuelType ?? null,
    euroClass: dto?.euroClass ?? null,
    ownCharacteristics: (dto?.characteristics ?? []).filter((c) => c.origin === ORIGIN_VEHICLE).map(toCharacteristicRow),
    defaultDrivers: (dto?.defaultDrivers ?? []).map((d) => ({
      key: d.driverId ?? `new-${++rowCounter}`,
      driverId: d.driverId ?? "",
      name: d.name ?? "",
      sortOrder: d.sortOrder ?? 0,
    })),
    externalIdentifiers: (dto?.externalIdentifiers ?? []).map((e) => ({
      key: e.id ?? `new-${++rowCounter}`,
      id: e.id ?? null,
      source: e.source,
      identifier: e.identifier,
    })),
  });

function toCharacteristicRow(c: VehicleCharacteristicDto): CharacteristicRow {
  return { id: c.id ?? "", code: c.code ?? "", name: c.name ?? "", origin: c.origin ?? ORIGIN_VEHICLE };
}

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:NameRequired";
  if (!v.vehicleTypeId) errors.vehicleTypeId = "MasterData:VehicleTypeRequired";
  if (!v.owner) errors.owner = "MasterData:OwnerRequired";
  if (!isLicencePlateShape(v.licencePlate)) errors.licencePlate = "MasterData:InvalidLicencePlate";
  else if (v.licencePlate.trim() && !v.registrationCountryId) errors.registrationCountryId = "MasterData:RegistrationCountryRequired";
  const vin = v.vin.trim().toUpperCase();
  if (vin && !VIN_SHAPE.test(vin)) errors.vin = "MasterData:InvalidVin";
  for (const field of NUMBER_FIELDS) {
    const error = numberError(v[field], { min: 0 });
    if (error) errors[field] = error;
  }
  if (v.defaultDrivers.length > MAX_DEFAULT_DRIVERS || new Set(v.defaultDrivers.map((d) => d.driverId)).size !== v.defaultDrivers.length) {
    errors.defaultDrivers = "MasterData:DefaultDriversMax";
  }
  if (v.externalIdentifiers.some((e) => !e.source.trim() || !e.identifier.trim())) {
    errors.externalIdentifiers = "MasterData:ExternalIdentifierIncomplete";
  }
  return errors;
};

const GENERAL_FIELDS: (keyof Values & string)[] = ["name", "vehicleTypeId", "isArchived", "registrationCountryId", "licencePlate", "vin", "owner", "homeDepot", "organizationUnitIds", "description"];

export function VehicleEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const detail = vehicles.useDetail(mode === "edit" ? id : undefined);
  const create = vehicles.useCreate();
  const update = vehicles.useUpdate();
  const remove = vehicles.useDelete();
  const archive = useArchiveVehicle();
  const unarchive = useUnarchiveVehicle();
  const canDelete = useIsGranted("MasterData.Vehicles.Delete");
  const canUpdate = useIsGranted("MasterData.Vehicles.Update");
  /* The one "Add new …" gate (permission + node feature chain + not read-only, UI-LOOK-004 / UI-PERM-006). */
  const canCreate = useCanCreateInPlace();
  const canUpdateRelation = useIsGranted("MasterData.Relations.Update");

  /* A new vehicle starts with the own company as owner and the tenant's default country (design 48); it waits for
   * both (cached after the first open) so its initial values hold them and an untouched record stays clean. */
  const ownCompany = useOwnCompany(open && mode === "create");
  const defaultCountry = useDefaultCountry(open && mode === "create");
  const ready = mode !== "create" || (ownCompany.isFetched && defaultCountry.isFetched);
  const ownerDefault = ownCompany.data ?? null;
  const countryDefault = defaultCountry.data?.id ?? null;
  const typedName = mode === "create" ? initialName : undefined;
  const initialValues = useMemo(
    () => initialValuesFor({ owner: ownerDefault, countryId: countryDefault, name: typedName }),
    [ownerDefault, countryDefault, typedName],
  );
  const inPlace = useCreateInPlace();

  const typeLookup = vehicleTypes.useLookup(open);
  const typeOptions = useMemo(() => (typeLookup.data ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })), [typeLookup.data]);
  const countryLookup = countries.useLookup(open);
  const countryOptions = useMemo(
    () => (countryLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })),
    [countryLookup.data],
  );
  const countryCodeOf = useCallback((countryId: string | null) => countryLookup.data?.find((c) => c.id === countryId)?.code ?? null, [countryLookup.data]);
  /* DEVIATION(UI-LOOK-001): organisation units are a MultiSelect, not a TreeSelect: a vehicle belongs to several
   * units and TreeSelect is single-valued, approved in docs/designs/48-master-data-vehicles.md (UI design, General). */
  const units = useAllOrganizationUnits();
  const unitOptions = useMemo(() => (units.data ?? []).map((u) => ({ id: u.id ?? "", label: u.displayName ?? "" })), [units.data]);
  const ownerPage = useMemo(() => relationLookupPage(EMPLOYER_ROLES), []);
  const depotPage = useMemo(() => relationLookupPage(HOME_DEPOT_ROLES), []);
  const fuelItems = useMemo(() => FUEL_TYPES.map((f) => ({ value: f.value, label: t(f.key) })), [t]);
  const euroItems = useMemo(() => EURO_CLASSES.map((e) => ({ value: e.value, label: t(e.key) })), [t]);

  const [applyValues, setApplyValues] = useState<{ key: number; update: (values: Values) => Values } | undefined>();
  const apply = useCallback((update: (values: Values) => Values) => setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update })), []);
  const [relationDialog, setRelationDialog] = useState<
    { field: "owner" | "homeDepot"; mode: "create"; term: string } | { field: "owner" | "homeDepot"; mode: "edit"; id: string } | null
  >(null);
  const [newCharacteristic, setNewCharacteristic] = useState<{ term: string } | null>(null);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  /* Design 48: choosing a type on a new vehicle fills the empty dimension fields from the type's defaults. */
  const fillFromType = useCallback(
    async (vehicleTypeId: string) => {
      try {
        const defaults = await queryClient.fetchQuery({
          queryKey: masterDataVehicleTypeDefaultsKey(vehicleTypeId),
          queryFn: () => fetchVehicleTypeDefaults(vehicleTypeId),
        });
        apply((values) => {
          if (values.vehicleTypeId !== vehicleTypeId) return values;
          const next = { ...values };
          for (const { field, pick } of TYPE_DEFAULT_FIELDS) {
            const value = pick(defaults);
            if (!next[field].trim() && value != null) next[field] = numberToText(value);
          }
          return next;
        });
      } catch (err) {
        setActionError(getApiErrorMessage(err, t));
      }
    },
    [apply, queryClient, t],
  );

  const save = useCallback(
    async (v: Values, dto: VehicleDto | undefined) => {
      const text = (value: string) => value.trim() || null;
      const body = {
        name: v.name.trim(),
        vehicleTypeId: v.vehicleTypeId!,
        licencePlate: text(v.licencePlate),
        registrationCountryId: v.registrationCountryId,
        vin: text(v.vin)?.toUpperCase() ?? null,
        ownerRelationId: v.owner!.id,
        homeDepotRelationId: v.homeDepot?.id ?? null,
        lengthCm: parseNumber(v.lengthCm),
        widthCm: parseNumber(v.widthCm),
        heightCm: parseNumber(v.heightCm),
        grossVehicleWeightKg: parseNumber(v.grossVehicleWeightKg),
        emptyWeightKg: parseNumber(v.emptyWeightKg),
        maxEpq: parseNumber(v.maxEpq),
        maxLoadingMeters: parseNumber(v.maxLoadingMeters),
        maxPayloadKg: parseNumber(v.maxPayloadKg),
        costPricePerKm: parseNumber(v.costPricePerKm),
        /* The generated client drops the nullability of enum references; an absent value is null on the server. */
        fuelType: v.fuelType ?? undefined,
        euroClass: v.euroClass ?? undefined,
        isArchived: v.isArchived,
        description: text(v.description),
        transportCharacteristicIds: v.ownCharacteristics.map((c) => c.id),
        defaultDrivers: [...v.defaultDrivers]
          .sort((a, b) => a.sortOrder - b.sortOrder)
          .map((d, index) => ({ driverId: d.driverId, sortOrder: index + 1 })),
        organizationUnitIds: v.organizationUnitIds,
        externalIdentifiers: v.externalIdentifiers.map((e) => ({ id: e.id, source: e.source.trim(), identifier: e.identifier.trim() })),
      };
      if (mode === "create") {
        return create.mutateAsync(body);
      }
      return update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const handleArchive = async () => {
    if (!id || !detail.data) return;
    const archived = detail.data.isArchived === true;
    try {
      await (archived ? unarchive : archive).mutateAsync(id);
      void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(vehicles.entity) });
      setConfirmArchive(false);
      toast.success(t(archived ? "MasterData:VehicleUnarchived" : "MasterData:VehicleArchived"));
      onSaved();
      onClose();
    } catch (err) {
      setConfirmArchive(false);
      setActionError(getApiErrorMessage(err, t));
    }
  };

  const relationLookup = (form: FormContext<Values>, field: "owner" | "homeDepot", fetchPage: typeof ownerPage, required: boolean) => (
    <EntityLookup<RelationOption>
      id={form.id(field)}
      value={form.values[field]}
      onChange={(next) => form.set(field, next)}
      fetchPage={fetchPage}
      getLabel={(r) => r.name}
      renderOption={(r) => (
        <>
          <div className="font-medium">{r.name}</div>
          <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
        </>
      )}
      onCreateNew={canCreate("Relation", form.readOnly) ? (term) => setRelationDialog({ field, mode: "create", term }) : undefined}
      createLabel={t("MasterData:AddNewRelation")}
      onEdit={canUpdateRelation && !form.readOnly ? (r) => setRelationDialog({ field, mode: "edit", id: r.id }) : undefined}
      required={required}
      aria-invalid={form.error(field) != null}
      disabled={form.readOnly}
    />
  );

  const enumSelect = <T extends number>(form: FormContext<Values>, field: "fuelType" | "euroClass", labelKey: string, items: { value: T; label: string }[]) => (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} floating="always">
      <Select
        items={items}
        value={(form.values[field] as T | null) ?? null}
        onValueChange={(next) => form.set(field, ((next as T | null) ?? null) as Values[typeof field])}
        disabled={form.readOnly}
      >
        <SelectTrigger id={form.id(field)} className="h-10 w-full bg-background">
          <SelectValue placeholder={t("MasterData:Select")} />
        </SelectTrigger>
        <SelectContent>
          {items.map((item) => (
            <SelectItem key={item.value} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </FormField>
  );

  const externalIdColumns = useMemo<ColDef<ExternalIdRow>[]>(
    () => [
      { field: "identifier", headerName: t("MasterData:ExternalIdentifier"), flex: 1, minWidth: 140, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 128 } },
      { field: "source", headerName: t("MasterData:ExternalSource"), flex: 1, minWidth: 120, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 64 } },
    ],
    [readOnly, t],
  );

  const driverColumns = useMemo<ColDef<DriverLine>[]>(
    () => [
      { field: "sortOrder", headerName: t("MasterData:Order"), width: 110, minWidth: 90, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { min: 1, max: MAX_DEFAULT_DRIVERS, precision: 0 }, type: "rightAligned" },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 160 },
    ],
    [readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: GENERAL_FIELDS,
      render: (form) => (
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:Vehicle")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={64} />
              {/* DEVIATION(UI-LOOK-001): vehicle type is a ReferenceCombobox, not an EntityLookup: at most 50 types per tenant and no lookup-page endpoint, approved in docs/designs/48-master-data-vehicles.md (UI design, vehicle editor). */}
              <FormField label={t("MasterData:VehicleType")} htmlFor={form.id("vehicleTypeId")} required error={form.error("vehicleTypeId")} floating="always">
                <ReferenceCombobox
                  id={form.id("vehicleTypeId")}
                  value={form.values.vehicleTypeId}
                  onChange={(next) => {
                    form.set("vehicleTypeId", next);
                    form.blur("vehicleTypeId");
                    if (mode === "create" && next) void fillFromType(next);
                  }}
                  options={typeOptions}
                  required
                  aria-invalid={form.error("vehicleTypeId") != null}
                  disabled={form.readOnly}
                  {...inPlace.offer("VehicleType", form.readOnly, (r) => {
                    /* Like a picked type: a new vehicle takes the type's dimensions (design 48). */
                    form.set("vehicleTypeId", r.id);
                    form.blur("vehicleTypeId");
                    if (mode === "create") void fillFromType(r.id);
                  })}
                />
              </FormField>
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Registration")}>
            <div className="grid grid-cols-1 gap-3">
              <FormField label={t("MasterData:RegistrationCountry")} htmlFor={form.id("registrationCountryId")} error={form.error("registrationCountryId")} floating="always">
                <ReferenceCombobox
                  id={form.id("registrationCountryId")}
                  value={form.values.registrationCountryId}
                  onChange={(next) => {
                    form.set("registrationCountryId", next);
                    if (isLicencePlateShape(form.values.licencePlate)) form.set("licencePlate", formatLicencePlate(form.values.licencePlate, countryCodeOf(next)));
                  }}
                  options={countryOptions}
                  aria-invalid={form.error("registrationCountryId") != null}
                  disabled={form.readOnly}
                  {...inPlace.offer("Country", form.readOnly, (r) => {
                    form.set("registrationCountryId", r.id);
                    if (isLicencePlateShape(form.values.licencePlate)) form.set("licencePlate", formatLicencePlate(form.values.licencePlate, r.code));
                  })}
                />
              </FormField>
              <FormField label={t("MasterData:LicencePlate")} htmlFor={form.id("licencePlate")} error={form.error("licencePlate")}>
                <PlateInput form={form} countryCode={countryCodeOf(form.values.registrationCountryId)} />
              </FormField>
              <TextField form={form} field="vin" labelKey="MasterData:Vin" maxLength={17} upper />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Ownership")} className="col-span-2">
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <FormField label={t("MasterData:Owner")} htmlFor={form.id("owner")} required error={form.error("owner")} floating="always">
                {relationLookup(form, "owner", ownerPage, true)}
              </FormField>
              <FormField label={t("MasterData:HomeDepot")} htmlFor={form.id("homeDepot")} floating="always">
                {relationLookup(form, "homeDepot", depotPage, false)}
              </FormField>
              <FormField label={t("MasterData:OrganizationUnits")} htmlFor={form.id("organizationUnitIds")} floating="always" className="col-span-2">
                <MultiSelect
                  id={form.id("organizationUnitIds")}
                  value={form.values.organizationUnitIds}
                  onChange={(next) => form.set("organizationUnitIds", next)}
                  options={unitOptions}
                  disabled={form.readOnly}
                  {...inPlace.offer("OrganizationUnit", form.readOnly, (r) =>
                    form.update((v) => ({ ...v, organizationUnitIds: withId(v.organizationUnitIds, r.id) })),
                  )}
                />
              </FormField>
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Remarks")} className="col-span-2">
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
          </FormSection>
        </div>
      ),
    },
    {
      value: "dimensions",
      labelKey: "MasterData:DimensionsAndCapacity",
      fields: [...NUMBER_FIELDS, "fuelType", "euroClass"],
      render: (form) => (
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:DimensionsAndCapacity")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <NumberField form={form} field="lengthCm" labelKey="MasterData:LengthCm" />
              <NumberField form={form} field="widthCm" labelKey="MasterData:WidthCm" />
              <NumberField form={form} field="heightCm" labelKey="MasterData:HeightCm" />
              <NumberField form={form} field="emptyWeightKg" labelKey="MasterData:EmptyWeightKg" />
              <NumberField form={form} field="grossVehicleWeightKg" labelKey="MasterData:GrossVehicleWeightKg" />
              <NumberField form={form} field="maxPayloadKg" labelKey="MasterData:MaxPayloadKg" />
              <NumberField form={form} field="maxEpq" labelKey="MasterData:MaxEpq" />
              <NumberField form={form} field="maxLoadingMeters" labelKey="MasterData:MaxLoadingMeters" />
            </div>
          </FormSection>
          <div className="flex flex-col gap-4">
            <FormSection title={t("MasterData:Engine")}>
              <div className="grid grid-cols-1 gap-3">
                {enumSelect(form, "fuelType", "MasterData:FuelType", fuelItems)}
                {enumSelect(form, "euroClass", "MasterData:EuroClass", euroItems)}
              </div>
            </FormSection>
            <FormSection title={t("MasterData:Financial")}>
              <CostPricePerKmField form={form} />
            </FormSection>
          </div>
        </div>
      ),
    },
    {
      value: "characteristics",
      labelKey: "MasterData:Characteristics",
      fields: ["ownCharacteristics"],
      render: (form) => (
        <CharacteristicsTab
          form={form}
          typeCharacteristics={detail.data?.vehicleTypeId === form.values.vehicleTypeId ? (detail.data?.characteristics ?? []).filter((c) => c.origin === ORIGIN_TYPE) : undefined}
          onCreateNew={canCreate("TransportCharacteristics", form.readOnly) ? (term) => setNewCharacteristic({ term }) : undefined}
        />
      ),
    },
    {
      value: "drivers",
      labelKey: "MasterData:DriversAndIdentification",
      fields: ["defaultDrivers", "externalIdentifiers"],
      render: (form) => (
        <div className="grid h-80 grid-cols-2 gap-4">
          <FormSection title={t("MasterData:DefaultDrivers")} className="flex min-h-0 flex-col">
            <div className="flex min-h-32 flex-1 flex-col gap-2">
              {form.error("defaultDrivers") && <p className="text-label text-destructive">{form.error("defaultDrivers")}</p>}
              <LazyLinesGrid<DriverLine>
                rows={form.values.defaultDrivers}
                onRowsChange={(rows) => form.set("defaultDrivers", rows)}
                getRowId={(row) => row.key}
                columnDefs={driverColumns}
                readOnly={form.readOnly}
                toolbar={
                  form.values.defaultDrivers.length < MAX_DEFAULT_DRIVERS ? (
                    <>
                      <label htmlFor={form.id("defaultDrivers")} className="sr-only">
                        {t("MasterData:AddDefaultDriver")}
                      </label>
                      <EntityLookup<DriverOption>
                        id={form.id("defaultDrivers")}
                        value={null}
                        onChange={(next) => {
                          if (!next || form.values.defaultDrivers.some((d) => d.driverId === next.id)) return;
                          const order = Math.max(0, ...form.values.defaultDrivers.map((d) => d.sortOrder)) + 1;
                          form.set("defaultDrivers", [...form.values.defaultDrivers, { key: next.id, driverId: next.id, name: next.name, sortOrder: order }]);
                        }}
                        fetchPage={driverLookupPage}
                        getLabel={(d) => d.name}
                        renderOption={(d) => (
                          <>
                            <div className="font-medium">{d.name}</div>
                            <div className="text-label text-muted-foreground">{d.code}</div>
                          </>
                        )}
                        placeholder={t("MasterData:AddDefaultDriver")}
                        className="w-72"
                        {...inPlace.offer<DriverDto>("Driver", form.readOnly, (r) =>
                          form.update((v) =>
                            v.defaultDrivers.some((d) => d.driverId === r.id) || v.defaultDrivers.length >= MAX_DEFAULT_DRIVERS
                              ? v
                              : {
                                  ...v,
                                  defaultDrivers: [
                                    ...v.defaultDrivers,
                                    { key: r.id, driverId: r.id, name: r.name, sortOrder: Math.max(0, ...v.defaultDrivers.map((d) => d.sortOrder)) + 1 },
                                  ],
                                },
                          ),
                        )}
                      />
                    </>
                  ) : undefined
                }
              />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:ExternalIdentifiers")} className="flex min-h-0 flex-col">
            <div className="flex min-h-32 flex-1 flex-col gap-2">
              {form.error("externalIdentifiers") && <p className="text-label text-destructive">{form.error("externalIdentifiers")}</p>}
              <LazyLinesGrid<ExternalIdRow>
                rows={form.values.externalIdentifiers}
                onRowsChange={(rows) => form.set("externalIdentifiers", rows)}
                getRowId={(row) => row.key}
                columnDefs={externalIdColumns}
                readOnly={form.readOnly}
                toolbar={
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      form.set("externalIdentifiers", [...form.values.externalIdentifiers, { key: `new-${++rowCounter}`, id: null, source: "", identifier: "" }])
                    }
                  >
                    <Plus /> {t("MasterData:AddExternalIdentifier")}
                  </Button>
                }
              />
            </div>
          </FormSection>
        </div>
      ),
    },
  ];

  const badges =
    mode === "edit" && detail.data ? (
      <>
        <Badge variant="secondary">{t(vehicleTypeKindKey(detail.data.kind))}</Badge>
        {detail.data.isArchived && <Badge variant="outline">{t("MasterData:Archived")}</Badge>}
      </>
    ) : undefined;

  const archived = detail.data?.isArchived === true;

  if (!ready) return null;

  return (
    <>
      <MasterDataEditor<Values, VehicleDto>
        entityKey="Vehicle"
        queryEntity={vehicles.entity}
        size="lg"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.name ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        extraCommands={
          mode === "edit" && !readOnly && canUpdate
            ? [
                {
                  id: "archive",
                  labelKey: archived ? "MasterData:Unarchive" : "MasterData:Archive",
                  icon: archived ? ArchiveRestore : Archive,
                  groupKey: "MasterData:Vehicle",
                  disabled: !detail.data,
                  onClick: () => setConfirmArchive(true),
                },
              ]
            : undefined
        }
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        notice={
          actionError ? (
            <Alert variant="destructive">
              <AlertDescription>{actionError}</AlertDescription>
            </Alert>
          ) : undefined
        }
        badges={badges}
        applyValues={applyValues}
      />
      {relationDialog && (
        /* "Add new relation…" and the edit pencil of the owner and home-depot lookups (UI-LOOK-004/005). */
        <RelationEditor
          mode={relationDialog.mode}
          id={relationDialog.mode === "edit" ? relationDialog.id : undefined}
          initialName={relationDialog.mode === "create" ? relationDialog.term : undefined}
          readOnly={false}
          open
          onClose={() => setRelationDialog(null)}
          onSaved={(saved) => {
            const dto = saved as RelationDto | undefined;
            if (!dto?.id) return;
            const option: RelationOption = { id: dto.id, code: dto.code ?? "", name: dto.name ?? "", city: null, countryCode: null, roles: dto.roles ?? 0 };
            const field = relationDialog.field;
            apply((values) => ({ ...values, [field]: option }));
          }}
        />
      )}
      {newCharacteristic && (
        <TransportCharacteristicsEditor
          mode="create"
          initialName={newCharacteristic.term}
          readOnly={false}
          open
          onClose={() => setNewCharacteristic(null)}
          onSaved={(saved) => {
            const created = saved as TransportCharacteristicsDto | undefined;
            if (!created?.id) return;
            const row: CharacteristicRow = { id: created.id, code: created.code ?? "", name: created.name ?? "", origin: ORIGIN_VEHICLE };
            apply((values) => (values.ownCharacteristics.some((c) => c.id === row.id) ? values : { ...values, ownCharacteristics: [...values.ownCharacteristics, row] }));
          }}
        />
      )}
      <ConfirmDialog
        open={confirmArchive}
        onOpenChange={(next) => {
          if (!next) setConfirmArchive(false);
        }}
        title={t(archived ? "MasterData:UnarchiveVehicle" : "MasterData:ArchiveVehicle")}
        description={t(archived ? "MasterData:UnarchiveVehicleQuestion" : "MasterData:ArchiveVehicleQuestion").replace("{0}", detail.data?.name ?? "")}
        confirmLabel={t(archived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={handleArchive}
      />
      {inPlace.dialog}
    </>
  );
}

/** The plate as typed; on blur it takes the display form of its country (NL by sidecode). */
function PlateInput({ form, countryCode }: { form: FormContext<Values>; countryCode: string | null }) {
  return (
    <Input
      id={form.id("licencePlate")}
      value={form.values.licencePlate}
      maxLength={32}
      disabled={form.readOnly}
      onChange={(e) => form.set("licencePlate", e.target.value.toUpperCase())}
      onBlur={() => {
        if (isLicencePlateShape(form.values.licencePlate)) form.set("licencePlate", formatLicencePlate(form.values.licencePlate, countryCode));
        form.blur("licencePlate");
      }}
    />
  );
}

/** QP10: the vehicle's own cost price per kilometre. The server uses it, else the type's: while the field is empty,
 *  the hint names the selected type's value (UI-FORM-003: a hint, not a placeholder that repeats a value). */
function CostPricePerKmField({ form }: { form: FormContext<Values> }) {
  const { t } = useL();
  const f = useFormat();
  const defaults = useVehicleTypeDefaults(form.values.vehicleTypeId);
  const typeValue = defaults.data?.costPricePerKm;
  const hint = form.values.costPricePerKm.trim()
    ? undefined
    : typeValue != null
      ? t("MasterData:CostPricePerKmFromTypeHint").replace("{0}", f.unitPrice(typeValue))
      : form.values.vehicleTypeId && defaults.isSuccess
        ? t("MasterData:CostPricePerKmNoTypeValueHint")
        : undefined;
  return <NumberField form={form} field="costPricePerKm" labelKey="MasterData:CostPricePerKm" hint={hint} />;
}

/** One grid: the type's characteristics (read-only, "from type") and the vehicle's own (removable, picked above). */
function CharacteristicsTab({
  form,
  typeCharacteristics,
  onCreateNew,
}: {
  form: FormContext<Values>;
  /** The stored record's type rows while its type is unchanged; otherwise the selected type's are fetched. */
  typeCharacteristics: VehicleCharacteristicDto[] | undefined;
  onCreateNew?: (term: string) => void;
}) {
  const { t } = useL();
  const defaults = useVehicleTypeDefaults(typeCharacteristics ? null : form.values.vehicleTypeId);
  const typeRows = useMemo(
    () => (typeCharacteristics ?? defaults.data?.characteristics ?? []).map(toCharacteristicRow).map((c) => ({ ...c, origin: ORIGIN_TYPE })),
    [defaults.data, typeCharacteristics],
  );
  const typeIds = useMemo(() => new Set(typeRows.map((r) => r.id)), [typeRows]);
  const rows = useMemo(() => [...typeRows, ...form.values.ownCharacteristics.filter((c) => !typeIds.has(c.id))], [form.values.ownCharacteristics, typeIds, typeRows]);

  const columns = useMemo<ColDef<CharacteristicRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 160 },
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      {
        field: "origin",
        headerName: t("MasterData:Origin"),
        flex: 1,
        minWidth: 110,
        cellRenderer: (params: ICellRendererParams<CharacteristicRow>) =>
          params.data?.origin === ORIGIN_TYPE ? <Badge variant="secondary">{t("MasterData:FromType")}</Badge> : null,
      },
    ],
    [t],
  );

  const add = (option: TransportCharacteristicOption) => {
    if (typeIds.has(option.id) || form.values.ownCharacteristics.some((c) => c.id === option.id)) return;
    form.set("ownCharacteristics", [...form.values.ownCharacteristics, { id: option.id, code: option.code, name: option.name, origin: ORIGIN_VEHICLE }]);
  };

  return (
    <div className="flex h-80 flex-col gap-2">
      <LazyLinesGrid<CharacteristicRow>
        rows={rows}
        onRowsChange={(next) => form.set("ownCharacteristics", next.filter((c) => c.origin === ORIGIN_VEHICLE))}
        getRowId={(row) => row.id}
        columnDefs={columns}
        readOnly={form.readOnly}
        canRemoveRow={(row) => row.origin === ORIGIN_VEHICLE}
        toolbar={
          <>
            <label htmlFor={form.id("ownCharacteristics")} className="sr-only">
              {t("MasterData:AddTransportCharacteristic")}
            </label>
            <EntityLookup<TransportCharacteristicOption>
              id={form.id("ownCharacteristics")}
              value={null}
              onChange={(next) => {
                if (next) add(next);
              }}
              fetchPage={transportCharacteristicLookupPage}
              getLabel={(c) => c.name}
              renderOption={(c) => (
                <>
                  <div className="font-medium">{c.name}</div>
                  <div className="text-label text-muted-foreground">{[c.code, c.description].filter(Boolean).join(", ")}</div>
                </>
              )}
              onCreateNew={onCreateNew}
              createLabel={t("MasterData:AddNewTransportCharacteristics")}
              placeholder={t("MasterData:AddTransportCharacteristic")}
              className="w-72"
            />
          </>
        }
      />
    </div>
  );
}
