import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 61 "Order page review" (PO decisions 2026-10-28) on the order page, against the client mock:
 * - 3: the Financial tab's block Price calculation shows the revenue lists per basis (PriceListsPerBasis), the fixed
 *   price disables the rows, and the save sends `chargedBases`; an open line ("To be priced", kind PriceList without a
 *   list) shows the badge and its basis, its unit price is adjusted inline and it can be removed like a manual line,
 *   adjusted or not, while a list's line cannot;
 * - 4: the purchase prices of a freight item in Edit freight item → Pricing: before the item is on a trip a note and no
 *   Add, then the item's cost lines (only its own) with the trip as a link, basis and total; Add purchase line prefills
 *   the quantity from the item's value for the basis, preselects the only trip and posts trip, item and the item's first
 *   leg on it; Adjust unit price on a calculated line; Remove only on a manual line; hidden without the view permission;
 * - 5: the legs grid's Split leg at … (disabled with its reason on a trip) and Remove split (only on a split-at-address
 *   half, disabled when either half is on a trip), hidden without PlanningExecution.Legs.Update; the dialog defaults the
 *   departure to the arrival's successive activity and the date to the origin date and posts the body; Remove split asks
 *   and posts.
 * Risk class: Medium (UI flows; the money and planning rules are the server's, covered by the backend tests). */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
/* The save report's keys carry their {0}, so a test sees which section names were filled in. */
/* The stop and item names carry their placeholders, so a test sees which stop a message names. */
const TEXTS: Record<string, string> = { "PlanningExecution:SplitLegAtAddressHint": "Split {0} → {1}", "PlanningExecution:RemoveSplitQuestion": "Remove the split of {0}?", "Orders:StopNamed": "Stop {0} ({1})", "Orders:StopNumbered": "Stop {0}", "Orders:ItemProblem": "Item {0} · {1}", "Orders:DeleteFreightItemQuestion": "Delete freight item {0}?" };
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => (k.startsWith("Orders:Sections") ? `${k} {0}` : (TEXTS[k] ?? k)), culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Orders.Templates": "true", "Apps.Transport.Trips": "true" } } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/lib/realtime/record-updates", () => ({
  DerivedValueStatus: { Pending: 1, UpToDate: 2, Failed: 3 },
  recordUpdatesClient: () => ({ watch: () => () => undefined, subscribe: () => () => undefined, onReconnected: () => () => undefined }),
}));
vi.mock("@/components/ui/opentms-grid-lazy", async () => ({ LazyOpenTmsGrid: (await import("./transport-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGrid }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/tree-select", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/tree-select")>()), TreeSelect: (await import("./transport-test-mocks")).MockTreeSelect }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/apps/transport/wizards/PrintCmrWizard", () => ({ PrintCmrWizard: ({ open }: { open: boolean }) => (open ? <div data-testid="print-wizard" /> : null) }));
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { OrderPage } from "@/apps/transport/pages/OrderPage";
import { fail, gridRows, itemTab, lookupItems, openItem, openTab, ribbonButton, section, stubGlobals } from "./transport-test-mocks";
import { DELETE, GET, POST, PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const VIEW = "Orders.TransportOrders";
const ALL = { [VIEW]: true, "PlanningExecution.TransportTrips": true, "Orders.TransportOrders.Update": true, "Orders.TransportOrders.Confirm": true, "Orders.TransportOrders.Cancel": true, "Orders.TransportOrders.Approve": true, "Orders.TransportOrders.Revise": true, "Orders.TransportOrders.Archive": true, "Orders.TransportOrders.Print": true, "Orders.TransportOrders.Delete": true, "Financial.RateLines.Update": true };

const stops = (date1: string, date2: string) => [
  { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationName: "Acme", relationAddressId: "adr-1", city: "Utrecht", date: date1, reference: "LOAD-1", windowFrom: "08:00:00" },
  { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationName: "Beta", relationAddressId: "adr-2", city: "Gouda", date: date2 },
];
const order = (status: number, extra: Record<string, unknown> = {}) => ({
  id: "o1",
  number: status === 1 ? null : "TO-2026-00001",
  status,
  transportOrderTypeId: "type-1",
  transportOrderTypeName: "Standard",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  customerReference: "REF-1",
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-25T08:00:00Z",
  defaultStops: [{ waypointTypeId: "wp-load", waypointTypeName: "Load", dayOffset: 0, position: 1 }],
  lines: [{ id: "l1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 2, weightKg: 500, paymentInstruction: 0, stops: stops("2026-10-25", "2026-10-26") }],
  financialTerms: { currencyCode: "EUR", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] },
  summary: { revenueAmount: 250 },
  ...extra,
});
const twoLines = () => [order(1).lines[0], { ...order(1).lines[0], id: "l2", position: 2, stops: stops("2026-10-26", "2026-10-27").map((s) => ({ ...s, id: `${s.id}b` })) }];

const rateLines = {
  currencyCode: "EUR",
  lines: [
    { id: "r1", orderId: "o1", orderLineId: "l1", kind: 1, quantity: 2, unitPrice: 100, amount: 200, priceListCode: "PL-NL", position: 1, concurrencyStamp: "r1s", isManual: false },
    { id: "r2", orderId: "o1", kind: 2, quantity: 1, unitPrice: 50, amount: 50, description: "Handling", position: 2, concurrencyStamp: "r2s", isManual: true },
  ],
  totals: { linesAmount: 250, discountsAndSurchargesAmount: 0, additionalServicesAmount: 0, total: 250, hasFixedPrice: false, byAnchor: [{ anchorId: "l1", amount: 200 }] },
  state: { status: 2 },
};

const LOAD_UNLOAD = [{ waypointTypeId: "wp-load", name: "Load", position: 1 }, { waypointTypeId: "wp-unload", name: "Unload", position: 2 }];

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { ...ALL };
  gridRows.current = [];
  lookupItems.current = {};
  resetClient();
  routes.GET["/api/orders/transport-order/{id}"] = () => order(2);
  routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 1 | 64, defaultWaypoints: LOAD_UNLOAD });
  routes.GET["/api/financial/rate-line"] = () => rateLines;
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", name: "Pallet" }, { id: "unit-2", name: "Box" }];
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-load", name: "Load" }, { id: "wp-unload", name: "Unload" }];
  routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }];
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }];
});

