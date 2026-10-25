import { useCallback, useMemo } from "react";

import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  MAX_TRIP_ATTRIBUTE_KEY_LENGTH,
  MAX_TRIP_ATTRIBUTE_POSITION,
  TRIP_ATTRIBUTE_KEY_SHAPE,
  tripAttributeDefinitions,
  type TripAttributeDefinitionDto,
} from "@/domains/planning-execution/trip-attribute-definitions";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The trip attribute editor (design 59, design 61): `md`; General (key, name, position, archived), Remarks, Record
 * identification. The key follows the server's shape (a letter, then letters, digits or underscores). The name is one
 * field in the user's language (PO 2026-10-11). */

interface Values {
  key: string;
  name: string;
  position: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: TripAttributeDefinitionDto | undefined): Values => ({
  key: dto?.key ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  position: numberToText(dto?.position ?? 1),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  const key = v.key.trim();
  if (!key) errors.key = "MasterData:Required";
  else if (!TRIP_ATTRIBUTE_KEY_SHAPE.test(key)) errors.key = "PlanningExecution:AttributeKeyShapeHint";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const position = parseInteger(v.position) == null ? "MasterData:Required" : numberError(v.position, { integer: true, min: 1, max: MAX_TRIP_ATTRIBUTE_POSITION });
  if (position) errors.position = position;
  return errors;
};

export function TripAttributeDefinitionEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = tripAttributeDefinitions.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = tripAttributeDefinitions.useCreate();
  const update = tripAttributeDefinitions.useUpdate();
  const remove = tripAttributeDefinitions.useDelete();
  const canDelete = useIsGranted("PlanningExecution.TripAttributeDefinitions.Delete");

  const save = useCallback(
    async (v: Values, dto: TripAttributeDefinitionDto | undefined) => {
      const body = {
        key: v.key.trim(),
        names: withTextForCulture(dto?.names, culture, v.name),
        position: parseInteger(v.position) ?? 1,
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create" ? await create.mutateAsync(body) : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["key", "name", "position", "isArchived"],
      render: (form) => (
        <FormSection title={t("PlanningExecution:TripAttributeDefinition")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="key" labelKey="PlanningExecution:Key" required maxLength={MAX_TRIP_ATTRIBUTE_KEY_LENGTH} hintKey="PlanningExecution:AttributeKeyShapeHint" />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <NumberField form={form} field="position" labelKey="PlanningExecution:Position" required />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
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
    <MasterDataEditor<Values, TripAttributeDefinitionDto>
      entityKey="TripAttributeDefinition"
      resource="PlanningExecution"
      queryEntity={tripAttributeDefinitions.entity}
      size="md"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => resolveLocalizedName(dto.names, culture) || dto.name || dto.key || ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
