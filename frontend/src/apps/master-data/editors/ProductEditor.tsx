import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseNumber } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { products, type ProductDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  description: string;
  isArchived: boolean;
  isAdr: boolean;
  adrUnNumber: string;
  adrClass: string;
  adrPackingGroup: string;
  adrDescription: string;
  defaultQuantity: string;
  defaultWeightKg: string;
  defaultColli: string;
  defaultVolumeM3: string;
  defaultLoadingMeters: string;
  defaultEpq: string;
  defaultLengthCm: string;
  defaultWidthCm: string;
  defaultHeightCm: string;
  incompatibleProductIds: string[];
}

const NUMBER_FIELDS = [
  "defaultQuantity",
  "defaultWeightKg",
  "defaultColli",
  "defaultVolumeM3",
  "defaultLoadingMeters",
  "defaultEpq",
  "defaultLengthCm",
  "defaultWidthCm",
  "defaultHeightCm",
] as const;

const PACKING_GROUPS = ["", "I", "II", "III"];

const initialValues = (dto: ProductDto | undefined): Values => ({
  name: dto?.name ?? "",
  description: dto?.description ?? "",
  isArchived: dto?.isArchived ?? false,
  isAdr: dto?.isAdr ?? false,
  adrUnNumber: dto?.adrUnNumber ?? "",
  adrClass: dto?.adrClass ?? "",
  adrPackingGroup: dto?.adrPackingGroup ?? "",
  adrDescription: dto?.adrDescription ?? "",
  defaultQuantity: numberToText(dto?.defaultQuantity),
  defaultWeightKg: numberToText(dto?.defaultWeightKg),
  defaultColli: numberToText(dto?.defaultColli),
  defaultVolumeM3: numberToText(dto?.defaultVolumeM3),
  defaultLoadingMeters: numberToText(dto?.defaultLoadingMeters),
  defaultEpq: numberToText(dto?.defaultEpq),
  defaultLengthCm: numberToText(dto?.defaultLengthCm),
  defaultWidthCm: numberToText(dto?.defaultWidthCm),
  defaultHeightCm: numberToText(dto?.defaultHeightCm),
  incompatibleProductIds: (dto?.incompatibleProducts ?? []).map((i) => i.id ?? "").filter(Boolean),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  if (v.isAdr) {
    if (!/^\d{4}$/.test(v.adrUnNumber.trim())) errors.adrUnNumber = v.adrUnNumber.trim() ? "MasterData:InvalidUnNumber" : "MasterData:Required";
    if (!v.adrClass.trim()) errors.adrClass = "MasterData:Required";
  }
  for (const field of NUMBER_FIELDS) {
    const error = numberError(v[field], { min: 0 });
    if (error) errors[field] = error;
  }
  return errors;
};

export function ProductEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = products.useDetail(mode === "edit" ? id : undefined);
  const create = products.useCreate();
  const update = products.useUpdate();
  const remove = products.useDelete();
  const canDelete = useIsGranted("MasterData.Products.Delete");
  const lookup = products.useLookup(open);
  const options = useMemo(
    () => (lookup.data ?? []).filter((i) => i.id !== id).map((i) => ({ id: i.id ?? "", label: i.name ?? "" })),
    [lookup.data, id],
  );

  const save = useCallback(
    async (v: Values, dto: ProductDto | undefined) => {
      const body = {
        name: v.name.trim(),
        description: v.description.trim() || null,
        isArchived: v.isArchived,
        isAdr: v.isAdr,
        adrUnNumber: v.isAdr ? v.adrUnNumber.trim() : null,
        adrClass: v.isAdr ? v.adrClass.trim().toUpperCase() : null,
        adrPackingGroup: v.isAdr ? v.adrPackingGroup || null : null,
        adrDescription: v.isAdr ? v.adrDescription.trim() || null : null,
        defaultQuantity: parseNumber(v.defaultQuantity),
        defaultWeightKg: parseNumber(v.defaultWeightKg),
        defaultColli: parseNumber(v.defaultColli),
        defaultVolumeM3: parseNumber(v.defaultVolumeM3),
        defaultLoadingMeters: parseNumber(v.defaultLoadingMeters),
        defaultEpq: parseNumber(v.defaultEpq),
        defaultLengthCm: parseNumber(v.defaultLengthCm),
        defaultWidthCm: parseNumber(v.defaultWidthCm),
        defaultHeightCm: parseNumber(v.defaultHeightCm),
        incompatibleProductIds: v.incompatibleProductIds,
      };
      if (mode === "create") {
        await create.mutateAsync(body);
      } else {
        await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
      }
    },
    [create, update, mode, id],
  );

  const packingItems = PACKING_GROUPS.map((g) => ({ value: g, label: g || "–" }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["name", "description", ...NUMBER_FIELDS],
      render: (form) => (
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:Product")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
              <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:DefaultsPerLine")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <NumberField form={form} field="defaultQuantity" labelKey="MasterData:DefaultQuantity" />
              <NumberField form={form} field="defaultColli" labelKey="MasterData:DefaultColli" />
              <NumberField form={form} field="defaultWeightKg" labelKey="MasterData:DefaultWeightKg" />
              <NumberField form={form} field="defaultVolumeM3" labelKey="MasterData:DefaultVolumeM3" />
              <NumberField form={form} field="defaultLoadingMeters" labelKey="MasterData:DefaultLoadingMeters" />
              <NumberField form={form} field="defaultEpq" labelKey="MasterData:DefaultEpq" />
              <NumberField form={form} field="defaultLengthCm" labelKey="MasterData:DefaultLengthCm" />
              <NumberField form={form} field="defaultWidthCm" labelKey="MasterData:DefaultWidthCm" />
              <NumberField form={form} field="defaultHeightCm" labelKey="MasterData:DefaultHeightCm" />
            </div>
          </FormSection>
        </div>
      ),
    },
    {
      value: "adr",
      labelKey: "MasterData:Adr",
      fields: ["adrUnNumber", "adrClass", "adrPackingGroup", "adrDescription"],
      render: (form) => {
        const adrForm = { ...form, readOnly: form.readOnly || !form.values.isAdr };
        return (
          <FormSection title={t("MasterData:Adr")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <CheckboxField form={form} field="isAdr" labelKey="MasterData:IsAdr" className="col-span-2" />
              <TextField form={adrForm} field="adrUnNumber" labelKey="MasterData:AdrUnNumber" required={form.values.isAdr} maxLength={4} />
              <TextField form={adrForm} field="adrClass" labelKey="MasterData:AdrClass" required={form.values.isAdr} maxLength={8} upper />
              <FormField label={t("MasterData:AdrPackingGroup")} htmlFor={form.id("adrPackingGroup")} floating="always">
                <Select
                  items={packingItems}
                  value={form.values.adrPackingGroup}
                  onValueChange={(next) => form.set("adrPackingGroup", next ?? "")}
                  disabled={adrForm.readOnly}
                >
                  <SelectTrigger id={form.id("adrPackingGroup")} className="h-10 w-full bg-background">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {packingItems.map((item) => (
                      <SelectItem key={item.value} value={item.value}>
                        {item.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </FormField>
              <TextAreaField form={adrForm} field="adrDescription" labelKey="MasterData:AdrDescription" maxLength={256} className="col-span-2" />
            </div>
          </FormSection>
        );
      },
    },
    {
      value: "incompatibilities",
      labelKey: "MasterData:DoNotCombineWith",
      fields: ["incompatibleProductIds"],
      render: (form) => (
        <FormSection title={t("MasterData:DoNotCombineWith")}>
          <FormField label={t("MasterData:Products")} htmlFor={form.id("incompatibleProductIds")} floating="always">
            <MultiSelect
              id={form.id("incompatibleProductIds")}
              value={form.values.incompatibleProductIds}
              onChange={(next) => form.set("incompatibleProductIds", next)}
              options={options}
              disabled={form.readOnly}
            />
          </FormField>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, ProductDto>
      entityKey="Product"
      queryEntity={products.entity}
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