function renderPage(client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })) {
  return render(
    <QueryClientProvider client={client}>
      <OrderPage id="o1" />
    </QueryClientProvider>,
  );
}

const click = async (element: Element) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const loaded = () => screen.findByText("Orders:TransportOrder TO-2026-00001");
/** A main tab's trigger in the page's strip. */

/* ── fixtures ─────────────────────────────────────────────────────────────────────────────────────────────── */

/* The item's revenue lines: a list's line, an open line never priced ("To be priced", by weight) and an adjusted open line (by distance). */
const OPEN = { id: "r-open", orderId: "o1", orderLineId: "l1", kind: 1, side: 1, quantity: 500, quantitySource: 4, unitPrice: 0, amount: 0, priceListId: null, priceListCode: null, descriptionKey: "Financial:RateLine.OpenBasis", position: 2, concurrencyStamp: "ro", isManual: false, isAdjusted: false };
const ADJUSTED_OPEN = { ...OPEN, id: "r-adj", quantitySource: 9, quantity: 100, unitPrice: 1.2, amount: 120, isAdjusted: true, position: 3, concurrencyStamp: "ra" };
const REVENUE = { ...rateLines, lines: [rateLines.lines[0], OPEN, ADJUSTED_OPEN, rateLines.lines[1]] };

/* The order's cost lines (side 2): a calculated and a manual one of item l1 on trip TR-7, and one of item l2. */
const COST = {
  currencyCode: "EUR",
  lines: [
    { id: "c1", legId: "leg-1", anchorTripId: "trip-7", tripNumber: "TR-7", legOrderLineId: "l1", kind: 1, side: 2, quantity: 2, quantitySource: 1, unitPrice: 40, amount: 80, priceListId: "cl-1", priceListCode: "CL-NL", isManual: false, position: 1, concurrencyStamp: "c1s" },
    { id: "c2", legId: "leg-1", anchorTripId: "trip-7", tripNumber: "TR-7", legOrderLineId: "l1", kind: 2, side: 2, quantity: 1, quantitySource: 0, unitPrice: 30, amount: 30, description: "Waiting time", isManual: true, position: 2, concurrencyStamp: "c2s" },
    { id: "c3", legId: "leg-2", anchorTripId: "trip-8", tripNumber: "TR-8", legOrderLineId: "l2", kind: 1, side: 2, quantity: 3, quantitySource: 1, unitPrice: 333, amount: 999, priceListId: "cl-1", priceListCode: "CL-NL", isManual: false, position: 1, concurrencyStamp: "c3s" },
  ],
  totals: { total: 1109 },
  state: { status: 2 },
};

