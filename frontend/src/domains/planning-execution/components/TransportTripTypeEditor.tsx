import { useCallback, useMemo } from "react";

import { Badge } from "@/components/ui/badge";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextAreaField, TextField, parseInteger } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import {
  NUMBERING_FIELDS,
  NumberingFields,
  numberingDtoOf,
  numberingValuesOf,
  validateNumbering,
  type NumberingFormValues,
} from "@/components/record-editor/numbering-fields";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  TRIP_EXECUTIONS,
  transportTripTypes,
  type TransportTripTypeDto,
  type TripExecution,
} from "@/domains/planning-execution/reference-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values extends NumberingFormValues {
  code: string;
  name: string;
  defaultExecution: TripExecution;
  sendToFleetManagementSystem: boolean;
  isArchived: boolean;
  description: string;
}

/* A new type starts as the seeded one does (design 53): TR, year, "-", five digits. */
const NEW_NUMBERING = { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 };

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: TransportTripTypeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  defaultExecution: dto?.defaultExecution ?? 1,
  sendToFleetManagementSystem: dto?.sendToFleetManagementSystem ?? true,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  ...numberingValuesOf(dto?.numbering, dto?.nextNumber, NEW_NUMBERING),
});

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
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const executionItems = TRIP_EXECUTIONS.map((e) => ({ value: e.value, label: t(e.key) }));

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
