import { useCallback, useMemo, useState } from "react";
import type { ColDef, EditableCallbackParams } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DATE_CELL_EDITOR_COL_DEF } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { referenceCellEditorColDef } from "@/components/ui/reference-cell-editor";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { TextAreaField, TextField, CheckboxField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { ValidFromCell, byDate, dateOnly, newLineKey, todayIso, validityError, type ValidityLine } from "@/apps/master-data/components/validity-rows";
import {
  ALL_DAYS,
  DIRECTIONS,
  REGION_APPLIES_AT,
  SIDES,
  VALUE_KINDS,
  WEEK_DAYS,
  asDaysOfWeek,
  discountsOrSurcharges,
  keyOf,
  needsCurrency,
  vatPercentages,
  type DiscountOrSurchargeDirection,
  type DiscountOrSurchargeDto,
  type DiscountOrSurchargeSide,
  type DiscountOrSurchargeValueKind,
  type RegionAppliesAt,
} from "@/domains/financial/reference-data";
import { currencies, geographicalRegions } from "@/domains/master-data/entities";
import { useFormat } from "@/lib/format";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The discount or surcharge editor (design 52, UI design): `lg`; General (code, name, direction, kind, side, days of
 * the week as seven toggles, VAT, ledger account, archived), Values (the validity rows, §3.4: a value that has taken
 * effect is read-only with a lock; the currency column only for fixed amounts and amounts per kilometre), Regions
 * (none = everywhere; each region once, with where the address must lie), Remarks, Record identification. The values
 * and regions are sent with the record and saved in its unit of work (assessment §6). */

interface ValueLine extends ValidityLine {
  value: number | null;
  currencyCode: string | null;
}

interface RegionLine {
  key: string;
  geographicalRegionId: string | null;
  appliesAt: RegionAppliesAt;
}

interface Values {
  code: string;
  name: string;
  direction: DiscountOrSurchargeDirection;
  valueKind: DiscountOrSurchargeValueKind;
  side: DiscountOrSurchargeSide;
  daysOfWeek: number;
  vatPercentageId: string | null;
  ledgerAccount: string;
  isArchived: boolean;
  description: string;
  values: ValueLine[];
  regions: RegionLine[];
}

const initialValuesFor = (initialName: string | undefined) => (dto: DiscountOrSurchargeDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto?.name ?? initialName ?? "",
  direction: dto?.direction ?? 1,
  valueKind: dto?.valueKind ?? 1,
  side: dto?.side ?? 1,
  daysOfWeek: Number(dto?.daysOfWeek ?? ALL_DAYS),
  vatPercentageId: dto?.vatPercentageId ?? null,
  ledgerAccount: dto?.ledgerAccount ?? "",
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  values: (dto?.values ?? []).map((v) => ({
    key: `${v.validFrom}`,
    validFrom: v.validFrom ?? null,
    value: v.value ?? null,
    currencyCode: v.currencyCode ?? null,
    isFixed: v.isFixed ?? false,
  })),
  regions: (dto?.regions ?? []).map((r) => ({ key: r.geographicalRegionId ?? newLineKey(), geographicalRegionId: r.geographicalRegionId ?? null, appliesAt: r.appliesAt ?? 3 })),
});

/** ≥ 0 with at most four decimals (numeric(18,4)); a percentage at most 1,000. */
const validValue = (value: number | null, kind: DiscountOrSurchargeValueKind) =>
  value != null && Number.isFinite(value) && value >= 0 && Math.abs(Math.round(value * 10000) - value * 10000) < 1e-6 && (kind !== 2 || value <= 1000);

