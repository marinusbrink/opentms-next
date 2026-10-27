/* Lines that belong to the record being edited (UI-GRID-014): an AG Grid in the client-side
 * row model on the editor's form state, with the same theme as the lists. Rows are removed
 * through the context menu and a ghost trash icon at the row end, without a confirmation,
 * because Close without saving undoes everything; nothing reaches the API before Save.
 * Lazy-import this file like opentms-grid.tsx (ag-grid-enterprise stays out of the main chunk):
 * use LazyLinesGrid from lines-grid-lazy.tsx. */

import { useCallback, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import type { CellKeyDownEvent, ColDef, GetContextMenuItemsParams, GridApi, GridReadyEvent, ICellRendererParams, RowDoubleClickedEvent } from "ag-grid-community";
import { ModuleRegistry } from "ag-grid-community";
import { AllEnterpriseModule } from "ag-grid-enterprise";
import { AgGridReact } from "ag-grid-react";
import { Pencil, Trash2 } from "lucide-react";

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
  /** Lines edited in their own dialog instead of inline (design 43: addresses, contact persons): double click,
   *  Enter and the Edit item of the context menu open the line (UI-GRID-007); a ghost pencil at the row end too. */
  onOpen?: (row: TRow) => void;
  /** Rows that may not be removed (design 48: a vehicle's characteristics inherited from its type) get no trash
   *  icon and are skipped by the context menu's Remove; absent → every row is removable. */
  canRemoveRow?: (row: TRow) => boolean;
  /** Removal that asks first (design 61: a freight item is deleted after "Delete freight item {n}?"): the trash icon
   *  and the context menu's Remove hand the removable rows here instead of removing them; the caller confirms and
   *  then changes the rows itself. Absent → rows are removed at once (Close without saving undoes it). */
  onRemoveRequest?: (rows: TRow[]) => void;
  /** Localized text for the no-rows overlay. */
  emptyText?: string;
  className?: string;
}

const REMOVE_COLUMN_ID = "__remove__";
const OPEN_COLUMN_ID = "__open__";

