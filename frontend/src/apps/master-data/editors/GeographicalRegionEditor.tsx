import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { countries, geographicalRegions, type GeographicalRegionDto } from "@/domains/master-data/entities";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface RangeRow {
  key: string;
  id: string | null;
  countryId: string | null;
  fromNumeric: number | null;
  tillNumeric: number | null;
  prefix: string | null;
}

interface Values {
  name: string;
  description: string;
  postalCodeRanges: RangeRow[];
}

let rowCounter = 0;

const initialValues = (dto: GeographicalRegionDto | undefined): Values => ({
  name: dto?.name ?? "",
  description: dto?.description ?? "",
  postalCodeRanges: (dto?.postalCodeRanges ?? []).map((r) => ({
    key: r.id ?? `new-${++rowCounter}`,
    id: r.id ?? null,
    countryId: r.countryId ?? null,
    fromNumeric: r.fromNumeric ?? null,
    tillNumeric: r.tillNumeric ?? null,
    prefix: r.prefix ?? null,
  })),
});

/** The rule of the postal-code range (design 41): a country and either from+till (from ≤ till) or a prefix. */
export function isValidRange(r: RangeRow): boolean {
  if (!r.countryId) return false;
  const prefix = (r.prefix ?? "").trim();
  const hasNumeric = r.fromNumeric != null || r.tillNumeric != null;
  if (prefix) return !hasNumeric;
  return r.fromNumeric != null && r.tillNumeric != null && r.fromNumeric >= 0 && r.fromNumeric <= r.tillNumeric;
}

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "MasterData:Required";
  if (v.postalCodeRanges.some((r) => !isValidRange(r))) errors.postalCodeRanges = "MasterData:PostalCodeRangeInvalid";
  return errors;
};

export function GeographicalRegionEditor({ mode, id, readOnly, open, onClose, onSaved }: EntityEditorProps) {
  const { t } = useL();
  const detail = geographicalRegions.useDetail(mode === "edit" ? id : undefined);
  const create = geographicalRegions.useCreate();
  const update = geographicalRegions.useUpdate();
  const remove = geographicalRegions.useDelete();
  const canDelete = useIsGranted("MasterData.GeographicalRegions.Delete");
  const countryLookup = countries.useLookup(open);
  const countryLabels = useMemo(
    () => new Map((countryLookup.data ?? []).map((c) => [c.id ?? "", `${c.code ?? ""} ${c.name ?? ""}`.trim()])),
    [countryLookup.data],
  );
  const countryIds = useMemo(() => (countryLookup.data ?? []).map((c) => c.id ?? ""), [countryLookup.data]);

  const rangeColumns = useMemo<ColDef<RangeRow>[]>(
    () => [
      {
        field: "countryId",
        headerName: t("MasterData:Country"),
        flex: 2,
        minWidth: 180,
        editable: !readOnly,
        cellEditor: "agRichSelectCellEditor",
        cellEditorParams: { values: countryIds, formatValue: (value: string) => countryLabels.get(value) ?? "", allowTyping: true, filterList: true, searchType: "matchAny" },
        valueFormatter: (p) => (p.value ? (countryLabels.get(p.value as string) ?? "") : ""),
      },
      { field: "fromNumeric", headerName: t("MasterData:FromNumeric"), flex: 1, minWidth: 100, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { precision: 0, min: 0 }, type: "rightAligned" },
      { field: "tillNumeric", headerName: t("MasterData:TillNumeric"), flex: 1, minWidth: 100, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { precision: 0, min: 0 }, type: "rightAligned" },
      { field: "prefix", headerName: t("MasterData:Prefix"), flex: 1, minWidth: 110, editable: !readOnly, cellEditor: "agTextCellEditor", cellEditorParams: { maxLength: 16 } },
    ],
    [countryIds, countryLabels, readOnly, t],
  );

  const save = useCallback(
    async (v: Values, dto: GeographicalRegionDto | undefined) => {
      const body = {
        name: v.name.trim(),
        description: v.description.trim() || null,
        postalCodeRanges: v.postalCodeRanges.map((r) => ({
          id: r.id,
          countryId: r.countryId!,
          fromNumeric: (r.prefix ?? "").trim() ? null : r.fromNumeric,
          tillNumeric: (r.prefix ?? "").trim() ? null : r.tillNumeric,
          prefix: (r.prefix ?? "").trim() || null,
        })),
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
      fields: ["name", "description"],
      render: (form) => (
        <FormSection title={t("MasterData:GeographicalRegion")}>
          <div className="grid grid-cols-1 gap-3">
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <TextAreaField form={form} field="description" labelKey="MasterData:Description" maxLength={2500} />
          </div>
        </FormSection>
      ),
    },
    {
      value: "ranges",
      labelKey: "MasterData:PostalCodeRanges",
      fields: ["postalCodeRanges"],
      render: (form) => (
        <div className="flex h-80 flex-col gap-2">
          {form.error("postalCodeRanges") && <p className="text-label text-destructive">{form.error("postalCodeRanges")}</p>}
          <LazyLinesGrid<RangeRow>
            rows={form.values.postalCodeRanges}
            onRowsChange={(rows) => form.set("postalCodeRanges", rows)}
            getRowId={(row) => row.key}
            columnDefs={rangeColumns}
            readOnly={form.readOnly}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() =>
                  form.set("postalCodeRanges", [
                    ...form.values.postalCodeRanges,
                    { key: `new-${++rowCounter}`, id: null, countryId: null, fromNumeric: null, tillNumeric: null, prefix: null },
                  ])
                }
              >
                <Plus /> {t("MasterData:AddPostalCodeRange")}
              </Button>
            }
          />
        </div>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, GeographicalRegionDto>
      entityKey="GeographicalRegion"
      queryEntity={geographicalRegions.entity}
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
