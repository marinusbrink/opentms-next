import { useCallback, useMemo } from "react";
import type { ColDef, EditableCallbackParams } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DATE_CELL_EDITOR_COL_DEF } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { PercentCell } from "@/components/ui/grid-cells";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { ValidFromCell, byDate, dateOnly, newLineKey, todayIso, validityError, type ValidityLine } from "@/components/record-editor/validity-rows";
import { VAT_KINDS, vatPercentages, type VatKind, type VatPercentageDto } from "@/domains/financial/reference-data";
import { countries } from "@/domains/master-data/entities";
import { resolveLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The VAT percentage editor (design 52, UI design): `md`; General (code, name, kind, country by ISO code, archived),
 * Rates (the validity rows, §3.4: a rate that has taken effect is read-only with a lock; a new rate on an existing
 * category starts after today), Ledger (sales and purchase account), Remarks, Record identification. */

interface RateLine extends ValidityLine {
  percentage: number | null;
}

interface Values {
  code: string;
  name: string;
  kind: VatKind;
  countryCode: string | null;
  isArchived: boolean;
  salesLedgerAccount: string;
  purchaseLedgerAccount: string;
  description: string;
  rates: RateLine[];
}

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: VatPercentageDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto ? resolveLocalizedName(dto.names, culture) : (initialName ?? ""),
  kind: dto?.kind ?? 1,
  countryCode: dto?.countryCode ?? null,
  isArchived: dto?.isArchived ?? false,
  salesLedgerAccount: dto?.salesLedgerAccount ?? "",
  purchaseLedgerAccount: dto?.purchaseLedgerAccount ?? "",
  description: dto?.description ?? "",
  rates: (dto?.rates ?? []).map((r) => ({
    key: `${r.validFrom}`,
    validFrom: r.validFrom ?? null,
    percentage: r.percentage ?? null,
    isFixed: r.isFixed ?? false,
  })),
});

/** 0–100 with at most two decimals: numeric(5,2). */
const validPercentage = (value: number | null) => value != null && value >= 0 && value <= 100 && Math.round(value * 100) === value * 100;

