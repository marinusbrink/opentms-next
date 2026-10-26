import { useCallback, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ColDef } from "ag-grid-community";
import { Copy, Download, Percent, Plus, Upload } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, EnumSelectField, NumberField, TextAreaField, TextField, numberError, numberToText, parseNumber } from "@/components/record-editor/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { todayIso } from "@/components/record-editor/validity-rows";
import {
  COLUMN_QUANTITY_SOURCES,
  PRICE_LIST_BASES,
  PRICE_MULTIPLIERS,
  RATE_SIDES,
  STOP_MATCHINGS,
  keyOf,
  priceLists,
  useExportPriceList,
  type ColumnQuantitySource,
  type PriceListDto,
  type PriceMultiplier,
  type QuantitySource,
  type RateSide,
  type StopMatching,
} from "@/domains/financial/price-lists";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { referenceDataQueryKey } from "@/lib/reference-data/crud-hooks";
import { currencies } from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { AdjustPriceListDialog, CopyPriceListDialog, ImportPriceListDialog } from "./price-list/PriceListCommandDialogs";
import { PriceListRulesTab } from "./price-list/PriceListRulesTab";
import { PriceListSimulateTab } from "./price-list/PriceListSimulateTab";
import { columnsError, columnsFromDto, columnsToDto, openLine, type ColumnLine } from "./price-list/rule-ladder";

/* The price list editor (design 57 §UI, design 61): `full` (UI-DLG-003 names the price list under the full tier).
 * General (code, name, side, currency, basis, multiplier, column quantity, stop matching, conversion factor, validity,
 * archived), Columns (the horizontal brackets as lines: an upper bound each, the last open), Rules (the master-detail
 * with the tier badges and the bracket ladder, saved per rule), Simulate (with the Simulate permission), Remarks,
 * Record identification. Ribbon: Copy (Create), Adjust (Adjust, with preview), Export (view), Import (Update); they
 * act on the SAVED list, so unsaved changes disable them (UI-ACT-008). */

interface Values {
  code: string;
  name: string;
  side: RateSide;
  currencyCode: string | null;
  basis: QuantitySource;
  multiplier: PriceMultiplier;
  columnQuantity: ColumnQuantitySource;
  stopMatching: StopMatching;
  unitConversionFactor: string;
  validFrom: string | null;
  validTill: string | null;
  isArchived: boolean;
  description: string;
  columns: ColumnLine[];
}

const initialValuesFor = (initialName: string | undefined) => (dto: PriceListDto | undefined): Values => ({
  code: dto?.code ?? "",
  name: dto?.name ?? initialName ?? "",
  side: dto?.side ?? 1,
  currencyCode: dto?.currencyCode ?? "EUR",
  basis: dto?.basis ?? 1,
  multiplier: dto?.multiplier ?? 1,
  columnQuantity: dto?.columnQuantity ?? 1,
  stopMatching: dto?.stopMatching ?? 3,
  unitConversionFactor: numberToText(dto?.unitConversionFactor ?? 1),
  validFrom: dto?.validFrom ?? todayIso(),
  validTill: dto?.validTill ?? null,
  isArchived: dto?.isArchived ?? false,
  description: dto?.description ?? "",
  columns: columnsFromDto(dto?.columns),
});

export const validatePriceList = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:Required";
  if (!v.name.trim()) errors.name = "MasterData:Required";
  if (!v.currencyCode) errors.currencyCode = "MasterData:Required";
  const factor = parseNumber(v.unitConversionFactor);
  if (factor == null) errors.unitConversionFactor = "MasterData:Required";
  else {
    const factorError = numberError(v.unitConversionFactor, { min: 0.000001 });
    if (factorError) errors.unitConversionFactor = factorError;
  }
  if (!v.validFrom) errors.validFrom = "MasterData:Required";
  else if (v.validTill && v.validTill < v.validFrom) errors.validTill = "Financial:ValidTillBeforeFromHint";
  const columns = columnsError(v.columns);
  if (columns) errors.columns = columns;
  return errors;
};

const TEMPLATE_GROUP = "Financial:PriceList";

