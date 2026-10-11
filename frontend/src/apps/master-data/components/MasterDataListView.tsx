import { lazy, Suspense, useCallback, useMemo, useState, type ReactNode } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import type { GridRequest, GridResponse, GridRowAction, GridSelectionDto, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import type { BulkDeleteResult } from "@/domains/master-data/shared";
import { MASTER_DATA_FEATURE } from "@/domains/master-data/shared";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsFeatureEnabled, useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

const _OpenTmsGrid = lazy(() => import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })));

function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

export interface EditorState {
  mode: "create" | "edit";
  id?: string;
  readOnly: boolean;
}

export interface MasterDataListViewProps<TRow extends { id?: string }> {
  /** The localization stem: "Currency" → MasterData:NewCurrency, MasterData:DeleteCurrencyQuestion, … */
  entityKey: string;
  /** The view permission; Create/Update/Delete are its children. */
  permission: string;
  gridId: string;
  columnDefs: ColDef<TRow>[];
  fetchRows: (request: GridRequest) => Promise<GridResponse<TRow>>;
  rowName: (row: TRow) => string;
  deleteOne: { mutateAsync: (id: string) => Promise<unknown> };
  bulkDelete: { mutateAsync: (selection: GridSelectionDto) => Promise<BulkDeleteResult> };
  /** The toolbar filters (UI-GRID-005). */
  filters?: ReactNode;
  /** Extra row actions between Edit and Delete (UI-GRID-008 order). */
  extraRowActions?: (row: TRow) => GridRowAction[];
  /** Renders the entity's editor for the open state; `onSaved` refreshes the grid. */
  renderEditor: (state: EditorState, onClose: () => void, onSaved: () => void) => ReactNode;
}

/* Every list page of the Master data app (UI-PAGE-001): command bar, full-height server-side grid,
 * the page's dialogs. The four states are the grid's own plus the shared PermissionDenied panel,
 * which also shows while the per-tenant feature is off (design 41, UI design). */
export function MasterDataListView<TRow extends { id?: string }>({
  entityKey,
  permission,
  gridId,
  columnDefs,
  fetchRows,
  rowName,
  deleteOne,
  bulkDelete,
  filters,
  extraRowActions,
  renderEditor,
}: MasterDataListViewProps<TRow>) {
  const { t } = useL();
  const featureEnabled = useIsFeatureEnabled(MASTER_DATA_FEATURE);
  const canView = useIsGranted(permission);
  const canCreate = useIsGranted(`${permission}.Create`);
  const canUpdate = useIsGranted(`${permission}.Update`);
  const canDelete = useIsGranted(`${permission}.Delete`);

  const [refreshKey, setRefreshKey] = useState(0);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [rowToDelete, setRowToDelete] = useState<TRow | null>(null);
  const [bulkTarget, setBulkTarget] = useState<{ selection: GridSelectionDto; count: number } | null>(null);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [lastFilteredCount, setLastFilteredCount] = useState(0);
  const [busy, setBusy] = useState(false);

  const fetchWithRefresh = useCallback(
    async (request: GridRequest) => {
      const result = await fetchRows(request);
      setLastFilteredCount(result.filteredCount);
      return result;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [fetchRows, refreshKey],
  );

  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  const selectionCount =
    selection.mode === "Explicit" ? selection.explicitIds.length : Math.max(0, lastFilteredCount - selection.excludedIds.length);

  const openRecord = useCallback(
    (row: TRow) => setEditor({ mode: "edit", id: row.id, readOnly: !canUpdate }),
    [canUpdate],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () => [
      ...(canCreate
        ? [
            {
              id: "new",
              labelKey: `MasterData:New${entityKey}`,
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create", readOnly: false }),
            } satisfies AppCommandBarCommand,
          ]
        : []),
      ...(canDelete
        ? [
            {
              id: "delete-selected",
              labelKey: "MasterData:DeleteSelected",
              selectionLabelKeys: {
                zero: "MasterData:DeleteSelected",
                one: "MasterData:DeleteOneSelected",
                many: "MasterData:DeleteNSelected",
              },
              icon: Trash2,
              variant: "destructive" as const,
              requiresSelection: true,
              disabled: bulkTarget != null,
              onClick: () => setBulkTarget({ selection, count: selectionCount }),
            } satisfies AppCommandBarCommand,
          ]
        : []),
    ],
    [canCreate, canDelete, entityKey, bulkTarget, selection, selectionCount],
  );

  const rowActions = useCallback(
    (rows: TRow[]): GridRowAction[] => {
      const single = rows.length === 1 ? rows[0] : undefined;
      return [
        ...(canUpdate && single ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => openRecord(single) }] : []),
        ...(single && extraRowActions ? extraRowActions(single) : []),
        ...(canDelete
          ? [
              {
                id: "delete",
                labelKey: "Delete",
                icon: Trash2,
                destructive: true,
                multi: true,
                onClick: () => {
                  if (rows.length === 1) {
                    setRowToDelete(rows[0]);
                  } else {
                    setBulkTarget({
                      selection: { mode: "Explicit", explicitIds: rows.map((r) => r.id!).filter(Boolean), filterRequest: null, excludedIds: [] },
                      count: rows.length,
                    });
                  }
                },
              },
            ]
          : []),
      ];
    },
    [canDelete, canUpdate, extraRowActions, openRecord],
  );

  const deleteSingle = async () => {
    if (!rowToDelete?.id) return;
    setBusy(true);
    try {
      await deleteOne.mutateAsync(rowToDelete.id);
      setRowToDelete(null);
      toast.success(t(`MasterData:${entityKey}Deleted`));
      invalidateGrid();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  const deleteBulk = async () => {
    if (!bulkTarget) return;
    setBusy(true);
    try {
      const result = await bulkDelete.mutateAsync(bulkTarget.selection);
      setBulkTarget(null);
      const skipped = result.skippedRows ?? [];
      if (skipped.length > 0) {
        toast.warning(
          `${t("MasterData:BulkDeleteResult").replace("{0}", String(result.deletedCount ?? 0)).replace("{1}", String(skipped.length))} ` +
            skipped.map((s) => `${s.name}: ${s.reason}`).join("; "),
        );
      } else {
        toast.success(t("MasterData:Deleted").replace("{0}", String(result.deletedCount ?? 0)));
      }
      invalidateGrid();
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusy(false);
    }
  };

  if (!featureEnabled || !canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} selectionCount={selectionCount} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<TRow>
            gridId={gridId}
            columnDefs={columnDefs}
            fetchRows={fetchWithRefresh}
            onSelectionChange={canDelete ? (sel) => setSelection(sel) : undefined}
            onOpen={openRecord}
            rowActions={rowActions}
            filters={filters}
          />
        </Suspense>
      </div>

      {editor && renderEditor(editor, () => setEditor(null), invalidateGrid)}

      <ConfirmDialog
        open={rowToDelete != null}
        onOpenChange={(open) => {
          if (!open && !busy) setRowToDelete(null);
        }}
        title={t(`MasterData:Delete${entityKey}`)}
        description={`${t(`MasterData:Delete${entityKey}Question`).replace("{0}", rowToDelete ? rowName(rowToDelete) : "")} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteSingle}
      />

      <ConfirmDialog
        open={bulkTarget != null}
        onOpenChange={(open) => {
          if (!open && !busy) setBulkTarget(null);
        }}
        title={t("MasterData:DeleteSelected")}
        description={`${t("MasterData:DeleteSelectedQuestion").replace("{0}", String(bulkTarget?.count ?? 0))} ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteBulk}
      />
    </div>
  );
}
