import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { AddressTypeEditor } from "@/apps/master-data/editors/AddressTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { addressTypeKindKey, addressTypes, type AddressTypeRow } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

export function AddressTypesView() {
  const { t } = useL();
  const fetchRows = addressTypes.useGridFetcher();
  const deleteOne = addressTypes.useDelete();
  const bulk = addressTypes.useBulkDelete();

  const columnDefs = useMemo<ColDef<AddressTypeRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      { field: "kind", headerName: t("MasterData:Kind"), flex: 1, minWidth: 140, valueFormatter: (p) => t(addressTypeKindKey(p.value)) },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<AddressTypeRow>
      entityKey="AddressType"
      permission="MasterData.AddressTypes"
      gridId="masterData.addressTypes"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      renderEditor={(state, onClose, onSaved) => (
        <AddressTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
