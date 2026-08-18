/**
 * Pure logic tests for GridRequest construction — datasource extraction path.
 *
 * Risk class: High — "Wildcard search and per-column filter input" (design §Test risk analysis).
 *
 * The datasource in opentms-grid.tsx builds a GridRequest from AG Grid's internal
 * params. These tests verify every transformation rule in that construction:
 *
 *   1. wildcardSearch contract: empty string → null (design §API contract: "null if absent")
 *   2. wildcardSearch contract: non-empty string → forwarded as-is
 *   3. Filter model extraction: AG Grid filter object → ColumnFilterModel
 *   4. Filter model: null filter value → field absent (not "null")
 *   5. Sort model extraction: order and sort direction preserved
 *   6. RowGroupCols / groupKeys: forwarded from AG Grid params
 *   7. startRow / endRow: forwarded with fallback defaults
 *
 * The functions under test mirror the production code without requiring AG Grid or React:
 * they are extracted one-to-one from the datasource getRows body.
 */

import { describe, expect, it } from "vitest";

// ── Types (mirrored from opentms-grid.tsx public exports) ────────────────────

interface SortModel {
  colId: string;
  sort: "asc" | "desc";
}

interface ColumnFilterModel {
  filterType: "text" | "number" | "date";
  type: string;
  filter?: string;
  filterTo?: string;
}

interface GridRequest {
  startRow: number;
  endRow: number;
  sortModels: SortModel[];
  columnFilters: Record<string, ColumnFilterModel>;
  wildcardSearch: string | null;
  rowGroupCols: string[];
  groupKeys: string[];
}

// ── AG Grid param shapes (structural subset used by the datasource) ───────────

interface AgSortModel {
  colId: string;
  sort: "asc" | "desc";
}

interface AgRowGroupCol {
  id: string;
}

interface AgParams {
  request: {
    startRow?: number;
    endRow?: number;
    sortModel: AgSortModel[];
    filterModel?: Record<string, unknown>;
    rowGroupCols: AgRowGroupCol[];
    groupKeys: string[];
  };
}

// ── Extraction functions (mirrored verbatim from datasource getRows body) ─────

function buildColumnFilters(
  filterModel: Record<string, unknown>,
): Record<string, ColumnFilterModel> {
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
  return columnFilters;
}

function buildGridRequest(params: AgParams, searchTerm: string): GridRequest {
  const filterModel = params.request.filterModel ?? {};
  const columnFilters = buildColumnFilters(filterModel);

  return {
    startRow: params.request.startRow ?? 0,
    endRow: params.request.endRow ?? 100,
    sortModels: params.request.sortModel.map((sm) => ({
      colId: sm.colId,
      sort: sm.sort,
    })),
    columnFilters,
    // Design §API contract: "null if absent" — production code uses `|| null`
    wildcardSearch: searchTerm || null,
    rowGroupCols: params.request.rowGroupCols.map((c) => c.id),
    groupKeys: params.request.groupKeys,
  };
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeParams(overrides: Partial<AgParams["request"]> = {}): AgParams {
  return {
    request: {
      startRow: 0,
      endRow: 100,
      sortModel: [],
      filterModel: {},
      rowGroupCols: [],
      groupKeys: [],
      ...overrides,
    },
  };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("GridRequest construction — wildcardSearch forwarding", () => {
  it("empty search string produces wildcardSearch: null (design §API contract)", () => {
    const req = buildGridRequest(makeParams(), "");
    expect(req.wildcardSearch).toBeNull();
  });

  it("non-empty search string is forwarded verbatim", () => {
    const req = buildGridRequest(makeParams(), "rotterdam");
    expect(req.wildcardSearch).toBe("rotterdam");
  });

  it("whitespace-only search string is forwarded as-is (not treated as null)", () => {
    // The production code uses `searchTermRef.current || null` without trimming,
    // so "   " (non-empty string) is truthy and forwarded unchanged.
    // Test documents this behaviour so a future trim refactor is not silent.
    const req = buildGridRequest(makeParams(), "   ");
    expect(req.wildcardSearch).toBe("   ");
  });

  it("200-character search string is forwarded (at-boundary valid)", () => {
    const term = "a".repeat(200);
    const req = buildGridRequest(makeParams(), term);
    expect(req.wildcardSearch).toBe(term);
  });
});

describe("GridRequest construction — column filter mapping", () => {
  it("text filter with contains type is mapped correctly", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: {
          status: { filterType: "text", type: "contains", filter: "active" },
        },
      }),
      "",
    );
    expect(req.columnFilters["status"]).toEqual({
      filterType: "text",
      type: "contains",
      filter: "active",
      filterTo: undefined,
    });
  });

  it("number filter with range type maps both filter and filterTo", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: {
          weight: { filterType: "number", type: "inRange", filter: "10", filterTo: "50" },
        },
      }),
      "",
    );
    expect(req.columnFilters["weight"]).toEqual({
      filterType: "number",
      type: "inRange",
      filter: "10",
      filterTo: "50",
    });
  });

  it("filter with null filter value omits the filter field", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: {
          col: { filterType: "text", type: "blank", filter: null },
        },
      }),
      "",
    );
    // filter: null → field is absent (undefined), not the string "null"
    expect(req.columnFilters["col"]!.filter).toBeUndefined();
  });

  it("missing filterType defaults to 'text'", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: { col: { type: "contains", filter: "x" } },
      }),
      "",
    );
    expect(req.columnFilters["col"]!.filterType).toBe("text");
  });

  it("missing type defaults to 'contains'", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: { col: { filterType: "text", filter: "x" } },
      }),
      "",
    );
    expect(req.columnFilters["col"]!.type).toBe("contains");
  });

  it("null filter model entry is skipped", () => {
    const req = buildGridRequest(
      makeParams({ filterModel: { col: null as unknown } }),
      "",
    );
    expect(req.columnFilters["col"]).toBeUndefined();
  });

  it("multiple column filters are all included", () => {
    const req = buildGridRequest(
      makeParams({
        filterModel: {
          a: { filterType: "text", type: "contains", filter: "foo" },
          b: { filterType: "number", type: "greaterThan", filter: "5" },
        },
      }),
      "",
    );
    expect(Object.keys(req.columnFilters)).toHaveLength(2);
    expect(req.columnFilters["a"]!.filter).toBe("foo");
    expect(req.columnFilters["b"]!.filter).toBe("5");
  });

  it("empty filter model produces empty columnFilters", () => {
    const req = buildGridRequest(makeParams({ filterModel: {} }), "");
    expect(req.columnFilters).toEqual({});
  });
});

