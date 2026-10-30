import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { NumberCell } from "@/components/ui/grid-cells";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { GeographicalRegionEditor } from "@/domains/master-data/components/GeographicalRegionEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { countries, geographicalRegions, type GeographicalRegionRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function GeographicalRegionsView() {
  const { t } = useL();
  const [countryId, setCountryId] = useState<string | null>(null);
  const extra = useMemo(() => ({ countryId: countryId ?? undefined }), [countryId]);
  const fetchRows = geographicalRegions.useGridFetcher(extra);
  const deleteOne = geographicalRegions.useDelete();
  const bulk = geographicalRegions.useBulkDelete();
  const countryLookup = countries.useLookup();
  const countryOptions = useMemo(
    () => (countryLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })),
    [countryLookup.data],
  );

  const columnDefs = useMemo<ColDef<GeographicalRegionRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "description", headerName: t("MasterData:Description"), flex: 3, minWidth: 200 },
      { field: "rangeCount", headerName: t("MasterData:RangeCount"), cellRenderer: NumberCell, type: "rightAligned", flex: 1, minWidth: 100 },
    ],
    [t],
  );

  return (
    <MasterDataListView<GeographicalRegionRow>
      entityKey="GeographicalRegion"
      permission="MasterData.GeographicalRegions"
      gridId="masterData.geographicalRegions"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <div className="w-64">
          <ReferenceCombobox id="regions-country-filter" value={countryId} onChange={setCountryId} options={countryOptions} placeholder={t("MasterData:Country")} />
        </div>
      }
      renderEditor={(state, onClose, onSaved) => (
        <GeographicalRegionEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
