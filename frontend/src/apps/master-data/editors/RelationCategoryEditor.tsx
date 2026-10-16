import { useCallback, useMemo } from "react";

import { FormSection } from "@/components/ui/form-section";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { relationCategories, type RelationCategoryDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  description: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: RelationCategoryDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => (v.name.trim() ? {} : { name: "MasterData:Required" });

export function RelationCategoryEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const detail = relationCategories.useDetail(mode === "edit" ? id : undefined);
  const create = relationCategories.useCreate();
  const update = relationCategories.useUpdate();
  const remove = relationCategories.useDelete();
  const canDelete = useIsGranted("MasterData.RelationCategories.Delete");

  const save = useCallback(
    async (v: Values, dto: RelationCategoryDto | undefined) => {
      const body = { name: v.name.trim(), description: v.description.trim() || null };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:RelationCategory")}>
          <div className="grid grid-cols-1 gap-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, RelationCategoryDto>
      entityKey="RelationCategory"
      queryEntity={relationCategories.entity}
      size="sm"
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
