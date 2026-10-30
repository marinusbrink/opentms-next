import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { relationCategories, relationLookupPage, type RelationOption } from "@/domains/master-data/entities";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { WAYPOINT_ACTIVITIES, waypointTypes, type WaypointActivity, type WaypointTypeDto } from "@/domains/orders/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The waypoint type editor (design 51, UI design): `md`. General (Code, Name, Activity, Next type, Default duration,
 * Archived, Remarks); Defaults (Relation category, Default relation: Master Data rows, named by the server, which
 * refuses an unknown id); Fleet management codes as lines (vendor, code; one per vendor, at most 20). */

const MAX_CODES = 20;

interface CodeLine {
  key: string;
  vendor: string;
  code: string;
}

interface Values {
  code: string;
  name: string;
  activity: WaypointActivity;
  successiveWaypointTypeId: string | null;
  defaultDurationMinutes: string;
  isArchived: boolean;
  description: string;
  relationCategoryId: string | null;
  defaultRelation: RelationOption | null;
  fleetManagementCodes: CodeLine[];
}

let lineCounter = 0;
const newKey = () => `fms-${++lineCounter}`;

const toRelation = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: WaypointTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  activity: dto?.activity ?? 1,
  successiveWaypointTypeId: dto?.successiveWaypointTypeId ?? null,
  defaultDurationMinutes: numberToText(dto?.defaultDurationMinutes),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  relationCategoryId: dto?.relationCategoryId ?? null,
  defaultRelation: toRelation(dto?.defaultRelationId, dto?.defaultRelationName),
  fleetManagementCodes: (dto?.fleetManagementCodes ?? []).map((c) => ({ key: newKey(), vendor: c.vendor ?? "", code: c.code ?? "" })),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const duration = numberError(v.defaultDurationMinutes, { integer: true, min: 0, max: 1440 });
  if (duration) errors.defaultDurationMinutes = duration;
  const lines = v.fleetManagementCodes;
  if (lines.some((l) => !l.vendor.trim() || !l.code.trim())) errors.fleetManagementCodes = "Orders:FleetManagementCodeIncomplete";
  else if (new Set(lines.map((l) => l.vendor.trim().toUpperCase())).size !== lines.length) errors.fleetManagementCodes = "Orders:FleetManagementVendorTwice";
  return errors;
};

