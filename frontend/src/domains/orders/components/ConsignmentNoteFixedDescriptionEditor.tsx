import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Textarea } from "@/components/ui/textarea";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { consignmentNoteFixedDescriptions, MAX_FIXED_DESCRIPTION_TEXT_LENGTH, type ConsignmentNoteFixedDescriptionDto } from "@/domains/orders/consignment-note-fixed-descriptions";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The CMR fixed text editor (design 61, Master data lists): `md`; General (name, the text the CMR prints, archived),
 * Remarks, Record identification. */

interface Values {
  name: string;
  text: string;
  isArchived: boolean;
  description: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: ConsignmentNoteFixedDescriptionDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  text: dto?.text ?? "",
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  if (!v.text.trim()) errors.text = "MasterData:Required";
  return errors;
};

export function ConsignmentNoteFixedDescriptionEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const detail = consignmentNoteFixedDescriptions.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = consignmentNoteFixedDescriptions.useCreate();
  const update = consignmentNoteFixedDescriptions.useUpdate();
  const remove = consignmentNoteFixedDescriptions.useDelete();
  const canDelete = useIsGranted("Orders.ConsignmentNoteFixedDescriptions.Delete");

  const save = useCallback(
    async (v: Values, dto: ConsignmentNoteFixedDescriptionDto | undefined) => {
      const body = { name: v.name.trim(), text: v.text.trim(), isArchived: v.isArchived, description: v.description.trim() || null };
      return mode === "create" ? await create.mutateAsync(body) : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "text", "isArchived"],
      render: (form) => (
        <FormSection title={t("Orders:ConsignmentNoteFixedDescription")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            <FormField label={t("Orders:Text")} htmlFor={form.id("text")} required error={form.error("text")} className="col-span-2" floating="always">
              <Textarea
                id={form.id("text")}
                value={form.values.text}
                maxLength={MAX_FIXED_DESCRIPTION_TEXT_LENGTH}
                rows={8}
                disabled={form.readOnly}
                onChange={(e) => form.set("text", e.target.value)}
                onBlur={() => form.blur("text")}
              />
            </FormField>
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
    <MasterDataEditor<Values, ConsignmentNoteFixedDescriptionDto>
      entityKey="ConsignmentNoteFixedDescription"
      resource="Orders"
      queryEntity={consignmentNoteFixedDescriptions.entity}
      size="md"
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
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
