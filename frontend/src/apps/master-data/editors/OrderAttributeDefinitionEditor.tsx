import { useCallback, useMemo } from "react";

import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, EnumSelectField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import {
  MAX_ATTRIBUTE_KEY_LENGTH,
  MAX_ORDER_ATTRIBUTE_POSITION,
  ORDER_ATTRIBUTE_TARGETS,
  orderAttributeDefinitions,
  type OrderAttributeDefinitionDto,
  type OrderAttributeTarget,
} from "@/domains/orders/order-attribute-definitions";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The order attribute editor (design 58 "Attribute definitions", design 61): `md`; General (applies to the order or
 * its lines, the key, the name, position, archived), Remarks, Record identification. The name is one field in the
 * user's language (PO 2026-10-11; the other languages' texts are kept), as every localized master-data name. */

interface Values {
  target: OrderAttributeTarget;
  key: string;
  name: string;
  position: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: OrderAttributeDefinitionDto | undefined): Values => ({
  target: dto?.target ?? 1,
  key: dto?.key ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  position: numberToText(dto?.position ?? 1),
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.key.trim()) errors.key = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const position = parseInteger(v.position) == null ? "MasterData:Required" : numberError(v.position, { integer: true, min: 1, max: MAX_ORDER_ATTRIBUTE_POSITION });
  if (position) errors.position = position;
  return errors;
};

export function OrderAttributeDefinitionEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = orderAttributeDefinitions.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = orderAttributeDefinitions.useCreate();
  const update = orderAttributeDefinitions.useUpdate();
  const remove = orderAttributeDefinitions.useDelete();
  const canDelete = useIsGranted("Orders.OrderAttributeDefinitions.Delete");
  const targetItems = useMemo(() => ORDER_ATTRIBUTE_TARGETS.map((o) => ({ value: o.value, label: t(o.key) })), [t]);

  const save = useCallback(
    async (v: Values, dto: OrderAttributeDefinitionDto | undefined) => {
      const body = {
        target: v.target,
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
      fields: ["target", "key", "name", "position", "isArchived"],
      render: (form) => (
        <FormSection title={t("Orders:OrderAttributeDefinition")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <EnumSelectField form={form} field="target" labelKey="Orders:AttributeTarget" items={targetItems} required />
            <TextField form={form} field="key" labelKey="Orders:AttributeKey" required maxLength={MAX_ATTRIBUTE_KEY_LENGTH} hintKey="Orders:AttributeKeyHint" />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <NumberField form={form} field="position" labelKey="Orders:Position" required />
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
    <MasterDataEditor<Values, OrderAttributeDefinitionDto>
      entityKey="OrderAttributeDefinition"
      resource="Orders"
      queryEntity={orderAttributeDefinitions.entity}
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