export function PriceListEditor({ mode: requestedMode, id: requestedId, readOnly, open, onClose, onSaved, initialName, onOpenRecord }: EntityEditorProps) {
  const { t } = useL();
  /* A new list is useless without rules: after its create the editor stays open on the saved list, on the Rules tab
   * (finding 7). From then on it is an ordinary edit of that list. */
  const [createdId, setCreatedId] = useState<string | null>(null);
  const mode = createdId ? "edit" : requestedMode;
  const id = createdId ?? requestedId;
  const f = useFormat();
  const queryClient = useQueryClient();
  const detail = priceLists.useDetail(mode === "edit" ? id : undefined);
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const create = priceLists.useCreate();
  const update = priceLists.useUpdate();
  const remove = priceLists.useDelete();
  const exportList = useExportPriceList();
  const canDelete = useIsGranted("Financial.PriceLists.Delete");
  const canCreate = useIsGranted("Financial.PriceLists.Create");
  const canUpdate = useIsGranted("Financial.PriceLists.Update");
  const canAdjust = useIsGranted("Financial.PriceLists.Adjust");
  const canSimulate = useIsGranted("Financial.PriceLists.Simulate");
  const inPlace = useCreateInPlace();
  const [dialog, setDialog] = useState<"copy" | "adjust" | "import" | null>(null);
  const [commandError, setCommandError] = useState<string | null>(null);
  /* Adjust and import change the rules outside the Rules tab: its grid refetches on this key. */
  const [rulesRefresh, setRulesRefresh] = useState(0);
  const isEdit = mode === "edit" && id != null;

  const currencyLookup = currencies.useLookup(open);
  const currencyOptions = useMemo(
    () => (currencyLookup.data ?? []).map((c) => ({ value: c.code ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })).filter((o) => o.value),
    [currencyLookup.data],
  );
  const sideItems = useMemo(() => RATE_SIDES.map((s) => ({ value: s.value, label: t(s.key) })), [t]);
  const basisItems = useMemo(() => PRICE_LIST_BASES.map((b) => ({ value: b.value, label: t(b.key) })), [t]);
  const multiplierItems = useMemo(() => PRICE_MULTIPLIERS.map((m) => ({ value: m.value, label: t(m.key) })), [t]);
  const columnQuantityItems = useMemo(() => COLUMN_QUANTITY_SOURCES.map((c) => ({ value: c.value, label: t(c.key) })), [t]);
  const stopMatchingItems = useMemo(() => STOP_MATCHINGS.map((s) => ({ value: s.value, label: t(s.key) })), [t]);

  const save = useCallback(
    async (v: Values, dto: PriceListDto | undefined) => {
      const body = {
        code: v.code.trim().toUpperCase(),
        name: v.name.trim(),
        description: v.description.trim() || null,
        side: v.side,
        currencyCode: v.currencyCode!,
        basis: v.basis,
        multiplier: v.multiplier,
        columnQuantity: v.columnQuantity,
        stopMatching: v.stopMatching,
        unitConversionFactor: parseNumber(v.unitConversionFactor) ?? 1,
        validFrom: v.validFrom!,
        validTill: v.validTill,
        isArchived: v.isArchived,
        columns: columnsToDto(v.columns),
      };
      return mode === "create" ? await create.mutateAsync(body) : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const handleExport = async () => {
    if (!id) return;
    setCommandError(null);
    try {
      await exportList.mutateAsync({ id, fallbackName: detail.data?.code ?? "price-list" });
    } catch (err) {
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  const command = (commandId: string, labelKey: string, icon: RibbonCommand["icon"], isDirty: boolean, isPending: boolean, onClick: () => void): RibbonCommand => ({
    id: commandId,
    labelKey,
    icon,
    groupKey: TEMPLATE_GROUP,
    disabled: detail.data == null || isDirty || isPending,
    disabledReasonKey: isDirty ? "Platform:SaveChangesFirst" : undefined,
    onClick,
  });

  const actionCommands = ({ isDirty }: { isDirty: boolean }): RibbonCommand[] =>
    isEdit
      ? [
          ...(canCreate ? [command("copy", "Financial:Copy", Copy, isDirty, false, () => setDialog("copy"))] : []),
          ...(canAdjust && !readOnly ? [command("adjust", "Financial:Adjust", Percent, isDirty, false, () => setDialog("adjust"))] : []),
          command("export", exportList.isPending ? "Financial:Exporting" : "Financial:Export", Download, isDirty, exportList.isPending, () => void handleExport()),
          ...(canUpdate && !readOnly ? [command("import", "Financial:Import", Upload, isDirty, false, () => setDialog("import"))] : []),
        ]
      : [];

  const columnDefs = useMemo<ColDef<ColumnLine>[]>(
    () => [
      { colId: "position", headerName: t("Financial:Position"), width: 90, valueGetter: (p) => (p.node?.rowIndex ?? 0) + 1, type: "rightAligned" },
      {
        field: "upperBound",
        headerName: t("Financial:UpperBound"),
        flex: 1,
        minWidth: 160,
        editable: (p) => !readOnly && p.data?.upperBound != null,
        cellEditor: "agNumberCellEditor",
        cellEditorParams: { min: 0, precision: 4 },
        valueFormatter: (p) => (p.value == null ? t("Financial:Open") : f.number(p.value as number, { maxDecimals: 4 })),
        type: "rightAligned",
      },
    ],
    [t, f, readOnly],
  );

  const savedColumns = useMemo(() => columnsFromDto(detail.data?.columns), [detail.data]);
  const currencyCode = detail.data?.currencyCode ?? "EUR";

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: ["code", "name", "side", "currencyCode", "basis", "multiplier", "columnQuantity", "stopMatching", "unitConversionFactor", "validFrom", "validTill", "isArchived"],
      render: (form) => (
        <div className="min-h-0 overflow-y-auto pr-1">
          <FormSection title={t("Financial:PriceList")}>
            <div className="grid grid-cols-4 gap-x-4 gap-y-3">
              <TextField form={form} field="code" labelKey="MasterData:Code" required maxLength={16} upper />
              <TextField form={form} field="name" labelKey="MasterData:Name" required maxLength={128} className="col-span-2" />
              <EnumSelectField form={form} field="side" labelKey="Financial:RateSide" items={sideItems} required />
              <FormField label={t("Financial:Currency")} htmlFor={form.id("currencyCode")} required error={form.error("currencyCode")} floating="always">
                {/* UI-LOOK-001: currencies are the rule's static list of more than 15 items, so a client-searched Combobox (ReferenceCombobox) over the active currencies by ISO code; "Add new currency…" per UI-LOOK-004. */}
                <ReferenceCombobox
                  id={form.id("currencyCode")}
                  value={form.values.currencyCode}
                  onChange={(next) => {
                    form.set("currencyCode", next);
                    form.blur("currencyCode");
                  }}
                  options={currencyOptions}
                  required
                  aria-invalid={form.error("currencyCode") != null}
                  disabled={form.readOnly}
                  {...inPlace.offer("Currency", form.readOnly, (r) => form.set("currencyCode", r.code))}
                />
              </FormField>
              <FormField label={t("Financial:ValidFrom")} htmlFor={form.id("validFrom")} required error={form.error("validFrom")} floating="always">
                <DatePicker
                  id={form.id("validFrom")}
                  value={form.values.validFrom}
                  onChange={(next) => {
                    form.set("validFrom", next);
                    form.blur("validFrom");
                  }}
                  required
                  disabled={form.readOnly}
                  aria-invalid={form.error("validFrom") != null}
                />
              </FormField>
              <FormField label={t("Financial:ValidTill")} htmlFor={form.id("validTill")} error={form.error("validTill")} floating="always">
                <DatePicker
                  id={form.id("validTill")}
                  value={form.values.validTill}
                  onChange={(next) => {
                    form.set("validTill", next);
                    form.blur("validTill");
                  }}
                  disabled={form.readOnly}
                  aria-invalid={form.error("validTill") != null}
                />
              </FormField>
              <CheckboxField form={form} field="isArchived" labelKey="MasterData:IsArchived" />
            </div>
          </FormSection>
          <FormSection title={t("Financial:Calculation")}>
            <div className="grid grid-cols-4 gap-x-4 gap-y-3">
              <EnumSelectField form={form} field="basis" labelKey="Financial:Basis" items={basisItems} required />
              <EnumSelectField form={form} field="multiplier" labelKey="Financial:Multiplier" items={multiplierItems} required />
              <EnumSelectField form={form} field="columnQuantity" labelKey="Financial:ColumnQuantity" items={columnQuantityItems} required className="col-span-2" />
              <EnumSelectField form={form} field="stopMatching" labelKey="Financial:StopMatching" items={stopMatchingItems} required />
              <NumberField form={form} field="unitConversionFactor" labelKey="Financial:UnitConversionFactor" required hintKey="Financial:UnitConversionFactorHint" />
            </div>
          </FormSection>
        </div>
      ),
    },
    {
      value: "columns",
      labelKey: "Financial:Columns",
      fields: ["columns"],
      render: (form) => (
        <FormSection title={t("Financial:Columns")} className="flex h-full min-h-0 flex-col">
          <p className="text-label text-muted-foreground">{t("Financial:ColumnsHint").replace("{0}", t(keyOf(COLUMN_QUANTITY_SOURCES, form.values.columnQuantity)))}</p>
          <div className="flex min-h-0 flex-1 flex-col gap-2">
            {form.error("columns") && <p className="text-label text-destructive">{form.error("columns")}</p>}
            <LazyLinesGrid<ColumnLine>
              rows={form.values.columns}
              onRowsChange={(rows) => {
                form.set(
                  "columns",
                  rows.map((row) => ({ ...row, upperBound: row.upperBound == null || (row.upperBound as unknown) === "" ? null : Number(row.upperBound) })),
                );
                form.blur("columns");
              }}
              getRowId={(row) => row.key}
              columnDefs={columnDefs}
              readOnly={form.readOnly}
              canRemoveRow={(row) => row.upperBound != null}
              toolbar={
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    const bounded = form.values.columns.filter((c) => c.upperBound != null);
                    const lastBound = bounded.at(-1)?.upperBound ?? 0;
                    const open = form.values.columns.find((c) => c.upperBound == null) ?? openLine();
                    form.set("columns", [...bounded, { ...openLine(), upperBound: lastBound + 1 }, open]);
                    form.blur("columns");
                  }}
                >
                  <Plus /> {t("Financial:AddColumn")}
                </Button>
              }
            />
          </div>
        </FormSection>
      ),
    },
    {
      value: "rules",
      labelKey: "Financial:Rules",
      fields: [],
      render: (form) =>
        isEdit && detail.data ? (
          <PriceListRulesTab
            priceListId={id}
            columns={savedColumns}
            columnsDirty={JSON.stringify(columnsToDto(form.values.columns)) !== JSON.stringify(columnsToDto(savedColumns))}
            readOnly={form.readOnly}
            refreshKey={rulesRefresh}
          />
        ) : (
          <Alert>
            <AlertDescription>{t("Financial:SaveListBeforeRulesHint")}</AlertDescription>
          </Alert>
        ),
    },
    ...(isEdit && canSimulate
      ? [
          {
            value: "simulate",
            labelKey: "Financial:Simulate",
            fields: [] as (keyof Values & string)[],
            render: () => <PriceListSimulateTab priceListId={id} currencyCode={currencyCode} />,
          } satisfies EditorTab<Values>,
        ]
      : []),
    {
      value: "remarks",
      labelKey: "MasterData:Remarks",
      fields: ["description"],
      render: (form) => <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} />,
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, PriceListDto>
        entityKey="PriceList"
        resource="Financial"
        queryEntity={priceLists.entity}
        size="full"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => `${dto.code ?? ""} ${dto.name ?? ""}`.trim()}
        initialValues={initialValues}
        validate={validatePriceList}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        actionCommands={actionCommands}
        isCommandBusy={exportList.isPending}
        commandError={commandError}
        badges={
          detail.data ? (
            <>
              <Badge variant="secondary">{t(keyOf(RATE_SIDES, detail.data.side))}</Badge>
              {detail.data.isArchived && <Badge variant="outline">{t("MasterData:Archived")}</Badge>}
            </>
          ) : undefined
        }
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        panelScroll
        continueAfterCreate={{
          tab: "rules",
          onCreated: (saved) => {
            queryClient.setQueryData(referenceDataQueryKey(priceLists.entity, "detail", saved.id), saved);
            setCreatedId(saved.id!);
          },
        }}
      />
      {isEdit && detail.data && dialog === "copy" && (
        <CopyPriceListDialog
          open
          onClose={() => setDialog(null)}
          source={detail.data}
          onCopied={(copy) => {
            setDialog(null);
            void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(priceLists.entity) });
            toast.success(t("Financial:PriceListCopied"));
            onSaved(copy);
            if (copy.id && onOpenRecord) onOpenRecord(copy.id);
          }}
        />
      )}
      {isEdit && dialog === "adjust" && (
        <AdjustPriceListDialog
          open
          onClose={() => setDialog(null)}
          priceListId={id}
          currencyCode={currencyCode}
          onAdjusted={(result) => {
            setDialog(null);
            setRulesRefresh((k) => k + 1);
            toast.success(t("Financial:AdjustmentApplied").replace("{0}", String(result.changedCellCount ?? 0)));
          }}
        />
      )}
      {isEdit && dialog === "import" && (
        <ImportPriceListDialog
          open
          onClose={() => setDialog(null)}
          priceListId={id}
          onImported={(result) => {
            setDialog(null);
            setRulesRefresh((k) => k + 1);
            void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(priceLists.entity) });
            toast.success(t("Financial:ImportResult").replace("{0}", String(result.ruleCount ?? 0)).replace("{1}", String(result.bracketCount ?? 0)).replace("{2}", String(result.cellCount ?? 0)));
          }}
        />
      )}
      {inPlace.dialog}
    </>
  );
}
