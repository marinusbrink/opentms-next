import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseNumber } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { temperatures, type TemperatureDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  description: string;
  minCelsius: string;
  maxCelsius: string;
  isArchived: boolean;
  incompatibleTemperatureIds: string[];
}

const initialValuesFor = (initialName: string | undefined) => (dto: TemperatureDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  description: dto?.description ?? "",
  minCelsius: numberToText(dto?.minCelsius),
  maxCelsius: numberToText(dto?.maxCelsius),
  isArchived: dto?.isArchived ?? false,
  incompatibleTemperatureIds: (dto?.incompatibleTemperatures ?? []).map((i) => i.id ?? "").filter(Boolean),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  const min = numberError(v.minCelsius);
  const max = numberError(v.maxCelsius);
  if (min) errors.minCelsius = min;
  if (max) errors.maxCelsius = max;
  const minValue = parseNumber(v.minCelsius);
  const maxValue = parseNumber(v.maxCelsius);
  if (!min && !max && minValue != null && maxValue != null && minValue > maxValue) errors.maxCelsius = "MasterData:MinAboveMax";
  return errors;
};

export function TemperatureEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const inPlace = useCreateInPlace();
  const detail = temperatures.useDetail(mode === "edit" ? id : undefined);
  const create = temperatures.useCreate();
  const update = temperatures.useUpdate();
  const remove = temperatures.useDelete();
  const canDelete = useIsGranted("MasterData.Temperatures.Delete");
  const lookup = temperatures.useLookup(open);
  const options = useMemo(
    () => (lookup.data ?? []).filter((i) => i.id !== id).map((i) => ({ id: i.id ?? "", label: i.name ?? "" })),
    [lookup.data, id],
  );

  const save = useCallback(
    async (v: Values, dto: TemperatureDto | undefined) => {
      const body = {
        name: v.name.trim(),
        description: v.description.trim() || null,
        minCelsius: parseNumber(v.minCelsius),
        maxCelsius: parseNumber(v.maxCelsius),
        isArchived: v.isArchived,
        incompatibleTemperatureIds: v.incompatibleTemperatureIds,
      };
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
      fields: ["name", "description", "minCelsius", "maxCelsius"],
      render: (form) => (
        <FormSection title={t("MasterData:Temperature")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} className="col-span-2" />
            <NumberField form={form} field="minCelsius" labelKey="MasterData:MinCelsius" />
            <NumberField form={form} field="maxCelsius" labelKey="MasterData:MaxCelsius" />
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "incompatibilities",
      labelKey: "MasterData:DoNotCombineWith",
      fields: ["incompatibleTemperatureIds"],
      render: (form) => (
        <FormSection title={t("MasterData:DoNotCombineWith")}>
          <FormField label={t("MasterData:Temperatures")} htmlFor={form.id("incompatibleTemperatureIds")} floating="always">
            <MultiSelect
              id={form.id("incompatibleTemperatureIds")}
              value={form.values.incompatibleTemperatureIds}
              onChange={(next) => form.set("incompatibleTemperatureIds", next)}
              options={options}
              disabled={form.readOnly}
              {...inPlace.offer("Temperature", form.readOnly, (r) =>
                form.update((v) => ({ ...v, incompatibleTemperatureIds: withId(v.incompatibleTemperatureIds, r.id) })),
              )}
            />
          </FormField>
        </FormSection>
      ),
    },
  ];

  return (
    <>
    <MasterDataEditor<Values, TemperatureDto>
      entityKey="Temperature"
      queryEntity={temperatures.entity}
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
    {inPlace.dialog}
    </>
  );
}