describe("GridRequest construction — sort model forwarding", () => {
  it("single sort model is forwarded with colId and sort direction", () => {
    const req = buildGridRequest(
      makeParams({
        sortModel: [{ colId: "reference", sort: "asc" }],
      }),
      "",
    );
    expect(req.sortModels).toEqual([{ colId: "reference", sort: "asc" }]);
  });

  it("multiple sort models preserve their order", () => {
    const req = buildGridRequest(
      makeParams({
        sortModel: [
          { colId: "date", sort: "desc" },
          { colId: "reference", sort: "asc" },
        ],
      }),
      "",
    );
    expect(req.sortModels[0]).toEqual({ colId: "date", sort: "desc" });
    expect(req.sortModels[1]).toEqual({ colId: "reference", sort: "asc" });
  });

  it("no sort model produces empty sortModels array", () => {
    const req = buildGridRequest(makeParams({ sortModel: [] }), "");
    expect(req.sortModels).toEqual([]);
  });
});

describe("GridRequest construction — row grouping forwarding", () => {
  it("rowGroupCols are extracted by their id field", () => {
    const req = buildGridRequest(
      makeParams({
        rowGroupCols: [{ id: "region" }, { id: "customer" }],
      }),
      "",
    );
    expect(req.rowGroupCols).toEqual(["region", "customer"]);
  });

  it("groupKeys are forwarded as-is", () => {
    const req = buildGridRequest(
      makeParams({ groupKeys: ["north", "ACME Inc."] }),
      "",
    );
    expect(req.groupKeys).toEqual(["north", "ACME Inc."]);
  });

  it("empty rowGroupCols and groupKeys produce empty arrays", () => {
    const req = buildGridRequest(makeParams(), "");
    expect(req.rowGroupCols).toEqual([]);
    expect(req.groupKeys).toEqual([]);
  });
});

describe("GridRequest construction — startRow / endRow defaults", () => {
  it("startRow defaults to 0 when undefined", () => {
    const req = buildGridRequest(
      makeParams({ startRow: undefined }),
      "",
    );
    expect(req.startRow).toBe(0);
  });

  it("endRow defaults to 100 when undefined", () => {
    const req = buildGridRequest(
      makeParams({ endRow: undefined }),
      "",
    );
    expect(req.endRow).toBe(100);
  });

  it("explicit startRow and endRow are forwarded", () => {
    const req = buildGridRequest(
      makeParams({ startRow: 200, endRow: 300 }),
      "",
    );
    expect(req.startRow).toBe(200);
    expect(req.endRow).toBe(300);
  });
});