const legsPage = (rows: Record<string, unknown>[]) => ({ rows, totalCount: rows.length, filteredCount: rows.length });
const leg = (id: string, extra: Record<string, unknown> = {}) => ({
  id, orderId: "o1", orderLineId: "l1", position: 1, source: 1, quantity: 2, concurrencyStamp: `${id}-s`,
  origin: { name: "Acme", city: "Utrecht", plannedDate: "2026-10-25" }, destination: { name: "Beta", city: "Gouda", plannedDate: "2026-10-26" }, ...extra,
});

/* The view permission of the rate lines and the planner's leg commands on top of the page's grants. */
const REVIEW = { ...ALL, "Financial.RateLines": true, "PlanningExecution.Legs.Update": true };

beforeEach(() => {
  grants = { ...REVIEW };
  routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { lines: twoLines() });
  routes.GET["/api/financial/rate-line"] = (init) => (init?.params?.query?.Side === 2 ? COST : REVENUE);
  routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-1", { tripId: "trip-7", tripNumber: "TR-7" }), leg("leg-2", { orderLineId: "l2" })]);
});

const sales = (pricing: HTMLElement) => pricing.querySelector<HTMLElement>("[data-slot=item-pricing] > div [data-slot=mock-lines-grid]")!;
const purchase = () => document.querySelector<HTMLElement>("[data-slot=item-purchase-prices]");
const lineOf = (scope: HTMLElement, id: string) => scope.querySelector<HTMLElement>(`[data-slot=mock-line][data-key="${id}"]`);
const actionDialog = () => document.querySelector<HTMLElement>("[role=dialog][data-variant=action]");
const submitAction = async () =>
  act(async () => {
    fireEvent.submit(actionDialog()!.querySelector("form")!);
    await Promise.resolve();
  });

/* ── 3: the per-basis rows and the open lines ─────────────────────────────────────────────────────────────── */

describe("Order page review 3 — price lists per basis on the Financial tab", () => {
  beforeEach(() => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(2, { lines: twoLines(), financialTerms: { currencyCode: "EUR", priceListIds: ["pl-w"], priceListNames: ["Weight NL"], priceListBases: [4], chargedBases: [9], multipleMatches: 1, discountsOrSurcharges: [] } });
    routes.GET["/api/financial/price-list/lookup"] = (init) => (init?.params?.query?.basis === 4 ? [{ id: "pl-w", code: "W", name: "Weight NL" }, { id: "pl-w2", code: "W2", name: "Weight BE" }] : []);
    routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => order(2, { concurrencyStamp: "stamp-2" });
  });

  const row = (basis: number) => section("Orders:PriceCalculation").querySelector<HTMLElement>(`[data-slot=charged-basis-row][data-basis="${basis}"]`)!;

  const keyOf = (name: string) => `Financial:ChargedBasis.${name}`;
  const shownBases = () => Array.from(section("Orders:PriceCalculation").querySelectorAll<HTMLElement>("[data-slot=charged-basis-row]")).map((r) => Number(r.dataset.basis));
  const addBasisButton = () => within(section("Orders:PriceCalculation")).getByRole("button", { name: /Financial:AddChargedBasis/ });

  it("shows a row per charged basis with its stored lists, and saves the changed rows as chargedBases", async () => {
    renderPage();
    await loaded();
    openTab("Orders:Financial");
    await waitFor(() => expect(within(row(4)).getByLabelText("W Weight NL")).toBeChecked());
    /* Weight through its stored list, distance switched on; the other bases have no row. */
    expect(shownBases()).toEqual([4, 9]);

    /* A second weight list; the distance row removed; quantity added without a list. */
    fireEvent.click(within(row(4)).getByLabelText("W2 Weight BE"));
    fireEvent.click(within(row(9)).getByRole("button", { name: `Financial:RemoveChargedBasis: ${keyOf("DistanceKm")}` }));
    fireEvent.click(addBasisButton());
    fireEvent.click(await screen.findByRole("menuitem", { name: keyOf("LineQuantity") }));
    expect(shownBases()).toEqual([1, 4]);
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body).toEqual(expect.objectContaining({ priceListIds: ["pl-w", "pl-w2"], chargedBases: [1, 4] }));
  });

  it("the fixed price disables the rows, their removes and Add price basis", async () => {
    renderPage();
    await loaded();
    openTab("Orders:Financial");
    await waitFor(() => expect(within(row(4)).getByLabelText("W Weight NL")).not.toBeDisabled());
    const fixed = within(section("Orders:PriceCalculation")).getAllByRole("radio").find((r) => r.getAttribute("value") === "fixed" || /Fixed/i.test(r.getAttribute("aria-label") ?? r.closest("label")?.textContent ?? ""));
    expect(fixed, "the fixed-price choice").toBeTruthy();
    fireEvent.click(fixed!);
    await waitFor(() => expect(within(row(4)).getByLabelText("W Weight NL")).toBeDisabled());
    expect(within(row(9)).getByRole("button", { name: `Financial:RemoveChargedBasis: ${keyOf("DistanceKm")}` })).toBeDisabled();
    expect(addBasisButton()).toHaveAttribute("data-disabled");
  });

  it("stacks Price calculation and Invoicing left, Revenue overview and Remarks right, so no section stretches to its neighbour", async () => {
    renderPage();
    await loaded();
    openTab("Orders:Financial");
    const columns = Array.from(document.querySelector<HTMLElement>("[data-slot=financial-terms]")!.children).filter((c) => c.tagName === "DIV");
    const titles = (column: Element) => Array.from(column.querySelectorAll(":scope > [data-slot=form-section]")).map((sec) => sec.getAttribute("aria-label"));
    expect(columns.map(titles)).toEqual([
      ["Orders:PriceCalculation", "Orders:Invoicing"],
      ["Orders:RevenueOverview", "Orders:InvoicingRemarksBlock"],
    ]);
    expect(document.querySelector("[data-slot=financial-terms]")).toHaveClass("items-start");
    /* The fixed price for the order is the first choice (PO 2026-10-28). */
    expect(within(section("Orders:PriceCalculation")).getAllByRole("radio").map((r) => r.closest("label")?.textContent)).toEqual(["Orders:FixedPriceForOrder", "Orders:PricedPerFreightItem"]);
  });
});

