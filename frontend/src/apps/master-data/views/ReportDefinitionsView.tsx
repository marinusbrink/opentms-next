import { useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, ArchiveRestore } from "lucide-react";
import { toast } from "sonner";

import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { BoolCell } from "@/components/ui/grid-cells";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { GridRowAction } from "@/components/ui/opentms-grid";
import { ReportDefinitionEditor } from "@/apps/master-data/editors/ReportDefinitionEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import {
  REPORT_PURPOSES,
  cultureKey,
  reportDefinitions,
  reportPurposeKey,
  reportRenderingProviderKey,
  useArchiveReportDefinition,
  useUnarchiveReportDefinition,
  type ReportDefinitionRow,
  type ReportPurpose,
  type ReportRenderingProvider,
} from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Report definitions list (design 54, UI design): Name, Purpose, Language, Provider, Default; Archived hidden.
 * Purpose (the API contract's `purpose`, as the Kind filter on the vehicles list) and Show archived are the filters;
 * Archive / Unarchive are confirmed row actions, as the archivable Master Data lists. */

const ALL_PURPOSES = "all";

export function ReportDefinitionsView() {
  const { t } = useL();
  const [showArchived, setShowArchived] = useState(false);
  const [purpose, setPurpose] = useState<string>(ALL_PURPOSES);
  const extra = useMemo(
    () => ({ includeArchived: showArchived, purpose: purpose === ALL_PURPOSES ? undefined : (Number(purpose) as ReportPurpose) }),
    [showArchived, purpose],
  );
  const purposeItems = useMemo(
    () => [{ value: ALL_PURPOSES, label: t("Platform:AllPurposes") }, ...REPORT_PURPOSES.map((p) => ({ value: String(p.value), label: t(p.key) }))],
    [t],
  );
  const fetchRows = reportDefinitions.useGridFetcher(extra);
  const deleteOne = reportDefinitions.useDelete();
  const bulk = reportDefinitions.useBulkDelete();
  const archive = useArchiveReportDefinition();
  const unarchive = useUnarchiveReportDefinition();
  const canUpdate = useIsGranted("Platform.ReportDefinitions.Update");

  const [archiveTarget, setArchiveTarget] = useState<{ row: ReportDefinitionRow; refresh: () => void } | null>(null);
  const [busy, setBusy] = useState(false);

  const columnDefs = useMemo<ColDef<ReportDefinitionRow>[]>(
    () => [
      { field: "name", headerName: t("Platform:Name"), flex: 2, minWidth: 200 },
      {
        field: "purpose",
        headerName: t("Platform:Purpose"),
        valueFormatter: (p) => t(reportPurposeKey(p.value as ReportPurpose | undefined)),
        flex: 2,
        minWidth: 180,
      },
      { field: "culture", headerName: t("Platform:Culture"), valueFormatter: (p) => (p.value ? t(cultureKey(p.value as string)) : ""), flex: 1, minWidth: 120 },
      {
        field: "provider",
        headerName: t("Platform:Provider"),
        valueFormatter: (p) => t(reportRenderingProviderKey(p.value as ReportRenderingProvider | undefined)),
        flex: 1,
        minWidth: 160,
      },
      { field: "isDefault", headerName: t("Platform:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
      { field: "isArchived", headerName: t("Platform:IsArchived"), cellRenderer: BoolCell, flex: 1, minWidth: 100, hide: true },
    ],
    [t],
  );

  const extraRowActions = useCallback(
    (row: ReportDefinitionRow, refresh: () => void): GridRowAction[] =>
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
      toast.success(t(row.isArchived ? "Platform:ReportDefinitionUnarchived" : "Platform:ReportDefinitionArchived"));
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
      <MasterDataListView<ReportDefinitionRow>
        entityKey="ReportDefinition"
        resource="Platform"
        permission="Platform.ReportDefinitions"
        gridId="platform.reportDefinitions"
        columnDefs={columnDefs}
        fetchRows={fetchRows}
        rowName={(row) => row.name ?? ""}
        deleteOne={deleteOne}
        bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
        filters={
          <>
            <div className="w-56">
              <Select items={purposeItems} value={purpose} onValueChange={(next) => setPurpose((next as string | null) ?? ALL_PURPOSES)}>
                <SelectTrigger aria-label={t("Platform:Purpose")} className="h-9 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {purposeItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={showArchived} onCheckedChange={(next) => setShowArchived(next === true)} />
              {t("Platform:IncludeArchived")}
            </label>
          </>
        }
        extraRowActions={extraRowActions}
        renderEditor={(state, onClose, onSaved, openRecord) => (
          <ReportDefinitionEditor
            key={`${state.mode}-${state.id ?? "new"}`}
            mode={state.mode}
            id={state.id}
            readOnly={state.readOnly}
            open
            onClose={onClose}
            onSaved={onSaved}
            onOpenRecord={openRecord}
          />
        )}
      />
      <ConfirmDialog
        open={archiveTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setArchiveTarget(null);
        }}
        title={t(targetArchived ? "Platform:UnarchiveReportDefinition" : "Platform:ArchiveReportDefinition")}
        description={t(targetArchived ? "Platform:UnarchiveReportDefinitionQuestion" : "Platform:ArchiveReportDefinitionQuestion").replace("{0}", archiveTarget?.row.name ?? "")}
        confirmLabel={t(targetArchived ? "Platform:Unarchive" : "Platform:Archive")}
        onConfirm={toggleArchive}
      />
    </>
  );
}
