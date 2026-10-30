import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The new order (design 61 "Order page, legacy tab structure" 9: New opens the page directly, no New dialog): the default type
 * (or the one New per type names), today, the cursor in Customer, the customer's financial defaults copied when it
 * is chosen; Save creates the order in one call with its freight items and opens its page; Confirm (Ctrl/⌘+Enter)
 * creates and confirms; a Tevens trip for some of the new items names them by the client ids posted on the lines.
 * And Paste from Excel (3): the rows become items after a preview that marks the unmatched cells; a row without a
 * date takes the order date. Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
/* The save report's keys carry their {0}, so a test sees which section names were filled in. */
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => (k.startsWith("Orders:Sections") ? `${k} {0}` : k), culture: "en", isLoading: false }) }));

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
import { toast } from "sonner";
import { fail, itemRows, itemTab, lookupItems, openItem, openNested, openTab, ribbonButton, ribbonLabels, section, stubGlobals, tabLabels } from "./transport-test-mocks";
import { GET, POST, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const ALL = { "Orders.TransportOrders": true, "Orders.TransportOrders.Create": true, "Orders.TransportOrders.Update": true, "Orders.TransportOrders.Confirm": true, "Financial.RateLines.Update": true };
const LOAD_UNLOAD = [{ waypointTypeId: "wp-load", name: "Load", position: 1 }, { waypointTypeId: "wp-unload", name: "Unload", position: 2 }];
const today = (() => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
})();

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  vi.mocked(toast.success).mockClear();
  grants = { ...ALL };
  lookupItems.current = {
    "*-customer": [{ id: "rel-1", name: "Acme" }],
    "*-entry-strip-stop-0-relation": [{ id: "rel-1", name: "Acme" }],
    "*-entry-strip-stop-1-relation": [{ id: "rel-2", name: "Beta" }],
  };
  resetClient();
  routes.GET["/api/orders/transport-order-type"] = () => ({ rows: [{ id: "type-1", name: "Standard", isDefault: true }, { id: "type-2", name: "Express" }], totalCount: 2, filteredCount: 2 });
  routes.GET["/api/orders/transport-order-type/{id}"] = (init) => ({ id: String(init?.params?.path?.id), name: "Standard", requiredFields: 0, defaultWaypoints: LOAD_UNLOAD });
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }, { id: "type-2", name: "Express" }];
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", code: "PL", name: "Pallet" }];
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-load", name: "Load" }, { id: "wp-unload", name: "Unload" }];
  routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => [{ id: `${String(init?.params?.path?.id)}-main`, addressTypeId: null, city: "Town" }];
  routes.GET["/api/financial/relation-financial-settings/by-relation/{relationId}"] = () => ({ currencyCode: "EUR", paymentTermDays: 14, revenuePriceLists: [{ priceListId: "pl-1", position: 1 }], discountsOrSurcharges: [] });
  routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }];
});

