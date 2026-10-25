import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DATE_CELL_EDITOR_COL_DEF } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { referenceCellEditorColDef } from "@/components/ui/reference-cell-editor";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, EnumSelectField, NumberField, numberError, numberToText, parseInteger } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors, type FormContext } from "@/apps/master-data/components/MasterDataEditor";
import { dateOnly, newLineKey } from "@/apps/master-data/components/validity-rows";
import { MULTIPLE_MATCHES, usePriceListLookup, type MultipleMatches, type RateSide } from "@/domains/financial/price-lists";
import { discountsOrSurcharges, vatPercentages } from "@/domains/financial/reference-data";
import {
  MAX_RELATION_DISCOUNTS_OR_SURCHARGES,
  MAX_RELATION_PRICE_LISTS,
  RELATION_FINANCIAL_SETTINGS_ENTITY,
  useRelationFinancialSettings,
  useUpsertRelationFinancialSettings,
  type RelationFinancialSettingsDto,
} from "@/domains/financial/relation-financial-settings";
import { currencies, relationLookupPage, type RelationOption } from "@/domains/master-data/entities";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The relation financial settings editor (design 57 §UI, design 61): `md`; General (the relation as an EntityLookup,
 * fixed once the settings exist; VAT, payment term, currency, fixed price by default, documents with the invoice),
 * Price lists (the revenue and the cost lists as lines in their order, each side's "several matching lists" rule),
 * Discounts and surcharges (the defaults with their validity), Record identification. The settings are created on the
 * first edit and upserted with the stamp afterwards; picking a relation that has settings already edits those. */

interface ListLine {
  key: string;
  priceListId: string | null;
}

interface DiscountLine {
  key: string;
  discountOrSurchargeId: string | null;
  validFrom: string | null;
  validTill: string | null;
}

interface Values {
  relation: RelationOption | null;
  revenuePriceLists: ListLine[];
  costPriceLists: ListLine[];
  revenueMultipleMatches: MultipleMatches;
  costMultipleMatches: MultipleMatches;
  discountsOrSurcharges: DiscountLine[];
  vatPercentageId: string | null;
  paymentTermDays: string;
  chargeFixedPriceByDefault: boolean;
  sendFreightDocumentsWithInvoice: boolean;
  currencyCode: string | null;
}

export interface RelationFinancialSettingsEditorProps extends EntityEditorProps {
  /** Edit mode: the relation whose settings open (the list's rows are keyed by the settings id, the record by the relation). */
  relationId?: string;
}

const toRelation = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

const valuesOf = (dto: RelationFinancialSettingsDto | undefined): Values => ({
  relation: toRelation(dto?.relationId, dto?.relationName),
  revenuePriceLists: (dto?.revenuePriceLists ?? []).map((l) => ({ key: newLineKey(), priceListId: l.priceListId ?? null })),
  costPriceLists: (dto?.costPriceLists ?? []).map((l) => ({ key: newLineKey(), priceListId: l.priceListId ?? null })),
  revenueMultipleMatches: dto?.revenueMultipleMatches ?? 1,
  costMultipleMatches: dto?.costMultipleMatches ?? 1,
  discountsOrSurcharges: (dto?.discountsOrSurcharges ?? []).map((d) => ({
    key: newLineKey(),
    discountOrSurchargeId: d.discountOrSurchargeId ?? null,
    validFrom: d.validFrom ?? null,
    validTill: d.validTill ?? null,
  })),
  vatPercentageId: dto?.vatPercentageId ?? null,
  paymentTermDays: numberToText(dto?.paymentTermDays),
  chargeFixedPriceByDefault: dto?.chargeFixedPriceByDefault ?? false,
  sendFreightDocumentsWithInvoice: dto?.sendFreightDocumentsWithInvoice ?? false,
  currencyCode: dto?.currencyCode ?? null,
});

const listError = (lines: ListLine[]): string | undefined => {
  const ids = lines.map((l) => l.priceListId);
  if (ids.some((id) => !id)) return "MasterData:Required";
  if (new Set(ids).size !== ids.length) return "Financial:DuplicatePriceListHint";
  if (ids.length > MAX_RELATION_PRICE_LISTS) return "Financial:TooManyPriceListsHint";
  return undefined;
};

const validateFor =
  (mode: "create" | "edit") =>
  (v: Values): FieldErrors<Values> => {
    const errors: FieldErrors<Values> = {};
    if (mode === "create" && !v.relation) errors.relation = "MasterData:Required";
    const revenue = listError(v.revenuePriceLists);
    if (revenue) errors.revenuePriceLists = revenue;
    const cost = listError(v.costPriceLists);
    if (cost) errors.costPriceLists = cost;
    const lines = v.discountsOrSurcharges;
    if (lines.some((l) => !l.discountOrSurchargeId || !l.validFrom)) errors.discountsOrSurcharges = "MasterData:Required";
    else if (lines.some((l) => l.validTill && l.validFrom && l.validTill < l.validFrom)) errors.discountsOrSurcharges = "Financial:ValidTillBeforeFromHint";
    else if (lines.length > MAX_RELATION_DISCOUNTS_OR_SURCHARGES) errors.discountsOrSurcharges = "Financial:TooManyDiscountsOrSurchargesHint";
    const term = numberError(v.paymentTermDays, { integer: true, min: 0, max: 366 });
    if (term) errors.paymentTermDays = term;
    return errors;
  };

export function RelationFinancialSettingsEditor({ mode, relationId, readOnly, open, onClose, onSaved }: RelationFinancialSettingsEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const inPlace = useCreateInPlace();
  /* Create mode: the picked relation; its existing settings (if any) load and replace the form once. */
  const [pickedRelationId, setPickedRelationId] = useState<string | undefined>(undefined);
  const effectiveRelationId = mode === "edit" ? relationId : pickedRelationId;
  const settings = useRelationFinancialSettings(effectiveRelationId);
  const upsert = useUpsertRelationFinancialSettings();
  const [applyValues, setApplyValues] = useState<{ key: number; update: (values: Values) => Values } | undefined>();
  const apply = useCallback((update: (values: Values) => Values) => setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update })), []);

  const existing = mode === "edit" ? (settings.data ?? undefined) : pickedRelationId && settings.data ? settings.data : undefined;
  const appliedFor = useRef<string | null>(null);
  useEffect(() => {
    if (mode !== "create" || !pickedRelationId || settings.data === undefined) return;
    if (appliedFor.current === pickedRelationId) return;
    appliedFor.current = pickedRelationId;
    if (settings.data) {
      const loaded = settings.data;
      apply((v) => ({ ...valuesOf(loaded), relation: v.relation }));
    }
  }, [mode, pickedRelationId, settings.data, apply]);

  const detail = useMemo(
    () => ({ data: mode === "edit" ? (settings.data ?? undefined) : undefined, isLoading: mode === "edit" && settings.isLoading, error: settings.error }),
    [mode, settings.data, settings.isLoading, settings.error],
  );
  const initialValues = useCallback((dto: RelationFinancialSettingsDto | undefined) => valuesOf(dto), []);
  const validate = useMemo(() => validateFor(mode), [mode]);

  const revenueLookup = usePriceListLookup(1 as RateSide, open);
  const costLookup = usePriceListLookup(2 as RateSide, open);
  const discountLookup = discountsOrSurcharges.useLookup(open);
  const vatLookup = vatPercentages.useLookup(open);
  const currencyLookup = currencies.useLookup(open);

  const relationPage = useMemo(() => relationLookupPage(), []);
  const matchItems = useMemo(() => MULTIPLE_MATCHES.map((m) => ({ value: m.value, label: t(m.key) })), [t]);
  const vatOptions = useMemo(() => {
    const options = (vatLookup.data ?? []).map((v) => ({ value: v.id ?? "", label: v.name ?? v.code ?? "" }));
    if (existing?.vatPercentageId && !options.some((o) => o.value === existing.vatPercentageId)) options.push({ value: existing.vatPercentageId, label: existing.vatPercentageName ?? "" });
    return options;
  }, [vatLookup.data, existing]);
  const currencyOptions = useMemo(
    () => (currencyLookup.data ?? []).map((c) => ({ value: c.code ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })).filter((o) => o.value),
    [currencyLookup.data],
  );
  /* A list the lookup no longer returns (archived) keeps the stored code and name. */
  const listOptions = useCallback(
    (lookup: { id?: string; code?: string | null; name?: string | null }[] | undefined, stored: { priceListId?: string; code?: string | null; name?: string | null }[] | null | undefined) => {
      const options = (lookup ?? []).map((l) => ({ value: l.id ?? "", label: [l.code, l.name].filter(Boolean).join(" ") }));
      for (const s of stored ?? []) if (s.priceListId && !options.some((o) => o.value === s.priceListId)) options.push({ value: s.priceListId, label: [s.code, s.name].filter(Boolean).join(" ") });
      return options;
    },
    [],
  );
  const revenueOptions = useMemo(() => listOptions(revenueLookup.data, existing?.revenuePriceLists), [listOptions, revenueLookup.data, existing]);
  const costOptions = useMemo(() => listOptions(costLookup.data, existing?.costPriceLists), [listOptions, costLookup.data, existing]);
  const discountOptions = useMemo(() => {
    const options = (discountLookup.data ?? []).map((d) => ({ value: d.id ?? "", label: [d.code, d.name].filter(Boolean).join(" ") }));
    for (const d of existing?.discountsOrSurcharges ?? []) if (d.discountOrSurchargeId && !options.some((o) => o.value === d.discountOrSurchargeId)) options.push({ value: d.discountOrSurchargeId, label: d.name ?? "" });
    return options;
  }, [discountLookup.data, existing]);
  const labelOf = (options: { value: string; label: string }[]) => (value: unknown) => (value ? (options.find((o) => o.value === value)?.label ?? "") : "");

  const save = useCallback(
    async (v: Values) => {
      const targetRelationId = mode === "edit" ? relationId! : v.relation!.id;
      return await upsert.mutateAsync({
        relationId: targetRelationId,
        dto: {
          revenuePriceListIds: v.revenuePriceLists.map((l) => l.priceListId!),
          costPriceListIds: v.costPriceLists.map((l) => l.priceListId!),
          revenueMultipleMatches: v.revenueMultipleMatches,
          costMultipleMatches: v.costMultipleMatches,
          discountsOrSurcharges: v.discountsOrSurcharges.map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId!, validFrom: dateOnly(d.validFrom)!, validTill: dateOnly(d.validTill) })),
          vatPercentageId: v.vatPercentageId,
          paymentTermDays: parseInteger(v.paymentTermDays),
          chargeFixedPriceByDefault: v.chargeFixedPriceByDefault,
          sendFreightDocumentsWithInvoice: v.sendFreightDocumentsWithInvoice,
          currencyCode: v.currencyCode,
          /* Required once the settings exist (assessment §3.14); absent on the first edit. */
          concurrencyStamp: existing?.concurrencyStamp ?? null,
        },
      });
    },
    [upsert, mode, relationId, existing],
  );

  /* Records created from a grid cell reach their row through applyValues (the columns live outside the tabs). */
  const createPriceListFor = useCallback(
    (side: "revenuePriceLists" | "costPriceLists") => (term: string, row: ListLine) =>
      inPlace.offer("PriceList", readOnly, (r) => apply((v) => ({ ...v, [side]: v[side].map((x) => (x.key === row.key ? { ...x, priceListId: r.id } : x)) }))).onCreateNew?.(term),
    [inPlace, readOnly, apply],
  );
  const createDiscountFor = useCallback(
    (term: string, row: DiscountLine) =>
      inPlace.offer("DiscountOrSurcharge", readOnly, (r) => apply((v) => ({ ...v, discountsOrSurcharges: v.discountsOrSurcharges.map((x) => (x.key === row.key ? { ...x, discountOrSurchargeId: r.id } : x)) }))).onCreateNew?.(term),
    [inPlace, readOnly, apply],
  );
  const canCreatePriceList = inPlace.canCreate("PriceList", readOnly);
  const canCreateDiscount = inPlace.canCreate("DiscountOrSurcharge", readOnly);

  const listColumns = useCallback(
    (options: { value: string; label: string }[], side: "revenuePriceLists" | "costPriceLists"): ColDef<ListLine>[] => [
      { colId: "position", headerName: t("Financial:Position"), width: 90, valueGetter: (p) => (p.node?.rowIndex ?? 0) + 1, type: "rightAligned" },
      {
        field: "priceListId",
        headerName: t("Financial:PriceList"),
        flex: 1,
        minWidth: 200,
        editable: !readOnly,
        ...referenceCellEditorColDef<ListLine>({ options, onCreateNew: canCreatePriceList ? createPriceListFor(side) : undefined, createLabel: t("Financial:AddNewPriceList") }),
        valueFormatter: (p) => labelOf(options)(p.value),
      },
    ],
    [t, readOnly, canCreatePriceList, createPriceListFor],
  );
  const revenueColumns = useMemo(() => listColumns(revenueOptions, "revenuePriceLists"), [listColumns, revenueOptions]);
  const costColumns = useMemo(() => listColumns(costOptions, "costPriceLists"), [listColumns, costOptions]);
  const discountColumns = useMemo<ColDef<DiscountLine>[]>(
    () => [
      {
        field: "discountOrSurchargeId",
        headerName: t("Financial:DiscountOrSurcharge"),
        flex: 2,
        minWidth: 200,
        editable: !readOnly,
        ...referenceCellEditorColDef<DiscountLine>({ options: discountOptions, onCreateNew: canCreateDiscount ? createDiscountFor : undefined, createLabel: t("Financial:AddNewDiscountOrSurcharge") }),
        valueFormatter: (p) => labelOf(discountOptions)(p.value),
      },
      { field: "validFrom", headerName: t("Financial:ValidFrom"), flex: 1, minWidth: 140, editable: !readOnly, ...DATE_CELL_EDITOR_COL_DEF, valueFormatter: (p) => f.date(p.value as string | null) },
      { field: "validTill", headerName: t("Financial:ValidTill"), flex: 1, minWidth: 140, editable: !readOnly, ...DATE_CELL_EDITOR_COL_DEF, valueFormatter: (p) => f.date(p.value as string | null) },
    ],
    [t, f, readOnly, discountOptions, canCreateDiscount, createDiscountFor],
  );

  const linesGrid = <TRow extends { key: string }>(
    form: FormContext<Values>,
    field: "revenuePriceLists" | "costPriceLists" | "discountsOrSurcharges",
    columns: ColDef<TRow>[],
    addLabelKey: string,
    newLine: () => TRow,
    max: number,
  ) => (
    <div className="flex h-48 flex-col gap-2">
      {form.error(field) && <p className="text-label text-destructive">{form.error(field)}</p>}
      <LazyLinesGrid<TRow>
        rows={form.values[field] as unknown as TRow[]}
        onRowsChange={(rows) => {
          form.set(field, rows as unknown as Values[typeof field]);
          form.blur(field);
        }}
        getRowId={(row) => row.key}
        columnDefs={columns}
        readOnly={form.readOnly}
        toolbar={
          <Button type="button" size="sm" variant="outline" disabled={form.values[field].length >= max} onClick={() => form.set(field, [...(form.values[field] as unknown as TRow[]), newLine()] as unknown as Values[typeof field])}>
            <Plus /> {t(addLabelKey)}
          </Button>
        }
      />
    </div>
  );

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["relation", "vatPercentageId", "paymentTermDays", "currencyCode", "chargeFixedPriceByDefault", "sendFreightDocumentsWithInvoice"],
      render: (form) => (
        <FormSection title={t("Financial:RelationFinancialSettings")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("Financial:Relation")} htmlFor={form.id("relation")} required error={form.error("relation")} floating="always" className="col-span-2">
              <EntityLookup<RelationOption>
                id={form.id("relation")}
                value={form.values.relation}
                onChange={(next) => {
                  form.set("relation", next);
                  form.blur("relation");
                  setPickedRelationId(next?.id);
                }}
                fetchPage={relationPage}
                getLabel={(r) => r.name}
                renderOption={(r) => (
                  <>
                    <div className="font-medium">{r.name}</div>
                    <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
                  </>
                )}
                required
                aria-invalid={form.error("relation") != null}
                /* The relation is the key of the settings: fixed once they exist. */
                disabled={form.readOnly || mode === "edit"}
                {...inPlace.offer("Relation", form.readOnly || mode === "edit", (r) => {
                  form.set("relation", { id: r.id, code: r.code ?? "", name: r.name, city: null, countryCode: null, roles: 0 });
                  setPickedRelationId(r.id);
                })}
              />
            </FormField>
            {mode === "create" && pickedRelationId && settings.data && (
              <Alert className="col-span-2">
                <AlertDescription>{t("Financial:SettingsExistForRelationHint")}</AlertDescription>
              </Alert>
            )}
            <FormField label={t("Financial:Vat")} htmlFor={form.id("vatPercentageId")} floating="always">
              {/* DEVIATION(UI-LOOK-001): VAT categories use ReferenceCombobox over their lookup (under 500 per tenant), approved in docs/designs/52-financial-master-data.md */}
              <ReferenceCombobox
                id={form.id("vatPercentageId")}
                value={form.values.vatPercentageId}
                onChange={(next) => form.set("vatPercentageId", next)}
                options={vatOptions}
                placeholder={t("Financial:VatDefaultPlaceholder")}
                disabled={form.readOnly}
                {...inPlace.offer("VatPercentage", form.readOnly, (r) => form.set("vatPercentageId", r.id))}
              />
            </FormField>
            <NumberField form={form} field="paymentTermDays" labelKey="Financial:PaymentTermDays" hintKey="Financial:PaymentTermDaysHint" />
            <FormField label={t("Financial:Currency")} htmlFor={form.id("currencyCode")} floating="always" hint={t("Financial:RelationCurrencyHint")}>
              <ReferenceCombobox
                id={form.id("currencyCode")}
                value={form.values.currencyCode}
                onChange={(next) => form.set("currencyCode", next)}
                options={currencyOptions}
                disabled={form.readOnly}
                {...inPlace.offer("Currency", form.readOnly, (r) => form.set("currencyCode", r.code))}
              />
            </FormField>
            <div className="flex flex-col">
              <CheckboxField form={form} field="chargeFixedPriceByDefault" labelKey="Financial:ChargeFixedPriceByDefault" />
              <CheckboxField form={form} field="sendFreightDocumentsWithInvoice" labelKey="Financial:SendFreightDocumentsWithInvoice" />
            </div>
          </div>
        </FormSection>
      ),
    },
    {
      value: "priceLists",
      labelKey: "Financial:PriceLists",
      fields: ["revenuePriceLists", "costPriceLists", "revenueMultipleMatches", "costMultipleMatches"],
      render: (form) => (
        <>
          <FormSection title={t("Financial:RevenuePriceLists")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <EnumSelectField form={form} field="revenueMultipleMatches" labelKey="Financial:MultipleMatches" items={matchItems} required />
            </div>
            {linesGrid<ListLine>(form, "revenuePriceLists", revenueColumns, "Financial:AddPriceList", () => ({ key: newLineKey(), priceListId: null }), MAX_RELATION_PRICE_LISTS)}
          </FormSection>
          <FormSection title={t("Financial:CostPriceLists")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <EnumSelectField form={form} field="costMultipleMatches" labelKey="Financial:MultipleMatches" items={matchItems} required />
            </div>
            {linesGrid<ListLine>(form, "costPriceLists", costColumns, "Financial:AddPriceList", () => ({ key: newLineKey(), priceListId: null }), MAX_RELATION_PRICE_LISTS)}
          </FormSection>
        </>
      ),
    },
    {
      value: "discounts",
      labelKey: "Financial:DefaultDiscountsOrSurcharges",
      fields: ["discountsOrSurcharges"],
      render: (form) => (
        <FormSection title={t("Financial:DefaultDiscountsOrSurcharges")}>
          <p className="text-label text-muted-foreground">{t("Financial:DefaultDiscountsOrSurchargesHint")}</p>
          {linesGrid<DiscountLine>(
            form,
            "discountsOrSurcharges",
            discountColumns,
            "Financial:AddDiscountOrSurcharge",
            () => ({ key: newLineKey(), discountOrSurchargeId: null, validFrom: null, validTill: null }),
            MAX_RELATION_DISCOUNTS_OR_SURCHARGES,
          )}
        </FormSection>
      ),
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, RelationFinancialSettingsDto>
        entityKey="RelationFinancialSettings"
        resource="Financial"
        queryEntity={RELATION_FINANCIAL_SETTINGS_ENTITY}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.relationName ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={upsert.isPending}
        tabs={tabs}
        applyValues={applyValues}
      />
      {inPlace.dialog}
    </>
  );
}