export function VatPercentageEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t, culture } = useL();
  const detail = vatPercentages.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const create = vatPercentages.useCreate();
  const update = vatPercentages.useUpdate();
  const remove = vatPercentages.useDelete();
  const canDelete = useIsGranted("Financial.VatPercentages.Delete");
  const inPlace = useCreateInPlace();
  const countryLookup = countries.useLookup(open);
  /* The category refers to its country by ISO code (assessment §3.7): the option value is the code, not the id. */
  const countryOptions = useMemo(
    () => (countryLookup.data ?? []).map((c) => ({ value: c.code ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })).filter((o) => o.value),
    [countryLookup.data],
  );

  const validate = useCallback(
    (v: Values): FieldErrors<Values> => {
      const errors: FieldErrors<Values> = {};
      if (!v.code.trim()) errors.code = "MasterData:Required";
      if (!v.name.trim()) errors.name = "MasterData:Required";
      if (!v.countryCode) errors.countryCode = "MasterData:Required";
      if (v.isArchived && detail.data?.isDefault) errors.isArchived = "MasterData:ArchivedCannotBeDefault";
      const rates = validityError(v.rates, mode) ?? (v.rates.some((r) => !validPercentage(r.percentage)) ? "Financial:PercentageRangeHint" : undefined);
      if (rates) errors.rates = rates;
      return errors;
    },
    [detail.data?.isDefault, mode],
  );

  const save = useCallback(
    async (v: Values, dto: VatPercentageDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        names: withTextForCulture(dto?.names, culture, v.name),
        kind: v.kind,
        countryCode: v.countryCode ?? "",
        salesLedgerAccount: v.salesLedgerAccount.trim() || null,
        purchaseLedgerAccount: v.purchaseLedgerAccount.trim() || null,
        rates: byDate(v.rates).map((r) => ({ validFrom: dateOnly(r.validFrom)!, percentage: r.percentage ?? 0 })),
        isArchived: v.isArchived,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id, culture],
  );

  const rateColumns = useMemo<ColDef<RateLine>[]>(() => {
    const editable = (p: EditableCallbackParams<RateLine>) => !readOnly && !p.data?.isFixed;
    return [
      {
        field: "validFrom",
        headerName: t("MasterData:ValidFrom"),
        flex: 1,
        minWidth: 150,
        editable,
        ...DATE_CELL_EDITOR_COL_DEF,
        cellRenderer: ValidFromCell,
        cellRendererParams: { tooltipKey: "Financial:FixedRateTooltip" },
        sort: "asc",
      },
      {
        field: "percentage",
        headerName: t("Financial:Percentage"),
        flex: 1,
        minWidth: 120,
        editable,
        cellEditor: "agNumberCellEditor",
        cellEditorParams: { min: 0, max: 100, precision: 2 },
        cellRenderer: PercentCell,
        type: "rightAligned",
      },
    ];
  }, [readOnly, t]);

  const kindItems = VAT_KINDS.map((k) => ({ value: k.value, label: t(k.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "kind", "countryCode", "isArchived"],
      render: (form) => (
        <FormSection title={t("Financial:VatPercentage")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
            <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} />
            <FormField label={t("MasterData:Kind")} htmlFor={form.id("kind")} required floating="always">
              <Select items={kindItems} value={form.values.kind} onValueChange={(next) => form.set("kind", (next as VatKind | null) ?? 1)} disabled={form.readOnly}>
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
            <FormField label={t("MasterData:Country")} htmlFor={form.id("countryCode")} required error={form.error("countryCode")} floating="always">
              {/* DEVIATION(UI-LOOK-001): countries use ReferenceCombobox over their lookup (under 500 per tenant), approved in docs/designs/52-financial-master-data.md */}
              <ReferenceCombobox
                id={form.id("countryCode")}
                value={form.values.countryCode}
                onChange={(next) => {
                  form.set("countryCode", next);
                  form.blur("countryCode");
                }}
                options={countryOptions}
                required
                disabled={form.readOnly}
                aria-invalid={form.error("countryCode") != null}
                {...inPlace.offer("Country", form.readOnly, (r) => form.set("countryCode", r.code))}
              />
            </FormField>
            <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "rates",
      labelKey: "Financial:Rates",
      fields: ["rates"],
      render: (form) => (
        <div className="flex h-72 flex-col gap-2">
          {form.error("rates") && <p className="text-label text-destructive">{form.error("rates")}</p>}
          <LazyLinesGrid<RateLine>
            rows={form.values.rates}
            onRowsChange={(rows) => {
              form.set("rates", rows);
              form.blur("rates");
            }}
            getRowId={(row) => row.key}
            columnDefs={rateColumns}
            readOnly={form.readOnly}
            canRemoveRow={(row) => !row.isFixed}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => form.set("rates", [...form.values.rates, { key: newLineKey(), validFrom: mode === "edit" ? null : todayIso(), percentage: null, isFixed: false }])}
              >
                <Plus /> {t("Financial:AddRate")}
              </Button>
            }
          />
        </div>
      ),
    },
    {
      value: "ledger",
      labelKey: "Financial:Ledger",
      fields: ["salesLedgerAccount", "purchaseLedgerAccount"],
      render: (form) => (
        <FormSection title={t("Financial:Ledger")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="salesLedgerAccount" labelKey="Financial:SalesLedgerAccount" maxLength={32} />
            <TextField form={form} field="purchaseLedgerAccount" labelKey="Financial:PurchaseLedgerAccount" maxLength={32} />
          </div>
        </FormSection>
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
      <MasterDataEditor<Values, VatPercentageDto>
        entityKey="VatPercentage"
        resource="Financial"
        queryEntity={vatPercentages.entity}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => resolveLocalizedName(dto.names, culture) || dto.code || ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        badges={detail.data?.isDefault ? <Badge variant="secondary">{t("MasterData:IsDefault")}</Badge> : undefined}
        deleteRecord={mode === "edit" && canDelete && !detail.data?.isDefault ? remove : undefined}
      />
      {inPlace.dialog}
    </>
  );
}
