import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  NUMBERING_FIELDS,
  NumberingFields,
  numberingDtoOf,
  numberingValuesOf,
  validateNumbering,
  type NumberingFormValues,
} from "@/apps/master-data/components/numbering-fields";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  REQUIRED_FIELDS,
  transportOrderTypes,
  waypointTypes,
  type TransportOrderRequiredFields,
  type TransportOrderTypeCreateDto,
  type TransportOrderTypeDto,
} from "@/domains/orders/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The transport order type editor (design 51, UI design): `md`. General (Code, Name, Archived, Remarks); Default stops
 * as lines (position, waypoint type) with the waypoint type picker above them, at most 10, a type may repeat; Required
 * fields as one checkbox per flag; Numbering (design 50). The default type gets a badge and no Delete. */

const MAX_STOPS = 10;

interface StopLine {
  key: string;
  waypointTypeId: string;
  name: string;
  position: number;
}

interface Values extends NumberingFormValues {
  code: string;
  name: string;
  isArchived: boolean;
  description: string;
  requiredFields: TransportOrderRequiredFields;
  defaultWaypoints: StopLine[];
}

/* A new type starts as the seeded one does (design 51): TO, year, "-", five digits. */
const NEW_NUMBERING = { prefix: "TO", includeYear: true, includeMonth: false, separator: "-", digits: 5 };

let lineCounter = 0;
const newKey = () => `stop-${++lineCounter}`;

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: TransportOrderTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  requiredFields: (dto?.requiredFields as number | undefined) ?? 0,
  defaultWaypoints: (dto?.defaultWaypoints ?? []).map((w) => ({
    key: newKey(),
    waypointTypeId: w.waypointTypeId ?? "",
    name: w.name ?? "",
    position: w.position ?? 0,
  })),
  ...numberingValuesOf(dto?.numbering, dto?.nextNumber, NEW_NUMBERING),
});

const ordered = (lines: StopLine[]) => [...lines].sort((a, b) => a.position - b.position);

export function TransportOrderTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = transportOrderTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = transportOrderTypes.useCreate();
  const update = transportOrderTypes.useUpdate();
  const remove = transportOrderTypes.useDelete();
  const canDelete = useIsGranted("Orders.TransportOrderTypes.Delete");
  const minimumNextNumber = Number(detail.data?.nextNumber ?? 1);
  const inPlace = useCreateInPlace();

  /* DEVIATION(UI-LOOK-001): the waypoint type picker is a ReferenceCombobox over the lookup, not an EntityLookup: at
   * most 100 types per tenant and no lookup-page endpoint, approved in docs/designs/51-orders-master-data.md (UI design). */
  const typeLookup = waypointTypes.useLookup(open);
  const typeOptions = useMemo(() => (typeLookup.data ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? "" })), [typeLookup.data]);

  const validate = useCallback(
    (v: Values): FieldErrors<Values> => {
      const errors: FieldErrors<Values> = { ...validateNumbering(v, minimumNextNumber) };
      if (!v.code.trim()) errors.code = "MasterData:Required";
      if (!v.name.trim()) errors.name = "MasterData:Required";
      if (v.isArchived && detail.data?.isDefault) errors.isArchived = "MasterData:ArchivedCannotBeDefault";
      return errors;
    },
    [minimumNextNumber, detail.data?.isDefault],
  );

  const save = useCallback(
    async (v: Values, dto: TransportOrderTypeDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        requiredFields: v.requiredFields as TransportOrderTypeCreateDto["requiredFields"],
        defaultWaypoints: ordered(v.defaultWaypoints).map((w, index) => ({ waypointTypeId: w.waypointTypeId, position: index + 1 })),
        numbering: numberingDtoOf(v),
        nextNumber: parseInteger(v.nextNumber),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const stopColumns = useMemo<ColDef<StopLine>[]>(
    () => [
      { field: "position", headerName: t("MasterData:Position"), width: 110, minWidth: 90, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { min: 1, max: MAX_STOPS, precision: 0 }, type: "rightAligned", sort: "asc" },
      { field: "name", headerName: t("Orders:WaypointType"), flex: 2, minWidth: 160 },
    ],
    [readOnly, t],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "isArchived", "description"],
      render: (form) => (
        <FormSection title={t("Orders:TransportOrderType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "stops",
      labelKey: "Orders:DefaultWaypoints",
      fields: ["defaultWaypoints"],
      render: (form) => {
        const addStop = (waypointTypeId: string, name: string) => {
          const lines = ordered(form.values.defaultWaypoints);
          if (lines.length >= MAX_STOPS) return;
          form.set("defaultWaypoints", [...lines, { key: newKey(), waypointTypeId, name, position: lines.length + 1 }]);
          form.blur("defaultWaypoints");
        };
        return (
          <FormSection title={t("Orders:DefaultWaypoints")}>
            <div className="flex h-60 flex-col gap-2">
              {form.error("defaultWaypoints") && <p className="text-label text-destructive">{form.error("defaultWaypoints")}</p>}
              <LazyLinesGrid<StopLine>
                rows={form.values.defaultWaypoints}
                onRowsChange={(rows) => form.set("defaultWaypoints", rows)}
                getRowId={(row) => row.key}
                columnDefs={stopColumns}
                readOnly={form.readOnly}
                toolbar={
                  form.values.defaultWaypoints.length < MAX_STOPS ? (
                    <>
                      <label htmlFor={form.id("defaultWaypoints")} className="sr-only">
                        {t("Orders:AddDefaultWaypoint")}
                      </label>
                      <ReferenceCombobox
                        /* A picked type becomes the next stop; the picker empties again for the one after. */
                        key={form.values.defaultWaypoints.length}
                        id={form.id("defaultWaypoints")}
                        value={null}
                        onChange={(next) => {
                          if (next) addStop(next, typeOptions.find((o) => o.value === next)?.label ?? "");
                        }}
                        options={typeOptions}
                        placeholder={t("Orders:AddDefaultWaypoint")}
                        className="w-72"
                        disabled={form.readOnly}
                        {...inPlace.offer("WaypointType", form.readOnly, (r) => addStop(r.id, r.name))}
                      />
                    </>
                  ) : undefined
                }
              />
            </div>
          </FormSection>
        );
      },
    },
    {
      value: "required",
      labelKey: "Orders:RequiredFields",
      fields: ["requiredFields"],
      render: (form) => (
        <FormSection title={t("Orders:RequiredFields")}>
          <p className="mb-2 text-label text-muted-foreground">{t("Orders:RequiredFieldsHint")}</p>
          <div className="grid grid-cols-2 gap-x-4 gap-y-1">
            {REQUIRED_FIELDS.map(({ flag, key }) => (
              <label key={flag} className="flex h-8 items-center gap-2 text-sm" htmlFor={form.id(`required-${flag}`)}>
                <Checkbox
                  id={form.id(`required-${flag}`)}
                  checked={(form.values.requiredFields & flag) !== 0}
                  disabled={form.readOnly}
                  onCheckedChange={(next) =>
                    form.set("requiredFields", next === true ? form.values.requiredFields | flag : form.values.requiredFields & ~flag)
                  }
                />
                {t(key)}
              </label>
            ))}
          </div>
        </FormSection>
      ),
    },
    {
      value: "numbering",
      labelKey: "MasterData:Numbering",
      fields: [...NUMBERING_FIELDS],
      render: (form) => <NumberingFields form={form} />,
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, TransportOrderTypeDto>
        entityKey="TransportOrderType"
        resource="Orders"
        queryEntity={transportOrderTypes.entity}
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
      {inPlace.dialog}
    </>
  );
}
