import { useEffect, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Archive, CheckCheck, Pencil, Trash2 } from "lucide-react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import type { GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { ImportProfileEditor } from "@/apps/integrations/editors/ImportProfileEditor";
import {
  DOCUMENT_FORMATS,
  IMPORT_PROFILE_STATUSES,
  enumLabel,
  enumName,
  enumOptions,
  useActivateImportProfile,
  useDeleteImportProfile,
  useImportProfilesGridFetcher,
  useOrderIntakeFeature,
  useRetireImportProfile,
  type ImportProfileRow,
  type ImportProfileStatus,
} from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Integrations → Order intake → Import profiles (design 72 "UI design"): the profiles the pipeline learned,
 * on a full-height server-side grid (UI-PAGE-001) with the status filter; no New (profiles are learned, never typed in).
 * Row actions Edit (rename, read-only rules), Activate, Retire and Delete. `?open={id}` opens a profile's editor (the
 * trace links here); the parameter is removed once it is open. Order intake is a tenant feature (UI-PERM-006). */

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

export function ImportProfilesView() {
  const { t } = useL();
  const location = useLocation();
  const navigate = useNavigate();
  const feature = useOrderIntakeFeature();
  const canView = useIsGranted("Integrations.ImportProfiles");
  const canManage = useIsGranted("Integrations.ImportProfiles.Manage");
  const [statuses, setStatuses] = useState<ImportProfileStatus[]>([]);
  const [refreshKey, setRefreshKey] = useState(0);
  const filters = useMemo(() => ({ status: statuses }), [statuses]);
  const fetchRows = useImportProfilesGridFetcher(filters, refreshKey);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [editing, setEditing] = useState<string | null>(null);
  const [toDelete, setToDelete] = useState<{ ids: string[]; name?: string } | null>(null);
  const activate = useActivateImportProfile();
  const retire = useRetireImportProfile();
  const remove = useDeleteImportProfile();
  const refresh = () => setRefreshKey((k) => k + 1);

  const search = location.search as Record<string, unknown> | undefined;
  const openId = typeof search?.open === "string" ? search.open : undefined;
  useEffect(() => {
    if (feature !== "on" || !openId) return;
    setEditing(openId);
    void navigate({
      to: location.pathname,
      search: (previous: Record<string, unknown>) => Object.fromEntries(Object.entries(previous).filter(([key]) => key !== "open")),
      replace: true,
    });
  }, [feature, openId, navigate, location.pathname]);

  const columns = useMemo<ColDef<ImportProfileRow>[]>(
    () => [
      { field: "name", headerName: t("Integrations:ImportProfileName"), flex: 1, minWidth: 180 },
      { field: "senderKey", headerName: t("Integrations:Sender"), flex: 1, minWidth: 160, valueFormatter: (p) => (p.value as string | null) || t("Integrations:AnySender") },
      { field: "format", headerName: t("Integrations:Format"), minWidth: 110, valueFormatter: (p) => enumLabel(t, "DocumentFormat", DOCUMENT_FORMATS, p.value as number) },
      { field: "status", headerName: t("Integrations:Status"), minWidth: 120, cellRenderer: (p: { value: number | undefined }) => <StatusCell entity="ImportProfile" value={enumName(IMPORT_PROFILE_STATUSES, p.value)} /> },
      { field: "version", headerName: t("Integrations:Version"), minWidth: 90, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "confirmedCount", headerName: t("Integrations:ConfirmedCount"), minWidth: 110, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "correctedCount", headerName: t("Integrations:CorrectedCount"), minWidth: 110, cellRenderer: NumberCell, type: "rightAligned" },
      { field: "lastUsedAt", headerName: t("Integrations:LastUsedAt"), minWidth: 150, cellRenderer: DateTimeCell },
    ],
    [t],
  );

  if (feature === "off") return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const runRowAction = (action: () => Promise<unknown>, successKey: string) =>
    action().then(
      () => {
        toast.success(t(successKey));
        refresh();
      },
      (err: unknown) => toast.error(getApiErrorMessage(err, t)),
    );

  const selectedIds = selection.mode === "Explicit" ? selection.explicitIds : [];
  const commands: AppCommandBarCommand[] = canManage
    ? [
        {
          id: "delete-selected",
          labelKey: "Delete",
          icon: Trash2,
          variant: "destructive",
          requiresSelection: true,
          selectionLabelKeys: { zero: "Delete", one: "Delete", many: "Integrations:DeleteN" },
          disabled: selectedIds.length === 0,
          disabledReasonKey: "Integrations:SelectRows",
          onClick: () => setToDelete({ ids: selectedIds }),
        },
      ]
    : [];

  const rowActions = (rows: ImportProfileRow[]): GridRowAction[] => {
    const row = rows.length === 1 ? rows[0] : undefined;
    const id = row?.id;
    const status = enumName(IMPORT_PROFILE_STATUSES, row?.status);
    if (!canManage) return id ? [{ id: "open", labelKey: "Open", onClick: () => setEditing(id) }] : [];
    return [
      ...(id ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditing(id) }] : []),
      ...(id && status !== "Active" ? [{ id: "activate", labelKey: "Integrations:Activate", icon: CheckCheck, onClick: () => void runRowAction(() => activate.mutateAsync(id), "Integrations:ImportProfileActivated") }] : []),
      ...(id && status !== "Retired" ? [{ id: "retire", labelKey: "Integrations:Retire", icon: Archive, onClick: () => void runRowAction(() => retire.mutateAsync(id), "Integrations:ImportProfileRetired") }] : []),
      {
        id: "delete",
        labelKey: "Delete",
        icon: Trash2,
        destructive: true,
        multi: true,
        onClick: () => setToDelete({ ids: rows.map((r) => r.id).filter((value): value is string => value != null), name: row?.name ?? undefined }),
      },
    ];
  };

  const deleteRows = async () => {
    if (!toDelete) return;
    for (const id of toDelete.ids) {
      await remove.mutateAsync(id);
    }
    setToDelete(null);
    setSelection(EMPTY_SELECTION);
    toast.success(t("Integrations:ImportProfileDeleted"));
    refresh();
  };

  return (
    <div className="flex h-full flex-col" data-slot="import-profiles-view">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<ImportProfileRow>
          gridId="integrations.importProfiles"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={canManage ? setSelection : undefined}
          onOpen={(row) => row.id && setEditing(row.id)}
          rowActions={rowActions}
          filters={
            <MultiSelect
              id="import-profiles-status-filter"
              value={statuses.map(String)}
              onChange={(next) => setStatuses(next.map(Number) as ImportProfileStatus[])}
              options={enumOptions(t, "ImportProfileStatus", IMPORT_PROFILE_STATUSES)}
              className="w-44"
            />
          }
        />
      </div>
      {editing && <ImportProfileEditor key={editing} id={editing} readOnly={!canManage} open onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(next) => !next && setToDelete(null)}
        title={t("Integrations:DeleteImportProfile")}
        description={`${
          toDelete?.name != null
            ? t("Integrations:DeleteImportProfileQuestion").replace("{0}", toDelete.name)
            : countText(t, "Integrations:DeleteNImportProfilesQuestion", toDelete?.ids.length ?? 0)
        } ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteRows}
      />
    </div>
  );
}
