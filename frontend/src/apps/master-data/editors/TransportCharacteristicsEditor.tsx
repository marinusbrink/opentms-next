import { useCallback } from "react";

import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { transportCharacteristics, type TransportCharacteristicsDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  code: string;
  name: string;
  description: string;
}

const initialValues = (dto: TransportCharacteristicsDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto?.name ?? "",
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  return errors;
};

export function TransportCharacteristicsEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = transportCharacteristics.useDetail(mode === "edit" ? id : undefined);
  const create = transportCharacteristics.useCreate();
  const update = transportCharacteristics.useUpdate();
  const remove = transportCharacteristics.useDelete();
  const canDelete = useIsGranted("MasterData.TransportCharacteristics.Delete");

  const save = useCallback(
    async (v: Values, dto: TransportCharacteristicsDto | undefined) => {
      const body = { code: v.code.trim().toUpperCase(), name: v.name.trim(), description: v.description.trim() || null };
      if (mode === "create") {
        await create.mutateAsync(body);
      } else {
        await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
      }
    },
    [create, update, mode, id],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:TransportCharacteristic")}>
          {/* Rows per the PO (2026-10-11): code (a quarter) + name (three quarters); description. */}
          <div className="grid grid-cols-4 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={32} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} className="col-span-3" />
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-4" />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, TransportCharacteristicsDto>
      entityKey="TransportCharacteristics"
      queryEntity={transportCharacteristics.entity}
      size="sm"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => dto.code ?? ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