export function DiscountOrSurchargeEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const detail = discountsOrSurcharges.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = discountsOrSurcharges.useCreate();
  const update = discountsOrSurcharges.useUpdate();
  const remove = discountsOrSurcharges.useDelete();
  const canDelete = useIsGranted("Financial.DiscountsOrSurcharges.Delete");
  const { offer, dialog: createDialog } = useCreateInPlace();
  /* Records created from a grid cell reach their row through applyValues (the columns live outside the tabs). */
  const [applyValues, setApplyValues] = useState<{ key: number; update: (values: Values) => Values } | undefined>();

  const vatLookup = vatPercentages.useLookup(open);
  const currencyLookup = currencies.useLookup(open);
  const regionLookup = geographicalRegions.useLookup(open);

  const vatOptions = useMemo(() => {
    const options = (vatLookup.data ?? []).map((v) => ({ value: v.id ?? "", label: v.name ?? v.code ?? "" }));
    const current = detail.data;
    if (current?.vatPercentageId && !options.some((o) => o.value === current.vatPercentageId)) {
      options.push({ value: current.vatPercentageId, label: current.vatPercentageName ?? "" });
    }
    return options;
  }, [vatLookup.data, detail.data]);
  /* Values refer to their currency by ISO code (assessment §3.7); the lookup lists active currencies only. */
  const currencyOptions = useMemo(
    () => (currencyLookup.data ?? []).map((c) => ({ value: c.code ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })).filter((o) => o.value),
    [currencyLookup.data],
  );
  const regionOptions = useMemo(() => (regionLookup.data ?? []).map((r) => ({ value: r.id ?? "", label: r.name ?? "" })), [regionLookup.data]);
  const regionLabels = useMemo(() => {
    const labels = new Map(regionOptions.map((o) => [o.value, o.label]));
    for (const r of detail.data?.regions ?? []) if (r.geographicalRegionId && !labels.has(r.geographicalRegionId)) labels.set(r.geographicalRegionId, r.name ?? "");
    return labels;
  }, [regionOptions, detail.data]);

  const intoRow = useCallback(
    (change: (values: Values) => Values) => setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update: change })),
    [],
  );
  const createCurrencyFor = useCallback(
    (term: string, row: ValueLine) =>
      offer("Currency", readOnly, (r) => intoRow((v) => ({ ...v, values: v.values.map((x) => (x.key === row.key ? { ...x, currencyCode: r.code } : x)) }))).onCreateNew?.(term),
    [offer, readOnly, intoRow],
  );
  const createRegionFor = useCallback(
    (term: string, row: RegionLine) =>
      offer("GeographicalRegion", readOnly, (r) => intoRow((v) => ({ ...v, regions: v.regions.map((x) => (x.key === row.key ? { ...x, geographicalRegionId: r.id } : x)) }))).onCreateNew?.(term),
    [offer, readOnly, intoRow],
  );
  const canCreateCurrency = offer("Currency", readOnly, () => undefined).onCreateNew != null;
  const canCreateRegion = offer("GeographicalRegion", readOnly, () => undefined).onCreateNew != null;

  const validate = useCallback(
    (v: Values): FieldErrors<Values> => {
      const errors: FieldErrors<Values> = {};
      if (!v.code.trim()) errors.code = "MasterData:Required";
      if (!v.name.trim()) errors.name = "MasterData:Required";
      if ((v.daysOfWeek & ALL_DAYS) === 0) errors.daysOfWeek = "Financial:DaysOfWeekRequiredHint";
      const values =
        validityError(v.values, mode) ??
        (v.values.some((l) => !validValue(l.value, v.valueKind))
          ? v.valueKind === 2
            ? "Financial:DiscountPercentageRangeHint"
            : "Financial:ValueRangeHint"
          : needsCurrency(v.valueKind) && v.values.some((l) => !l.currencyCode)
            ? "Financial:CurrencyRequiredHint"
            : undefined);
      if (values) errors.values = values;
      const regionIds = v.regions.map((r) => r.geographicalRegionId);
      if (regionIds.some((r) => !r)) errors.regions = "MasterData:Required";
      else if (new Set(regionIds).size !== regionIds.length) errors.regions = "Financial:DuplicateRegionHint";
      return errors;
    },
    [mode],
  );

  const save = useCallback(
    async (v: Values, dto: DiscountOrSurchargeDto | undefined) => {
      const currency = needsCurrency(v.valueKind);
      const body = {
        code: v.code.trim().toUpperCase(),
        name: v.name.trim(),
        direction: v.direction,
        valueKind: v.valueKind,
        side: v.side,
        daysOfWeek: asDaysOfWeek(v.daysOfWeek),
        vatPercentageId: v.vatPercentageId,
        ledgerAccount: v.ledgerAccount.trim() || null,
        values: byDate(v.values).map((l) => ({
          validFrom: dateOnly(l.validFrom)!,
          value: l.value ?? 0,
          /* A fixed row goes back exactly as it is; a new or future row follows the kind. */
          currencyCode: l.isFixed ? l.currencyCode : currency ? l.currencyCode : null,
        })),
        regions: v.regions.map((r) => ({ geographicalRegionId: r.geographicalRegionId!, appliesAt: r.appliesAt })),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  /* Two column sets: with the currency column (fixed amounts, per kilometre) and without it (percentages). */
  const valueColumns = useMemo(() => {
    const editable = (p: EditableCallbackParams<ValueLine>) => !readOnly && !p.data?.isFixed;
    const build = (kind: DiscountOrSurchargeValueKind): ColDef<ValueLine>[] => [
      {
        field: "validFrom",
        headerName: t("MasterData:ValidFrom"),
        flex: 1,
        minWidth: 150,
        editable,
        ...DATE_CELL_EDITOR_COL_DEF,
        cellRenderer: ValidFromCell,
        cellRendererParams: { tooltipKey: "Financial:FixedValueTooltip" },
        sort: "asc",
      },
      {
        field: "value",
        headerName: kind === 2 ? `${t("Financial:Value")} (%)` : t("Financial:Value"),
        flex: 1,
        minWidth: 130,
        editable,
        cellEditor: "agNumberCellEditor",
        cellEditorParams: { min: 0, max: kind === 2 ? 1000 : undefined, precision: 4 },
        valueFormatter: (p) => f.number(p.value as number | null | undefined, { maxDecimals: 4 }),
        type: "rightAligned",
      },
      ...(needsCurrency(kind)
        ? [
            {
              field: "currencyCode",
              headerName: t("MasterData:Currency"),
              flex: 1,
              minWidth: 150,
              editable,
              ...referenceCellEditorColDef<ValueLine>({
                options: currencyOptions,
                onCreateNew: canCreateCurrency ? createCurrencyFor : undefined,
                createLabel: t("MasterData:AddNewCurrency"),
              }),
            } satisfies ColDef<ValueLine>,
          ]
        : []),
    ];
    return { money: build(1), perKilometre: build(3), percentage: build(2) };
  }, [readOnly, t, f, currencyOptions, canCreateCurrency, createCurrencyFor]);

  const appliesAtValues = useMemo(() => REGION_APPLIES_AT.map((a) => a.value), []);
  const regionColumns = useMemo<ColDef<RegionLine>[]>(
    () => [
      {
        field: "geographicalRegionId",
        headerName: t("MasterData:GeographicalRegion"),
        flex: 2,
        minWidth: 180,
        editable: !readOnly,
        ...referenceCellEditorColDef<RegionLine>({
          options: regionOptions,
          onCreateNew: canCreateRegion ? createRegionFor : undefined,
          createLabel: t("MasterData:AddNewGeographicalRegion"),
        }),
        valueFormatter: (p) => (p.value ? (regionLabels.get(p.value as string) ?? "") : ""),
      },
      {
        field: "appliesAt",
        headerName: t("Financial:AppliesAt"),
        flex: 2,
        minWidth: 180,
        editable: !readOnly,
        cellEditor: "agRichSelectCellEditor",
        cellEditorParams: { values: appliesAtValues, formatValue: (value: RegionAppliesAt) => t(keyOf(REGION_APPLIES_AT, value)) },
        valueFormatter: (p) => t(keyOf(REGION_APPLIES_AT, p.value as number | undefined)),
      },
    ],
    [readOnly, t, regionOptions, regionLabels, canCreateRegion, createRegionFor, appliesAtValues],
  );

  const directionItems = DIRECTIONS.map((d) => ({ value: d.value, label: t(d.key) }));
  const valueKindItems = VALUE_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));
  const sideItems = SIDES.map((s) => ({ value: s.value, label: t(s.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "direction", "valueKind", "side", "daysOfWeek", "vatPercentageId", "ledgerAccount", "isArchived"],
      render: (form) => (
        <FormSection title={t("Financial:DiscountOrSurcharge")}>
          <div className="grid grid-cols-3 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} className="col-span-2" />
            <FormField label={t("Financial:Direction")} htmlFor={form.id("direction")} required floating="always">
              <Select
                items={directionItems}
                value={form.values.direction}
                onValueChange={(next) => form.set("direction", (next as DiscountOrSurchargeDirection | null) ?? 1)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("direction")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {directionItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Financial:ValueKind")} htmlFor={form.id("valueKind")} required floating="always">
              <Select
                items={valueKindItems}
                value={form.values.valueKind}
                onValueChange={(next) => form.set("valueKind", (next as DiscountOrSurchargeValueKind | null) ?? 1)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("valueKind")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {valueKindItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Financial:Side")} htmlFor={form.id("side")} required floating="always">
              <Select
                items={sideItems}
                value={form.values.side}
                onValueChange={(next) => form.set("side", (next as DiscountOrSurchargeSide | null) ?? 1)}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("side")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {sideItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <div className="col-span-3 flex flex-col gap-1" role="group" aria-labelledby={form.id("daysOfWeek")}>
              <span id={form.id("daysOfWeek")} className="text-label text-muted-foreground">
                {t("Financial:DaysOfWeek")}
              </span>
              <div className="flex flex-wrap gap-1">
                {WEEK_DAYS.map((day) => {
                  const on = (form.values.daysOfWeek & day.flag) !== 0;
                  return (
                    <Button
                      key={day.flag}
                      type="button"
                      size="sm"
                      variant={on ? "default" : "outline"}
                      aria-pressed={on}
                      disabled={form.readOnly}
                      onClick={() => {
                        form.set("daysOfWeek", form.values.daysOfWeek ^ day.flag);
                        form.blur("daysOfWeek");
                      }}
                    >
                      {t(day.key)}
                    </Button>
                  );
                })}
              </div>
              {form.error("daysOfWeek") && <p className="text-label text-destructive">{form.error("daysOfWeek")}</p>}
            </div>
            <FormField label={t("Financial:Vat")} htmlFor={form.id("vatPercentageId")} floating="always">
              {/* DEVIATION(UI-LOOK-001): VAT categories use ReferenceCombobox over their lookup (under 500 per tenant), approved in docs/designs/52-financial-master-data.md */}
              <ReferenceCombobox
                id={form.id("vatPercentageId")}
                value={form.values.vatPercentageId}
                onChange={(next) => form.set("vatPercentageId", next)}
                options={vatOptions}
                placeholder={t("Financial:VatDefaultPlaceholder")}
                disabled={form.readOnly}
                {...offer("VatPercentage", form.readOnly, (r) => form.set("vatPercentageId", r.id))}
              />
            </FormField>
            <TextField form={form} field="ledgerAccount" labelKey="Financial:LedgerAccount" maxLength={32} />
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "values",
      labelKey: "Financial:Values",
      fields: ["values"],
      render: (form) => {
        const kind = form.values.valueKind;
        const columns = kind === 2 ? valueColumns.percentage : kind === 3 ? valueColumns.perKilometre : valueColumns.money;
        return (
          <div className="flex h-80 flex-col gap-2">
            {form.error("values") && <p className="text-label text-destructive">{form.error("values")}</p>}
            <LazyLinesGrid<ValueLine>
              /* The column set changes with the kind: a new grid instance for it. */
              key={`values-${kind}`}
              rows={form.values.values}
              onRowsChange={(rows) => {
                form.set("values", rows);
                form.blur("values");
              }}
              getRowId={(row) => row.key}
              columnDefs={columns}
              readOnly={form.readOnly}
              canRemoveRow={(row) => !row.isFixed}
              toolbar={
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    /* A new value continues in the currency of the latest row. */
                    const last = byDate(form.values.values).at(-1);
                    form.set("values", [
                      ...form.values.values,
                      { key: newLineKey(), validFrom: mode === "edit" ? null : todayIso(), value: null, currencyCode: needsCurrency(kind) ? (last?.currencyCode ?? null) : null, isFixed: false },
                    ]);
                  }}
                >
                  <Plus /> {t("Financial:AddValue")}
                </Button>
              }
            />
          </div>
        );
      },
    },
    {
      value: "regions",
      labelKey: "Financial:Regions",
      fields: ["regions"],
      render: (form) => (
        <div className="flex h-80 flex-col gap-2">
          <p className="text-label text-muted-foreground">{t("Financial:RegionsHint")}</p>
          {form.error("regions") && <p className="text-label text-destructive">{form.error("regions")}</p>}
          <LazyLinesGrid<RegionLine>
            rows={form.values.regions}
            onRowsChange={(rows) => {
              form.set("regions", rows);
              form.blur("regions");
            }}
            getRowId={(row) => row.key}
            columnDefs={regionColumns}
            readOnly={form.readOnly}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => form.set("regions", [...form.values.regions, { key: newLineKey(), geographicalRegionId: null, appliesAt: 3 }])}
              >
                <Plus /> {t("Financial:AddRegion")}
              </Button>
            }
          />
        </div>
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
    <>
      <MasterDataEditor<Values, DiscountOrSurchargeDto>
        entityKey="DiscountOrSurcharge"
        resource="Financial"
        queryEntity={discountsOrSurcharges.entity}
        size="lg"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.name ?? dto.code ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        applyValues={applyValues}
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
      />
      {createDialog}
    </>
  );
}

