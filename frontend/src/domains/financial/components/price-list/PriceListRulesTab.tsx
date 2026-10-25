import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef, ICellRendererParams, ValueFormatterParams } from "ag-grid-community";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { NumberCell } from "@/components/ui/grid-cells";
import type { GridRowAction, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { QUANTITY_SOURCES, keyOf, useDeletePriceListRule, usePriceListRulesFetcher, type PriceListRuleRow, type QuantitySource } from "@/domains/financial/price-lists";
import { geographicalRegions, products, transportUnits, vehicleTypes } from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { PriceListRuleDetail, type RuleLookups } from "./PriceListRuleDetail";
import type { ColumnLine } from "./rule-ladder";

const _OpenTmsGrid = lazy(() => import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })));

function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

/* The Rules tab of the price list editor (design 57 §UI): a master-detail. Left the server-side grid of the list's rules
 * (paged under the list, design 57 build note 3) with the tier badge, the lane, the dimensions, the row quantity, the
 * amounts and the bracket count, filtered by tier ("Overrides" = tier 1–3, "Defaults" = tier 0); right the selected
 * rule. The grid is an embedded normal grid (UI-BSP); the rows carry the relation and region names, the dimension
 * names come from the Master Data lookups (build note 4). */

export interface PriceListRulesTabProps {
  priceListId: string;
  /** The list's SAVED columns. */
  columns: ColumnLine[];
  /** The form's columns differ from the saved ones: rules are edited against the saved shape. */
  columnsDirty: boolean;
  readOnly: boolean;
  /** Bumped by a command that changed the rules outside this tab (adjust, import). */
  refreshKey: number;
}

type TierFilter = "all" | "overrides" | "defaults" | "1" | "2" | "3";

