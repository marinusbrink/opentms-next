import { useCallback, useMemo } from "react";
import type { ColDef } from "ag-grid-community";

import { BoolCell } from "@/components/ui/grid-cells";
import { NumberSequenceEditor } from "@/apps/master-data/editors/NumberSequenceEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { numberSequenceSchemeKey, numberSequences, type NumberSequenceRow, type NumberSequenceScheme } from "@/domains/platform/master-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Number sequences list (design 49, UI design): Name, Scheme, Example number (the first number of today's
 * period), In use. Example and In use are computed by the server and not sortable. A sequence in use cannot be
 * deleted: its Delete item is disabled with the reason; a bulk delete reports it as skipped (server-checked). */

export function NumberSequencesView() {
  const { t } = useL();
  const fetchRows = numberSequences.useGridFetcher();
  const deleteOne = numberSequences.useDelete();
  const bulk = numberSequences.useBulkDelete();

  const deleteDisabledReasonKey = useCallback(
    (row: NumberSequenceRow) => (row.isInUse ? "Platform:NumberSequenceInUseCannotDelete" : undefined),
    [],
  );

  const columnDefs = useMemo<ColDef<NumberSequenceRow>[]>(
    () => [
      { field: "name", headerName: t("Platform:Name"), flex: 2, minWidth: 200 },
      {
        field: "scheme",
        headerName: t("Platform:Scheme"),
        valueFormatter: (p) => t(numberSequenceSchemeKey(p.value as NumberSequenceScheme | undefined)),
        flex: 2,
        minWidth: 180,
      },
      { field: "exampleNumber", headerName: t("Platform:ExampleNumber"), sortable: false, flex: 1, minWidth: 150 },
      { field: "isInUse", headerName: t("Platform:IsInUse"), cellRenderer: BoolCell, sortable: false, flex: 1, minWidth: 100 },
    ],
    [t],
  );

  return (
    <MasterDataListView<NumberSequenceRow>
      entityKey="NumberSequence"
      resource="Platform"
      permission="Platform.NumberSequences"
      gridId="platform.numberSequences"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection }) }}
      deleteDisabledReasonKey={deleteDisabledReasonKey}
      renderEditor={(state, onClose, onSaved) => (
        <NumberSequenceEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
