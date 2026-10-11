import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { NumberField, TextAreaField, TextField, numberError, numberToText, parseNumber } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { VEHICLE_TYPE_KINDS, transportCharacteristics, vehicleTypes, type VehicleTypeDto, type VehicleTypeKind } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface Values {
  name: string;
  kind: VehicleTypeKind;
  description: string;
  maxEpq: string;
  maxLoadingMeters: string;
  defaultLengthCm: string;
  defaultWidthCm: string;
  defaultHeightCm: string;
  defaultGrossVehicleWeightKg: string;
  transportCharacteristicsIds: string[];
}

const NUMBER_FIELDS = ["maxEpq", "maxLoadingMeters", "defaultLengthCm", "defaultWidthCm", "defaultHeightCm", "defaultGrossVehicleWeightKg"] as const;

const initialValues = (dto: VehicleTypeDto | undefined): Values => ({
  name: dto?.name ?? "",
  kind: dto?.kind ?? 1,
  description: dto?.description ?? "",
  maxEpq: numberToText(dto?.maxEpq),
  maxLoadingMeters: numberToText(dto?.maxLoadingMeters),
  defaultLengthCm: numberToText(dto?.defaultLengthCm),
  defaultWidthCm: numberToText(dto?.defaultWidthCm),
  defaultHeightCm: numberToText(dto?.defaultHeightCm),
  defaultGrossVehicleWeightKg: numberToText(dto?.defaultGrossVehicleWeightKg),
  transportCharacteristicsIds: (dto?.transportCharacteristics ?? []).map((i) => i.id ?? "").filter(Boolean),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  for (const field of NUMBER_FIELDS) {
    const error = numberError(v[field], { min: 0 });
    if (error) errors[field] = error;
  }
  return errors;
};

export function VehicleTypeEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = vehicleTypes.useDetail(mode === "edit" ? id : undefined);
  const create = vehicleTypes.useCreate();
  const update = vehicleTypes.useUpdate();
  const remove = vehicleTypes.useDelete();
  const canDelete = useIsGranted("MasterData.VehicleTypes.Delete");
  const lookup = transportCharacteristics.useLookup(open);
  const options = useMemo(
    () => (lookup.data ?? []).map((i) => ({ id: i.id ?? "", label: `${i.code ?? ""} ${i.name ?? ""}`.trim() })),
    [lookup.data],
  );
  const kindItems = VEHICLE_TYPE_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));

  const save = useCallback(
    async (v: Values, dto: VehicleTypeDto | undefined) => {
      const body = {
        name: v.name.trim(),
        kind: v.kind,
        description: v.description.trim() || null,
        maxEpq: parseNumber(v.maxEpq),
        maxLoadingMeters: parseNumber(v.maxLoadingMeters),
        defaultLengthCm: parseNumber(v.defaultLengthCm),
        defaultWidthCm: parseNumber(v.defaultWidthCm),
        defaultHeightCm: parseNumber(v.defaultHeightCm),
        defaultGrossVehicleWeightKg: parseNumber(v.defaultGrossVehicleWeightKg),
        transportCharacteristicsIds: v.transportCharacteristicsIds,
      };
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
      fields: ["name", "kind", "description", ...NUMBER_FIELDS],
      render: (form) => (
        <FormSection title={t("MasterData:VehicleType")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("MasterData:Kind")} htmlFor={form.id("kind")} required floating="always">
              <Select items={kindItems} value={form.values.kind} onValueChange={(next) => form.set("kind", (next ?? 1) as VehicleTypeKind)} disabled={form.readOnly}>
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
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} className="col-span-2" />
            <NumberField form={form} field="maxEpq" labelKey="MasterData:MaxEpq" />
            <NumberField form={form} field="maxLoadingMeters" labelKey="MasterData:MaxLoadingMeters" />
            <NumberField form={form} field="defaultLengthCm" labelKey="MasterData:DefaultLengthCm" />
            <NumberField form={form} field="defaultWidthCm" labelKey="MasterData:DefaultWidthCm" />
            <NumberField form={form} field="defaultHeightCm" labelKey="MasterData:DefaultHeightCm" />
            <NumberField form={form} field="defaultGrossVehicleWeightKg" labelKey="MasterData:DefaultGrossVehicleWeightKg" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "characteristics",
      labelKey: "MasterData:Characteristics",
      fields: ["transportCharacteristicsIds"],
      render: (form) => (
        <FormSection title={t("MasterData:Characteristics")}>
          <FormField label={t("MasterData:TransportCharacteristics")} htmlFor={form.id("transportCharacteristicsIds")} floating="always">
            <MultiSelect
              id={form.id("transportCharacteristicsIds")}
              value={form.values.transportCharacteristicsIds}
              onChange={(next) => form.set("transportCharacteristicsIds", next)}
              options={options}
              disabled={form.readOnly}
            />
          </FormField>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, VehicleTypeDto>
      entityKey="VehicleType"
      queryEntity={vehicleTypes.entity}
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
