import { useCallback, useMemo } from "react";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, EnumSelectField, NumberField, TextAreaField, numberError, numberToText, parseInteger } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors, type FormContext } from "@/components/record-editor/MasterDataEditor";
import { geographicalRegions, relationLookupPage, useRelationAddresses, type RelationOption } from "@/domains/master-data/entities";
import { waypointTypes } from "@/domains/orders/reference-data";
import {
  MAX_VIA_DEPOT_POSITION,
  VIA_DEPOT_SCOPES,
  scopeNeedsRegion,
  scopeNeedsRelation,
  viaDepotRuleName,
  viaDepotRules,
  type ViaDepotRuleDto,
  type ViaDepotScope,
} from "@/domains/planning-execution/via-depot-rules";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The via-depot rule editor (design 59 "ViaDepotRule", design 61): `md`; General (scope; the relation for the Relation
 * and Customer scopes or the region for Region; the depot relation and one of its addresses; the activity on arrival
 * and, optionally, on departure; overnight; position; archived), Remarks, Record identification. Relations are
 * EntityLookups (UI-LOOK-001); regions and waypoint types ReferenceComboboxes over their lookups, as designs 51/52
 * decided for those lists. Every picker offers "Add new …" (UI-LOOK-004). */

/** Depot | OwnCompany: the roles a depot relation carries (as the vehicle's home depot, design 48). */
const DEPOT_ROLES = 8 | 16;

interface Values {
  scope: ViaDepotScope;
  relation: RelationOption | null;
  geographicalRegionId: string | null;
  depotRelation: RelationOption | null;
  depotAddressId: string | null;
  arrivalWaypointTypeId: string | null;
  departureWaypointTypeId: string | null;
  overnight: boolean;
  position: string;
  isArchived: boolean;
  description: string;
}

const toRelation = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

