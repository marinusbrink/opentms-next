import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The relation financial settings editor (design 57 §UI, design 61): created on the first edit (no stamp), loaded by
 * relation and upserted with the stamp afterwards; picking a relation that has settings already edits those; the price
 * lists per side as lines in their order with each list once; the default discounts with their validity. The domain
 * hooks are real; the generated client is the mock; the pickers and grids are native stand-ins. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: {} }, features: { values: {} }, localization: { languages: [] } } }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./run2-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/entity-lookup", async () => ({ EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./run2-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGridFn }));
vi.mock("@/components/ui/date-picker", async () => ({
  DatePicker: (await import("./transport-test-mocks")).MockDatePicker,
  DATE_CELL_EDITOR_COL_DEF: (await import("./transport-test-mocks")).DATE_CELL_EDITOR_COL_DEF_MOCK,
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { RelationFinancialSettingsEditor } from "@/apps/master-data/editors/RelationFinancialSettingsEditor";
import { editorDialog, fieldIn, panel, submitEditor, tab, typeIn } from "./platform-test-helpers";
import { ok } from "./transport-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;

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
const LISTS = "Financial:PriceLists";
const DISCOUNTS = "Financial:DefaultDiscountsOrSurcharges";

/* Design 61 "Order page review" 3: the revenue lists are looked up per basis (R1 by weight, R2 by quantity). */
const REVENUE_LISTS = [
  { id: "pl-r1", code: "R1", name: "Benelux", basis: 4 },
  { id: "pl-r2", code: "R2", name: "Germany", basis: 1 },
];
const COST_LISTS = [{ id: "pl-c1", code: "C1", name: "Carrier X" }];
const DISCOUNT_ITEMS = [{ id: "ds-1", code: "FUEL", name: "Fuel surcharge" }];

const EXISTING = {
  id: "s1", relationId: "rel-1", relationName: "Acme",
  revenuePriceLists: [{ priceListId: "pl-r2", code: "R2", name: "Germany", position: 1, basis: 1 }],
  revenueChargedBases: [3],
  costPriceLists: [],
  revenueMultipleMatches: 2, costMultipleMatches: 1,
  discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", name: "Fuel surcharge", validFrom: "2026-01-01", validTill: null }],
  vatPercentageId: null, paymentTermDays: 30, chargeFixedPriceByDefault: true, sendFreightDocumentsWithInvoice: false, currencyCode: null,
  concurrencyStamp: "stamp-s1", creationTime: "2026-10-25T10:00:00Z",
};

let settingsByRelation: Record<string, unknown> | null = null;

beforeEach(() => {
  settingsByRelation = null;
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/financial/price-list/lookup": {
        const query = init?.params?.query ?? {};
        if (query.side !== 1) return ok(COST_LISTS);
        return ok(REVENUE_LISTS.filter((l) => query.basis == null || l.basis === query.basis).map(({ basis: _basis, ...l }) => l));
      }
      case "/api/financial/discount-or-surcharge/lookup":
        return ok(DISCOUNT_ITEMS);
      case "/api/financial/relation-financial-settings/by-relation/{relationId}":
        return settingsByRelation ? ok(settingsByRelation) : ok(undefined, 204);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async () => ok({ ...EXISTING }));
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const bodyOf = (mock: Mock<ClientCall>) => mock.mock.calls[0][1]!.body as Record<string, unknown>;
const lines = (tabLabel: string) => Array.from(panel(tabLabel).querySelectorAll<HTMLElement>("[data-slot=line]"));
const matchesSelects = () => within(panel(LISTS)).getAllByLabelText(/^Financial:MultipleMatches(?![A-Za-z.])/, { selector: "select" }) as HTMLSelectElement[];
const noop = () => undefined;
/* A row of the block "Price calculation" (PriceListsPerBasis, a row per charged basis): its lists and its remove button;
 * "Add price basis" adds one. */
const basisRow = (basis: number) => panel(LISTS).querySelector<HTMLElement>(`[data-slot=charged-basis-row][data-basis="${basis}"]`)!;
const shownBases = () => Array.from(panel(LISTS).querySelectorAll<HTMLElement>("[data-slot=charged-basis-row]")).map((r) => Number(r.dataset.basis));
const removeBasis = (basis: number, label: string) => within(basisRow(basis)).getByRole("button", { name: `Financial:RemoveChargedBasis: ${label}` });
const addBasis = async (label: string) => {
  fireEvent.click(within(panel(LISTS)).getByRole("button", { name: /Financial:AddChargedBasis/ }));
  fireEvent.click(await screen.findByRole("menuitem", { name: label }));
};

describe("RelationFinancialSettingsEditor — create", () => {
  it("needs a relation, then upserts by relation without a stamp with the lists in order and the discounts", async () => {
    renderWith(<RelationFinancialSettingsEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("MasterData:Required").length).toBeGreaterThan(0);

    fireEvent.change(fieldIn(GENERAL, "Financial:Relation"), { target: { value: "rel-1" } });
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/relation-financial-settings/by-relation/{relationId}", { params: { path: { relationId: "rel-1" } } }));
    typeIn(GENERAL, "Financial:PaymentTermDays", "14");
    fireEvent.click(within(editorDialog()).getByRole("checkbox", { name: "Financial:ChargeFixedPriceByDefault" }));

    tab(LISTS);
    /* Two "several matching lists" rules: the revenue side's first, the cost side's second. */
    fireEvent.change(matchesSelects()[0], { target: { value: "3" } });
    /* The revenue lists per basis: none yet; R2 in an added quantity row, R1 in an added weight row, distance added
     * without a list. */
    expect(shownBases()).toEqual([]);
    await addBasis("Financial:ChargedBasis.LineQuantity");
    await waitFor(() => expect(within(basisRow(1)).getByLabelText("R2 Germany")).toBeInTheDocument());
    fireEvent.click(within(basisRow(1)).getByLabelText("R2 Germany"));
    await addBasis("Financial:ChargedBasis.WeightKg");
    await waitFor(() => expect(within(basisRow(4)).getByLabelText("R1 Benelux")).toBeInTheDocument());
    fireEvent.click(within(basisRow(4)).getByLabelText("R1 Benelux"));
    await addBasis("Financial:ChargedBasis.DistanceKm");
    expect(shownBases()).toEqual([1, 4, 9]);
    expect(within(basisRow(1)).queryByLabelText("R1 Benelux")).toBeNull();
    /* The cost side keeps its plain list. */
    fireEvent.click(within(panel(LISTS)).getByRole("button", { name: /Financial:AddPriceList/ }));
    fireEvent.change(within(panel(LISTS)).getAllByLabelText(/^Financial:PriceList \d$/)[0], { target: { value: "pl-c1" } });

    tab(DISCOUNTS);
    fireEvent.click(within(panel(DISCOUNTS)).getByRole("button", { name: /Financial:AddDiscountOrSurcharge/ }));
    fireEvent.change(within(panel(DISCOUNTS)).getByLabelText("Financial:DiscountOrSurcharge 0"), { target: { value: "ds-1" } });
    fireEvent.change(within(panel(DISCOUNTS)).getByLabelText("Financial:ValidFrom 0"), { target: { value: "2026-11-01" } });

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/financial/relation-financial-settings/upsert-by-relation/{relationId}", expect.anything()));
    expect(POST.mock.calls[0][1]!.params).toEqual({ path: { relationId: "rel-1" } });
    expect(bodyOf(POST)).toEqual({
      revenuePriceListIds: ["pl-r2", "pl-r1"],
      revenueChargedBases: [1, 4, 9],
      costPriceListIds: ["pl-c1"],
      revenueMultipleMatches: 3,
      costMultipleMatches: 1,
      discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", validFrom: "2026-11-01", validTill: null }],
      vatPercentageId: null,
      paymentTermDays: 14,
      chargeFixedPriceByDefault: true,
      sendFreightDocumentsWithInvoice: false,
      currencyCode: null,
      concurrencyStamp: null,
    });
  });

  it("refuses the same list twice on a side and a discount without a start date", async () => {
    renderWith(<RelationFinancialSettingsEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    fireEvent.change(fieldIn(GENERAL, "Financial:Relation"), { target: { value: "rel-1" } });
    tab(LISTS);
    const [addRevenue] = within(panel(LISTS)).getAllByRole("button", { name: /Financial:AddPriceList/ });
    fireEvent.click(addRevenue);
    fireEvent.click(addRevenue);
    for (const input of within(panel(LISTS)).getAllByLabelText(/^Financial:PriceList \d$/)) fireEvent.change(input, { target: { value: "pl-r1" } });
    tab(DISCOUNTS);
    fireEvent.click(within(panel(DISCOUNTS)).getByRole("button", { name: /Financial:AddDiscountOrSurcharge/ }));
    fireEvent.change(within(panel(DISCOUNTS)).getByLabelText("Financial:DiscountOrSurcharge 0"), { target: { value: "ds-1" } });

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(LISTS)).getByText("Financial:DuplicatePriceListHint")).toBeInTheDocument();
    expect(within(panel(DISCOUNTS)).getByText("MasterData:Required")).toBeInTheDocument();
  });

  it("picks up the settings a relation has already and sends their stamp", async () => {
    settingsByRelation = EXISTING;
    renderWith(<RelationFinancialSettingsEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    fireEvent.change(fieldIn(GENERAL, "Financial:Relation"), { target: { value: "rel-1" } });
    await waitFor(() => expect(fieldIn(GENERAL, "Financial:PaymentTermDays").value).toBe("30"));
    expect(screen.getByText("Financial:SettingsExistForRelationHint")).toBeInTheDocument();
    tab(LISTS);
    /* No cost list; the revenue lists are rows per basis, not lines. */
    expect(lines(LISTS)).toHaveLength(0);
    expect(matchesSelects()[0].value).toBe("2");
    /* The stored list in its basis row, the stored colli basis as a row without a list, no other rows. */
    await waitFor(() => expect(within(basisRow(1)).getByLabelText("R2 Germany")).toBeChecked());
    expect(shownBases()).toEqual([1, 3]);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalled());
    expect(bodyOf(POST)).toMatchObject({ revenuePriceListIds: ["pl-r2"], revenueChargedBases: [3], revenueMultipleMatches: 2, paymentTermDays: 30, concurrencyStamp: "stamp-s1" });
  });
});

describe("RelationFinancialSettingsEditor — edit", () => {
  it("loads the settings by relation with the relation fixed, and upserts with the stamp", async () => {
    settingsByRelation = EXISTING;
    renderWith(<RelationFinancialSettingsEditor mode="edit" id="s1" relationId="rel-1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "Financial:PaymentTermDays").value).toBe("30"));
    expect(fieldIn(GENERAL, "Financial:Relation")).toBeDisabled();
    expect(within(editorDialog()).getByRole("checkbox", { name: "Financial:ChargeFixedPriceByDefault" })).toBeChecked();

    tab(DISCOUNTS);
    expect(within(panel(DISCOUNTS)).getByLabelText("Financial:ValidFrom 0")).toHaveValue("2026-01-01");
    fireEvent.change(within(panel(DISCOUNTS)).getByLabelText("Financial:ValidTill 0"), { target: { value: "2026-12-31" } });

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalled());
    expect(POST.mock.calls[0][1]!.params).toEqual({ path: { relationId: "rel-1" } });
    expect(bodyOf(POST)).toMatchObject({
      discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", validFrom: "2026-01-01", validTill: "2026-12-31" }],
      concurrencyStamp: "stamp-s1",
    });
  });

  it("is read-only without Update: no Save and no editable control", async () => {
    settingsByRelation = EXISTING;
    renderWith(<RelationFinancialSettingsEditor mode="edit" id="s1" relationId="rel-1" readOnly open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "Financial:PaymentTermDays").value).toBe("30"));
    expect(fieldIn(GENERAL, "Financial:PaymentTermDays")).toBeDisabled();
    expect(Array.from(editorDialog().querySelectorAll<HTMLElement>("[data-slot=ribbon-button]")).some((b) => b.textContent?.includes("Save"))).toBe(false);
    tab(LISTS);
    expect(within(panel(LISTS)).queryByRole("button", { name: /Financial:AddPriceList/ })).toBeNull();
    expect(removeBasis(3, "Financial:ChargedBasis.Colli")).toBeDisabled();
    await waitFor(() => expect(within(basisRow(1)).getByLabelText("R2 Germany")).toBeDisabled());
  });
});
