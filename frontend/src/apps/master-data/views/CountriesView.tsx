import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { NumberCell } from "@/components/ui/grid-cells";
import { CountryEditor } from "@/apps/master-data/editors/CountryEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { countries, type CountryRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function CountriesView() {
  const { t } = useL();
  const fetchRows = countries.useGridFetcher();
  const deleteOne = countries.useDelete();
  const bulk = countries.useBulkDelete();

  const columnDefs = useMemo<ColDef<CountryRow>[]>(
    () => [
      { field: "isoAlpha2", headerName: t("MasterData:IsoAlpha2"), flex: 1, minWidth: 90 },
      { field: "isoAlpha3", headerName: t("MasterData:IsoAlpha3"), flex: 1, minWidth: 90 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "defaultCurrencyCode", headerName: t("MasterData:DefaultCurrency"), flex: 1, minWidth: 120 },
      { field: "primaryTimeZoneId", headerName: t("MasterData:PrimaryTimeZone"), flex: 2, minWidth: 160 },
      { field: "isoNumeric", headerName: t("MasterData:IsoNumeric"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 110, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<CountryRow>
      entityKey="Country"
      permission="MasterData.Countries"
      gridId="masterData.countries"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.isoAlpha2 ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      renderEditor={(state, onClose, onSaved) => (
        <CountryEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