export function WaypointTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = waypointTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = waypointTypes.useCreate();
  const update = waypointTypes.useUpdate();
  const remove = waypointTypes.useDelete();
  const canDelete = useIsGranted("Orders.WaypointTypes.Delete");
  const inPlace = useCreateInPlace();

  /* DEVIATION(UI-LOOK-001): waypoint types and relation categories are ReferenceComboboxes over their lookups, not
   * EntityLookups: at most 100 per tenant and no lookup-page endpoint, approved in docs/designs/51-orders-master-data.md (UI design). */
  const typeLookup = waypointTypes.useLookup(open);
  const typeOptions = useMemo(
    () => (typeLookup.data ?? []).filter((i) => i.id !== id).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })),
    [typeLookup.data, id],
  );
  const categoryLookup = relationCategories.useLookup(open);
  const categoryOptions = useMemo(() => (categoryLookup.data ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })), [categoryLookup.data]);
  const relationPage = useMemo(() => relationLookupPage(), []);
  const activityItems = useMemo(() => WAYPOINT_ACTIVITIES.map((a) => ({ value: a.value, label: t(a.key) })), [t]);

  /* A stored reference that the lookup no longer returns (archived, or deleted in Master Data) keeps its name. */
  const withStored = (options: { value: string; label: string }[], value: string | null, storedName: string | null | undefined) =>
    value && !options.some((o) => o.value === value) ? [...options, { value, label: storedName ?? "" }] : options;

  const save = useCallback(
    async (v: Values, dto: WaypointTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        activity: v.activity,
        successiveWaypointTypeId: v.successiveWaypointTypeId,
        defaultDurationMinutes: parseInteger(v.defaultDurationMinutes),
        relationCategoryId: v.relationCategoryId,
        defaultRelationId: v.defaultRelation?.id ?? null,
        fleetManagementCodes: v.fleetManagementCodes.map((l) => ({ vendor: l.vendor.trim(), code: l.code.trim() })),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const codeColumns = useMemo<ColDef<CodeLine>[]>(
    () => [
      { field: "vendor", headerName: t("Orders:FleetManagementVendor"), flex: 1, minWidth: 140, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 64 } },
      { field: "code", headerName: t("Orders:FleetManagementCode"), flex: 1, minWidth: 120, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 64 } },
    ],
    [readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "activity", "successiveWaypointTypeId", "defaultDurationMinutes", "isArchived", "description"],
      render: (form) => (
        <FormSection title={t("Orders:WaypointType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("Orders:Activity")} htmlFor={form.id("activity")} required floating="always">
              <Select
                items={activityItems}
                value={form.values.activity}
                onValueChange={(next) => form.set("activity", (next as WaypointActivity | null) ?? 1)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("activity")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {activityItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Orders:SuccessiveWaypointType")} htmlFor={form.id("successiveWaypointTypeId")} error={form.error("successiveWaypointTypeId")} floating="always">
              <ReferenceCombobox
                id={form.id("successiveWaypointTypeId")}
                value={form.values.successiveWaypointTypeId}
                onChange={(next) => form.set("successiveWaypointTypeId", next)}
                options={withStored(typeOptions, form.values.successiveWaypointTypeId, detail.data?.successiveWaypointTypeName)}
                aria-invalid={form.error("successiveWaypointTypeId") != null}
                disabled={form.readOnly}
                {...inPlace.offer("WaypointType", form.readOnly, (r) => form.set("successiveWaypointTypeId", r.id))}
              />
            </FormField>
            <NumberField form={form} field="defaultDurationMinutes" labelKey="Orders:DefaultDurationMinutes" />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "defaults",
      labelKey: "Orders:Defaults",
      fields: ["relationCategoryId", "defaultRelation"],
      render: (form) => (
        <FormSection title={t("Orders:Defaults")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("Orders:RelationCategory")} htmlFor={form.id("relationCategoryId")} error={form.error("relationCategoryId")} floating="always">
              <ReferenceCombobox
                id={form.id("relationCategoryId")}
                value={form.values.relationCategoryId}
                onChange={(next) => form.set("relationCategoryId", next)}
                options={withStored(categoryOptions, form.values.relationCategoryId, detail.data?.relationCategoryName)}
                aria-invalid={form.error("relationCategoryId") != null}
                disabled={form.readOnly}
                {...inPlace.offer("RelationCategory", form.readOnly, (r) => form.set("relationCategoryId", r.id))}
              />
            </FormField>
            <FormField label={t("Orders:DefaultRelation")} htmlFor={form.id("defaultRelation")} error={form.error("defaultRelation")} floating="always">
              <EntityLookup<RelationOption>
                id={form.id("defaultRelation")}
                value={form.values.defaultRelation}
                onChange={(next) => form.set("defaultRelation", next)}
                fetchPage={relationPage}
                getLabel={(r) => r.name}
                renderOption={(r) => (
                  <>
                    <div className="font-medium">{r.name}</div>
                    <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
                  </>
                )}
                aria-invalid={form.error("defaultRelation") != null}
                disabled={form.readOnly}
                {...inPlace.offer("Relation", form.readOnly, (r) => form.set("defaultRelation", toRelation(r.id, r.name)))}
              />
            </FormField>
          </div>
        </FormSection>
      ),
    },
    {
      value: "fleet-management",
      labelKey: "Orders:FleetManagementCodes",
      fields: ["fleetManagementCodes"],
      render: (form) => (
        <FormSection title={t("Orders:FleetManagementCodes")}>
          <div className="flex h-60 flex-col gap-2">
            {form.error("fleetManagementCodes") && <p className="text-label text-destructive">{form.error("fleetManagementCodes")}</p>}
            <LazyLinesGrid<CodeLine>
              rows={form.values.fleetManagementCodes}
              onRowsChange={(rows) => form.set("fleetManagementCodes", rows)}
              getRowId={(row) => row.key}
              columnDefs={codeColumns}
              readOnly={form.readOnly}
              toolbar={
                form.values.fleetManagementCodes.length < MAX_CODES ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => form.set("fleetManagementCodes", [...form.values.fleetManagementCodes, { key: newKey(), vendor: "", code: "" }])}
                  >
                    <Plus />
                    {t("Orders:AddFleetManagementCode")}
                  </Button>
                ) : undefined
              }
            />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, WaypointTypeDto>
        entityKey="WaypointType"
        resource="Orders"
        queryEntity={waypointTypes.entity}
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
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      />
      {inPlace.dialog}
    </>
  );
}
