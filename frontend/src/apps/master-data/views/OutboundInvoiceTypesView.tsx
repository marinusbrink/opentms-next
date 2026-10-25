import { useCallback, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { OutboundInvoiceTypeEditor } from "@/apps/master-data/editors/OutboundInvoiceTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  OUTBOUND_INVOICE_KINDS,
  keyOf,
  outboundInvoiceTypes,
  useSetDefaultOutboundInvoiceType,
  type OutboundInvoiceTypeRow,
} from "@/domains/financial/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 52: the Financial domain group of the Master data app, Invoicing → Outbound invoice types. One default per
 * kind: "Set as default" moves the default of the row's own kind only. */
export function OutboundInvoiceTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = outboundInvoiceTypes.useGridFetcher(extra);
  const deleteOne = outboundInvoiceTypes.useDelete();
  const bulk = outboundInvoiceTypes.useBulkDelete();
  const setDefault = useSetDefaultOutboundInvoiceType();
  const canUpdate = useIsGranted("Financial.OutboundInvoiceTypes.Update");

  const columnDefs = useMemo<ColDef<OutboundInvoiceTypeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "kind",
        headerName: t("MasterData:Kind"),
        flex: 1,
        minWidth: 120,
        valueFormatter: (p: ValueFormatterParams<OutboundInvoiceTypeRow>) => t(keyOf(OUTBOUND_INVOICE_KINDS, p.value as number | undefined)),
      },
      { field: "numberingExample", headerName: t("Financial:NumberingExample"), flex: 1, minWidth: 140, sortable: false },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: OutboundInvoiceTypeRow, refresh: () => void): GridRowAction[] =>
      canUpdate && !row.isDefault
        ? [
            {
              id: "set-default",
              labelKey: "MasterData:SetAsDefault",
              icon: Star,
              disabled: row.isArchived === true,
              disabledReasonKey: "MasterData:ArchivedCannotBeDefault",
              onClick: () => {
                setDefault.mutateAsync(row.id!).then(
                  () => {
                    toast.success(t("Financial:OutboundInvoiceTypeSaved"));
                    refresh();
                  },
                  (err: unknown) => toast.error(getApiErrorMessage(err, t)),
                );
              },
            },
          ]
        : [],
    [canUpdate, setDefault, t],
  );

  return (
    <MasterDataListView<OutboundInvoiceTypeRow>
      entityKey="OutboundInvoiceType"
      resource="Financial"
      permission="Financial.OutboundInvoiceTypes"
      gridId="masterData.outboundInvoiceTypes"
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
      extraRowActions={extraRowActions}
      renderEditor={(state, onClose, onSaved) => (
        <OutboundInvoiceTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
