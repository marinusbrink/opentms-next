import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { ViaDepotRuleEditor } from "@/apps/master-data/editors/ViaDepotRuleEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { viaDepotRuleName, viaDepotRules, viaDepotScopeKey, type ViaDepotRuleRow, type ViaDepotScope } from "@/domains/planning-execution/via-depot-rules";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 59 "ViaDepotRule"): the Planning & Execution domain group of the Master data app, Trips → Via
 * depot rules. The Master Data names (relation, region, depot, city) are looked up per page and are not sortable. */
export function ViaDepotRulesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = viaDepotRules.useGridFetcher(extra);
  const deleteOne = viaDepotRules.useDelete();
  const bulk = viaDepotRules.useBulkDelete();

  const columnDefs = useMemo<ColDef<ViaDepotRuleRow>[]>(
    () => [
      { field: "scope", headerName: t("PlanningExecution:Scope"), flex: 1, minWidth: 110, valueFormatter: (p: ValueFormatterParams<ViaDepotRuleRow>) => t(viaDepotScopeKey(p.value as ViaDepotScope | undefined)) },
      { field: "relationName", headerName: t("PlanningExecution:Relation"), flex: 2, minWidth: 160, sortable: false },
      { field: "geographicalRegionName", headerName: t("PlanningExecution:GeographicalRegion"), flex: 2, minWidth: 160, sortable: false },
      { field: "depotRelationName", headerName: t("PlanningExecution:DepotRelation"), flex: 2, minWidth: 160, sortable: false },
      { field: "depotAddressCity", headerName: t("PlanningExecution:DepotAddress"), flex: 1, minWidth: 120, sortable: false },
      { field: "overnight", headerName: t("PlanningExecution:Overnight"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "position", headerName: t("PlanningExecution:Position"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<ViaDepotRuleRow>
      entityKey="ViaDepotRule"
      resource="PlanningExecution"
      permission="PlanningExecution.ViaDepotRules"
      gridId="masterData.viaDepotRules"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={viaDepotRuleName}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <ViaDepotRuleEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
