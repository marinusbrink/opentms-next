import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import { TimeSlotEditor } from "@/apps/master-data/editors/TimeSlotEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { timeOfDay, timeSlots, type TimeSlotRow } from "@/domains/orders/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 51: the Orders domain group of the Master data app, Transport orders → Time slots. */
export function TimeSlotsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = timeSlots.useGridFetcher(extra);
  const deleteOne = timeSlots.useDelete();
  const bulk = timeSlots.useBulkDelete();

  const columnDefs = useMemo<ColDef<TimeSlotRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "startTime",
        headerName: t("Orders:StartTime"),
        flex: 1,
        minWidth: 90,
        valueFormatter: (p: ValueFormatterParams<TimeSlotRow>) => timeOfDay(p.value as string | undefined),
      },
      {
        field: "endTime",
        headerName: t("Orders:EndTime"),
        flex: 1,
        minWidth: 90,
        valueFormatter: (p: ValueFormatterParams<TimeSlotRow>) => timeOfDay(p.value as string | undefined),
      },
      { field: "isOvernight", headerName: t("Orders:IsOvernight"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true, sortable: false },
    ],
    [t],
  );

  return (
    <MasterDataListView<TimeSlotRow>
      entityKey="TimeSlot"
      resource="Orders"
      permission="Orders.TimeSlots"
      gridId="masterData.timeSlots"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? row.code ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
          {t("MasterData:ShowArchived")}
        </label>
      }
      renderEditor={(state, onClose, onSaved) => (
        <TimeSlotEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