export function PriceListRulesTab({ priceListId, columns, columnsDirty, readOnly, refreshKey }: PriceListRulesTabProps) {
  const { t } = useL();
  const f = useFormat();
  const [tierFilter, setTierFilter] = useState<TierFilter>("all");
  const [localRefresh, setLocalRefresh] = useState(0);
  const [selected, setSelected] = useState<{ ruleId: string | null } | null>(null);
  const [toDelete, setToDelete] = useState<PriceListRuleRow | null>(null);
  const remove = useDeletePriceListRule();

  /* "Overrides" and "Defaults" group by tier: tier 0 = defaults; the server filters one tier at a time, so overrides
   * (1–3) are fetched unfiltered and the grouping shows in the badge; the single-tier choices narrow further. */
  const tier = tierFilter === "1" || tierFilter === "2" || tierFilter === "3" ? Number(tierFilter) : tierFilter === "defaults" ? 0 : undefined;
  const fetchRows = usePriceListRulesFetcher(priceListId, tier, refreshKey + localRefresh);
  const refresh = useCallback(() => setLocalRefresh((k) => k + 1), []);

  const regionLookup = geographicalRegions.useLookup();
  const unitLookup = transportUnits.useLookup();
  const productLookup = products.useLookup();
  const vehicleTypeLookup = vehicleTypes.useLookup();
  const toOptions = (items: { id?: string; code?: string | null; name?: string | null }[] | undefined) => (items ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? i.code ?? "" }));
  const lookups = useMemo<RuleLookups>(
    () => ({ regions: toOptions(regionLookup.data), transportUnits: toOptions(unitLookup.data), products: toOptions(productLookup.data), vehicleTypes: toOptions(vehicleTypeLookup.data) }),
    [regionLookup.data, unitLookup.data, productLookup.data, vehicleTypeLookup.data],
  );
  const nameOf = useCallback((options: { value: string; label: string }[], id: string | null | undefined) => (id ? (options.find((o) => o.value === id)?.label ?? "…") : ""), []);

  const tierItems = useMemo(
    () => [
      { value: "all" as TierFilter, label: t("Financial:AllRules") },
      { value: "overrides" as TierFilter, label: t("Financial:Overrides") },
      { value: "defaults" as TierFilter, label: t("Financial:Defaults") },
      ...(["1", "2", "3"] as const).map((n) => ({ value: n as TierFilter, label: `${t("Financial:Tier")} ${n}` })),
    ],
    [t],
  );

  const columnDefs = useMemo<ColDef<PriceListRuleRow>[]>(
    () => [
      { field: "position", headerName: t("Financial:Position"), width: 90, cellRenderer: NumberCell, type: "rightAligned" },
      {
        field: "tier",
        headerName: t("Financial:Tier"),
        width: 110,
        cellRenderer: (p: ICellRendererParams<PriceListRuleRow, number>) =>
          p.value == null ? null : <Badge variant={p.value > 0 ? "default" : "secondary"}>{p.value > 0 ? `${t("Financial:Overrides")} ${p.value}` : t("Financial:Defaults")}</Badge>,
      },
      {
        colId: "lane",
        headerName: t("Financial:Lane"),
        flex: 2,
        minWidth: 200,
        sortable: false,
        valueGetter: (p) => `${p.data?.fromRelationName ?? p.data?.fromRegionName ?? "*"} → ${p.data?.toRelationName ?? p.data?.toRegionName ?? "*"}`,
      },
      {
        colId: "dimensions",
        headerName: t("Financial:Dimensions"),
        flex: 2,
        minWidth: 180,
        sortable: false,
        valueGetter: (p) =>
          [nameOf(lookups.transportUnits, p.data?.transportUnitId), nameOf(lookups.products, p.data?.productId), nameOf(lookups.vehicleTypes, p.data?.vehicleTypeId)].filter(Boolean).join(", ") || t("Financial:Any"),
      },
      { field: "rowQuantity", headerName: t("Financial:RowQuantity"), flex: 1, minWidth: 130, valueFormatter: (p: ValueFormatterParams<PriceListRuleRow>) => t(keyOf(QUANTITY_SOURCES, p.value as QuantitySource | undefined)) },
      { field: "minAmount", headerName: t("Financial:MinAmount"), width: 120, sortable: false, type: "rightAligned", valueFormatter: (p) => (p.value == null ? "" : f.amount(p.value as number)) },
      { field: "maxAmount", headerName: t("Financial:MaxAmount"), width: 120, sortable: false, type: "rightAligned", valueFormatter: (p) => (p.value == null ? "" : f.amount(p.value as number)) },
      { field: "bracketCount", headerName: t("Financial:Brackets"), width: 100, sortable: false, cellRenderer: NumberCell, type: "rightAligned" },
    ],
    [t, f, lookups, nameOf],
  );

  const rowActions = useCallback(
    (rows: PriceListRuleRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      if (!single) return [];
      return [
        { id: "edit", labelKey: readOnly ? "Open" : "Edit", icon: Pencil, onClick: () => setSelected({ ruleId: single.id ?? null }) },
        ...(readOnly ? [] : [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => setToDelete(single) }]),
      ];
    },
    [readOnly],
  );

  const deleteRule = async () => {
    if (!toDelete?.id) return;
    try {
      await remove.mutateAsync({ priceListId, ruleId: toDelete.id });
      setToDelete(null);
      if (selected?.ruleId === toDelete.id) setSelected(null);
      toast.success(t("Financial:RuleDeleted"));
      refresh();
    } catch (err) {
      setToDelete(null);
      toast.error(getApiErrorMessage(err, t));
    }
  };

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      {columnsDirty && (
        <Alert>
          <AlertDescription>{t("Financial:SaveColumnsFirstHint")}</AlertDescription>
        </Alert>
      )}
      <div className="flex min-h-0 flex-1 gap-4">
        <div className={`flex min-h-0 min-w-0 flex-col gap-2 ${selected ? "w-1/2" : "flex-1"}`}>
          <div className="flex items-center gap-2">
            {!readOnly && (
              <Button type="button" size="sm" onClick={() => setSelected({ ruleId: null })} disabled={columnsDirty}>
                <Plus /> {t("Financial:AddRule")}
              </Button>
            )}
          </div>
          <div className="min-h-0 flex-1">
            <Suspense fallback={<Skeleton className="h-full w-full" />}>
              <OpenTmsGrid<PriceListRuleRow>
                gridId={`masterData.priceListRules`}
                columnDefs={columnDefs}
                fetchRows={fetchRows}
                onOpen={(row) => setSelected({ ruleId: row.id ?? null })}
                rowActions={rowActions}
                filters={
                  <div className="w-44">
                    <Select items={tierItems} value={tierFilter} onValueChange={(next) => setTierFilter((next as TierFilter | null) ?? "all")}>
                      <SelectTrigger aria-label={t("Financial:Tier")} className="h-9 w-full bg-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {tierItems.map((item) => (
                          <SelectItem key={String(item.value)} value={item.value}>
                            {item.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                }
              />
            </Suspense>
          </div>
        </div>
        {selected && (
          <div className="min-h-0 w-1/2 min-w-0 rounded-lg border border-border p-3">
            <PriceListRuleDetail
              key={selected.ruleId ?? "new"}
              priceListId={priceListId}
              ruleId={selected.ruleId}
              columns={columns}
              readOnly={readOnly || columnsDirty}
              lookups={lookups}
              onSaved={(rule) => {
                setSelected({ ruleId: rule.id ?? null });
                refresh();
              }}
              onDeleted={() => {
                setSelected(null);
                refresh();
              }}
              onClose={() => setSelected(null)}
            />
          </div>
        )}
      </div>
      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(open) => {
          if (!open) setToDelete(null);
        }}
        title={t("Financial:DeleteRule")}
        description={`${t("Financial:DeleteRuleQuestion").replace("{0}", String(toDelete?.position ?? ""))} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteRule}
      />
    </div>
  );
}
