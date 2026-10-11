import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, NumberCell } from "@/components/ui/grid-cells";
import { CurrencyEditor } from "@/apps/master-data/editors/CurrencyEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { currencies, type CurrencyRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function CurrenciesView() {
  const { t } = useL();
  const [activeOnly, setActiveOnly] = useState(true);
  const extra = useMemo(() => ({ isActive: activeOnly ? true : undefined }), [activeOnly]);
  const fetchRows = currencies.useGridFetcher(extra);
  const deleteOne = currencies.useDelete();
  const bulk = currencies.useBulkDelete();

  const columnDefs = useMemo<ColDef<CurrencyRow>[]>(
    () => [
      { field: "isoCode", headerName: t("MasterData:IsoCode"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "symbol", headerName: t("MasterData:Symbol"), flex: 1, minWidth: 90 },
      { field: "minorUnits", headerName: t("MasterData:MinorUnits"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110 },
      { field: "isActive", headerName: t("MasterData:IsActive"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "isoNumeric", headerName: t("MasterData:IsoNumeric"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<CurrencyRow>
      entityKey="Currency"
      permission="MasterData.Currencies"
      gridId="masterData.currencies"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.isoCode ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={activeOnly} onCheckedChange={(next) => setActiveOnly(next === true)} />
          {t("MasterData:ActiveOnly")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <CurrencyEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
