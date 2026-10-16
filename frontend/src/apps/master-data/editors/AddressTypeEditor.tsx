import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { ADDRESS_TYPE_KINDS, addressTypes, type AddressTypeDto, type AddressTypeKind } from "@/domains/master-data/entities";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  kind: AddressTypeKind;
  description: string;
}

/* One Name field in the user's language (PO, 2026-10-11), as in the currency and country editors. */
const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: AddressTypeDto | undefined): Values => ({
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  kind: dto?.kind ?? 0,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => (v.name.trim() ? {} : { name: "MasterData:Required" });

export function AddressTypeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = addressTypes.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = addressTypes.useCreate();
  const update = addressTypes.useUpdate();
  const remove = addressTypes.useDelete();
  const canDelete = useIsGranted("MasterData.AddressTypes.Delete");

  const save = useCallback(
    async (v: Values, dto: AddressTypeDto | undefined) => {
      const body = { kind: v.kind, names: withTextForCulture(dto?.names, culture, v.name), description: v.description.trim() || null };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const kindItems = ADDRESS_TYPE_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "kind", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:AddressType")}>
          <div className="grid grid-cols-1 gap-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("MasterData:Kind")} htmlFor={form.id("kind")} required floating="always">
              <Select
                items={kindItems}
                value={form.values.kind}
                onValueChange={(next) => form.set("kind", (next ?? 0) as AddressTypeKind)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("kind")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {kindItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, AddressTypeDto>
      entityKey="AddressType"
      queryEntity={addressTypes.entity}
      size="sm"
      mode={mode}
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(dto) => resolveLocalizedName(dto.names, culture) || dto.name || ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={create.isPending || update.isPending}
      tabs={tabs}
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
