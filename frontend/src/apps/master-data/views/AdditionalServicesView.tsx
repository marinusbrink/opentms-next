import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";

import { Checkbox } from "@/components/ui/checkbox";
import { BoolCell } from "@/components/ui/grid-cells";
import { AdditionalServiceEditor } from "@/apps/master-data/editors/AdditionalServiceEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { SERVICE_UNITS, additionalServices, keyOf, type AdditionalServiceRow } from "@/domains/financial/reference-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 52: the Financial domain group of the Master data app, Pricing → Additional services. */
export function AdditionalServicesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = additionalServices.useGridFetcher(extra);
  const deleteOne = additionalServices.useDelete();
  const bulk = additionalServices.useBulkDelete();

  const columnDefs = useMemo<ColDef<AdditionalServiceRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 100 },
      { field: "name", headerName: t("MasterData:Name"), flex: 2, minWidth: 180 },
      {
        field: "unit",
        headerName: t("Financial:Unit"),
        flex: 1,
        minWidth: 110,
        valueFormatter: (p: ValueFormatterParams<AdditionalServiceRow>) => t(keyOf(SERVICE_UNITS, p.value as number | undefined)),
      },
      {
        field: "vatPercentageName",
        headerName: t("Financial:Vat"),
        flex: 1,
        minWidth: 140,
        /* Empty = the tenant's default VAT percentage. */
        valueFormatter: (p: ValueFormatterParams<AdditionalServiceRow>) => (p.value as string | null | undefined) || t("Financial:VatDefaultPlaceholder"),
      },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 90, hide: true },
      { field: "description", headerName: t("MasterData:Description"), flex: 2, minWidth: 180, hide: true },
    ],
    [t],
  );

  return (
    <MasterDataListView<AdditionalServiceRow>
      entityKey="AdditionalService"
      resource="Financial"
      permission="Financial.AdditionalServices"
      gridId="masterData.additionalServices"
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
        <AdditionalServiceEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
