import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import { ConsignmentNoteFixedDescriptionEditor } from "@/apps/master-data/editors/ConsignmentNoteFixedDescriptionEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { consignmentNoteFixedDescriptions, type ConsignmentNoteFixedDescriptionRow } from "@/domains/orders/consignment-note-fixed-descriptions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 61 (design 58 "CMR"): the Orders domain group of the Master data app, Transport orders → CMR fixed texts. */
export function ConsignmentNoteFixedDescriptionsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = consignmentNoteFixedDescriptions.useGridFetcher(extra);
  const deleteOne = consignmentNoteFixedDescriptions.useDelete();
  const bulk = consignmentNoteFixedDescriptions.useBulkDelete();

  const columnDefs = useMemo<ColDef<ConsignmentNoteFixedDescriptionRow>[]>(
    () => [
      { field: "name", headerName: t("MasterData:Name"), flex: 1, minWidth: 160 },
      { field: "text", headerName: t("Orders:Text"), flex: 3, minWidth: 260 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<ConsignmentNoteFixedDescriptionRow>
      entityKey="ConsignmentNoteFixedDescription"
      resource="Orders"
      permission="Orders.ConsignmentNoteFixedDescriptions"
      gridId="masterData.consignmentNoteFixedDescriptions"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <ConsignmentNoteFixedDescriptionEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
