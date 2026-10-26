/* AG Grid Enterprise SSRM wrapper (design #6).
 * This file must be lazy-imported by consumers:
 *   const OpenTmsGrid = React.lazy(() => import('@/components/ui/opentms-grid'))
 * Eager import of ag-grid-enterprise (~800 KB) on any route will fail the CI bundle check.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

import type {
  CellKeyDownEvent,
  ColDef,
  ColDefField,
  ColumnMovedEvent,
  ColumnResizedEvent,
  ColumnState,
  ColumnVisibleEvent,
  FilterChangedEvent,
  FullWidthCellKeyDownEvent,
  GetContextMenuItemsParams,
  GridApi,
  GridReadyEvent,
  IServerSideDatasource,
  IServerSideGetRowsParams,
  RowDoubleClickedEvent,
  SelectionChangedEvent,
  SortChangedEvent,
} from "ag-grid-community";
import { ModuleRegistry } from "ag-grid-community";
import { AllEnterpriseModule, LicenseManager } from "ag-grid-enterprise";
import { AgGridReact } from "ag-grid-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { opentmsGridTheme } from "@/components/ui/grid-theme";
import {
  buildContextMenuItems,
  canOpenRow,
  resolveTargetRows,
  type GridRowAction,
} from "@/components/ui/opentms-grid-actions";
import { OpenTmsGridFooter } from "@/components/ui/opentms-grid-footer";
import { createGridSettingsPersister } from "@/components/ui/opentms-grid-settings";
import { buildExportContext, type GridExportContext } from "@/components/ui/opentms-grid-export";
import { OpenTmsGridToolbar } from "@/components/ui/opentms-grid-toolbar";
import { apiClient } from "@/lib/api/client";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

// ─── Module-level setup (runs once on first import) ─────────────────────────

ModuleRegistry.registerModules([AllEnterpriseModule]);

const licenseKey =
  (import.meta.env.VITE_AG_GRID_LICENSE_KEY as string | undefined) ?? "";
const licenseDetails = LicenseManager.getLicenseDetails(licenseKey);
if (!licenseDetails.valid) {
  console.error({ event: "AgGridLicenseInvalid" });
}
LicenseManager.setLicenseKey(licenseKey);

/** The colId AG Grid gives the expanding column of tree data. */
const AUTO_COLUMN_ID = "ag-Grid-AutoColumn";

// ─── Public types ────────────────────────────────────────────────────────────

export interface SortModel {
  colId: string;
  sort: "asc" | "desc";
}

export interface ColumnFilterModel {
  filterType: "text" | "number" | "date";
  type: string;
  filter?: string;
  filterTo?: string;
}

export interface GridRequest {
  startRow: number;
  endRow: number;
  sortModels: SortModel[];
  columnFilters: Record<string, ColumnFilterModel>;
  wildcardSearch: string | null;
  rowGroupCols: string[];
  groupKeys: string[];
}

export interface GridResponse<T> {
  rows: T[];
  totalCount: number;
  filteredCount: number;
}

export interface GridSelectionDto {
  mode: "Explicit" | "FilterBased";
  explicitIds: string[];
  filterRequest: GridRequest | null;
  excludedIds: string[];
}

export type { GridRowAction } from "@/components/ui/opentms-grid-actions";

export type { GridExportColumn, GridExportContext } from "@/components/ui/opentms-grid-export";

/** Tree data (organisation units): rows expand into children fetched per parent. The grid
 *  passes the keys of the expanded ancestors in `GridRequest.groupKeys`, last one deepest. */
export interface GridTreeDataOptions<TRow> {
  /** Whether the row can expand (the server tells: `hasChildren`). */
  isGroup: (row: TRow) => boolean;
  /** The key the children request carries for this row, normally its id. */
  getGroupKey: (row: TRow) => string;
  /** The expanding column: its header and the field it shows. */
  headerName: string;
  field: ColDefField<TRow>;
  minWidth?: number;
}

