import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The price list editor (design 57 §UI, design 61): the matrix helpers (columns and brackets as lines, the ascending
 * bounds with the open one last, the ladder rows), the header with its columns (create and update), the Rules tab as a
 * master-detail (the paged rules grid under the list, the rule's lane, dimensions and bracket ladder saved per rule),
 * Simulate (the snapshot and the ranked candidates) and the ribbon commands (Export, Adjust with preview, Copy). The
 * domain hooks are real; the generated client is the mock; the pickers and grids are native stand-ins. Risk class:
 * Medium; the server tests prove the calculation. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: {} }, localization: { languages: [] } } }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./run2-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./run2-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/entity-lookup", async () => ({ EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGridFn }));
vi.mock("@/components/ui/date-picker", async () => ({
  DatePicker: (await import("./transport-test-mocks")).MockDatePicker,
  DATE_CELL_EDITOR_COL_DEF: (await import("./transport-test-mocks")).DATE_CELL_EDITOR_COL_DEF_MOCK,
}));
vi.mock("@/components/ui/opentms-grid", async () => ({ OpenTmsGrid: (await import("./run2-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/lib/browser", () => ({
  saveBlob: vi.fn(),
  fileNameFromContentDisposition: () => null,
  fallbackFileName: (name: string, _type: string | null | undefined, extension: string) => `${name}${extension}`,
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";

import { apiClient } from "@/lib/api/client";
import { saveBlob } from "@/lib/browser";
import { PriceListEditor } from "@/domains/financial/components/PriceListEditor";
import { validateRule } from "@/domains/financial/components/price-list/PriceListRuleDetail";
import { toSimulateDto } from "@/domains/financial/components/price-list/PriceListSimulateTab";
import {
  boundsProblem,
  bracketsError,
  bracketsFromDto,
  bracketsToDto,
  columnsError,
  columnsFromDto,
  columnsToDto,
  fromBracketRow,
  toBracketRow,
} from "@/domains/financial/components/price-list/rule-ladder";
import { tierOf } from "@/domains/financial/price-lists";
import { editorDialog, fieldIn, ok, panel, ribbonButton, submitEditor, tab, typeIn } from "./platform-test-helpers";
import { gridRows } from "./run2-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown; parseAs?: string }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

const stubBrowser = () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
};

beforeAll(stubBrowser);
afterEach(() => {
  vi.unstubAllGlobals();
  stubBrowser();
});

const GENERAL = "General";
const COLUMNS = "Financial:Columns";
const RULES = "Financial:Rules";
const SIMULATE = "Financial:Simulate";

const LIST = {
  id: "pl-1", code: "BNL", name: "Benelux", side: 1, currencyCode: "EUR", basis: 4, multiplier: 1, columnQuantity: 6, stopMatching: 3, unitConversionFactor: 1,
  validFrom: "2026-01-01", validTill: null, isArchived: false, description: null,
  columns: [{ position: 1, upperBound: 100 }, { position: 2, upperBound: 500 }, { position: 3, upperBound: null }],
  ruleCount: 1, concurrencyStamp: "stamp-pl1", creationTime: "2026-10-25T10:00:00Z",
};
const RULE_ROW = { id: "rule-1", position: 1, tier: 1, fromRelationId: "rel-1", fromRelationName: "Acme", toRegionId: "r-1", toRegionName: "Randstad", transportUnitId: "tu-1", rowQuantity: 4, minAmount: null, maxAmount: null, bracketCount: 2 };
const RULE = {
  ...RULE_ROW,
  brackets: [
    { id: "b-1", position: 1, upperBound: 1000, unitPrices: [10, 9, 8] },
    { id: "b-2", position: 2, upperBound: null, unitPrices: [7, 6, 5] },
  ],
};

beforeEach(() => {
  grants = {};
  gridRows.current = [];
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/financial/price-list/{id}":
        return ok(LIST);
      case "/api/financial/price-list/{id}/rules":
        return ok({ rows: gridRows.current, totalCount: gridRows.current.length, filteredCount: gridRows.current.length });
      case "/api/financial/price-list/{id}/rule/{ruleId}":
        return ok(RULE);
      case "/api/master-data/currency/lookup":
        return ok([{ id: "cur-eur", code: "EUR", name: "Euro" }, { id: "cur-gbp", code: "GBP", name: "Pound" }]);
      case "/api/master-data/geographical-region/lookup":
        return ok([{ id: "r-1", name: "Randstad" }]);
      case "/api/master-data/transport-unit/lookup":
        return ok([{ id: "tu-1", name: "Pallet" }]);
      case "/api/master-data/product/lookup":
        return ok([{ id: "pr-1", name: "Steel" }]);
      case "/api/master-data/vehicle-type/lookup":
        return ok([{ id: "vt-1", name: "Trailer" }]);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => (path.endsWith("/export") ? ok(new Blob(["x"])) : ok({ id: "new" })));
  PUT.mockReset().mockImplementation(async () => ok(RULE));
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const bodyOf = (mock: Mock<ClientCall>) => mock.mock.calls.at(-1)![1]!.body as Record<string, unknown>;
const noop = () => undefined;

describe("rule-ladder helpers", () => {
  it("reads the columns in position order and always ends with the open column", () => {
    expect(columnsToDto(columnsFromDto([{ position: 2, upperBound: null }, { position: 1, upperBound: 100 }]))).toEqual([100, null]);
    expect(columnsToDto(columnsFromDto([]))).toEqual([null]);
    expect(columnsToDto(columnsFromDto([{ position: 1, upperBound: 50 }]))).toEqual([50, null]);
  });

  it("checks the bounds: strictly ascending, the open one last and only there", () => {
    expect(boundsProblem([100, 500, null])).toBeUndefined();
    expect(boundsProblem([null])).toBeUndefined();
    expect(boundsProblem([500, 100, null])).toBe(2);
    expect(boundsProblem([100, null, null])).toBe(2);
    expect(boundsProblem([100, 500])).toBe(2);
    expect(boundsProblem([0, null])).toBe(1);
    expect(boundsProblem([])).toBe(1);
    expect(columnsError(columnsFromDto([{ position: 1, upperBound: 5 }, { position: 2, upperBound: 5 }]))).toBe("Financial:ColumnsNotAscendingHint");
    expect(columnsError(columnsFromDto(Array.from({ length: 51 }, (_, i) => ({ position: i + 1, upperBound: i + 1 }))))).toBe("Financial:TooManyColumnsHint");
  });

  it("fits a rule's brackets to the list's columns and checks the ladder", () => {
    const brackets = bracketsFromDto([{ id: "b", position: 1, upperBound: null, unitPrices: [1] }], 3);
    expect(brackets[0].unitPrices).toEqual([1, null, null]);
    expect(bracketsToDto(brackets, 2)).toEqual([{ upperBound: null, unitPrices: [1, null] }]);
    expect(bracketsFromDto([], 2)).toHaveLength(1);
    expect(bracketsError([])).toBe("Financial:OpenRowRequiredHint");
    expect(bracketsError([{ key: "a", upperBound: 10, unitPrices: [1] }])).toBe("Financial:BracketsNotAscendingHint");
    expect(bracketsError([{ key: "a", upperBound: null, unitPrices: [1.12345] }])).toBe("Financial:UnitPriceRangeHint");
    expect(bracketsError([{ key: "a", upperBound: null, unitPrices: [-1] }])).toBe("Financial:UnitPriceRangeHint");
    expect(bracketsError([{ key: "a", upperBound: 10, unitPrices: [1, null] }, { key: "b", upperBound: null, unitPrices: [2, 3] }])).toBeUndefined();
  });

  it("flattens a bracket to grid fields and back, reading typed text as numbers", () => {
    const row = toBracketRow({ key: "k", upperBound: 10, unitPrices: [1, null] });
    expect(row).toMatchObject({ key: "k", upperBound: 10, price0: 1, price1: null });
    expect(fromBracketRow({ ...row, price1: "2.5" as unknown as number, upperBound: "" as unknown as number }, 2)).toEqual({ key: "k", upperBound: null, unitPrices: [1, 2.5] });
  });

  it("counts a rule's tier as its set dimensions", () => {
    expect(tierOf({ transportUnitId: null, productId: null, vehicleTypeId: null })).toBe(0);
    expect(tierOf({ transportUnitId: "a", productId: null, vehicleTypeId: "c" })).toBe(2);
  });
});

describe("validateRule", () => {
  const base = { fromRelation: null, fromRegionId: null, toRelation: null, toRegionId: null, transportUnitId: null, productId: null, vehicleTypeId: null, rowQuantity: 0 as const, minAmount: "", maxAmount: "", brackets: [{ key: "o", upperBound: null, unitPrices: [1] }] };
  const relation = { id: "rel-1", code: "A", name: "Acme", city: null, countryCode: null, roles: 0 };

  it("needs a lane on at least one side, never a relation and a region on one side", () => {
    expect(validateRule(base).fromRelation).toBe("Financial:LaneRequiredHint");
    expect(validateRule({ ...base, toRegionId: "r-1" }).fromRelation).toBeUndefined();
    expect(validateRule({ ...base, fromRelation: relation, fromRegionId: "r-1" }).fromRegionId).toBe("Financial:RelationAndRegionOnOneSideHint");
  });

  it("checks the amounts and keeps a typed row quantity to its open bracket", () => {
    expect(validateRule({ ...base, toRegionId: "r-1", minAmount: "10", maxAmount: "5" }).maxAmount).toBe("Financial:MaxBelowMinHint");
    expect(validateRule({ ...base, toRegionId: "r-1", minAmount: "abc" }).minAmount).toBe("MasterData:InvalidNumber");
    expect(validateRule({ ...base, toRegionId: "r-1", brackets: [{ key: "a", upperBound: 5, unitPrices: [1] }, ...base.brackets] }).brackets).toBe("Financial:TypedQuantitySingleRowHint");
    expect(validateRule({ ...base, toRegionId: "r-1", rowQuantity: 4, brackets: [{ key: "a", upperBound: 5, unitPrices: [1] }, ...base.brackets] }).brackets).toBeUndefined();
  });
});

describe("toSimulateDto", () => {
  const relation = { id: "rel-1", code: "A", name: "Acme", city: null, countryCode: null, roles: 0 };
  const values = {
    fromRelation: relation, fromRegionIds: ["r-1"], toRelation: relation, toRegionIds: [], transportUnitId: "tu-1", productId: null, vehicleTypeId: null,
    quantity: "2", positions: "", colli: "", weightKg: "1.250,5".replace(".", "").replace(",", "."), volumeM3: "", loadingMeters: "", loadFactor: "", epq: "", distanceKm: "120", tollDistanceKm: "", durationH: "", asOf: "2026-06-01",
  };

  it("needs both relations and the transport unit, and parses the quantities", () => {
    expect(toSimulateDto({ ...values, transportUnitId: null })).toBeUndefined();
    expect(toSimulateDto({ ...values, toRelation: null })).toBeUndefined();
    expect(toSimulateDto({ ...values, colli: "x" })).toBeUndefined();
    expect(toSimulateDto(values)).toMatchObject({ fromRelationId: "rel-1", fromRegionIds: ["r-1"], toRelationId: "rel-1", transportUnitId: "tu-1", quantity: 2, weightKg: 1250.5, distanceKm: 120, colli: null, asOf: "2026-06-01" });
  });
});

describe("PriceListEditor — header and columns", () => {
  it("creates with the header, the enums and the columns (bounded ones, then the open one)", async () => {
    renderWith(<PriceListEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "GBP Pound" })).toBeInTheDocument());
    typeIn(GENERAL, "MasterData:Code", "bnl");
    typeIn(GENERAL, "MasterData:Name", "Benelux");
    fireEvent.change(fieldIn(GENERAL, "Financial:RateSide"), { target: { value: "2" } });
    fireEvent.change(fieldIn(GENERAL, "Financial:Currency"), { target: { value: "GBP" } });
    fireEvent.change(fieldIn(GENERAL, "Financial:Basis"), { target: { value: "9" } });
    fireEvent.change(fieldIn(GENERAL, "Financial:ColumnQuantity"), { target: { value: "13" } });
    fireEvent.change(fieldIn(GENERAL, "Financial:ValidFrom"), { target: { value: "2026-03-01" } });
    typeIn(GENERAL, "Financial:UnitConversionFactor", "1000");

    tab(COLUMNS);
    expect(within(panel(COLUMNS)).getAllByText("Financial:Open")).toHaveLength(1);
    fireEvent.click(within(panel(COLUMNS)).getByRole("button", { name: /Financial:AddColumn/ }));
    fireEvent.click(within(panel(COLUMNS)).getByRole("button", { name: /Financial:AddColumn/ }));
    const bounds = within(panel(COLUMNS)).getAllByLabelText(/^Financial:UpperBound \d$/);
    expect(bounds).toHaveLength(2);
    fireEvent.change(bounds[0], { target: { value: "100" } });
    fireEvent.change(bounds[1], { target: { value: "500" } });
    /* The open column has no Remove. */
    expect(within(panel(COLUMNS)).queryByRole("button", { name: "Remove 2" })).toBeNull();

    tab(RULES);
    expect(within(panel(RULES)).getByText("Financial:SaveListBeforeRulesHint")).toBeInTheDocument();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list", expect.anything()));
    expect(bodyOf(POST)).toEqual({
      code: "BNL",
      name: "Benelux",
      description: null,
      side: 2,
      currencyCode: "GBP",
      basis: 9,
      multiplier: 1,
      columnQuantity: 13,
      stopMatching: 3,
      unitConversionFactor: 1000,
      validFrom: "2026-03-01",
      validTill: null,
      isArchived: false,
      columns: [100, 500, null],
    });
  });

  it("refuses columns that do not rise and a validity that ends before it starts", async () => {
    renderWith(<PriceListEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "MasterData:Code", "X");
    typeIn(GENERAL, "MasterData:Name", "X");
    fireEvent.change(fieldIn(GENERAL, "Financial:ValidFrom"), { target: { value: "2026-03-01" } });
    fireEvent.change(fieldIn(GENERAL, "Financial:ValidTill"), { target: { value: "2026-02-01" } });
    tab(COLUMNS);
    fireEvent.click(within(panel(COLUMNS)).getByRole("button", { name: /Financial:AddColumn/ }));
    fireEvent.click(within(panel(COLUMNS)).getByRole("button", { name: /Financial:AddColumn/ }));
    const bounds = within(panel(COLUMNS)).getAllByLabelText(/^Financial:UpperBound \d$/);
    fireEvent.change(bounds[0], { target: { value: "500" } });
    fireEvent.change(bounds[1], { target: { value: "100" } });
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(COLUMNS)).getByText("Financial:ColumnsNotAscendingHint")).toBeInTheDocument();
    expect(within(panel(GENERAL)).getByText("Financial:ValidTillBeforeFromHint")).toBeInTheDocument();
  });

  it("loads a list with its columns and updates with the stamp; the ribbon commands follow the permissions", async () => {
    grants = { "Financial.PriceLists.Create": true, "Financial.PriceLists.Update": true };
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    expect(ribbonButton("Financial:Copy")).not.toBeNull();
    expect(ribbonButton("Financial:Export")).not.toBeNull();
    expect(ribbonButton("Financial:Import")).not.toBeNull();
    expect(ribbonButton("Financial:Adjust")).toBeNull();
    expect(screen.queryByRole("tab", { name: SIMULATE })).toBeNull();

    tab(COLUMNS);
    const bounds = within(panel(COLUMNS)).getAllByLabelText(/^Financial:UpperBound \d$/);
    expect(bounds.map((b) => (b as HTMLInputElement).value)).toEqual(["100", "500"]);
    fireEvent.change(bounds[1], { target: { value: "600" } });
    /* Unsaved changes park the commands (UI-ACT-008); ribbon buttons carry the state as aria-disabled. */
    expect(ribbonButton("Financial:Export")).toHaveAttribute("aria-disabled", "true");

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/financial/price-list/{id}", expect.anything()));
    expect(bodyOf(PUT)).toMatchObject({ code: "BNL", columns: [100, 600, null], concurrencyStamp: "stamp-pl1" });
  });

  it("exports the saved list as a download", async () => {
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    fireEvent.click(ribbonButton("Financial:Export")!);
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list/{id}/export", { params: { path: { id: "pl-1" } }, parseAs: "blob" }));
    await waitFor(() => expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), "BNL.xlsx"));
  });
});

