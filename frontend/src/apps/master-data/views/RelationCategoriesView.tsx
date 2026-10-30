import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { RelationCategoryEditor } from "@/domains/master-data/components/RelationCategoryEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { relationCategories, type RelationCategoryRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function RelationCategoriesView() {
  const { t } = useL();
  const fetchRows = relationCategories.useGridFetcher();
  const deleteOne = relationCategories.useDelete();
  const bulk = relationCategories.useBulkDelete();

  const columnDefs = useMemo<ColDef<RelationCategoryRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "description", headerName: t("MasterData:Description"), flex: 3, minWidth: 200 },
    ],
    [t],
  );

  return (
    <MasterDataListView<RelationCategoryRow>
      entityKey="RelationCategory"
      permission="MasterData.RelationCategories"
      gridId="masterData.relationCategories"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      renderEditor={(state, onClose, onSaved) => (
        <RelationCategoryEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