describe("Order page review 3 — open lines in the item's Pricing", () => {
  it("an open line shows \"To be priced\" and its basis; adjusted it shows its kind; both can be removed, the list's line cannot", async () => {
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    const grid = await waitFor(() => {
      const found = sales(pricing);
      expect(lineOf(found, "r-open")).not.toBeNull();
      return found;
    });

    const open = lineOf(grid, "r-open")!;
    expect(open.querySelector("[data-slot=to-be-priced]")).toHaveTextContent("Financial:RateLine.OpenBasis");
    expect(open).toHaveTextContent("Financial:QuantitySource.WeightKg");
    const adjusted = lineOf(grid, "r-adj")!;
    expect(adjusted.querySelector("[data-slot=to-be-priced]")).toBeNull();
    expect(adjusted).toHaveTextContent("Financial:RateLineKind.PriceList");
    expect(adjusted).toHaveTextContent("Financial:QuantitySource.DistanceKm");
    const listLine = lineOf(grid, "r1")!;
    expect(listLine).toHaveTextContent("PL-NL");

    expect(within(open).getByRole("button", { name: "Remove" })).toBeInTheDocument();
    expect(within(adjusted).getByRole("button", { name: "Remove" })).toBeInTheDocument();
    expect(within(listLine).queryByRole("button", { name: "Remove" })).toBeNull();
    expect(within(open).queryByLabelText("Financial:Quantity"), "the quantity of an open line follows the item").toBeNull();
  });

  it("Adjust unit price prices an open line (PUT with its stamp); Remove deletes it", async () => {
    routes.PUT["/api/financial/rate-line/{id}"] = () => ({ ...OPEN, unitPrice: 0.1, amount: 50, isAdjusted: true });
    routes.DELETE["/api/financial/rate-line/{id}"] = () => undefined;
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(lineOf(sales(pricing), "r-open")).not.toBeNull());

    fireEvent.change(within(lineOf(sales(pricing), "r-open")!).getByLabelText("Financial:UnitPrice"), { target: { value: "0.1" } });
    await waitFor(() => expect(calls(PUT, "/api/financial/rate-line/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/financial/rate-line/{id}")).toEqual({ params: { path: { id: "r-open" } }, body: expect.objectContaining({ unitPrice: 0.1, quantity: null, concurrencyStamp: "ro" }) });

    await click(within(lineOf(sales(pricing), "r-adj")!).getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(calls(DELETE, "/api/financial/rate-line/{id}")).toHaveLength(1));
    expect(lastCall(DELETE, "/api/financial/rate-line/{id}")).toEqual({ params: { path: { id: "r-adj" } } });
  });

  it("the preview names an open line's basis", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => ({
      currencyCode: "EUR",
      lines: [{ orderLineId: "l1", kind: 1, descriptionKey: "Financial:RateLine.OpenBasis", quantity: 600, quantitySource: 4, unitPrice: 0, amount: 0, priceListId: null }],
      totalAmount: 0,
    });
    renderPage();
    await loaded();
    openItem(0);
    fireEvent.change(within(itemTab("Orders:FreightDetails")).getByLabelText(/^Orders:WeightKg/), { target: { value: "600" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(pricing.querySelector("[data-slot=item-preview-lines]")).toHaveTextContent("Financial:RateLine.OpenBasis · Financial:QuantitySource.WeightKg"));
    vi.useRealTimers();
  });
});

/* ── 4: purchase prices per freight item ──────────────────────────────────────────────────────────────────── */

describe("Order page review 4 — purchase prices in Edit freight item → Pricing", () => {
  it("before the item is on a trip: the note, no table, no Add and no cost read", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-1"), leg("leg-2", { orderLineId: "l2", tripId: "trip-8" })]);
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(pricing.querySelector("[data-slot=purchase-prices-after-trip]")).toHaveTextContent("Financial:PurchasePricesAfterTrip"));
    expect(purchase()).toBeNull();
    expect(within(pricing).queryByText("Financial:AddPurchaseLine")).toBeNull();
    expect(GET.mock.calls.some(([path, init]) => path === "/api/financial/rate-line" && init?.params?.query?.Side === 2)).toBe(false);
  });

  it("on a trip: only the item's cost lines with the trip as a link, the basis and the total", async () => {
    renderPage();
    await loaded();
    openItem(0);
    itemTab("Orders:Pricing");
    await waitFor(() => expect(purchase()).not.toBeNull());
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/rate-line", { params: { query: { OrderId: "o1", Side: 2 } } }));
    const grid = purchase()!;
    await waitFor(() => expect(lineOf(grid, "c1")).not.toBeNull());
    expect(lineOf(grid, "c2")).not.toBeNull();
    expect(lineOf(grid, "c3"), "another freight item's line").toBeNull();
    expect(Array.from(grid.querySelectorAll("[data-slot=mock-lines-grid-columns] > span")).map((s) => s.getAttribute("data-column"))).toEqual(["kind", "tripNumber", "description", "basis", "quantity", "unitPrice", "amount"]);
    expect(lineOf(grid, "c1")).toHaveTextContent("Financial:QuantitySource.LineQuantity");
    expect(within(grid.closest<HTMLElement>("[data-slot=item-purchase-prices]")!.querySelector<HTMLElement>("[data-slot=totals-block]")!).getByText("110.00")).toBeInTheDocument();

    await click(within(lineOf(grid, "c1")!).getByRole("button", { name: "TR-7" }));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: expect.stringContaining("/transport/trips") }));
  });

  it("Adjust unit price on a calculated line; Remove only on the manual line", async () => {
    routes.PUT["/api/financial/rate-line/{id}"] = () => ({ ...COST.lines[0], unitPrice: 35 });
    routes.DELETE["/api/financial/rate-line/{id}"] = () => undefined;
    renderPage();
    await loaded();
    openItem(0);
    itemTab("Orders:Pricing");
    await waitFor(() => expect(lineOf(purchase()!, "c1")).not.toBeNull());
    const grid = purchase()!;
    expect(within(lineOf(grid, "c1")!).queryByRole("button", { name: "Remove" })).toBeNull();
    expect(within(lineOf(grid, "c2")!).getByRole("button", { name: "Remove" })).toBeInTheDocument();

    fireEvent.change(within(lineOf(grid, "c1")!).getByLabelText("Financial:UnitPrice"), { target: { value: "35" } });
    await waitFor(() => expect(calls(PUT, "/api/financial/rate-line/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/financial/rate-line/{id}")).toEqual({ params: { path: { id: "c1" } }, body: expect.objectContaining({ unitPrice: 35, quantity: null, concurrencyStamp: "c1s" }) });
    await click(within(lineOf(purchase()!, "c2")!).getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(lastCall(DELETE, "/api/financial/rate-line/{id}")).toEqual({ params: { path: { id: "c2" } } }));
  });

  it("Add purchase line: the quantity follows the item's value for the basis, the only trip is preselected, and the body names trip, item and the item's first leg", async () => {
    routes.GET["/api/planning/leg"] = () =>
      legsPage([leg("leg-0", { position: 2, tripId: "trip-7", tripNumber: "TR-7" }), leg("leg-1", { position: 1, tripId: "trip-7", tripNumber: "TR-7" }), leg("leg-2", { orderLineId: "l2", tripId: "trip-8", tripNumber: "TR-8" })]);
    routes.POST["/api/financial/rate-line/manual"] = () => ({ id: "c9" });
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("Financial:AddPurchaseLine")).toBeInTheDocument());
    fireEvent.click(within(pricing).getByText("Financial:AddPurchaseLine"));
    await waitFor(() => expect(actionDialog()).not.toBeNull());
    const dialog = actionDialog()!;
    expect(dialog.querySelector<HTMLInputElement>("#purchase-line-quantity")).toHaveValue("2");
    expect(dialog.querySelector<HTMLSelectElement>("#purchase-line-trip")).toHaveValue("trip-7");
    expect(within(dialog.querySelector<HTMLSelectElement>("#purchase-line-trip")!).queryByRole("option", { name: "TR-8" }), "only the trips of this item").toBeNull();

    fireEvent.change(dialog.querySelector("#purchase-line-basis")!, { target: { value: "4" } });
    expect(dialog.querySelector<HTMLInputElement>("#purchase-line-quantity")).toHaveValue("500");
    fireEvent.change(dialog.querySelector("#purchase-line-description")!, { target: { value: "Extra weight" } });

    await submitAction();
    expect(within(dialog).getByText("MasterData:Required"), "the unit price is required").toBeInTheDocument();
    expect(calls(POST, "/api/financial/rate-line/manual")).toHaveLength(0);

    fireEvent.change(dialog.querySelector("#purchase-line-price")!, { target: { value: "0,05" } });
    await submitAction();
    await waitFor(() => expect(calls(POST, "/api/financial/rate-line/manual")).toHaveLength(1));
    expect(lastCall(POST, "/api/financial/rate-line/manual")!.body).toEqual({
      tripId: "trip-7", orderLineId: "l1", legId: "leg-1", side: 2, kind: 2, description: "Extra weight", quantitySource: 4, quantity: 500, unitPrice: 0.05, currencyCode: "EUR",
    });
  });

  it("Add purchase line with two trips: none preselected, the trip is required", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-1", { tripId: "trip-7", tripNumber: "TR-7" }), leg("leg-3", { position: 2, tripId: "trip-9", tripNumber: "TR-9" })]);
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("Financial:AddPurchaseLine")).toBeInTheDocument());
    fireEvent.click(within(pricing).getByText("Financial:AddPurchaseLine"));
    await waitFor(() => expect(actionDialog()).not.toBeNull());
    const dialog = actionDialog()!;
    expect(dialog.querySelector<HTMLSelectElement>("#purchase-line-trip")).toHaveValue("");
    fireEvent.change(dialog.querySelector("#purchase-line-price")!, { target: { value: "10" } });
    await submitAction();
    expect(calls(POST, "/api/financial/rate-line/manual")).toHaveLength(0);
    expect(within(dialog).getByText("MasterData:Required")).toBeInTheDocument();
  });

  it("read-only without Financial.RateLines.Update and hidden without Financial.RateLines", async () => {
    grants = { ...REVIEW, "Financial.RateLines.Update": false };
    const first = renderPage();
    await loaded();
    openItem(0);
    itemTab("Orders:Pricing");
    await waitFor(() => expect(lineOf(purchase()!, "c1")).not.toBeNull());
    expect(purchase()!.querySelector("[data-slot=mock-lines-grid]")).toHaveAttribute("data-readonly", "true");
    expect(screen.queryByText("Financial:AddPurchaseLine")).toBeNull();
    first.unmount();
    GET.mockClear();

    grants = { ...REVIEW, "Financial.RateLines": false };
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    expect(within(pricing).queryByText("Financial:PurchasePrices")).toBeNull();
    expect(purchase()).toBeNull();
    expect(GET.mock.calls.some(([path, init]) => path === "/api/financial/rate-line" && init?.params?.query?.Side === 2)).toBe(false);
  });
});

