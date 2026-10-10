/* Lines that belong to the record being edited (UI-GRID-014): an AG Grid in the client-side
 * row model on the editor's form state, with the same theme as the lists. Rows are removed
 * through the context menu and a ghost trash icon at the row end, without a confirmation,
 * because Close without saving undoes everything; nothing reaches the API before Save.
 * Lazy-import this file like opentms-grid.tsx (ag-grid-enterprise stays out of the main chunk):
 * use LazyLinesGrid from lines-grid-lazy.tsx. */

import { useCallback, useMemo, type ReactNode } from "react";
import type { ColDef, GetContextMenuItemsParams, ICellRendererParams } from "ag-grid-community";
import { ModuleRegistry } from "ag-grid-community";
import { AllEnterpriseModule } from "ag-grid-enterprise";
import { AgGridReact } from "ag-grid-react";
import { Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { opentmsGridTheme } from "@/components/ui/grid-theme";
import { resolveTargetRows } from "@/components/ui/opentms-grid-actions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

ModuleRegistry.registerModules([AllEnterpriseModule]);

export interface LinesGridProps<TRow> {
  rows: TRow[];
  onRowsChange: (rows: TRow[]) => void;
  getRowId: (row: TRow) => string;
  columnDefs: ColDef<TRow>[];
  /** The add commands above the grid: `Button size="sm" variant="outline"` with a Plus icon. */
  toolbar?: ReactNode;
  /** Read-only editors (UI-PERM-004): no toolbar, no removal, no editing. */
  readOnly?: boolean;
  /** Localized text for the no-rows overlay. */
  emptyText?: string;
  className?: string;
}

const REMOVE_COLUMN_ID = "__remove__";

export function LinesGrid<TRow>({
  rows,
  onRowsChange,
  getRowId,
  columnDefs,
  toolbar,
  readOnly = false,
  emptyText,
  className,
}: LinesGridProps<TRow>) {
  const { t } = useL();

  const removeRows = useCallback(
    (targets: TRow[]) => {
      const ids = new Set(targets.map(getRowId));
      onRowsChange(rows.filter((row) => !ids.has(getRowId(row))));
    },
    [getRowId, onRowsChange, rows],
  );

  const removeLabel = t("Remove");

  const allColumnDefs = useMemo<ColDef<TRow>[]>(() => {
    if (readOnly) return columnDefs;
    const removeColumn: ColDef<TRow> = {
      colId: REMOVE_COLUMN_ID,
      headerName: "",
      width: 44,
      minWidth: 44,
      maxWidth: 44,
      pinned: "right",
      sortable: false,
      filter: false,
      resizable: false,
      suppressMovable: true,
      cellRenderer: (params: ICellRendererParams<TRow>) =>
        params.data == null ? null : (
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={removeLabel}
            title={removeLabel}
            onClick={() => removeRows([params.data as TRow])}
          >
            <Trash2 />
          </Button>
        ),
    };
    return [...columnDefs, removeColumn];
  }, [columnDefs, readOnly, removeLabel, removeRows]);

  const getContextMenuItems = useCallback(
    (params: GetContextMenuItemsParams<TRow>) => {
      if (readOnly) return [];
      const clicked = params.node?.data as TRow | undefined;
      const targets = resolveTargetRows(clicked, params.api.getSelectedRows(), getRowId);
      if (targets.length === 0) return [];
      return [{ name: removeLabel, action: () => removeRows(targets), cssClasses: ["text-destructive"] }];
    },
    [getRowId, readOnly, removeLabel, removeRows],
  );

  const noRowsOverlayComponent = useCallback(
    () => <span className="text-sm text-muted-foreground">{emptyText ?? t("Grid:Empty")}</span>,
    [emptyText, t],
  );

  return (
    <div className={cn("flex h-full flex-col gap-2", className)} data-slot="lines-grid">
      {!readOnly && toolbar && <div className="flex items-center gap-2">{toolbar}</div>}
      <div className="min-h-0 flex-1 rounded-lg border bg-background">
        <AgGridReact<TRow>
          theme={opentmsGridTheme()}
          rowData={rows}
          columnDefs={allColumnDefs}
          getRowId={(params) => getRowId(params.data)}
          rowSelection={readOnly ? undefined : { mode: "multiRow", checkboxes: false, headerCheckbox: false, enableClickSelection: true }}
          singleClickEdit
          stopEditingWhenCellsLoseFocus
          getContextMenuItems={getContextMenuItems}
          suppressContextMenu={readOnly}
          preventDefaultOnContextMenu={!readOnly}
          noRowsOverlayComponent={noRowsOverlayComponent}
          animateRows={false}
        />
      </div>
    </div>
  );
}
