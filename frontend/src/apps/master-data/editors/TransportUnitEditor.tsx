import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { MultiSelect } from "@/components/ui/multi-select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, NumberField, TextAreaField, TextField, numberError, numberToText, parseInteger, parseNumber } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { transportUnits, vehicleTypes, type TransportUnitDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface LoadFactorRow {
  key: string;
  id: string | null;
  vehicleTypeId: string | null;
  loadingCapacityFactor: number | null;
  maxCapacity: number | null;
}

interface Values {
  name: string;
  description: string;
  isArchived: boolean;
  lengthCm: string;
  widthCm: string;
  heightCm: string;
  tareWeightKg: string;
  maxLoadKg: string;
  isStackable: boolean;
  maxStackCount: string;
  loadingMeters: string;
  epqFactor: string;
  isExchangeable: boolean;
  loadFactors: LoadFactorRow[];
  incompatibleTransportUnitIds: string[];
}

const NUMBER_FIELDS = ["lengthCm", "widthCm", "heightCm", "tareWeightKg", "maxLoadKg", "loadingMeters", "epqFactor"] as const;

let rowCounter = 0;

const initialValues = (dto: TransportUnitDto | undefined): Values => ({
  name: dto?.name ?? "",
  description: dto?.description ?? "",
  isArchived: dto?.isArchived ?? false,
  lengthCm: numberToText(dto?.lengthCm),
  widthCm: numberToText(dto?.widthCm),
  heightCm: numberToText(dto?.heightCm),
  tareWeightKg: numberToText(dto?.tareWeightKg),
  maxLoadKg: numberToText(dto?.maxLoadKg),
  isStackable: dto?.isStackable ?? false,
  maxStackCount: numberToText(dto?.maxStackCount),
  loadingMeters: numberToText(dto?.loadingMeters),
  epqFactor: numberToText(dto?.epqFactor),
  isExchangeable: dto?.isExchangeable ?? false,
  loadFactors: (dto?.loadFactors ?? []).map((f) => ({
    key: f.id ?? `new-${++rowCounter}`,
    id: f.id ?? null,
    vehicleTypeId: f.vehicleTypeId ?? null,
    loadingCapacityFactor: f.loadingCapacityFactor ?? null,
    maxCapacity: f.maxCapacity ?? null,
  })),
  incompatibleTransportUnitIds: (dto?.incompatibleTransportUnits ?? []).map((i) => i.id ?? "").filter(Boolean),
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  for (const field of NUMBER_FIELDS) {
    const error = numberError(v[field], { min: 0 });
    if (error) errors[field] = error;
  }
  const stack = numberError(v.maxStackCount, { integer: true, min: 1, max: 100 });
  if (stack) errors.maxStackCount = stack;
  if (v.loadFactors.some((f) => !f.vehicleTypeId)) errors.loadFactors = "MasterData:LoadFactorVehicleTypeRequired";
  else if (new Set(v.loadFactors.map((f) => f.vehicleTypeId)).size !== v.loadFactors.length) errors.loadFactors = "MasterData:LoadFactorDuplicate";
  return errors;
};

export function TransportUnitEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = transportUnits.useDetail(mode === "edit" ? id : undefined);
  const create = transportUnits.useCreate();
  const update = transportUnits.useUpdate();
  const remove = transportUnits.useDelete();
  const canDelete = useIsGranted("MasterData.TransportUnits.Delete");
  const unitLookup = transportUnits.useLookup(open);
  const vehicleTypeLookup = vehicleTypes.useLookup(open);
  const unitOptions = useMemo(
    () => (unitLookup.data ?? []).filter((i) => i.id !== id).map((i) => ({ id: i.id ?? "", label: i.name ?? "" })),
    [unitLookup.data, id],
  );
  const vehicleTypeNames = useMemo(() => new Map((vehicleTypeLookup.data ?? []).map((v) => [v.id ?? "", v.name ?? ""])), [vehicleTypeLookup.data]);
  const vehicleTypeIds = useMemo(() => (vehicleTypeLookup.data ?? []).map((v) => v.id ?? ""), [vehicleTypeLookup.data]);

  const loadFactorColumns = useMemo<ColDef<LoadFactorRow>[]>(
    () => [
      {
        field: "vehicleTypeId",
        headerName: t("MasterData:VehicleType"),
        flex: 2,
        minWidth: 180,
        editable: !readOnly,
        cellEditor: "agRichSelectCellEditor",
        cellEditorParams: { values: vehicleTypeIds, formatValue: (value: string) => vehicleTypeNames.get(value) ?? "", allowTyping: true, filterList: true, searchType: "matchAny" },
        valueFormatter: (p) => (p.value ? (vehicleTypeNames.get(p.value as string) ?? "") : ""),
      },
      { field: "loadingCapacityFactor", headerName: t("MasterData:LoadingCapacityFactor"), flex: 1, minWidth: 140, editable: !readOnly, cellEditor: "agNumberCellEditor", type: "rightAligned" },
      { field: "maxCapacity", headerName: t("MasterData:MaxCapacity"), flex: 1, minWidth: 120, editable: !readOnly, cellEditor: "agNumberCellEditor", type: "rightAligned" },
    ],
    [readOnly, t, vehicleTypeIds, vehicleTypeNames],
  );

  const save = useCallback(
    async (v: Values, dto: TransportUnitDto | undefined) => {
      const body = {
        name: v.name.trim(),
        description: v.description.trim() || null,
        isArchived: v.isArchived,
        lengthCm: parseNumber(v.lengthCm),
        widthCm: parseNumber(v.widthCm),
        heightCm: parseNumber(v.heightCm),
        tareWeightKg: parseNumber(v.tareWeightKg),
        maxLoadKg: parseNumber(v.maxLoadKg),
        isStackable: v.isStackable,
        maxStackCount: v.isStackable ? parseInteger(v.maxStackCount) : null,
        loadingMeters: parseNumber(v.loadingMeters),
        epqFactor: parseNumber(v.epqFactor),
        isExchangeable: v.isExchangeable,
        loadFactors: v.loadFactors.map((f) => ({
          id: f.id,
          vehicleTypeId: f.vehicleTypeId!,
          loadingCapacityFactor: f.loadingCapacityFactor,
          maxCapacity: f.maxCapacity,
        })),
        incompatibleTransportUnitIds: v.incompatibleTransportUnitIds,
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
      fields: ["name", "description", "maxStackCount", ...NUMBER_FIELDS],
      render: (form) => (
        /* Two topics, two sections (UI-FORM-005; PO 2026-10-11): the unit itself, and its dimensions and capacities. */
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:TransportUnit")}>
            <div className="grid grid-cols-1 gap-3">
              <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
              <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
              <CheckboxField form={form} field="isExchangeable" labelKey="MasterData:IsExchangeable" />
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            </div>
          </FormSection>
          <FormSection title={t("MasterData:DimensionsAndCapacities")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <NumberField form={form} field="lengthCm" labelKey="MasterData:LengthCm" />
              <NumberField form={form} field="widthCm" labelKey="MasterData:WidthCm" />
              <NumberField form={form} field="heightCm" labelKey="MasterData:HeightCm" />
              <NumberField form={form} field="tareWeightKg" labelKey="MasterData:TareWeightKg" />
              <NumberField form={form} field="maxLoadKg" labelKey="MasterData:MaxLoadKg" />
              <NumberField form={form} field="loadingMeters" labelKey="MasterData:LoadingMeters" />
              <NumberField form={form} field="epqFactor" labelKey="MasterData:EpqFactor" />
              <CheckboxField form={form} field="isStackable" labelKey="MasterData:IsStackable" />
              <NumberField form={{ ...form, readOnly: form.readOnly || !form.values.isStackable }} field="maxStackCount" labelKey="MasterData:MaxStackCount" />
            </div>
          </FormSection>
        </div>
      ),
    },
    {
      value: "load-factors",
      labelKey: "MasterData:LoadFactors",
      fields: ["loadFactors"],
      render: (form) => (
        <div className="flex h-80 flex-col gap-2">
          {form.error("loadFactors") && <p className="text-label text-destructive">{form.error("loadFactors")}</p>}
          <LazyLinesGrid<LoadFactorRow>
            rows={form.values.loadFactors}
            onRowsChange={(rows) => form.set("loadFactors", rows)}
            getRowId={(row) => row.key}
            columnDefs={loadFactorColumns}
            readOnly={form.readOnly}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() =>
                  form.set("loadFactors", [
                    ...form.values.loadFactors,
                    { key: `new-${++rowCounter}`, id: null, vehicleTypeId: null, loadingCapacityFactor: null, maxCapacity: null },
                  ])
                }
              >
                <Plus /> {t("MasterData:AddLoadFactor")}
              </Button>
            }
          />
        </div>
      ),
    },
    {
      value: "incompatibilities",
      labelKey: "MasterData:DoNotCombineWith",
      fields: ["incompatibleTransportUnitIds"],
      render: (form) => (
        <FormSection title={t("MasterData:DoNotCombineWith")}>
          <FormField label={t("MasterData:TransportUnits")} htmlFor={form.id("incompatibleTransportUnitIds")} floating="always">
            <MultiSelect
              id={form.id("incompatibleTransportUnitIds")}
              value={form.values.incompatibleTransportUnitIds}
              onChange={(next) => form.set("incompatibleTransportUnitIds", next)}
              options={unitOptions}
              disabled={form.readOnly}
            />
          </FormField>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, TransportUnitDto>
      entityKey="TransportUnit"
      queryEntity={transportUnits.entity}
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
