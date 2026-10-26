import { describe, expect, it } from "vitest";
import type { ColDef } from "ag-grid-community";

import { buildExportContext } from "@/components/ui/opentms-grid-export";
import type { GridRequest } from "@/components/ui/opentms-grid";

/* The Export button's context (UI-GRID-013): the columns in display order with their shown headers and visibility,
 * AG Grid's own columns and columns marked `export: false` left out, the sort of the column state, the search term
 * and the column filters of the last request. Risk class: Medium (it decides what leaves the tenant in a file). */

type Row = { number: string; status: number; co2Kg: number };
const defs: ColDef<Row>[] = [
  { field: "number", headerName: "Number" },
  { field: "status", headerName: "Status" },
  { colId: "firstStop", headerName: "First stop" },
  { field: "co2Kg", headerName: "CO2", context: { export: false } },
  { field: "status", colId: "hiddenOne", headerName: "Hidden one", hide: true },
];

describe("buildExportContext", () => {
  it("lists the columns in display order with headers and visibility, without AG Grid's own and non-exportable ones", () => {
    const ctx = buildExportContext(
      [
        { colId: "ag-Grid-SelectionColumn" },
        { colId: "firstStop" },
        { colId: "number" },
        { colId: "co2Kg" },
        { colId: "hiddenOne", hide: true },
        { colId: "status" },
      ],
      defs,
      "",
      null,
    );
    expect(ctx.columns).toEqual([
      { id: "firstStop", header: "First stop", visible: true },
      { id: "number", header: "Number", visible: true },
      { id: "hiddenOne", header: "Hidden one", visible: false },
      { id: "status", header: "Status", visible: true },
    ]);
  });

  it("carries the search term, the sort in sort order and the last request's column filters", () => {
    const last: GridRequest = { startRow: 100, endRow: 200, sortModels: [], columnFilters: { number: { filterType: "text", type: "contains", filter: "26" } }, wildcardSearch: "x", rowGroupCols: [], groupKeys: [] };
    const ctx = buildExportContext(
      [
        { colId: "number", sort: "desc", sortIndex: 1 },
        { colId: "status", sort: "asc", sortIndex: 0 },
        { colId: "firstStop", sort: null },
      ],
      defs,
      "acme",
      last,
    );
    expect(ctx.request.wildcardSearch).toBe("acme");
    expect(ctx.request.startRow).toBe(0);
    expect(ctx.request.sortModels).toEqual([{ colId: "status", sort: "asc" }, { colId: "number", sort: "desc" }]);
    expect(ctx.request.columnFilters).toEqual(last.columnFilters);
  });

  it("sends no search without a term and sorts the tree's auto column by its field", () => {
    const ctx = buildExportContext([{ colId: "ag-Grid-AutoColumn", sort: "asc" }], defs, "", null, { autoColumnId: "ag-Grid-AutoColumn", field: "name" });
    expect(ctx.request.wildcardSearch).toBeNull();
    expect(ctx.request.sortModels).toEqual([{ colId: "name", sort: "asc" }]);
    expect(ctx.columns).toEqual([]);
  });
});
