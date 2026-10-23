import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import { InboundInvoiceTypeEditor } from "@/apps/master-data/editors/InboundInvoiceTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { INBOUND_INVOICE_KINDS, inboundInvoiceTypes, keyOf, type InboundInvoiceTypeRow } from "@/domains/financial/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 52: the Financial domain group of the Master data app, Invoicing → Inbound invoice types. */
export function InboundInvoiceTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = inboundInvoiceTypes.useGridFetcher(extra);
  const deleteOne = inboundInvoiceTypes.useDelete();
  const bulk = inboundInvoiceTypes.useBulkDelete();

  const columnDefs = useMemo<ColDef<InboundInvoiceTypeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "kind",
        headerName: t("MasterData:Kind"),
        flex: 1,
        minWidth: 140,
        valueFormatter: (p: ValueFormatterParams<InboundInvoiceTypeRow>) => t(keyOf(INBOUND_INVOICE_KINDS, p.value as number | undefined)),
      },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<InboundInvoiceTypeRow>
      entityKey="InboundInvoiceType"
      resource="Financial"
      permission="Financial.InboundInvoiceTypes"
      gridId="masterData.inboundInvoiceTypes"
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
        <InboundInvoiceTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
