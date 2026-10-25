import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { ProductRelationCodeEditor } from "@/domains/master-data/components/ProductRelationCodeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  productRelationCodes,
  useArchiveProductRelationCode,
  useUnarchiveProductRelationCode,
  type ProductRelationCodeRow,
} from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Product relation codes list (design 48, UI design; the busiest list of the run, 50,000 rows, server-side
 * paged): Code, Destination (relation name + city), Product, Transport unit; Archived hidden; the wildcard searches
 * code, relation name and product name. Show archived as the filter; Archive / Unarchive as confirmed row actions. */

export function ProductRelationCodesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = productRelationCodes.useGridFetcher(extra);
  const deleteOne = productRelationCodes.useDelete();
  const bulk = productRelationCodes.useBulkDelete();
  const archive = useArchiveProductRelationCode();
  const unarchive = useUnarchiveProductRelationCode();
  const canUpdate = useIsGranted("MasterData.ProductRelationCodes.Update");

  const [archiveTarget, setArchiveTarget] = useState<{ row: ProductRelationCodeRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<ProductRelationCodeRow>[]>(
    () => [
      { field: "code", headerName: t("MasterData:Code"), flex: 1, minWidth: 140 },
      {
        colId: "relationName",
        field: "relationName",
        headerName: t("MasterData:Destination"),
        valueGetter: (p) => [p.data?.relationName, p.data?.relationCity].filter(Boolean).join(", "),
        flex: 2,
        minWidth: 200,
      },
      { field: "productName", headerName: t("MasterData:Product"), flex: 2, minWidth: 160 },
      { field: "transportUnitName", headerName: t("MasterData:TransportUnit"), flex: 1, minWidth: 140 },
      { field: "isArchived", headerName: t("MasterData:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: ProductRelationCodeRow, refresh: () => void): GridRowAction[] =>
      canUpdate
        ? [
            {
              id: "archive",
              labelKey: row.isArchived ? "MasterData:Unarchive" : "MasterData:Archive",
              icon: row.isArchived ? ArchiveRestore : Archive,
              onClick: () => setArchiveTarget({ row, refresh }),
            },
          ]
        : [],
    [canUpdate],
  );

  const toggleArchive = async () => {
    if (!archiveTarget?.row.id) return;
    const { row, refresh } = archiveTarget;
    setBusy(true);
    try {
      await (row.isArchived ? unarchive : archive).mutateAsync(row.id!);
      setArchiveTarget(null);
      toast.success(t(row.isArchived ? "MasterData:ProductRelationCodeUnarchived" : "MasterData:ProductRelationCodeArchived"));
      refresh();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  const targetArchived = archiveTarget?.row.isArchived === true;

  return (
    <>
      <MasterDataListView<ProductRelationCodeRow>
        entityKey="ProductRelationCode"
        permission="MasterData.ProductRelationCodes"
        gridId="masterData.productRelationCodes"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={(row) => row.code ?? ""}
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
          <ProductRelationCodeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "MasterData:UnarchiveProductRelationCode" : "MasterData:ArchiveProductRelationCode")}
        description={t(targetArchived ? "MasterData:UnarchiveProductRelationCodeQuestion" : "MasterData:ArchiveProductRelationCodeQuestion").replace("{0}", archiveTarget?.row.code ?? "")}
        confirmLabel={t(targetArchived ? "MasterData:Unarchive" : "MasterData:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