export interface OpenTmsGridProps<TRow> {
  gridId: string;
  columnDefs: ColDef<TRow>[];
  fetchRows: (request: GridRequest) => Promise<GridResponse<TRow>>;
  searchableColumns?: string[];
  onSelectionChange?: (selection: GridSelectionDto) => void;
  /** UI-GRID-007: double click and Enter open the record. */
  onOpen?: (row: TRow) => void;
  /** UI-GRID-008: the right-click menu for the clicked or selected rows. */
  rowActions?: (rows: TRow[]) => GridRowAction[];
  /** UI-GRID-005: the page's filter controls, rendered next to the search box. */
  filters?: ReactNode;
  /** UI-GRID-013: shows the Export button when the design includes export; it receives the current request and the
   * columns in display order (a column definition with `context: { export: false }` is not offered). */
  onExport?: (context: GridExportContext) => void;
  /** Hierarchical rows (UI-LOOK-001 "tree data"); the first column becomes the expanding one. */
  treeData?: GridTreeDataOptions<TRow>;
  /** UI-GRID-016: a picker grid toggles the selection on row click. */
  clickSelects?: boolean;
  className?: string;
}

// ─── Internal types ──────────────────────────────────────────────────────────

type BlockState = "loading" | "retrying" | "failed";

interface BlockStateEmitter {
  state: BlockState;
  listeners: Set<() => void>;
  setState(s: BlockState): void;
  subscribe(listener: () => void): () => void;
}

function createBlockStateEmitter(): BlockStateEmitter {
  const emitter: BlockStateEmitter = {
    state: "loading",
    listeners: new Set(),
    setState(s) {
      this.state = s;
      this.listeners.forEach((l) => l());
    },
    subscribe(listener) {
      this.listeners.add(listener);
      return () => this.listeners.delete(listener);
    },
  };
  return emitter;
}

// ─── Block loading cell renderer (module-level — stable reference) ───────────

interface BlockLoadCellRendererProps {
  emitter: BlockStateEmitter;
  onManualRetry: () => void;
  t: (key: string) => string;
}

function BlockLoadCellRenderer({ emitter, onManualRetry, t }: BlockLoadCellRendererProps) {
  const [state, setState] = useState<BlockState>(emitter.state);

  useEffect(() => {
    setState(emitter.state);
    return emitter.subscribe(() => setState(emitter.state));
  }, [emitter]);

  if (state === "retrying") {
    return (
      <span className="text-sm text-muted-foreground">{t("Grid:RetryingBlock")}</span>
    );
  }

  if (state === "failed") {
    return (
      <span className="flex items-center gap-2 text-sm">
        <span className="text-destructive">{t("Grid:BlockError")}</span>
        <Button size="xs" variant="outline" onClick={onManualRetry}>
          {t("Grid:RetryBlock")}
        </Button>
      </span>
    );
  }

  return null;
}

// ─── No-rows overlay (module-level — stable reference) ──────────────────────

interface NoRowsOverlayProps {
  getIsFiltered: () => boolean;
  onClearFilter: () => void;
  t: (key: string) => string;
}

function NoRowsOverlay({ getIsFiltered, onClearFilter, t }: NoRowsOverlayProps) {
  const hasFilter = getIsFiltered();

  if (hasFilter) {
    return (
      <div className="flex flex-col items-center gap-2 text-sm text-muted-foreground">
        <span>{t("Grid:EmptyFiltered")}</span>
        <Button size="sm" variant="outline" onClick={onClearFilter}>
          {t("Grid:ClearFilters")}
        </Button>
      </div>
    );
  }

  return <span className="text-sm text-muted-foreground">{t("Grid:Empty")}</span>;
}

// ─── Main component ──────────────────────────────────────────────────────────