export function LinesGrid<TRow>({
  rows,
  onRowsChange,
  getRowId,
  columnDefs,
  toolbar,
  readOnly = false,
  onOpen,
  canRemoveRow,
  onRemoveRequest,
  emptyText,
  className,
}: LinesGridProps<TRow>) {
  const { t } = useL();
  const apiRef = useRef<GridApi<TRow> | null>(null);
  /* Popup editors and menus are appended to the enclosing dialog (or the page) instead of the grid root, so a
   * popup taller than the lines area (the date editor's calendar) is not clipped by the grid's own bounds. */
  const [popupParent, setPopupParent] = useState<HTMLElement | null>(null);
  const hostRef = useCallback((element: HTMLDivElement | null) => {
    setPopupParent(element ? ((element.closest('[role="dialog"]') as HTMLElement | null) ?? document.body) : null);
  }, []);

  /* Escape inside an open cell editor cancels that edit and nothing else: the surrounding editor
   * dialog must not see it as a close request (UI-DLG-008). */
  const stopEscapeWhileEditing = useCallback((event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape" && (apiRef.current?.getEditingCells().length ?? 0) > 0) {
      event.stopPropagation();
    }
  }, []);

  const removeRows = useCallback(
    (targets: TRow[]) => {
      const removable = targets.filter((row) => !canRemoveRow || canRemoveRow(row));
      if (removable.length === 0) return;
      if (onRemoveRequest) {
        onRemoveRequest(removable);
        return;
      }
      const ids = new Set(removable.map(getRowId));
      onRowsChange(rows.filter((row) => !ids.has(getRowId(row))));
    },
    [canRemoveRow, getRowId, onRemoveRequest, onRowsChange, rows],
  );

  const removeLabel = t("Remove");
  const editLabel = t("Edit");

  const allColumnDefs = useMemo<ColDef<TRow>[]>(() => {
    if (readOnly) return columnDefs;
    const openColumn: ColDef<TRow> | null = onOpen
      ? {
          colId: OPEN_COLUMN_ID,
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
              <Button type="button" variant="ghost" size="icon-sm" aria-label={editLabel} title={editLabel} onClick={() => onOpen(params.data as TRow)}>
                <Pencil />
              </Button>
            ),
        }
      : null;
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
        params.data == null || (canRemoveRow && !canRemoveRow(params.data)) ? null : (
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
    return [...columnDefs, ...(openColumn ? [openColumn] : []), removeColumn];
  }, [canRemoveRow, columnDefs, editLabel, onOpen, readOnly, removeLabel, removeRows]);

  const getContextMenuItems = useCallback(
    (params: GetContextMenuItemsParams<TRow>) => {
      if (readOnly) return [];
      const clicked = params.node?.data as TRow | undefined;
      const targets = resolveTargetRows(clicked, params.api.getSelectedRows(), getRowId);
      if (targets.length === 0) return [];
      const removable = canRemoveRow ? targets.filter(canRemoveRow) : targets;
      return [
        ...(onOpen && clicked && targets.length === 1 ? [{ name: editLabel, action: () => onOpen(clicked) }] : []),
        ...(removable.length > 0 ? [{ name: removeLabel, action: () => removeRows(removable), cssClasses: ["text-destructive"] }] : []),
      ];
    },
    [canRemoveRow, editLabel, getRowId, onOpen, readOnly, removeLabel, removeRows],
  );

  const openRow = useCallback(
    (event: RowDoubleClickedEvent<TRow>) => {
      if (onOpen && event.data != null) onOpen(event.data);
    },
    [onOpen],
  );

  /* Enter opens the line when no cell is being edited (UI-GRID-007); inline editors keep their own Enter. */
  const openRowOnEnter = useCallback(
    (event: CellKeyDownEvent<TRow>) => {
      const key = (event.event as KeyboardEvent | null | undefined)?.key;
      if (onOpen && key === "Enter" && event.data != null && event.api.getEditingCells().length === 0) onOpen(event.data);
    },
    [onOpen],
  );

  const noRowsOverlayComponent = useCallback(
    () => <span className="text-sm text-muted-foreground">{emptyText ?? t("Grid:Empty")}</span>,
    [emptyText, t],
  );

  return (
    <div ref={hostRef} className={cn("flex h-full flex-col gap-2", className)} data-slot="lines-grid" onKeyDown={stopEscapeWhileEditing}>
      {!readOnly && toolbar && <div className="flex items-center gap-2">{toolbar}</div>}
      <div className="min-h-0 flex-1 rounded-lg border bg-background">
        <AgGridReact<TRow>
          theme={opentmsGridTheme()}
          rowData={rows}
          columnDefs={allColumnDefs}
          onGridReady={(event: GridReadyEvent<TRow>) => {
            apiRef.current = event.api;
          }}
          getRowId={(params) => getRowId(params.data)}
          rowSelection={readOnly ? undefined : { mode: "multiRow", checkboxes: false, headerCheckbox: false, enableClickSelection: true }}
          singleClickEdit
          stopEditingWhenCellsLoseFocus
          popupParent={popupParent}
          /* AG Grid edits the row object in place; hand the form a fresh copy so React state and the
           * dirty guard (UI-FORM-009) see the change. */
          onCellValueChanged={(event) =>
            onRowsChange(rows.map((row) => (row === event.data ? ({ ...(event.data as TRow) } as TRow) : row)))
          }
          getContextMenuItems={getContextMenuItems}
          onRowDoubleClicked={onOpen ? openRow : undefined}
          onCellKeyDown={onOpen ? openRowOnEnter : undefined}
          suppressContextMenu={readOnly}
          preventDefaultOnContextMenu={!readOnly}
          noRowsOverlayComponent={noRowsOverlayComponent}
          animateRows={false}
        />
      </div>
    </div>
  );
}
