import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { OrderAttributeDefinitionEditor } from "@/apps/master-data/editors/OrderAttributeDefinitionEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  ORDER_ATTRIBUTE_TARGETS,
  orderAttributeDefinitions,
  orderAttributeTargetKey,
  type OrderAttributeDefinitionRow,
  type OrderAttributeTarget,
} from "@/domains/orders/order-attribute-definitions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 58 "Attribute definitions"): the Orders domain group of the Master data app, Transport orders →
 * Order attributes. The target filter narrows the list to the order's or the lines' attributes. */
export function OrderAttributeDefinitionsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [target, setTarget] = useState<OrderAttributeTarget | null>(null);
  const extra = useMemo(() => ({ includeArchived: showArchived, target: target ?? undefined }), [showArchived, target]);
  const fetchRows = orderAttributeDefinitions.useGridFetcher(extra);
  const deleteOne = orderAttributeDefinitions.useDelete();
  const bulk = orderAttributeDefinitions.useBulkDelete();

  const targetItems = useMemo(() => [{ value: null as OrderAttributeTarget | null, label: t("Orders:AllTargets") }, ...ORDER_ATTRIBUTE_TARGETS.map((o) => ({ value: o.value as OrderAttributeTarget | null, label: t(o.key) }))], [t]);

  const columnDefs = useMemo<ColDef<OrderAttributeDefinitionRow>[]>(
    () => [
      {
        field: "target",
        headerName: t("Orders:AttributeTarget"),
        flex: 1,
        minWidth: 110,
        valueFormatter: (p: ValueFormatterParams<OrderAttributeDefinitionRow>) => t(orderAttributeTargetKey(p.value as OrderAttributeTarget | undefined)),
      },
      { field: "key", headerName: t("Orders:AttributeKey"), flex: 1, minWidth: 140 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "position", headerName: t("Orders:Position"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<OrderAttributeDefinitionRow>
      entityKey="OrderAttributeDefinition"
      resource="Orders"
      permission="Orders.OrderAttributeDefinitions"
      gridId="masterData.orderAttributeDefinitions"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? row.key ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <>
          <div className="w-44">
            <Select items={targetItems} value={target} onValueChange={(next) => setTarget((next as OrderAttributeTarget | null) ?? null)}>
              <SelectTrigger aria-label={t("Orders:AttributeTarget")} className="h-9 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {targetItems.map((item) => (
                  <SelectItem key={item.value ?? "all"} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
            {t("MasterData:ShowArchived")}
          </label>
        </>
      }
      renderEditor={(state, onClose, onSaved) => (
        <OrderAttributeDefinitionEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
