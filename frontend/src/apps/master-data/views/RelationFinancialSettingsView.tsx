import { useCallback, useMemo, useRef } from "react";
import type { ColDef } from "ag-grid-community";

import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import type { GridRequest } from "@/components/ui/opentms-grid";
import { RelationFinancialSettingsEditor } from "@/apps/master-data/editors/RelationFinancialSettingsEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { useRelationFinancialSettingsFetcher, type RelationFinancialSettingsRow } from "@/domains/financial/relation-financial-settings";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 57 §UI): the Financial domain group of the Master data app, Pricing → Relation financial
 * settings. The list is by relation (the search matches the relation name); there is no delete: settings exist as
 * long as the relation does, and New needs Update (the settings are created on the first edit). The editor is keyed
 * by the relation, so the view remembers which relation each row (a settings id) belongs to. */
export function RelationFinancialSettingsView() {
  const { t } = useL();
  const fetchPage = useRelationFinancialSettingsFetcher();
  const relationIdOf = useRef(new Map<string, string>());
  const fetchRows = useCallback(
    async (request: GridRequest) => {
      const page = await fetchPage(request);
      for (const row of page.rows) if (row.id && row.relationId) relationIdOf.current.set(row.id, row.relationId);
      return page;
    },
    [fetchPage],
  );

  const columnDefs = useMemo<ColDef<RelationFinancialSettingsRow>[]>(
    () => [
      { field: "relationName", headerName: t("Financial:Relation"), flex: 3, minWidth: 200 },
      { field: "revenuePriceListCount", headerName: t("Financial:RevenuePriceLists"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 120, sortable: false },
      { field: "costPriceListCount", headerName: t("Financial:CostPriceLists"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 120, sortable: false },
      { field: "vatPercentageName", headerName: t("Financial:Vat"), flex: 2, minWidth: 140, sortable: false },
      { field: "paymentTermDays", headerName: t("Financial:PaymentTermDays"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 120 },
      { field: "chargeFixedPriceByDefault", headerName: t("Financial:ChargeFixedPriceByDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 120, sortable: false },
      { field: "currencyCode", headerName: t("Financial:Currency"), flex: 1, minWidth: 100 },
    ],
    [t],
  );

  /* No delete endpoint exists (design 57); the Delete permission is never granted, so these are never called. */
  const noDelete = useMemo(() => ({ mutateAsync: () => Promise.reject(new Error("Relation financial settings cannot be deleted.")) }), []);

  return (
    <MasterDataListView<RelationFinancialSettingsRow>
      entityKey="RelationFinancialSettings"
      resource="Financial"
      permission="Financial.RelationFinancialSettings"
      createPermission="Financial.RelationFinancialSettings.Update"
      gridId="masterData.relationFinancialSettings"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.relationName ?? ""}
      deleteOne={noDelete}
      bulkDelete={noDelete}
      renderEditor={(state, onClose, onSaved) => (
        <RelationFinancialSettingsEditor
          key={`${state.mode}-${state.id ?? "new"}`}
          mode={state.mode}
          id={state.id}
          relationId={state.id ? relationIdOf.current.get(state.id) : undefined}
          readOnly={state.readOnly}
          open
          onClose={onClose}
          onSaved={onSaved}
        />
      )}
    />
  );
}