export function OpenTmsGrid<TRow>({
  gridId,
  columnDefs,
  fetchRows,
  onSelectionChange,
  onOpen,
  rowActions,
  filters,
  onExport,
  treeData,
  clickSelects = false,
  className,
}: OpenTmsGridProps<TRow>) {
  const { t } = useL();

  // Grid API ref
  const gridApiRef = useRef<GridApi<TRow> | null>(null);

  // Search
  const [searchTerm, setSearchTerm] = useState("");
  const searchTermRef = useRef("");
  const searchDebounceRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // Row counts for footer
  const [totalCount, setTotalCount] = useState<number | null>(null);
  const [filteredCount, setFilteredCount] = useState<number | null>(null);

  // Column visibility for toolbar
  const [columnVisibility, setColumnVisibility] = useState<Record<string, boolean>>({});

  // Block error state emitter (stable ref)
  const blockStateEmitter = useRef(createBlockStateEmitter()).current;

  // Retry tracking
  const manualRetryCountRef = useRef(0);

  // Current grid request (for FilterBased selection mode)
  const currentGridRequestRef = useRef<GridRequest | null>(null);

  // Selection mode tracking
  const isFilterBasedRef = useRef(false);

  // Settings save debounce
  const settingsSaveDebounceRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // Settings persistence (PUT/DELETE with the visible failure path, UI-FB-005)
  const settingsPersister = useMemo(() => createGridSettingsPersister(gridId, t), [gridId, t]);

  // ── Settings helpers ──────────────────────────────────────────────────────

  const syncColumnVisibility = useCallback(() => {
    const api = gridApiRef.current;
    if (!api) return;
    const vis: Record<string, boolean> = {};
    api.getColumnState().forEach((cs) => {
      vis[cs.colId] = !(cs.hide ?? false);
    });
    setColumnVisibility(vis);
  }, []);

  const scheduleSettingsSave = useCallback(() => {
    const api = gridApiRef.current;
    if (!api) return;

    clearTimeout(settingsSaveDebounceRef.current);
    settingsSaveDebounceRef.current = setTimeout(() => {
      // The user already sees the change; a failed save surfaces as a toast (UI-FB-005).
      void settingsPersister.save(api.getColumnState());
    }, 1000);
  }, [settingsPersister]);

  const handleReset = useCallback(() => {
    const api = gridApiRef.current;
    if (!api) return;

    // Optimistic: revert to code defaults immediately
    api.resetColumnState();
    // resetColumnState fires synchronous column events that re-arm the debounce; cancel it.
    clearTimeout(settingsSaveDebounceRef.current);
    syncColumnVisibility();
    manualRetryCountRef.current = 0;

    // DELETE in background: the local revert stands for this session; a failure shows a toast.
    void settingsPersister.reset();
  }, [settingsPersister, syncColumnVisibility]);

  // ── Column state change handlers ──────────────────────────────────────────

  const onColumnResized = useCallback(
    (event: ColumnResizedEvent<TRow>) => {
      if (!event.finished) return;
      syncColumnVisibility();
      scheduleSettingsSave();
    },
    [syncColumnVisibility, scheduleSettingsSave],
  );

  const onColumnMoved = useCallback(
    (event: ColumnMovedEvent<TRow>) => {
      if (!event.finished) return;
      syncColumnVisibility();
      scheduleSettingsSave();
    },
    [syncColumnVisibility, scheduleSettingsSave],
  );

  const onColumnVisible = useCallback(
    (_event: ColumnVisibleEvent<TRow>) => {
      syncColumnVisibility();
      scheduleSettingsSave();
    },
    [syncColumnVisibility, scheduleSettingsSave],
  );

  const onSortChanged = useCallback(
    (_event: SortChangedEvent<TRow>) => {
      manualRetryCountRef.current = 0;
      scheduleSettingsSave();
    },
    [scheduleSettingsSave],
  );

  // ── Toolbar column toggle ─────────────────────────────────────────────────

  const handleToggleColumn = useCallback((colId: string, visible: boolean) => {
    const api = gridApiRef.current;
    if (!api) return;
    api.applyColumnState({ state: [{ colId, hide: !visible }] });
    syncColumnVisibility();
    scheduleSettingsSave();
  }, [syncColumnVisibility, scheduleSettingsSave]);

  // ── Manual retry ──────────────────────────────────────────────────────────

  const handleManualRetry = useCallback(() => {
    manualRetryCountRef.current += 1;
    gridApiRef.current?.retryServerSideLoads();
  }, []);

  // ── Clear filters ─────────────────────────────────────────────────────────

  const handleClearFilter = useCallback(() => {
    manualRetryCountRef.current = 0;
    searchTermRef.current = "";
    setSearchTerm("");
    gridApiRef.current?.setFilterModel(null);
    // Refresh picks up the now-empty search term via ref
    gridApiRef.current?.refreshServerSide({ purge: true });
  }, []);

  // ── Datasource ────────────────────────────────────────────────────────────

  const datasource = useMemo<IServerSideDatasource<TRow>>(
    () => ({
      getRows: async (params: IServerSideGetRowsParams<TRow>) => {
        blockStateEmitter.setState("loading");

        // Build column filters from AG Grid's filter model
        const filterModel = params.request.filterModel ?? {};
        const columnFilters: Record<string, ColumnFilterModel> = {};
        for (const [colId, filter] of Object.entries(filterModel)) {
          if (filter && typeof filter === "object") {
            const f = filter as Record<string, unknown>;
            columnFilters[colId] = {
              filterType: (f["filterType"] as "text" | "number" | "date") ?? "text",
              type: String(f["type"] ?? "contains"),
              filter: f["filter"] != null ? String(f["filter"]) : undefined,
              filterTo: f["filterTo"] != null ? String(f["filterTo"]) : undefined,
            };
          }
        }

        const gridRequest: GridRequest = {
          startRow: params.request.startRow ?? 0,
          endRow: params.request.endRow ?? 100,
          sortModels: params.request.sortModel.map((sm) => ({
            /* The tree's expanding column is AG Grid's auto column; the server knows the field. */
            colId: treeData && sm.colId === AUTO_COLUMN_ID ? treeData.field : sm.colId,
            sort: sm.sort,
          })),
          columnFilters,
          wildcardSearch: searchTermRef.current || null,
          rowGroupCols: params.request.rowGroupCols.map((c) => c.id),
          groupKeys: params.request.groupKeys,
        };

        currentGridRequestRef.current = gridRequest;

        const attempt = async (retryIndex: number): Promise<void> => {
          try {
            const response = await fetchRows(gridRequest);
            params.success({ rowData: response.rows, rowCount: response.filteredCount });
            setTotalCount(response.totalCount);
            setFilteredCount(response.filteredCount);
            manualRetryCountRef.current = 0;
          } catch {
            if (retryIndex === 0) {
              // Auto-retry after 2 s
              blockStateEmitter.setState("retrying");
              await new Promise<void>((resolve) => setTimeout(resolve, 2000));
              await attempt(1);
            } else {
              // Manual retry mode
              blockStateEmitter.setState("failed");
              if (manualRetryCountRef.current >= 2) {
                toast.error(t("Grid:BlockError"));
              }
              params.fail();
            }
          }
        };

        await attempt(0);
      },
    }),
    // fetchRows identity is the consumer's responsibility; gridId is stable per mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [fetchRows, gridId, treeData],
  );

  // ── Datasource update after mount (supports identity-change-triggered refresh) ──

  useEffect(() => {
    const api = gridApiRef.current;
    if (!api) return;
    api.setGridOption("serverSideDatasource", datasource);
  }, [datasource]);

  // ── Grid ready ────────────────────────────────────────────────────────────

  const onGridReady = useCallback(
    async (event: GridReadyEvent<TRow>) => {
      const api = event.api as GridApi<TRow>;
      gridApiRef.current = api;

      // Load and apply saved column settings before first data fetch
      try {
        const { data: settings } = await apiClient.GET(
          "/api/platform/grid-settings/{gridId}",
          { params: { path: { gridId } } },
        );

        if (settings) {
          const validColIds = new Set(
            columnDefs.map((cd) => (cd.colId ?? cd.field) as string).filter(Boolean),
          );
          if (treeData) validColIds.add(AUTO_COLUMN_ID);

          if (settings.columnStates?.length) {
            const validStates: ColumnState[] = settings.columnStates
              .filter((cs) => validColIds.has(cs.colId))
              .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
              .map((cs) => ({
                colId: cs.colId,
                hide: !(cs.visible ?? true),
                width: cs.width ?? undefined,
              }));

            api.applyColumnState({ state: validStates, applyOrder: true });
          }

          if (settings.sortModels?.length) {
            const validSorts: ColumnState[] = settings.sortModels
              .filter((sm) => sm.colId && validColIds.has(sm.colId))
              .map((sm, i) => ({
                colId: sm.colId!,
                sort: (sm.sort as "asc" | "desc") ?? null,
                sortIndex: i,
              }));

            if (validSorts.length) {
              api.applyColumnState({ state: validSorts });
            }
          }
        }
      } catch {
        // Fall back to code defaults silently
      }

      syncColumnVisibility();

      // Connect datasource after settings are applied (ensures first fetch uses saved sort)
      api.setGridOption("serverSideDatasource", datasource);
    },
    // columnDefs reference is intentionally excluded — settings load once on gridId mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [gridId, datasource, syncColumnVisibility],
  );

  // ── Search ────────────────────────────────────────────────────────────────

  const handleSearchChange = useCallback((value: string) => {
    setSearchTerm(value);
    searchTermRef.current = value;

    clearTimeout(searchDebounceRef.current);
    searchDebounceRef.current = setTimeout(() => {
      manualRetryCountRef.current = 0;
      gridApiRef.current?.refreshServerSide({ purge: true });
    }, 300);
  }, []);

  // ── Refresh (toolbar) ─────────────────────────────────────────────────────

  const handleRefresh = useCallback(() => {
    manualRetryCountRef.current = 0;
    gridApiRef.current?.refreshServerSide({ purge: true });
  }, []);

  // ── Export (UI-GRID-013) ──────────────────────────────────────────────────

  const handleExport = useCallback(() => {
    if (!onExport) return;
    const state = gridApiRef.current?.getColumnState() ?? columnDefs.map((cd) => ({ colId: String(cd.colId ?? cd.field ?? ""), hide: cd.hide ?? false }));
    onExport(buildExportContext(state, columnDefs, searchTermRef.current, currentGridRequestRef.current, treeData ? { autoColumnId: AUTO_COLUMN_ID, field: treeData.field } : undefined));
  }, [onExport, columnDefs, treeData]);

  // ── Opening a record (UI-GRID-007) ────────────────────────────────────────

  const openRow = useCallback(
    (node: { group?: boolean; rowPinned?: string | null; data?: TRow }) => {
      if (onOpen && canOpenRow(node, treeData != null)) {
        onOpen(node.data as TRow);
      }
    },
    [onOpen, treeData],
  );

  const onRowDoubleClicked = useCallback(
    (event: RowDoubleClickedEvent<TRow>) => openRow(event.node),
    [openRow],
  );

  const onCellKeyDown = useCallback(
    (event: CellKeyDownEvent<TRow> | FullWidthCellKeyDownEvent<TRow>) => {
      const key = (event.event as KeyboardEvent | null | undefined)?.key;
      if (key === "Enter") {
        openRow(event.node);
      }
    },
    [openRow],
  );

  // ── Context menu (UI-GRID-008) ────────────────────────────────────────────

  const getContextMenuItems = useCallback(
    (params: GetContextMenuItemsParams<TRow>) => {
      if (!rowActions) return [];
      const clicked = params.node && canOpenRow(params.node, treeData != null) ? (params.node.data as TRow) : undefined;
      const rows = resolveTargetRows(
        clicked,
        params.api.getSelectedRows(),
        (row) => String((row as Record<string, unknown>)?.id ?? ""),
      );
      if (rows.length === 0) return [];
      return buildContextMenuItems(rowActions(rows), rows.length, t);
    },
    [rowActions, t, treeData],
  );

  // ── Filter changed (AG Grid column filters) ───────────────────────────────

  const onFilterChanged = useCallback((_event: FilterChangedEvent<TRow>) => {
    // SSRM auto-refreshes on filter change; we just reset error state
    manualRetryCountRef.current = 0;
  }, []);

  // ── Selection ─────────────────────────────────────────────────────────────

  const onSelectionChanged = useCallback(
    (event: SelectionChangedEvent<TRow>) => {
      if (!onSelectionChange) return;
      const api = event.api as GridApi<TRow>;

      const isSelectAll =
        event.source === "uiSelectAllFiltered" ||
        event.source === "apiSelectAllFiltered";

      if (isSelectAll) {
        isFilterBasedRef.current = true;
        onSelectionChange({
          mode: "FilterBased",
          explicitIds: [],
          filterRequest: currentGridRequestRef.current,
          excludedIds: [],
        });
        return;
      }

      if (
        event.source === "uiSelectAll" ||
        event.source === "apiSelectAll" ||
        event.source === "keyboardSelectAll"
      ) {
        isFilterBasedRef.current = false;
      }

      const selectedNodes = api.getSelectedNodes();

      if (selectedNodes.length === 0) {
        isFilterBasedRef.current = false;
        onSelectionChange({
          mode: "Explicit",
          explicitIds: [],
          filterRequest: null,
          excludedIds: [],
        });
        return;
      }

      if (isFilterBasedRef.current) {
        // After select-all, track deselected rows as excludedIds
        const excludedIds: string[] = [];
        api.forEachNode((node) => {
          if (!node.isSelected()) {
            const id = String((node.data as Record<string, unknown>)?.id ?? "");
            if (id) excludedIds.push(id);
          }
        });
        onSelectionChange({
          mode: "FilterBased",
          explicitIds: [],
          filterRequest: currentGridRequestRef.current,
          excludedIds,
        });
      } else {
        const explicitIds = selectedNodes
          .map((n) => String((n.data as Record<string, unknown>)?.id ?? ""))
          .filter(Boolean);
        onSelectionChange({
          mode: "Explicit",
          explicitIds,
          filterRequest: null,
          excludedIds: [],
        });
      }
    },
    [onSelectionChange],
  );

  // ── Loading cell renderer params (stable) ─────────────────────────────────

  const loadingCellRendererParams = useMemo(
    () => ({
      emitter: blockStateEmitter,
      onManualRetry: handleManualRetry,
      t,
    }),
    [blockStateEmitter, handleManualRetry, t],
  );

  // ── No-rows overlay params (stable — reads from refs at render time) ───────

  const getIsFiltered = useCallback(
    () =>
      searchTermRef.current !== "" ||
      Object.keys(gridApiRef.current?.getFilterModel() ?? {}).length > 0,
    [],
  );

  const noRowsOverlayComponentParams = useMemo(
    () => ({
      getIsFiltered,
      onClearFilter: handleClearFilter,
      t,
    }),
    [getIsFiltered, handleClearFilter, t],
  );

  // ── Row selection config ──────────────────────────────────────────────────

  /* UI-GRID-006: one dedicated checkbox column, provided by AG Grid's row-selection API.
   * Never add a second one through checkboxSelection on a column definition. */
  const rowSelectionOptions = useMemo(
    () =>
      onSelectionChange
        ? ({
            mode: "multiRow",
            selectAll: "filtered",
            checkboxes: true,
            headerCheckbox: true,
            enableClickSelection: clickSelects,
          } as const)
        : undefined,
    [onSelectionChange, clickSelects],
  );

  // ── Tree data (server-side) ───────────────────────────────────────────────

  const isServerSideGroup = useCallback(
    (dataItem: TRow) => (treeData ? treeData.isGroup(dataItem) : false),
    [treeData],
  );
  const getServerSideGroupKey = useCallback(
    (dataItem: TRow) => (treeData ? treeData.getGroupKey(dataItem) : ""),
    [treeData],
  );
  const autoGroupColumnDef = useMemo<ColDef<TRow> | undefined>(
    () =>
      treeData
        ? {
            headerName: treeData.headerName,
            field: treeData.field,
            flex: 1,
            minWidth: treeData.minWidth ?? 240,
            sortable: true,
            filter: false,
            cellRendererParams: { suppressCount: true },
          }
        : undefined,
    [treeData],
  );

  const selectionColumnDef = useMemo<ColDef>(
    () => ({
      width: 40,
      minWidth: 40,
      maxWidth: 40,
      pinned: "left",
      suppressMovable: true,
      resizable: false,
      sortable: false,
    }),
    [],
  );

  // ── Cleanup ───────────────────────────────────────────────────────────────

  useEffect(
    () => () => {
      clearTimeout(searchDebounceRef.current);
      clearTimeout(settingsSaveDebounceRef.current);
    },
    [],
  );

  // ─────────────────────────────────────────────────────────────────────────

  return (
    <div className={cn("flex h-full flex-col", className)}>
      <OpenTmsGridToolbar
        searchValue={searchTerm}
        onSearchChange={handleSearchChange}
        columnDefs={columnDefs}
        columnVisibility={columnVisibility}
        onToggleColumn={handleToggleColumn}
        onReset={handleReset}
        onRefresh={handleRefresh}
        onExport={onExport ? handleExport : undefined}
        filters={filters}
      />

      <div className="min-h-0 flex-1">
        <AgGridReact<TRow>
          theme={opentmsGridTheme()}
          columnDefs={columnDefs}
          selectionColumnDef={selectionColumnDef}
          rowModelType="serverSide"
          cacheBlockSize={100}
          treeData={treeData != null}
          isServerSideGroup={treeData ? isServerSideGroup : undefined}
          getServerSideGroupKey={treeData ? getServerSideGroupKey : undefined}
          autoGroupColumnDef={autoGroupColumnDef}
          rowSelection={rowSelectionOptions}
          getRowId={(params) =>
            String((params.data as Record<string, unknown>)?.id ?? "")
          }
          loadingCellRenderer={BlockLoadCellRenderer}
          loadingCellRendererParams={loadingCellRendererParams}
          noRowsOverlayComponent={NoRowsOverlay}
          noRowsOverlayComponentParams={noRowsOverlayComponentParams}
          onGridReady={onGridReady}
          onColumnResized={onColumnResized}
          onColumnMoved={onColumnMoved}
          onColumnVisible={onColumnVisible}
          onSortChanged={onSortChanged}
          onFilterChanged={onFilterChanged}
          onSelectionChanged={onSelectionChange ? onSelectionChanged : undefined}
          onRowDoubleClicked={onOpen ? onRowDoubleClicked : undefined}
          onCellKeyDown={onOpen ? onCellKeyDown : undefined}
          getContextMenuItems={rowActions ? getContextMenuItems : undefined}
          suppressContextMenu={!rowActions}
          preventDefaultOnContextMenu={!!rowActions}
          suppressMenuHide
          animateRows={false}
        />
      </div>

      <OpenTmsGridFooter totalCount={totalCount} filteredCount={filteredCount} />
    </div>
  );
}