/* ── 5: split a leg at an address ─────────────────────────────────────────────────────────────────────────── */

describe("Order page review 5 — Split leg at … and Remove split in Stops and legs", () => {
  const legsGrid = () => itemTab("Orders:StopsAndLegs").querySelector<HTMLElement>("[data-slot=item-legs]")!;
  const gridRow = (id: string) => legsGrid().querySelector<HTMLElement>(`[data-slot=mock-grid-row][data-id="${id}"]`)!;
  const action = (id: string, actionId: string) => gridRow(id)?.querySelector<HTMLButtonElement>(`[data-row-action="${actionId}"]`) ?? null;

  it("offers Split on every leg (disabled with its reason on a trip) and Remove split only on a split-at-address half, disabled when either half is on a trip", async () => {
    routes.GET["/api/planning/leg"] = () =>
      legsPage([
        leg("leg-a", { source: 6, splitFromLegId: "leg-x", position: 1 }),
        leg("leg-b", { source: 6, splitFromLegId: "leg-x", position: 2, tripId: "trip-7", tripNumber: "TR-7" }),
        leg("leg-c", { position: 3, tripId: "trip-8" }),
        leg("leg-d", { position: 4 }),
      ]);
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(gridRow("leg-d")).not.toBeNull());

    expect(action("leg-a", "split")).not.toBeDisabled();
    expect(action("leg-a", "removeSplit")).toBeDisabled();
    expect(action("leg-a", "removeSplit")).toHaveAttribute("title", "PlanningExecution:LegIsOnTrip");
    expect(action("leg-b", "split")).toBeDisabled();
    expect(action("leg-b", "split")).toHaveAttribute("title", "PlanningExecution:LegIsOnTrip");
    expect(action("leg-b", "removeSplit")).toBeDisabled();
    expect(action("leg-c", "split")).toBeDisabled();
    expect(action("leg-c", "removeSplit")).toBeNull();
    expect(action("leg-d", "split")).not.toBeDisabled();
    expect(action("leg-d", "removeSplit")).toBeNull();
  });

  it("offers neither without PlanningExecution.Legs.Update", async () => {
    grants = { ...REVIEW, "PlanningExecution.Legs.Update": false };
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-a", { source: 6, splitFromLegId: "leg-x" })]);
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(gridRow("leg-a")).not.toBeNull());
    expect(action("leg-a", "split")).toBeNull();
    expect(action("leg-a", "removeSplit")).toBeNull();
    expect(action("leg-a", "edit")).not.toBeNull();
  });

  it("the split dialog defaults the departure to the arrival's successive activity and the date to the origin date, and posts the body", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-d")]);
    routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-in", name: "Depot in" }, { id: "wp-out", name: "Depot out" }, { id: "wp-unload", name: "Unload" }];
    routes.GET["/api/orders/waypoint-type/{id}"] = (init) => (init?.params?.path?.id === "wp-in" ? { id: "wp-in", name: "Depot in", successiveWaypointTypeId: "wp-out" } : { id: "wp-unload", name: "Unload", successiveWaypointTypeId: null });
    routes.GET["/api/master-data/relation/{id}/addresses"] = () => [{ id: "adr-x", street: "Hubweg", houseNumber: "1", postalCode: "5000 AA", city: "Tilburg" }];
    routes.POST["/api/planning/leg/{id}/split-at-address"] = () => [leg("leg-d1", { source: 6 }), leg("leg-d2", { source: 6 })];
    lookupItems.current = { "split-leg-relation": [{ id: "rel-hub", name: "Hub Tilburg" }] };
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(action("leg-d", "split")).not.toBeNull());
    await click(action("leg-d", "split")!);
    await waitFor(() => expect(actionDialog()).not.toBeNull());
    const dialog = actionDialog()!;
    expect(dialog).toHaveTextContent("PlanningExecution:SplitLegAtAddress");
    expect(dialog.querySelector("[data-slot=split-leg-hint]")).toHaveTextContent("Acme, Utrecht");
    expect(dialog.querySelector("[data-slot=split-leg-hint]")).toHaveTextContent("Beta, Gouda");
    expect(dialog.querySelector<HTMLInputElement>("#split-leg-date")).toHaveValue("2026-10-25");

    await submitAction();
    expect(within(dialog).getAllByText("MasterData:Required").length).toBeGreaterThanOrEqual(2);
    expect(calls(POST, "/api/planning/leg/{id}/split-at-address")).toHaveLength(0);

    fireEvent.change(dialog.querySelector("#split-leg-relation")!, { target: { value: "rel-hub" } });
    await waitFor(() => expect(dialog.querySelector<HTMLSelectElement>("#split-leg-address")).toHaveValue("adr-x"));
    fireEvent.change(dialog.querySelector("#split-leg-arrival")!, { target: { value: "wp-in" } });
    await waitFor(() => expect(dialog.querySelector<HTMLSelectElement>("#split-leg-departure")).toHaveValue("wp-out"));

    await submitAction();
    await waitFor(() => expect(calls(POST, "/api/planning/leg/{id}/split-at-address")).toHaveLength(1));
    expect(lastCall(POST, "/api/planning/leg/{id}/split-at-address")).toEqual({
      params: { path: { id: "leg-d" } },
      body: { relationId: "rel-hub", relationAddressId: "adr-x", arrivalWaypointTypeId: "wp-in", departureWaypointTypeId: "wp-out", date: "2026-10-25" },
    });
  });

  it("without a successive activity the departure is the arrival activity; a departure the user picked stays", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-d")]);
    routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-in", name: "Depot in" }, { id: "wp-out", name: "Depot out" }, { id: "wp-unload", name: "Unload" }];
    routes.GET["/api/orders/waypoint-type/{id}"] = (init) => (init?.params?.path?.id === "wp-in" ? { id: "wp-in", successiveWaypointTypeId: "wp-out" } : { id: init?.params?.path?.id, successiveWaypointTypeId: null });
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(action("leg-d", "split")).not.toBeNull());
    await click(action("leg-d", "split")!);
    await waitFor(() => expect(actionDialog()).not.toBeNull());
    const dialog = actionDialog()!;
    fireEvent.change(dialog.querySelector("#split-leg-arrival")!, { target: { value: "wp-unload" } });
    await waitFor(() => expect(dialog.querySelector<HTMLSelectElement>("#split-leg-departure")).toHaveValue("wp-unload"));
    fireEvent.change(dialog.querySelector("#split-leg-departure")!, { target: { value: "wp-out" } });
    fireEvent.change(dialog.querySelector("#split-leg-arrival")!, { target: { value: "wp-in" } });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });
    expect(dialog.querySelector<HTMLSelectElement>("#split-leg-departure")).toHaveValue("wp-out");
  });

  it("the server's refusal (LegOnTrip) shows in the dialog", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-d")]);
    routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-unload", name: "Unload" }];
    routes.GET["/api/orders/waypoint-type/{id}"] = () => ({ id: "wp-unload", successiveWaypointTypeId: null });
    routes.GET["/api/master-data/relation/{id}/addresses"] = () => [{ id: "adr-x", street: "Hubweg", city: "Tilburg" }];
    routes.POST["/api/planning/leg/{id}/split-at-address"] = () => fail("The leg is on a trip; unassign it first", 409);
    lookupItems.current = { "split-leg-relation": [{ id: "rel-hub", name: "Hub Tilburg" }] };
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(action("leg-d", "split")).not.toBeNull());
    await click(action("leg-d", "split")!);
    await waitFor(() => expect(actionDialog()).not.toBeNull());
    const dialog = actionDialog()!;
    fireEvent.change(dialog.querySelector("#split-leg-relation")!, { target: { value: "rel-hub" } });
    await waitFor(() => expect(dialog.querySelector<HTMLSelectElement>("#split-leg-address")).toHaveValue("adr-x"));
    fireEvent.change(dialog.querySelector("#split-leg-arrival")!, { target: { value: "wp-unload" } });
    await submitAction();
    expect(await within(dialog).findByText("The leg is on a trip; unassign it first")).toBeInTheDocument();
  });

  it("Remove split asks, naming the leg, and posts remove-split for the half", async () => {
    routes.GET["/api/planning/leg"] = () => legsPage([leg("leg-a", { source: 6, splitFromLegId: "leg-x" }), leg("leg-b", { source: 6, splitFromLegId: "leg-x", position: 2 })]);
    routes.POST["/api/planning/leg/{id}/remove-split"] = () => leg("leg-x");
    renderPage();
    await loaded();
    openItem(0);
    await waitFor(() => expect(action("leg-b", "removeSplit")).not.toBeNull());
    expect(action("leg-b", "removeSplit")).not.toBeDisabled();
    await click(action("leg-b", "removeSplit")!);
    const confirm = await screen.findByRole("alertdialog");
    expect(confirm).toHaveTextContent("Remove the split of Acme, Utrecht → Beta, Gouda?");
    expect(calls(POST, "/api/planning/leg/{id}/remove-split")).toHaveLength(0);
    await click(within(confirm).getByRole("button", { name: "PlanningExecution:RemoveSplit" }));
    await waitFor(() => expect(calls(POST, "/api/planning/leg/{id}/remove-split")).toHaveLength(1));
    expect(lastCall(POST, "/api/planning/leg/{id}/remove-split")).toEqual({ params: { path: { id: "leg-b" } } });
  });
});
