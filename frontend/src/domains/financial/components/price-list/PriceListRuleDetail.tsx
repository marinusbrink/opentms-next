import { useEffect, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { parseNumber, numberToText } from "@/components/record-editor/fields";
import { Input } from "@/components/ui/input";
import {
  QUANTITY_SOURCES,
  keyOf,
  tierOf,
  useCreatePriceListRule,
  useDeletePriceListRule,
  usePriceListRule,
  useUpdatePriceListRule,
  type PriceListRuleDto,
  type QuantitySource,
} from "@/domains/financial/price-lists";
import { relationLookupPage, type RelationOption } from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import {
  bracketsError,
  bracketsFromDto,
  bracketsToDto,
  columnHeader,
  fromBracketRow,
  newBracket,
  priceField,
  toBracketRow,
  type BracketLine,
  type BracketRow,
  type ColumnLine,
} from "./rule-ladder";

/* The right half of the Rules tab (design 57 §UI): the selected rule, or a new one. The lane (per side a relation as
 * EntityLookup or a region as ReferenceCombobox; one of the two), the three dimensions with "any", the row quantity,
 * minimum and maximum amount, and the bracket ladder: a LinesGrid whose price columns are the LIST's columns. A rule
 * is saved with its own command (PUT/POST under the list, design 57 build note 3), never with the header. */

export interface RuleLookups {
  regions: ReferenceOption[];
  transportUnits: ReferenceOption[];
  products: ReferenceOption[];
  vehicleTypes: ReferenceOption[];
}

export interface PriceListRuleDetailProps {
  priceListId: string;
  /** The rule to edit; null = a new rule. */
  ruleId: string | null;
  /** The list's SAVED columns: the ladder's price columns. */
  columns: ColumnLine[];
  readOnly: boolean;
  lookups: RuleLookups;
  onSaved: (rule: PriceListRuleDto) => void;
  onDeleted: () => void;
  onClose: () => void;
}

interface RuleValues {
  fromRelation: RelationOption | null;
  fromRegionId: string | null;
  toRelation: RelationOption | null;
  toRegionId: string | null;
  transportUnitId: string | null;
  productId: string | null;
  vehicleTypeId: string | null;
  rowQuantity: QuantitySource;
  minAmount: string;
  maxAmount: string;
  brackets: BracketLine[];
}

type RuleErrors = Partial<Record<keyof RuleValues, string>>;

const toRelation = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

export const ruleValuesOf = (rule: PriceListRuleDto | undefined, columnCount: number): RuleValues => ({
  fromRelation: toRelation(rule?.fromRelationId, rule?.fromRelationName),
  fromRegionId: rule?.fromRegionId ?? null,
  toRelation: toRelation(rule?.toRelationId, rule?.toRelationName),
  toRegionId: rule?.toRegionId ?? null,
  transportUnitId: rule?.transportUnitId ?? null,
  productId: rule?.productId ?? null,
  vehicleTypeId: rule?.vehicleTypeId ?? null,
  rowQuantity: rule?.rowQuantity ?? 0,
  minAmount: numberToText(rule?.minAmount),
  maxAmount: numberToText(rule?.maxAmount),
  brackets: bracketsFromDto(rule?.brackets, columnCount),
});

/** The client-side checks of a rule (the server repeats them: LaneRequired, RelationAndRegionOnOneSide, the ladder's). */
export function validateRule(v: RuleValues): RuleErrors {
  const errors: RuleErrors = {};
  const fromSet = v.fromRelation != null || v.fromRegionId != null;
  const toSet = v.toRelation != null || v.toRegionId != null;
  if (!fromSet && !toSet) errors.fromRelation = "Financial:LaneRequiredHint";
  if (v.fromRelation && v.fromRegionId) errors.fromRegionId = "Financial:RelationAndRegionOnOneSideHint";
  if (v.toRelation && v.toRegionId) errors.toRegionId = "Financial:RelationAndRegionOnOneSideHint";
  const min = parseNumber(v.minAmount);
  const max = parseNumber(v.maxAmount);
  if (min != null && (Number.isNaN(min) || min < 0)) errors.minAmount = "MasterData:InvalidNumber";
  if (max != null && (Number.isNaN(max) || max < 0)) errors.maxAmount = "MasterData:InvalidNumber";
  if (min != null && max != null && !Number.isNaN(min) && !Number.isNaN(max) && max < min) errors.maxAmount = "Financial:MaxBelowMinHint";
  /* A typed row quantity has no grading: one open row only. */
  if (v.rowQuantity === 0 && v.brackets.length > 1) errors.brackets = "Financial:TypedQuantitySingleRowHint";
  else {
    const ladder = bracketsError(v.brackets);
    if (ladder) errors.brackets = ladder;
  }
  return errors;
}

export function PriceListRuleDetail({ priceListId, ruleId, columns, readOnly, lookups, onSaved, onDeleted, onClose }: PriceListRuleDetailProps) {
  const { t } = useL();
  const f = useFormat();
  const inPlace = useCreateInPlace();
  const rule = usePriceListRule(priceListId, ruleId ?? undefined);
  const create = useCreatePriceListRule();
  const update = useUpdatePriceListRule();
  const remove = useDeletePriceListRule();
  const columnCount = columns.length;

  const [values, setValues] = useState<RuleValues>(() => ruleValuesOf(undefined, columnCount));
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [instance] = useState(() => `rule-${Math.random().toString(36).slice(2, 8)}`);
  const id = (field: string) => `${instance}-${field}`;

  /* The rule arrives after the panel opened: reset once per loaded rule. */
  const loadedId = ruleId == null ? "new" : rule.data?.id;
  useEffect(() => {
    if (ruleId == null) {
      setValues(ruleValuesOf(undefined, columnCount));
    } else if (rule.data) {
      setValues(ruleValuesOf(rule.data, columnCount));
    }
    setTouched(new Set());
    setSubmitted(false);
    setServerError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedId, columnCount]);

  const loaded = ruleId == null || rule.data != null;
  const errors = useMemo(() => validateRule(values), [values]);
  const error = (field: keyof RuleValues) => (touched.has(field) || submitted ? (errors[field] ? t(errors[field]!) : undefined) : undefined);
  const set = <K extends keyof RuleValues>(field: K, value: RuleValues[K]) => setValues((v) => ({ ...v, [field]: value }));
  const blur = (field: keyof RuleValues) => setTouched((s) => (s.has(field) ? s : new Set(s).add(field)));

  const relationPage = useMemo(() => relationLookupPage(), []);
  const quantityItems = useMemo(() => QUANTITY_SOURCES.map((q) => ({ value: q.value, label: t(q.key) })), [t]);
  const tier = tierOf(values);

  const save = async () => {
    if (readOnly || !loaded) return;
    setSubmitted(true);
    setServerError(null);
    if (Object.keys(errors).length > 0) return;
    const dto = {
      fromRelationId: values.fromRelation?.id ?? null,
      fromRegionId: values.fromRegionId,
      toRelationId: values.toRelation?.id ?? null,
      toRegionId: values.toRegionId,
      transportUnitId: values.transportUnitId,
      productId: values.productId,
      vehicleTypeId: values.vehicleTypeId,
      rowQuantity: values.rowQuantity,
      minAmount: parseNumber(values.minAmount),
      maxAmount: parseNumber(values.maxAmount),
      brackets: bracketsToDto(values.brackets, columnCount),
    };
    try {
      const saved = ruleId == null ? await create.mutateAsync({ priceListId, dto }) : await update.mutateAsync({ priceListId, ruleId, dto });
      toast.success(t("Financial:RuleSaved"));
      onSaved(saved);
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const deleteRule = async () => {
    if (ruleId == null) return;
    try {
      await remove.mutateAsync({ priceListId, ruleId });
      setConfirmDelete(false);
      toast.success(t("Financial:RuleDeleted"));
      onDeleted();
    } catch (err) {
      setConfirmDelete(false);
      setServerError(getApiErrorMessage(err, t));
    }
  };

  /* The ladder: the bound column, then one price column per list column (headed by the column's bound). */
  const bracketColumns = useMemo<ColDef<BracketRow>[]>(
    () => [
      {
        field: "upperBound",
        headerName: t("Financial:UpperBound"),
        minWidth: 130,
        flex: 1,
        editable: (p) => !readOnly && p.node?.rowIndex !== values.brackets.length - 1,
        cellEditor: "agNumberCellEditor",
        cellEditorParams: { min: 0, precision: 4 },
        valueFormatter: (p) => (p.value == null ? t("Financial:Open") : f.number(p.value as number, { maxDecimals: 4 })),
        type: "rightAligned",
        pinned: "left",
      },
      ...columns.map(
        (column, index): ColDef<BracketRow> => ({
          field: priceField(index),
          headerName: columnHeader(f, t, column, columns.length),
          minWidth: 110,
          flex: 1,
          editable: !readOnly,
          cellEditor: "agNumberCellEditor",
          cellEditorParams: { min: 0, precision: 4 },
          valueFormatter: (p) => (p.value == null ? "" : f.unitPrice(p.value as number)),
          type: "rightAligned",
        }),
      ),
    ],
    [t, f, readOnly, columns, values.brackets.length],
  );
  const bracketRows = useMemo(() => values.brackets.map(toBracketRow), [values.brackets]);

  const lanePicker = (side: "from" | "to") => {
    const relationField = side === "from" ? "fromRelation" : "toRelation";
    const regionField = side === "from" ? "fromRegionId" : "toRegionId";
    return (
      <FormSection title={t(side === "from" ? "Financial:From" : "Financial:To")} className="min-w-0">
        <div className="grid grid-cols-1 gap-y-3">
          <FormField label={t("Financial:Relation")} htmlFor={id(relationField)} error={side === "from" ? error("fromRelation") : undefined} floating="always">
            <EntityLookup<RelationOption>
              id={id(relationField)}
              value={values[relationField]}
              onChange={(next) => {
                set(relationField, next);
                blur(relationField);
              }}
              fetchPage={relationPage}
              getLabel={(r) => r.name}
              renderOption={(r) => (
                <>
                  <div className="font-medium">{r.name}</div>
                  <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
                </>
              )}
              disabled={readOnly}
              aria-invalid={error(relationField) != null}
              {...inPlace.offer("Relation", readOnly, (r) => set(relationField, { id: r.id, code: r.code ?? "", name: r.name, city: null, countryCode: null, roles: 0 }))}
            />
          </FormField>
          <FormField label={t("Financial:Region")} htmlFor={id(regionField)} error={error(regionField)} floating="always">
            {/* DEVIATION(UI-LOOK-001): regions are a ReferenceCombobox over their lookup (under 500 per tenant), approved in docs/designs/52-financial-master-data.md (UI design) and kept by design 57 §UI. */}
            <ReferenceCombobox
              id={id(regionField)}
              value={values[regionField]}
              onChange={(next) => {
                set(regionField, next);
                blur(regionField);
              }}
              options={lookups.regions}
              disabled={readOnly}
              aria-invalid={error(regionField) != null}
              {...inPlace.offer("GeographicalRegion", readOnly, (r) => set(regionField, r.id))}
            />
          </FormField>
        </div>
      </FormSection>
    );
  };

  const dimension = (field: "transportUnitId" | "productId" | "vehicleTypeId", labelKey: string, options: ReferenceOption[], entity: "TransportUnit" | "Product" | "VehicleType") => (
    <FormField label={t(labelKey)} htmlFor={id(field)} floating="always">
      {/* DEVIATION(UI-LOOK-001): transport units, products and vehicle types are ReferenceComboboxes over their lookups (run-1 lists without a lookup-page endpoint), approved in docs/designs/48-master-data-vehicles.md (UI design). */}
      <ReferenceCombobox
        id={id(field)}
        value={values[field]}
        onChange={(next) => set(field, next)}
        options={options}
        placeholder={t("Financial:Any")}
        disabled={readOnly}
        {...inPlace.offer(entity, readOnly, (r) => set(field, r.id))}
      />
    </FormField>
  );

  if (!loaded) {
    return (
      <div className="grid grid-cols-1 gap-3 p-1">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-3" data-slot="rule-detail">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-title font-semibold">{ruleId == null ? t("Financial:NewRule") : `${t("Financial:Rule")} ${rule.data?.position ?? ""}`}</span>
          <Badge variant={tier > 0 ? "default" : "secondary"}>{`${t("Financial:Tier")} ${tier}`}</Badge>
        </div>
        <div className="flex items-center gap-2">
          {!readOnly && (
            <Button type="button" size="sm" onClick={() => void save()} disabled={create.isPending || update.isPending}>
              <Save /> {create.isPending || update.isPending ? t("Saving") : t("Financial:SaveRule")}
            </Button>
          )}
          {!readOnly && ruleId != null && (
            <Button type="button" size="sm" variant="destructive" onClick={() => setConfirmDelete(true)} disabled={remove.isPending}>
              <Trash2 /> {t("Financial:DeleteRule")}
            </Button>
          )}
          <Button type="button" size="sm" variant="outline" onClick={onClose}>
            {t("Close")}
          </Button>
        </div>
      </div>
      {serverError && (
        <Alert variant="destructive">
          <AlertDescription>{serverError}</AlertDescription>
        </Alert>
      )}
      {rule.error != null && (
        <Alert variant="destructive">
          <AlertDescription>{getApiErrorMessage(rule.error, t)}</AlertDescription>
        </Alert>
      )}
      <div className="min-h-0 flex-1 overflow-y-auto pr-1">
        <div className="grid grid-cols-2 gap-x-4">
          {lanePicker("from")}
          {lanePicker("to")}
        </div>
        <FormSection title={t("Financial:Dimensions")}>
          <div className="grid grid-cols-3 gap-x-4 gap-y-3">
            {dimension("transportUnitId", "Financial:TransportUnit", lookups.transportUnits, "TransportUnit")}
            {dimension("productId", "Financial:Product", lookups.products, "Product")}
            {dimension("vehicleTypeId", "Financial:VehicleType", lookups.vehicleTypes, "VehicleType")}
            <FormField label={t("Financial:RowQuantity")} htmlFor={id("rowQuantity")} required floating="always">
              <Select items={quantityItems} value={values.rowQuantity} onValueChange={(next) => set("rowQuantity", (next as QuantitySource | null) ?? 0)} disabled={readOnly}>
                <SelectTrigger id={id("rowQuantity")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {quantityItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Financial:MinAmount")} htmlFor={id("minAmount")} error={error("minAmount")}>
              <Input id={id("minAmount")} inputMode="decimal" value={values.minAmount} disabled={readOnly} onChange={(e) => set("minAmount", e.target.value)} onBlur={() => blur("minAmount")} />
            </FormField>
            <FormField label={t("Financial:MaxAmount")} htmlFor={id("maxAmount")} error={error("maxAmount")}>
              <Input id={id("maxAmount")} inputMode="decimal" value={values.maxAmount} disabled={readOnly} onChange={(e) => set("maxAmount", e.target.value)} onBlur={() => blur("maxAmount")} />
            </FormField>
          </div>
        </FormSection>
        <FormSection title={`${t("Financial:Brackets")} (${t(keyOf(QUANTITY_SOURCES, values.rowQuantity))})`}>
          <div className="flex h-64 flex-col gap-2">
            {error("brackets") && <p className="text-label text-destructive">{error("brackets")}</p>}
            <LazyLinesGrid<BracketRow>
              /* A new column set is a new grid instance. */
              key={`ladder-${columnCount}`}
              rows={bracketRows}
              onRowsChange={(rows) => {
                set("brackets", rows.map((row) => fromBracketRow(row, columnCount)));
                blur("brackets");
              }}
              getRowId={(row) => row.key}
              columnDefs={bracketColumns}
              readOnly={readOnly}
              /* The open row stays: it is the last row and cannot be removed. */
              canRemoveRow={(row) => row.upperBound != null}
              toolbar={
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={values.rowQuantity === 0}
                  onClick={() => {
                    /* A new bracket goes before the open row, one above the previous bound. */
                    const bounded = values.brackets.filter((b) => b.upperBound != null);
                    const lastBound = bounded.at(-1)?.upperBound ?? 0;
                    const open = values.brackets.find((b) => b.upperBound == null) ?? newBracket(columnCount, null);
                    set("brackets", [...bounded, newBracket(columnCount, lastBound + 1), open]);
                  }}
                >
                  <Plus /> {t("Financial:AddBracket")}
                </Button>
              }
            />
          </div>
        </FormSection>
      </div>
      {inPlace.dialog}
      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={(next) => {
          if (!next) setConfirmDelete(false);
        }}
        title={t("Financial:DeleteRule")}
        description={`${t("Financial:DeleteRuleQuestion").replace("{0}", String(rule.data?.position ?? ""))} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteRule}
      />
    </div>
  );
}
