import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { DocumentTypeEditor } from "@/apps/master-data/editors/DocumentTypeEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  documentTypes,
  useArchiveDocumentType,
  useUnarchiveDocumentType,
  type DocumentTypeRow,
} from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Document types list (design 54, UI design): Code, Name (in the user's language), Freight document, Include with
 * invoice; Archived hidden. The wildcard searches code, name and description. Show archived as the filter; Archive /
 * Unarchive as confirmed row actions, as the archivable Master Data lists. */

export function DocumentTypesView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const extra = useMemo(() => ({ includeArchived: showArchived }), [showArchived]);
  const fetchRows = documentTypes.useGridFetcher(extra);
  const deleteOne = documentTypes.useDelete();
  const bulk = documentTypes.useBulkDelete();
  const archive = useArchiveDocumentType();
  const unarchive = useUnarchiveDocumentType();
  const canUpdate = useIsGranted("Platform.DocumentTypes.Update");

  const [archiveTarget, setArchiveTarget] = useState<{ row: DocumentTypeRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<DocumentTypeRow>[]>(
    () => [
      { field: "code", headerName: t("Platform:Code"), flex: 1, minWidth: 120 },
      { field: "name", headerName: t("Platform:Name"), flex: 2, minWidth: 200 },
      { field: "isFreightDocument", headerName: t("Platform:IsFreightDocument"), cellRenderer: BoolCell, flex: 1, minWidth: 140 },
      { field: "includeWithInvoice", headerName: t("Platform:IncludeWithInvoice"), cellRenderer: BoolCell, flex: 1, minWidth: 140 },
      { field: "isArchived", headerName: t("Platform:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: DocumentTypeRow, refresh: () => void): GridRowAction[] =>
      canUpdate
        ? [
            {
              id: "archive",
              labelKey: row.isArchived ? "Platform:Unarchive" : "Platform:Archive",
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
      toast.success(t(row.isArchived ? "Platform:DocumentTypeUnarchived" : "Platform:DocumentTypeArchived"));
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
      <MasterDataListView<DocumentTypeRow>
        entityKey="DocumentType"
        resource="Platform"
        permission="Platform.DocumentTypes"
        gridId="platform.documentTypes"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={(row) => row.code ?? ""}
        deleteOne={deleteOne}
        bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
        filters={
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
            {t("Platform:IncludeArchived")}
          </label>
        }
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved) => (
          <DocumentTypeEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "Platform:UnarchiveDocumentType" : "Platform:ArchiveDocumentType")}
        description={t(targetArchived ? "Platform:UnarchiveDocumentTypeQuestion" : "Platform:ArchiveDocumentTypeQuestion").replace("{0}", archiveTarget?.row.code ?? "")}
        confirmLabel={t(targetArchived ? "Platform:Unarchive" : "Platform:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