function renderNew(typeId?: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <OrderPage id="new" typeId={typeId} />
    </QueryClientProvider>,
  );
}
const click = async (element: Element) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const key = async (element: Element, init: Record<string, unknown>) =>
  act(async () => {
    fireEvent.keyDown(element, init);
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
const header = () => section("Orders:OrderDetails");
const field = (scope: HTMLElement, suffix: string) => scope.querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-${suffix}']`)!;
const entry = () => document.querySelector<HTMLElement>("[data-slot=freight-item-entry]")!;
const created = (extra: Record<string, unknown> = {}) => ({ id: "o-new", status: 1, number: null, transportOrderTypeId: "type-1", customerRelationId: "rel-1", customerName: "Acme", orderDate: today, concurrencyStamp: "c1", lines: [], financialTerms: { currencyCode: "EUR", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] }, ...extra });

/** One item through the stop strip: Load at Acme, Unload at Beta, a pallet; Enter on the last stop's relation. */
async function addItem() {
  await waitFor(() => expect(field(entry(), "entry-strip-stop-1-activity").value).toBe("wp-unload"));
  fireEvent.change(field(entry(), "entry-strip-stop-0-relation"), { target: { value: "rel-1" } });
  fireEvent.change(field(entry(), "entry-strip-stop-1-relation"), { target: { value: "rel-2" } });
  fireEvent.change(field(entry(), "entry-unit"), { target: { value: "unit-1" } });
  await key(field(entry(), "entry-strip-stop-1-relation"), { key: "Enter" });
}

describe("New order: the page opens directly", () => {
  it("starts with the default type, today and the cursor in Customer; Save, Confirm and Close; no Documents tab and no revenue yet", async () => {
    renderNew();
    expect(await screen.findByText("Orders:NewTransportOrder")).toBeInTheDocument();
    expect(field(header(), "transportOrderTypeId").value).toBe("type-1");
    expect(field(header(), "orderDate").value).toBe(today);
    await waitFor(() => expect(document.activeElement?.id).toMatch(/-customer$/));
    expect(ribbonLabels()).toEqual(["Save", "Close", "Orders:Confirm"]);
    expect(tabLabels()).toEqual(["General", "Orders:Financial", "Orders:TripRequestTab", "Remarks", "RecordIdentification"]);
    expect(section("Orders:RevenueOverview").querySelector("[data-slot=revenue-after-save]")).toHaveTextContent("Orders:RevenueAfterSave");
    /* Additional services are stored rate lines: they come once the order is saved. */
    expect(openNested("Financial:AdditionalServices").querySelector("[data-slot=services-after-save]")).toHaveTextContent("Orders:AdditionalServicesAfterSave");
  });

  it("New per type starts with that type; without the Create permission the page is denied", async () => {
    const { unmount } = renderNew("type-2");
    await screen.findByText("Orders:NewTransportOrder");
    expect(field(header(), "transportOrderTypeId").value).toBe("type-2");
    unmount();
    grants = { ...ALL, "Orders.TransportOrders.Create": false };
    renderNew();
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
  });

  it("choosing the customer copies its financial defaults; Save creates the order with its items in one call and opens its page", async () => {
    routes.POST["/api/orders/transport-order"] = () => created({ lines: [{ id: "l1", position: 1, transportUnitId: "unit-1", quantity: 1, paymentInstruction: 0, stops: [] }] });
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await addItem();
    await waitFor(() => expect(itemRows()).toHaveLength(1));
    await waitFor(() => expect(calls(GET, "/api/financial/relation-financial-settings/by-relation/{relationId}")).toHaveLength(1));
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order")).toHaveLength(1));
    const body = lastCall(POST, "/api/orders/transport-order")!.body as Record<string, unknown> & { lines: { id: string; stops: Record<string, unknown>[] }[] };
    expect(body).toMatchObject({ transportOrderTypeId: "type-1", customerRelationId: "rel-1", orderDate: today, source: 1, confirm: false, financialTerms: expect.objectContaining({ paymentTermDays: 14, priceListIds: ["pl-1"] }) });
    expect(body.defaultStops).toBeUndefined();
    expect(body.lines).toHaveLength(1);
    expect(body.lines[0].stops.map((s) => [s.waypointTypeId, s.relationId, s.relationAddressId])).toEqual([["wp-load", "rel-1", "rel-1-main"], ["wp-unload", "rel-2", "rel-2-main"]]);
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved");
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o-new", replace: true }));
  });

  it("Ctrl/⌘+Enter creates and confirms; a Tevens trip for some new items names them by the posted client ids", async () => {
    routes.POST["/api/orders/transport-order"] = () => created({ status: 2, number: "TO-1" });
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await addItem();
    /* The strip kept its stops: Enter again adds the second item. */
    await key(field(entry(), "entry-strip-stop-1-relation"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    const trip = section("Orders:TripRequest");
    fireEvent.click(within(trip).getByRole("checkbox", { name: "Orders:TripRequestHint" }));
    fireEvent.click(within(trip.querySelector<HTMLElement>("[data-slot=trip-request-items]")!).getAllByRole("checkbox")[0]);
    await key(field(header(), "customerReference"), { key: "Enter", ctrlKey: true });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order")).toHaveLength(1));
    const body = lastCall(POST, "/api/orders/transport-order")!.body as { confirm: boolean; lines: { id: string }[]; tripRequest: { lineIds: string[] } };
    expect(body.confirm).toBe(true);
    expect(body.lines.map((l) => l.id)).toEqual([expect.stringMatching(/^[0-9a-f-]{36}$/), expect.stringMatching(/^[0-9a-f-]{36}$/)]);
    expect(body.tripRequest.lineIds).toEqual([body.lines[1].id]);
    expect(calls(POST, "/api/orders/transport-order/{id}/confirm")).toHaveLength(0);
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderConfirmed");
  });

  it("a refused create shows the error in the page and stays on the new order", async () => {
    routes.POST["/api/orders/transport-order"] = () => ({ data: undefined, error: { error: { message: "The order date is in a closed period." } }, response: new Response(null, { status: 400 }) });
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await click(ribbonButton("Save")!);
    expect(await screen.findByText("The order date is in a closed period.")).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });
});

/* Review prices before the first save (Marinus 2026-10-30): a new order is previewed as it stands once a customer is
 * chosen, through preview-new-order-prices; the grid, the freight items total, the revenue overview and the item's
 * Pricing show the preview, an item named by its position (linePosition). Risk class: Medium (UI) over a High read. */
describe("New order: the price preview before the first save", () => {
  const NEW_PREVIEW = "/api/orders/transport-order/preview-new-order-prices";
  const STORED_PREVIEW = "/api/orders/transport-order/{id}/preview-prices";
  const preview = () => ({
    currencyCode: "EUR",
    linesAmount: 250,
    discountsAndSurchargesAmount: 5,
    totalAmount: 255,
    lines: [
      { orderLineId: null, linePosition: 1, kind: 1, quantity: 1, unitPrice: 120, amount: 120, priceListId: "pl-1" },
      { orderLineId: null, linePosition: 2, kind: 1, quantity: 1, unitPrice: 130, amount: 130, priceListId: "pl-1" },
      { orderLineId: null, linePosition: null, kind: 4, quantity: 1, unitPrice: 5, amount: 5, discountOrSurchargeId: "ds-1" },
    ],
    warnings: [],
  });
  const advance = async () =>
    act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
  const itemsTotal = () => document.querySelector<HTMLElement>("[data-slot=freight-items] > [data-slot=totals-block]")!;
  const priceCell = (index: number) => itemRows()[index].querySelector("[data-field=price]")?.textContent;
  /** Two priceable items through the stop strip (Enter again adds the second with the same stops). */
  async function addTwoItems() {
    await addItem();
    await key(field(entry(), "entry-strip-stop-1-relation"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
  }

  beforeEach(() => {
    vi.mocked(toast.error).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("is not previewed before a customer is chosen; then once, after the debounce, as it stands, never through the stored-order route", async () => {
    routes.POST[NEW_PREVIEW] = preview;
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    await addItem();
    await waitFor(() => expect(itemRows()).toHaveLength(1));
    await advance();
    expect(calls(POST, NEW_PREVIEW)).toHaveLength(0);
    /* Without a customer and a preview the item says it is priced after the save. */
    openItem(0);
    expect(itemTab("Orders:Pricing").querySelector("[data-slot=item-priced-after-save]")).toHaveTextContent("Orders:ItemPricedAfterSave");
    fireEvent.keyDown(document.querySelector("[role=dialog][data-variant=editor]")!, { key: "Escape" });

    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    expect(calls(POST, NEW_PREVIEW)).toHaveLength(0);
    await waitFor(() => expect(calls(GET, "/api/financial/relation-financial-settings/by-relation/{relationId}")).toHaveLength(1));
    await advance();
    await waitFor(() => expect(calls(POST, NEW_PREVIEW).length).toBeGreaterThan(0));
    const body = lastCall(POST, NEW_PREVIEW)!.body as { customerRelationId: string; orderDate: string; financialTerms: { priceListIds: string[] }; lines: { id?: string | null; transportUnitId: string; stops: { relationId: string; relationAddressId: string }[] }[] };
    expect(body.customerRelationId).toBe("rel-1");
    expect(body.orderDate).toBe(today);
    expect(body.financialTerms.priceListIds).toEqual(["pl-1"]);
    expect(body.lines).toHaveLength(1);
    expect(body.lines[0].id ?? null).toBeNull();
    expect(body.lines[0].transportUnitId).toBe("unit-1");
    expect(body.lines[0].stops.map((s) => [s.relationId, s.relationAddressId])).toEqual([["rel-1", "rel-1-main"], ["rel-2", "rel-2-main"]]);
    expect(calls(POST, STORED_PREVIEW)).toHaveLength(0);
    expect(calls(POST, "/api/orders/transport-order")).toHaveLength(0);
  });

  it("a customer without priceable items is not previewed", async () => {
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await waitFor(() => expect(calls(GET, "/api/financial/relation-financial-settings/by-relation/{relationId}")).toHaveLength(1));
    await advance();
    expect(calls(POST, NEW_PREVIEW)).toHaveLength(0);
    expect(section("Orders:RevenueOverview").querySelector("[data-slot=revenue-after-save]")).toHaveTextContent("Orders:RevenueAfterSave");
  });

  it("the preview fills the grid's amounts by position, the freight items total, the revenue overview and the item's Pricing", async () => {
    routes.POST[NEW_PREVIEW] = preview;
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await addTwoItems();
    await advance();
    await waitFor(() => expect(priceCell(0)).toBe("120.00"));
    expect(priceCell(1)).toBe("130.00");
    /* The freight items total sums the items' preview lines (not the order-level line) and says it is a preview. */
    expect(within(itemsTotal()).getByText("Orders:FreightItemsTotal").nextElementSibling).toHaveTextContent("250.00");
    expect(within(itemsTotal()).getByText("Orders:PreviewPrices")).toBeInTheDocument();

    const revenue = section("Orders:RevenueOverview");
    expect(revenue.querySelector("[data-slot=revenue-after-save]")).toBeNull();
    const totals = revenue.querySelector<HTMLElement>("[data-slot=order-totals]")!;
    expect(totals).not.toBeNull();
    expect(within(totals).getByText("255.00")).toBeInTheDocument();
    expect(within(totals).getByText("Orders:PreviewPrices")).toBeInTheDocument();

    openTab("General");
    openItem(1);
    const pricing = itemTab("Orders:Pricing");
    expect(pricing.querySelector("[data-slot=item-priced-after-save]")).toBeNull();
    expect(pricing.querySelector("[data-slot=price-preview]")).not.toBeNull();
    const lines = pricing.querySelector<HTMLElement>("[data-slot=item-preview-lines]")!;
    expect(lines.children).toHaveLength(1);
    expect(lines).toHaveTextContent("130.00");
    expect(within(pricing.querySelector<HTMLElement>("[data-slot=totals-block]")!).getByText("130.00")).toBeInTheDocument();
    /* An unsaved item has no stored lines to edit and no purchase prices. */
    expect(within(pricing).queryByText("Financial:AddManualLine")).toBeNull();
    expect(calls(POST, STORED_PREVIEW)).toHaveLength(0);
  });

  it("an item without preview lines of its own shows 0.00 in the grid while the preview stands", async () => {
    routes.POST[NEW_PREVIEW] = () => ({ ...preview(), linesAmount: 120, totalAmount: 125, lines: preview().lines.filter((l) => l.linePosition !== 2) });
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await addTwoItems();
    await advance();
    await waitFor(() => expect(priceCell(0)).toBe("120.00"));
    expect(priceCell(1)).toBe("0.00");
    expect(within(itemsTotal()).getByText("Orders:FreightItemsTotal").nextElementSibling).toHaveTextContent("120.00");
  });

  it("a refused preview says the price is unavailable in the revenue overview and the item's Pricing, never a toast", async () => {
    routes.POST[NEW_PREVIEW] = () => fail("Your request is not valid!");
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.change(field(header(), "customer"), { target: { value: "rel-1" } });
    await addItem();
    await advance();
    await waitFor(() => expect(calls(POST, NEW_PREVIEW).length).toBeGreaterThan(0));
    await waitFor(() => expect(section("Orders:RevenueOverview").querySelector("[data-slot=revenue-after-save]")).toHaveTextContent("Orders:PricePreviewUnavailable"));
    openTab("General");
    openItem(0);
    expect(itemTab("Orders:Pricing").querySelector("[data-slot=item-priced-after-save]")).toHaveTextContent("Orders:PricePreviewUnavailable");
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe("Paste from Excel", () => {
  beforeEach(() => {
    routes.GET["/api/master-data/relation/lookup-page"] = (init) => {
      const term = String(init?.params?.query?.term ?? "").toLowerCase();
      const all = [{ id: "rel-1", code: "ACME", name: "Acme" }, { id: "rel-2", code: "BETA", name: "Beta" }];
      const items = all.filter((r) => r.code.toLowerCase().includes(term) || r.name.toLowerCase().includes(term));
      return { items, totalCount: items.length };
    };
  });

  it("rows pasted into the entry open the preview; unmatched cells are marked; Add makes them items with the matched values", async () => {
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    await waitFor(() => expect(calls(GET, "/api/orders/transport-order-type/{id}").length).toBeGreaterThan(0));
    const text = ["ACME\tBeta\t26-10-2026\t08:00\tPL\t2\t500\tR1", "acme\tNobody\t27/10/2026\t\tCrate\t1\t\tR2"].join("\n");
    await act(async () => {
      fireEvent.paste(field(entry(), "entry-strip-stop-0-reference"), { clipboardData: { getData: () => text } });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    const preview = await screen.findByRole("dialog");
    await waitFor(() => expect(within(preview).getAllByRole("row").filter((r) => r.getAttribute("data-slot") === "paste-row")).toHaveLength(2));
    const rows = preview.querySelectorAll("[data-slot=paste-row]");
    expect(rows[0].querySelector("[data-problem]")).toBeNull();
    expect(Array.from(rows[1].querySelectorAll("[data-problem]")).map((c) => c.getAttribute("data-column"))).toEqual(["to", "unit"]);
    expect(within(preview).getByText("Orders:PasteMarkedHint")).toBeInTheDocument();
    await click(within(preview).getByRole("button", { name: "Orders:AddFreightItems" }));
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    expect(itemRows()[0].querySelector("[data-field=route]")?.textContent).toBe("Acme, Town → Beta, Town");
    expect(itemRows()[0].querySelector("[data-field=when]")?.textContent).toBe("26/10/2026 08:00");
    expect((within(itemRows()[0]).getByLabelText("Orders:Quantity") as HTMLInputElement).value).toBe("2");
    expect((within(itemRows()[0]).getByLabelText("Orders:WeightKg") as HTMLInputElement).value).toBe("500");
    /* The unmatched To and unit stay empty: the item is marked until it is completed. */
    expect(itemRows()[1].querySelector("[data-field=route]")?.textContent).toBe("Acme, Town");
    expect(itemRows()[1].querySelector("[data-slot=item-problem]")).not.toBeNull();
  });

  it("the Paste command opens the preview with a text field; a row can be dropped before adding", async () => {
    renderNew();
    await screen.findByText("Orders:NewTransportOrder");
    fireEvent.click(within(entry()).getByText("Orders:PasteFromExcel"));
    const dialog = await screen.findByRole("dialog");
    const area = within(dialog).getByLabelText(/^Orders:PasteRows/);
    await act(async () => {
      fireEvent.paste(area, { clipboardData: { getData: () => "From\tTo\nACME\tBETA\t\t\tPL" } });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    await waitFor(() => expect(dialog.querySelectorAll("[data-slot=paste-row]")).toHaveLength(2));
    /* The header row matches nothing: drop it. */
    fireEvent.click(within(dialog.querySelectorAll<HTMLElement>("[data-slot=paste-row]")[0]).getByRole("button", { name: "Remove" }));
    expect(dialog.querySelectorAll("[data-slot=paste-row]")).toHaveLength(1);
    await click(within(dialog).getByRole("button", { name: "Orders:AddFreightItems" }));
    await waitFor(() => expect(itemRows()).toHaveLength(1));
    /* No date in the row: the order date (the API needs a date per stop). */
    expect(itemRows()[0].querySelector("[data-field=when]")?.textContent).toBe(today.split("-").reverse().join("/"));
    expect(itemRows()[0].querySelector("[data-slot=item-problem]")).toBeNull();
  });
});
