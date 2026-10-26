import type { ColDef } from "ag-grid-community";

import type { GridRequest } from "@/components/ui/opentms-grid";

/* The Export button of OpenTmsGrid (UI-GRID-013) hands the page what the server export needs to answer the rows and
 * columns the user sees: the list's current request (search, sort, column filters) and the columns in display order
 * with their shown headers and visibility. Pure, so it is tested without AG Grid. */

/** One column of the grid as the export offers it: its id, its shown header and whether it is visible. */
export interface GridExportColumn {
  id: string;
  header: string;
  visible: boolean;
}

export interface GridExportContext {
  request: GridRequest;
  columns: GridExportColumn[];
}

/** The part of AG Grid's column state the export reads (the state is in display order). */
export interface ExportColumnState {
  colId: string;
  hide?: boolean | null;
  sort?: "asc" | "desc" | null;
  sortIndex?: number | null;
}

/**
 * Builds the export context. Columns AG Grid adds itself (the selection column, the tree's auto column) have no
 * column definition and are left out, as is a definition marked `context: { export: false }` (a column the server
 * export does not know). The sort is the column state's, in sort order; the tree's auto column sorts by its field.
 */
export function buildExportContext<TRow>(
  state: ExportColumnState[],
  columnDefs: ColDef<TRow>[],
  searchTerm: string,
  lastRequest: GridRequest | null,
  tree?: { autoColumnId: string; field: string },
): GridExportContext {
  const exportable = columnDefs.filter((cd) => (cd.context as { export?: boolean } | undefined)?.export !== false);
  const headers = new Map(exportable.map((cd) => [String(cd.colId ?? cd.field ?? ""), String(cd.headerName ?? cd.field ?? cd.colId ?? "")]));
  const columns = state
    .filter((cs) => cs.colId !== "" && headers.has(cs.colId))
    .map((cs) => ({ id: cs.colId, header: headers.get(cs.colId) ?? cs.colId, visible: !(cs.hide ?? false) }));
  const sortModels = state
    .filter((cs) => cs.sort === "asc" || cs.sort === "desc")
    .sort((a, b) => (a.sortIndex ?? 0) - (b.sortIndex ?? 0))
    .map((cs) => ({ colId: tree && cs.colId === tree.autoColumnId ? tree.field : cs.colId, sort: cs.sort as "asc" | "desc" }));
  return {
    request: {
      startRow: 0,
      endRow: lastRequest?.endRow ?? 100,
      sortModels,
      columnFilters: lastRequest?.columnFilters ?? {},
      wildcardSearch: searchTerm || null,
      rowGroupCols: [],
      groupKeys: [],
    },
    columns,
  };
}