describe("PriceListEditor — rules", () => {
  it("lists the rules under the list, opens one and saves its lane, dimensions and ladder per rule", async () => {
    grants = { "Financial.PriceLists.Update": true };
    gridRows.current = [RULE_ROW];
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    tab(RULES);
    expect((await within(panel(RULES)).findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.priceListRules");
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/price-list/{id}/rules", expect.objectContaining({ params: expect.objectContaining({ path: { id: "pl-1" } }) })));
    const rulesCall = GET.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/rules")!;
    expect(rulesCall[1]!.params!.query).toMatchObject({ StartRow: 0, EndRow: 50, Tier: undefined });

    fireEvent.click(within(panel(RULES)).getByRole("button", { name: "Edit" }));
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/price-list/{id}/rule/{ruleId}", { params: { path: { id: "pl-1", ruleId: "rule-1" } } }));
    const detail = await within(panel(RULES)).findByText("Financial:Rule 1");
    expect(detail).toBeInTheDocument();
    /* The tier badge of the detail (the tier filter lists "Financial:Tier 1" as an option too). */
    expect(within(panel(RULES).querySelector("[data-slot=rule-detail]") as HTMLElement).getByText("Financial:Tier 1")).toBeInTheDocument();

    /* The ladder's price columns are the list's columns: two bounded ones and the open one. */
    const ladder = panel(RULES).querySelector("[data-slot=grid]")!;
    expect(Array.from(ladder.querySelectorAll("[data-slot=columns] > span")).map((s) => s.textContent)).toEqual(["Financial:UpperBound", "≤ 100", "≤ 500", "Financial:Open"]);
    const prices = within(ladder as HTMLElement).getAllByLabelText(/^≤ 100 \d$/);
    expect((prices[0] as HTMLInputElement).value).toBe("10");
    fireEvent.change(prices[0], { target: { value: "11" } });
    fireEvent.change(within(ladder as HTMLElement).getByLabelText("Financial:Open 1"), { target: { value: "" } });
    fireEvent.change(within(panel(RULES)).getByLabelText("Financial:Product"), { target: { value: "pr-1" } });
    typeIn(RULES, "Financial:MinAmount", "25");

    fireEvent.click(within(panel(RULES)).getByRole("button", { name: /Financial:SaveRule/ }));
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/financial/price-list/{id}/rule/{ruleId}", expect.anything()));
    const call = PUT.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/rule/{ruleId}")!;
    expect(call[1]!.params).toEqual({ path: { id: "pl-1", ruleId: "rule-1" } });
    expect(call[1]!.body).toEqual({
      fromRelationId: "rel-1",
      fromRegionId: null,
      toRelationId: null,
      toRegionId: "r-1",
      transportUnitId: "tu-1",
      productId: "pr-1",
      vehicleTypeId: null,
      rowQuantity: 4,
      minAmount: 25,
      maxAmount: null,
      brackets: [
        { upperBound: 1000, unitPrices: [11, 9, 8] },
        { upperBound: null, unitPrices: [7, 6, null] },
      ],
    });
  });

  it("adds a new rule: one open bracket with a price per column, refused without a lane", async () => {
    grants = { "Financial.PriceLists.Update": true };
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    tab(RULES);
    fireEvent.click(await within(panel(RULES)).findByRole("button", { name: /Financial:AddRule/ }));
    expect(within(panel(RULES)).getByText("Financial:NewRule")).toBeInTheDocument();
    fireEvent.click(within(panel(RULES)).getByRole("button", { name: /Financial:SaveRule/ }));
    expect(POST).not.toHaveBeenCalledWith("/api/financial/price-list/{id}/rule", expect.anything());
    expect(within(panel(RULES)).getByText("Financial:LaneRequiredHint")).toBeInTheDocument();

    fireEvent.change(within(panel(RULES)).getAllByLabelText("Financial:Region")[1], { target: { value: "r-1" } });
    const ladder = panel(RULES).querySelector("[data-slot=grid]") as HTMLElement;
    fireEvent.change(within(ladder).getByLabelText("≤ 100 0"), { target: { value: "12.5" } });
    fireEvent.click(within(panel(RULES)).getByRole("button", { name: /Financial:SaveRule/ }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list/{id}/rule", expect.anything()));
    const call = POST.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/rule")!;
    expect(call[1]!.body).toMatchObject({ fromRelationId: null, toRegionId: "r-1", rowQuantity: 0, brackets: [{ upperBound: null, unitPrices: [12.5, null, null] }] });
  });

  it("without Update the rules open read-only: no Add rule, no Save rule", async () => {
    gridRows.current = [RULE_ROW];
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    tab(RULES);
    await within(panel(RULES)).findByTestId("mock-grid");
    expect(within(panel(RULES)).queryByRole("button", { name: /Financial:AddRule/ })).toBeNull();
    fireEvent.click(within(panel(RULES)).getByRole("button", { name: "Open" }));
    await within(panel(RULES)).findByText("Financial:Rule 1");
    expect(within(panel(RULES)).queryByRole("button", { name: /Financial:SaveRule/ })).toBeNull();
  });
});

describe("PriceListEditor — simulate and adjust", () => {
  it("posts the line snapshot and shows the winner, the price and the ranked candidates", async () => {
    grants = { "Financial.PriceLists.Simulate": true };
    POST.mockImplementation(async (path) =>
      path.endsWith("/simulate")
        ? ok({
            winningRuleId: "rule-1",
            candidates: [
              { ruleId: "rule-1", tier: 1, laneScore: 3, position: 1, reason: "Winner" },
              { ruleId: "rule-2", tier: 0, laneScore: 1, position: 2, reason: "Candidate" },
            ],
            columnPosition: 2,
            unitPrice: 9,
            quantity: 250,
            amount: 2250,
            warning: null,
            isValidOnAsOf: true,
          })
        : ok({ id: "new" }),
    );
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    tab(SIMULATE);
    fireEvent.click(within(panel(SIMULATE)).getByRole("button", { name: /Financial:RunSimulation/ }));
    expect(POST).not.toHaveBeenCalledWith("/api/financial/price-list/{id}/simulate", expect.anything());
    expect(within(panel(SIMULATE)).getAllByText("MasterData:Required").length).toBeGreaterThan(0);

    const relations = within(panel(SIMULATE)).getAllByLabelText(/^Financial:Relation/);
    fireEvent.change(relations[0], { target: { value: "rel-1" } });
    fireEvent.change(relations[1], { target: { value: "rel-2" } });
    await waitFor(() => expect(within(panel(SIMULATE)).getByRole("option", { name: "Pallet" })).toBeInTheDocument());
    fireEvent.change(within(panel(SIMULATE)).getByLabelText(/^Financial:TransportUnit/), { target: { value: "tu-1" } });
    typeIn(SIMULATE, "Financial:QuantitySource.WeightKg", "250");
    fireEvent.change(fieldIn(SIMULATE, "Financial:AsOf"), { target: { value: "2026-06-01" } });
    fireEvent.click(within(panel(SIMULATE)).getByRole("button", { name: /Financial:RunSimulation/ }));

    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list/{id}/simulate", expect.anything()));
    const call = POST.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/simulate")!;
    expect(call[1]!.body).toMatchObject({ fromRelationId: "rel-1", toRelationId: "rel-2", transportUnitId: "tu-1", quantity: 1, weightKg: 250, asOf: "2026-06-01" });
    const result = await within(panel(SIMULATE)).findByText("Financial:Rule 1");
    expect(result).toBeInTheDocument();
    const rows = panel(SIMULATE).querySelectorAll("tbody tr");
    expect(rows).toHaveLength(2);
    expect(rows[0].getAttribute("data-winner")).toBe("true");
    expect(rows[0].textContent).toContain("Financial:Reason.Winner");
    expect(rows[1].textContent).toContain("Financial:Reason.Candidate");
  });

  it("previews an adjustment before applying it, and refreshes the rules afterwards", async () => {
    grants = { "Financial.PriceLists.Adjust": true, "Financial.PriceLists.Update": true };
    POST.mockImplementation(async (path, init) =>
      path.endsWith("/adjust")
        ? ok({ changedCellCount: 6, sample: (init?.body as { preview: boolean }).preview ? [{ ruleId: "rule-1", bracketPosition: 1, columnPosition: 1, before: 10, after: 10.5 }] : [] })
        : ok({ id: "new" }),
    );
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    fireEvent.click(ribbonButton("Financial:Adjust")!);
    const dialog = () => document.querySelectorAll<HTMLElement>("[role=dialog]")[1];
    await waitFor(() => expect(dialog()).toBeDefined());
    const apply = () => within(dialog()).getByRole("button", { name: "Financial:ApplyAdjustment" });
    expect(apply()).toBeDisabled();

    fireEvent.change(within(dialog()).getByLabelText(/^Financial:Percentage/), { target: { value: "5" } });
    fireEvent.change(within(dialog()).getByLabelText(/^Financial:Rounding/), { target: { value: "2" } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Financial:Preview" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list/{id}/adjust", expect.anything()));
    expect(POST.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/adjust")![1]!.body).toEqual({ percentage: 5, amount: null, rounding: 2, preview: true });
    await waitFor(() => expect(within(dialog()).getByText("6")).toBeInTheDocument());
    expect(dialog().querySelectorAll("tbody tr")).toHaveLength(1);

    fireEvent.click(apply());
    await waitFor(() => expect(POST.mock.calls.filter(([p]) => p === "/api/financial/price-list/{id}/adjust")).toHaveLength(2));
    expect(POST.mock.calls.filter(([p]) => p === "/api/financial/price-list/{id}/adjust")[1][1]!.body).toEqual({ percentage: 5, amount: null, rounding: 2, preview: false });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Financial:AdjustmentApplied"));
  });

  it("copies the list into a new one and opens it", async () => {
    grants = { "Financial.PriceLists.Create": true };
    const onOpenRecord = vi.fn();
    POST.mockImplementation(async (path) => (path.endsWith("/copy") ? ok({ ...LIST, id: "pl-2", code: "BNL2" }) : ok({ id: "new" })));
    renderWith(<PriceListEditor mode="edit" id="pl-1" readOnly={false} open onClose={noop} onSaved={noop} onOpenRecord={onOpenRecord} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Code").value).toBe("BNL"));
    fireEvent.click(ribbonButton("Financial:Copy")!);
    const dialog = () => document.querySelectorAll<HTMLElement>("[role=dialog]")[1];
    await waitFor(() => expect(dialog()).toBeDefined());
    fireEvent.change(within(dialog()).getByLabelText(/^MasterData:Code/), { target: { value: "bnl2" } });
    fireEvent.change(within(dialog()).getByLabelText(/^Financial:ValidFrom/), { target: { value: "2027-01-01" } });
    fireEvent.submit(dialog().querySelector("form")!);
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/price-list/{id}/copy", expect.anything()));
    expect(POST.mock.calls.find(([p]) => p === "/api/financial/price-list/{id}/copy")![1]!.body).toEqual({ code: "BNL2", name: "Benelux (financial:copy)", validFrom: "2027-01-01", validTill: null });
    await waitFor(() => expect(onOpenRecord).toHaveBeenCalledWith("pl-2"));
  });
});