const initialValues = (dto: ViaDepotRuleDto | undefined): Values => ({
  scope: dto?.scope ?? 1,
  relation: toRelation(dto?.relationId, dto?.relationName),
  geographicalRegionId: dto?.geographicalRegionId ?? null,
  depotRelation: toRelation(dto?.depotRelationId, dto?.depotRelationName),
  depotAddressId: dto?.depotAddressId ?? null,
  arrivalWaypointTypeId: dto?.arrivalWaypointTypeId ?? null,
  departureWaypointTypeId: dto?.departureWaypointTypeId ?? null,
  overnight: dto?.overnight ?? false,
  position: numberToText(dto?.position ?? 1),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (scopeNeedsRelation(v.scope) && !v.relation) errors.relation = "MasterData:Required";
  if (scopeNeedsRegion(v.scope) && !v.geographicalRegionId) errors.geographicalRegionId = "MasterData:Required";
  if (!v.depotRelation) errors.depotRelation = "MasterData:Required";
  if (!v.depotAddressId) errors.depotAddressId = "MasterData:Required";
  if (!v.arrivalWaypointTypeId) errors.arrivalWaypointTypeId = "MasterData:Required";
  const position = parseInteger(v.position) == null ? "MasterData:Required" : numberError(v.position, { integer: true, min: 1, max: MAX_VIA_DEPOT_POSITION });
  if (position) errors.position = position;
  return errors;
};

export function ViaDepotRuleEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = viaDepotRules.useDetail(mode === "edit" ? id : undefined);
  const create = viaDepotRules.useCreate();
  const update = viaDepotRules.useUpdate();
  const remove = viaDepotRules.useDelete();
  const canDelete = useIsGranted("PlanningExecution.ViaDepotRules.Delete");
  const inPlace = useCreateInPlace();

  const relationPage = useMemo(() => relationLookupPage(), []);
  const depotPage = useMemo(() => relationLookupPage(DEPOT_ROLES), []);
  const regionLookup = geographicalRegions.useLookup(open);
  const typeLookup = waypointTypes.useLookup(open);
  const scopeItems = useMemo(() => VIA_DEPOT_SCOPES.map((s) => ({ value: s.value, label: t(s.key) })), [t]);

  /* A stored reference the lookup no longer returns (archived, or deleted in Master Data) keeps its name. */
  const withStored = (options: { value: string; label: string }[], value: string | null | undefined, storedName: string | null | undefined) =>
    value && !options.some((o) => o.value === value) ? [...options, { value, label: storedName ?? "" }] : options;
  const regionOptions = useMemo(
    () => withStored((regionLookup.data ?? []).map((r) => ({ value: r.id ?? "", label: r.name ?? "" })), detail.data?.geographicalRegionId, detail.data?.geographicalRegionName),
    [regionLookup.data, detail.data],
  );
  const typeOptions = useMemo(() => (typeLookup.data ?? []).map((w) => ({ value: w.id ?? "", label: w.name ?? "" })), [typeLookup.data]);

  const save = useCallback(
    async (v: Values, dto: ViaDepotRuleDto | undefined) => {
      const body = {
        scope: v.scope,
        relationId: scopeNeedsRelation(v.scope) ? (v.relation?.id ?? null) : null,
        geographicalRegionId: scopeNeedsRegion(v.scope) ? v.geographicalRegionId : null,
        depotRelationId: v.depotRelation!.id,
        depotAddressId: v.depotAddressId!,
        arrivalWaypointTypeId: v.arrivalWaypointTypeId!,
        departureWaypointTypeId: v.departureWaypointTypeId,
        overnight: v.overnight,
        position: parseInteger(v.position) ?? 1,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create" ? await create.mutateAsync(body) : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const relationLookup = (form: FormContext<Values>, field: "relation" | "depotRelation", labelKey: string, fetchPage: typeof relationPage, onPicked?: () => void) => (
    <FormField label={t(labelKey)} htmlFor={form.id(field)} required error={form.error(field)} floating="always">
      <EntityLookup<RelationOption>
        id={form.id(field)}
        value={form.values[field]}
        onChange={(next) => {
          form.set(field, next);
          form.blur(field);
          onPicked?.();
        }}
        fetchPage={fetchPage}
        getLabel={(r) => r.name}
        renderOption={(r) => (
          <>
            <div className="font-medium">{r.name}</div>
            <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
          </>
        )}
        required
        aria-invalid={form.error(field) != null}
        disabled={form.readOnly}
        {...inPlace.offer("Relation", form.readOnly, (r) => {
          form.set(field, { id: r.id, code: r.code ?? "", name: r.name, city: null, countryCode: null, roles: 0 });
          onPicked?.();
        })}
      />
    </FormField>
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["scope", "relation", "geographicalRegionId", "depotRelation", "depotAddressId", "arrivalWaypointTypeId", "departureWaypointTypeId", "overnight", "position", "isArchived"],
      render: (form) => (
        <>
          <FormSection title={t("PlanningExecution:Scope")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <EnumSelectField form={form} field="scope" labelKey="PlanningExecution:Scope" items={scopeItems} required />
              {scopeNeedsRelation(form.values.scope) && relationLookup(form, "relation", "PlanningExecution:Relation", relationPage)}
              {scopeNeedsRegion(form.values.scope) && (
                <FormField label={t("PlanningExecution:GeographicalRegion")} htmlFor={form.id("geographicalRegionId")} required error={form.error("geographicalRegionId")} floating="always">
                  {/* DEVIATION(UI-LOOK-001): regions are a ReferenceCombobox over their lookup (under 500 per tenant, no lookup-page endpoint), approved in docs/designs/52-financial-master-data.md (UI design) and kept here. */}
                  <ReferenceCombobox
                    id={form.id("geographicalRegionId")}
                    value={form.values.geographicalRegionId}
                    onChange={(next) => {
                      form.set("geographicalRegionId", next);
                      form.blur("geographicalRegionId");
                    }}
                    options={regionOptions}
                    required
                    aria-invalid={form.error("geographicalRegionId") != null}
                    disabled={form.readOnly}
                    {...inPlace.offer("GeographicalRegion", form.readOnly, (r) => form.set("geographicalRegionId", r.id))}
                  />
                </FormField>
              )}
            </div>
          </FormSection>
          <FormSection title={t("PlanningExecution:DepotRelation")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              {relationLookup(form, "depotRelation", "PlanningExecution:DepotRelation", depotPage, () => form.set("depotAddressId", null))}
              <DepotAddressField form={form} storedCity={detail.data?.depotAddressCity} />
              <FormField label={t("PlanningExecution:ArrivalWaypointType")} htmlFor={form.id("arrivalWaypointTypeId")} required error={form.error("arrivalWaypointTypeId")} floating="always">
                {/* DEVIATION(UI-LOOK-001): waypoint types are a ReferenceCombobox over their lookup (at most 100 per tenant), approved in docs/designs/51-orders-master-data.md (UI design). */}
                <ReferenceCombobox
                  id={form.id("arrivalWaypointTypeId")}
                  value={form.values.arrivalWaypointTypeId}
                  onChange={(next) => {
                    form.set("arrivalWaypointTypeId", next);
                    form.blur("arrivalWaypointTypeId");
                  }}
                  options={typeOptions}
                  required
                  aria-invalid={form.error("arrivalWaypointTypeId") != null}
                  disabled={form.readOnly}
                  {...inPlace.offer("WaypointType", form.readOnly, (r) => form.set("arrivalWaypointTypeId", r.id))}
                />
              </FormField>
              <FormField label={t("PlanningExecution:DepartureWaypointType")} htmlFor={form.id("departureWaypointTypeId")} floating="always" hint={t("PlanningExecution:DepartureWaypointTypeHint")}>
                <ReferenceCombobox
                  id={form.id("departureWaypointTypeId")}
                  value={form.values.departureWaypointTypeId}
                  onChange={(next) => form.set("departureWaypointTypeId", next)}
                  options={typeOptions}
                  disabled={form.readOnly}
                  {...inPlace.offer("WaypointType", form.readOnly, (r) => form.set("departureWaypointTypeId", r.id))}
                />
              </FormField>
              <CheckboxField form={form} field="overnight" labelKey="PlanningExecution:Overnight" />
              <NumberField form={form} field="position" labelKey="PlanningExecution:Position" required hintKey="PlanningExecution:ViaDepotPositionHint" />
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            </div>
          </FormSection>
        </>
      ),
    },
    {
      value: "remarks",
      labelKey: "MasterData:Remarks",
      fields: ["description"],
      render: (form) => <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} />,
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, ViaDepotRuleDto>
        entityKey="ViaDepotRule"
        resource="PlanningExecution"
        queryEntity={viaDepotRules.entity}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={viaDepotRuleName}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      />
      {inPlace.dialog}
    </>
  );
}

/** The depot's address: the addresses of the picked depot relation, named street, number, postal code and city. */
function DepotAddressField({ form, storedCity }: { form: FormContext<Values>; storedCity: string | null | undefined }) {
  const { t } = useL();
  const depotId = form.values.depotRelation?.id;
  const addresses = useRelationAddresses(depotId);
  const options = useMemo(() => {
    const list = (addresses.data ?? []).map((a) => ({
      value: a.id ?? "",
      label: [[a.street, a.houseNumber, a.houseNumberSuffix].filter(Boolean).join(" "), [a.postalCode, a.city].filter(Boolean).join(" "), a.addressTypeName].filter(Boolean).join(", "),
    }));
    const current = form.values.depotAddressId;
    return current && !list.some((o) => o.value === current) ? [...list, { value: current, label: storedCity ?? "" }] : list;
  }, [addresses.data, form.values.depotAddressId, storedCity]);
  return (
    <FormField label={t("PlanningExecution:DepotAddress")} htmlFor={form.id("depotAddressId")} required error={form.error("depotAddressId")} floating="always" hint={depotId ? undefined : t("PlanningExecution:DepotAddressHint")}>
      {/* DEVIATION(UI-LOOK-004): the address list belongs to the picked depot relation, which has no address editor of its own; a new address is added through the relation editor. Reported for approval in the design-61 build notes (Master data lists). */}
      <ReferenceCombobox
        id={form.id("depotAddressId")}
        value={form.values.depotAddressId}
        onChange={(next) => {
          form.set("depotAddressId", next);
          form.blur("depotAddressId");
        }}
        options={options}
        required
        aria-invalid={form.error("depotAddressId") != null}
        disabled={form.readOnly || !depotId}
      />
    </FormField>
  );
}
