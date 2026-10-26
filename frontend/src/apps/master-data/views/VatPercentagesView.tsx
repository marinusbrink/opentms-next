import { useCallback, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Star } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell, PercentCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { VatPercentageEditor } from "@/domains/financial/components/VatPercentageEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { VAT_KINDS, keyOf, useSetDefaultVatPercentage, vatPercentages, type VatPercentageRow } from "@/domains/financial/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 52: the Financial domain group of the Master data app, Invoicing → VAT percentages. */
export function VatPercentagesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = vatPercentages.useGridFetcher(extra);
  const deleteOne = vatPercentages.useDelete();
  const bulk = vatPercentages.useBulkDelete();
  const setDefault = useSetDefaultVatPercentage();
  const canUpdate = useIsGranted("Financial.VatPercentages.Update");

  const columnDefs = useMemo<ColDef<VatPercentageRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "kind",
        headerName: t("MasterData:Kind"),
        flex: 1,
        minWidth: 140,
        valueFormatter: (p: ValueFormatterParams<VatPercentageRow>) => t(keyOf(VAT_KINDS, p.value as number | undefined)),
      },
      { field: "countryCode", headerName: t("MasterData:Country"), flex: 1, minWidth: 90 },
      { field: "currentPercentage", headerName: t("Financial:CurrentPercentage"), cellRenderer: PercentCell, type: "rightAligned", sortable: false, flex: 1, minWidth: 110 },
      { field: "isDefault", headerName: t("MasterData:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 90 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: VatPercentageRow, refresh: () => void): GridRowAction[] =>
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
                    toast.success(t("Financial:VatPercentageSaved"));
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
    <MasterDataListView<VatPercentageRow>
      entityKey="VatPercentage"
      resource="Financial"
      permission="Financial.VatPercentages"
      gridId="masterData.vatPercentages"
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
        <VatPercentageEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
