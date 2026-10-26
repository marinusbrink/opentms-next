import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The order page (design 61 "Order page, revised", PO 2026-10-26): one page without content tabs: the header block,
 * the freight items (list, fast entry row, the selected item's detail with its own tabs), the totals and the
 * collapsible order-level sections. Covered: the page states and the layout; the ribbon per status and permission;
 * the section saves with the stamp, the save that stops part-way and the concurrency conflict (unchanged rules); the
 * entry row's keyboard flow (Enter adds and keeps From, date and unit; the default address and the type's default
 * waypoints); the item detail tabs and Esc; the item's rate lines, the order-level lines and the totals with the
 * preview; the Tevens trip with the items on it (lineIds); the legs per item; read-only after confirm; the shortcuts;
 * template mode. New orders and paste are in order-page-new.test.tsx. Risk class: Medium. */

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
import { TemplatePage } from "@/apps/transport/pages/TemplatePage";
import { toast } from "sonner";
import { fail, gridRows, itemList, itemRows, itemTab, lookupItems, openItem, orderBody, ribbonButton, ribbonLabels, section, stubGlobals } from "./transport-test-mocks";
import { GET, POST, PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

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

const header = () => document.querySelector("[data-slot=record-page-header]") as HTMLElement;
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
const typeIn = (scope: HTMLElement, label: RegExp | string, value: string) => fireEvent.change(within(scope).getByLabelText(label), { target: { value } });
const referenceField = () => within(section("Orders:OrderDetails")).getByLabelText(/^Orders:CustomerReference/) as HTMLInputElement;
const quantityCell = (index = 0) => within(itemRows()[index]).getByLabelText("Orders:Quantity") as HTMLInputElement;
const closedSection = (caption: string) => document.querySelector<HTMLElement>(`[data-slot=form-section][aria-label='${caption}']`)!;
const loaded = () => screen.findByText("Orders:TransportOrder TO-2026-00001");

describe("OrderPage – states and layout", () => {
  it("shows PermissionDenied without the view permission and the skeleton, then one page without content tabs", async () => {
    grants = {};
    const { unmount } = renderPage();
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();

    grants = { ...ALL };
    renderPage();
    expect(document.querySelector("[data-slot=record-page-skeleton]")).not.toBeNull();
    await loaded();
    expect(within(header()).getByText("Orders:Status.Open")).toBeInTheDocument();
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    const captions = Array.from(orderBody().querySelectorAll(":scope > [data-slot=form-section]")).map((s) => s.getAttribute("aria-label"));
    expect(captions).toEqual(["Orders:OrderDetails", "Orders:Lines", "Orders:Totals", "Orders:FinancialTerms", "Orders:TripRequest", "Orders:ReferencesAndAttributes", "Orders:Documents", "RecordIdentification"]);
    /* The order-level sections start closed; there is no stop pattern on the order any more. */
    for (const caption of ["Orders:FinancialTerms", "Orders:TripRequest", "Orders:ReferencesAndAttributes", "Orders:Documents", "RecordIdentification"]) expect(closedSection(caption)).toHaveAttribute("data-state", "closed");
    expect(screen.queryByText("Orders:DefaultStops")).toBeNull();
  });

  it("shows RecordNotFound for a missing order", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => fail("not found", 404);
    renderPage();
    expect(await screen.findByText("RecordNotFound")).toBeInTheDocument();
  });

  it("is read-only without Update: no Save, no entry row, controls disabled, state commands still there", async () => {
    grants = { ...ALL, "Orders.TransportOrders.Update": false };
    renderPage();
    await loaded();
    expect(ribbonButton("Save")).toBeNull();
    expect(referenceField()).toBeDisabled();
    expect(document.querySelector("[data-slot=freight-item-entry]")).toBeNull();
    expect(itemList()).toHaveAttribute("data-readonly", "true");
    expect(ribbonButton("Orders:Approve")).not.toBeNull();
  });

  it("is read-only once ready for invoicing (after confirm and approve): no entry row, no editing", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(3);
    renderPage();
    await loaded();
    expect(document.querySelector("[data-slot=freight-item-entry]")).toBeNull();
    expect(itemList()).toHaveAttribute("data-readonly", "true");
    expect(referenceField()).toBeDisabled();
  });

  it("the item list shows position, from → to, date/time, unit, quantity, weight, reference, price and trip", async () => {
    routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", orderLineId: "l1", tripId: "trip-9", tripNumber: "TR-9", position: 1 }], totalCount: 1, filteredCount: 1 });
    renderPage();
    await loaded();
    await waitFor(() => expect(within(itemRows()[0]).getByText("200.00")).toBeInTheDocument());
    const row = itemRows()[0];
    const cell = (field: string) => row.querySelector(`[data-field=${field}]`)?.textContent;
    expect(cell("position")).toBe("1");
    expect(cell("route")).toBe("Acme, Utrecht → Beta, Gouda");
    expect(cell("when")).toBe("25/10/2026 08:00");
    expect((within(row).getByLabelText("Orders:Reference") as HTMLInputElement).value).toBe("LOAD-1");
    expect((within(row).getByLabelText("Orders:WeightKg") as HTMLInputElement).value).toBe("500");
    await click(await within(row).findByRole("button", { name: "TR-9" }));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/trips/trip-9" }));
  });
});

describe("OrderPage – ribbon per status (UI-ACT-002, UI-PERM-003)", () => {
  it.each([
    [1, ["Save", "Close", "Orders:Confirm", "Orders:Cancel", "Orders:PrintConsignmentNote", "Delete"]],
    [2, ["Save", "Close", "Orders:Approve", "Orders:Cancel", "Orders:PrintConsignmentNote", "Delete"]],
    [3, ["Close", "Orders:Archive", "Orders:Revise", "Orders:PrintConsignmentNote"]],
    [4, ["Close", "Orders:Archive", "Orders:PrintConsignmentNote"]],
    [5, ["Close", "Orders:Revise", "Orders:PrintConsignmentNote"]],
    [6, ["Close", "Orders:PrintConsignmentNote"]],
  ])("status %i shows %j", async (status, expected) => {
    grants = { ...ALL, "Orders.TransportOrderTemplates.Create": false };
    routes.GET["/api/orders/transport-order/{id}"] = () => order(status);
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    await waitFor(() => expect(ribbonLabels()).toEqual(expected));
  });

  it("hides a state command without its permission and Save as template needs the templates Create permission", async () => {
    grants = { ...ALL, "Orders.TransportOrders.Cancel": false, "Orders.TransportOrderTemplates.Create": true };
    renderPage();
    await loaded();
    expect(ribbonButton("Orders:Cancel")).toBeNull();
    expect(ribbonButton("Orders:SaveAsTemplate")).not.toBeNull();
  });
});

describe("OrderPage – saving and the shortcuts", () => {
  it("saves a changed header through the header command with the stamp, and nothing else", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    expect(referenceField().value).toBe("REF-1");
    fireEvent.change(referenceField(), { target: { value: "REF-2" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")).toEqual({
      params: { path: { id: "o1" } },
      body: expect.objectContaining({ concurrencyStamp: "stamp-1", customerReference: "REF-2", customerRelationId: "rel-1", transportOrderTypeId: "type-1" }),
    });
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved");
  });

  it("Ctrl/⌘+S saves from anywhere on the page", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "REF-2" } });
    await key(referenceField(), { key: "s", ctrlKey: true });
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ customerReference: "REF-2" }));
  });

  it("refuses to save without a customer and shows the message under the field", async () => {
    renderPage();
    await loaded();
    fireEvent.change(within(section("Orders:OrderDetails")).getByLabelText(/^Orders:Customer \*/), { target: { value: "" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(section("Orders:OrderDetails")).getByText("MasterData:Required")).toBeInTheDocument();
  });

  it("an item without a transport unit is marked in the list; Save refuses, names it and opens that item", async () => {
    renderPage();
    await loaded();
    fireEvent.change(within(itemRows()[0]).getByLabelText("Orders:TransportUnit *"), { target: { value: "" } });
    expect(itemRows()[0].querySelector("[data-slot=item-problem]")).not.toBeNull();
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(section("Orders:Lines")).getAllByText("Orders:LineNeedsTransportUnit").length).toBeGreaterThan(0);
    expect(document.querySelector("[data-slot=line-detail]")).toHaveAttribute("aria-label", "Orders:Line 1");
  });

  it("Confirm shows the API's refusal in the page (UI-FB-004)", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    routes.POST["/api/orders/transport-order/{id}/confirm"] = () => fail("The order type requires Customer reference.");
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    await click(ribbonButton("Orders:Confirm")!);
    expect(await screen.findByText("The order type requires Customer reference.")).toBeInTheDocument();
    expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/{id}/confirm", { params: { path: { id: "o1" } } });
  });

  it("Ctrl/⌘+Enter confirms a Draft and saves first when the form is dirty (UI-ACT-008)", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(1, { customerReference: "X", concurrencyStamp: "stamp-2" });
    routes.POST["/api/orders/transport-order/{id}/confirm"] = () => order(2, { concurrencyStamp: "stamp-3" });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    fireEvent.change(referenceField(), { target: { value: "X" } });
    await key(referenceField(), { key: "Enter", metaKey: true });
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/{id}/confirm", expect.anything()));
    expect(PUT.mock.invocationCallOrder[0]).toBeLessThan(POST.mock.invocationCallOrder[0]);
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderConfirmed");
  });

  it("Ctrl/⌘+Enter does nothing on an order that is no Draft", async () => {
    renderPage();
    await loaded();
    await key(referenceField(), { key: "Enter", ctrlKey: true });
    expect(calls(POST, "/api/orders/transport-order/{id}/confirm")).toHaveLength(0);
  });
});

describe("OrderPage – the fast entry row (keyboard first)", () => {
  const entry = () => document.querySelector<HTMLElement>("[data-slot=freight-item-entry]")!;
  const entryField = (suffix: string) => entry().querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-entry-${suffix}']`)!;
  beforeEach(() => {
    lookupItems.current = {
      "*-entry-from": [{ id: "rel-1", name: "Acme" }],
      "*-entry-to": [{ id: "rel-2", name: "Beta" }, { id: "rel-3", name: "Gamma" }],
    };
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => {
      const id = String(init?.params?.path?.id);
      return [{ id: `${id}-dock`, addressTypeId: "type-dock", street: "Dock", city: "Elsewhere" }, { id: `${id}-main`, addressTypeId: null, street: "Main", city: `City of ${id}` }];
    };
  });

  it("Enter adds the item with the default addresses and the type's Load/Unload, keeps From, date and unit, and the save sends it", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    await waitFor(() => expect(entryField("date").value).toBe("2026-10-25"));
    fireEvent.change(entryField("from"), { target: { value: "rel-1" } });
    fireEvent.change(entryField("to"), { target: { value: "rel-2" } });
    fireEvent.change(entryField("time"), { target: { value: "0730" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-2" } });
    fireEvent.change(entryField("weightKg"), { target: { value: "120" } });
    fireEvent.change(entryField("reference"), { target: { value: "L-77" } });
    await key(entryField("reference"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    expect(itemRows()[1].querySelector("[data-field=route]")?.textContent).toBe("Acme, City of rel-1 → Beta, City of rel-2");
    /* From, date, time and unit stay; To and the rest start empty; the cursor is back in To. */
    expect(entryField("from").value).toBe("rel-1");
    expect(entryField("to").value).toBe("");
    expect(entryField("date").value).toBe("2026-10-25");
    expect(entryField("unit").value).toBe("unit-2");
    expect(entryField("weightKg").value).toBe("");
    expect(document.activeElement).toBe(entryField("to"));

    /* The next one only needs its To: Enter on it. */
    fireEvent.change(entryField("to"), { target: { value: "rel-3" } });
    await key(entryField("to"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(3));

    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { lines: { id: string | null; transportUnitId: string; quantity: number; weightKg: number | null; stops: Record<string, unknown>[] }[] };
    expect(body.lines).toHaveLength(3);
    expect(body.lines[1]).toMatchObject({ id: null, transportUnitId: "unit-2", quantity: 1, weightKg: 120 });
    expect(body.lines[1].stops).toEqual([
      expect.objectContaining({ waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "rel-1-main", date: "2026-10-25", windowFrom: "07:30:00", reference: "L-77" }),
      expect.objectContaining({ waypointTypeId: "wp-unload", relationId: "rel-2", relationAddressId: "rel-2-main", date: "2026-10-25", reference: null }),
    ]);
    expect(body.lines[2].stops.map((s) => s.relationId)).toEqual(["rel-1", "rel-3"]);
    expect(body.lines[2]).toMatchObject({ transportUnitId: "unit-2", weightKg: null });
  });

  it("Enter without To or a unit adds nothing and names the missing fields; Enter in an open list never adds", async () => {
    renderPage();
    await loaded();
    fireEvent.change(entryField("from"), { target: { value: "rel-1" } });
    await key(entryField("from"), { key: "Enter" });
    expect(itemRows()).toHaveLength(1);
    expect(within(entry()).getAllByText("MasterData:Required")).toHaveLength(2);
    expect(document.activeElement).toBe(entryField("to"));

    fireEvent.change(entryField("to"), { target: { value: "rel-2" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-1" } });
    /* A lookup whose list is open takes Enter itself (it selects). */
    entryField("to").setAttribute("aria-expanded", "true");
    await key(entryField("to"), { key: "Enter" });
    expect(itemRows()).toHaveLength(1);
    entryField("to").removeAttribute("aria-expanded");
    await key(entryField("to"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
  });
});

describe("OrderPage – the item detail (its own tabs, inline under the list)", () => {
  it("a row click shows the item's detail with Freight details, Stops and legs and Pricing; Esc and the X close it", async () => {
    renderPage();
    await loaded();
    expect(document.querySelector("[data-slot=line-detail]")).toBeNull();
    const detail = openItem(0);
    expect(within(detail).getAllByRole("tab").map((t) => t.textContent)).toEqual(["Orders:FreightDetails", "Orders:StopsAndLegs", "Orders:Pricing"]);
    /* Cargo and quantities first, then the collapsible sections. */
    const captions = Array.from(detail.querySelectorAll("[data-slot=freight-details] > [data-slot=form-section]")).map((s) => s.getAttribute("aria-label"));
    expect(captions.slice(0, 4)).toEqual(["Orders:Cargo", "Orders:Container", "Orders:Measurements", "Orders:Texts"]);
    expect((within(detail).getByLabelText(/^Orders:WeightKg/) as HTMLInputElement).value).toBe("500");

    fireEvent.keyDown(within(detail).getByLabelText(/^Orders:WeightKg/), { key: "Escape" });
    expect(document.querySelector("[data-slot=line-detail]")).toBeNull();
    openItem(0);
    fireEvent.click(within(document.querySelector<HTMLElement>("[data-slot=line-detail]")!).getByRole("button", { name: "Close" }));
    expect(document.querySelector("[data-slot=line-detail]")).toBeNull();
  });

  it("an edit in the detail is an edit of the item: saved with the items, and the item stays open", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    const detail = openItem(0);
    typeIn(detail, /^Orders:Positions/, "4");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body).toEqual(expect.objectContaining({ lines: [expect.objectContaining({ id: "l1", positions: 4 })] }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved"));
    expect(document.querySelector("[data-slot=line-detail]")).not.toBeNull();
  });

  it("Stops and legs: the item's stops and, after Confirm, only its own legs; the leg dialog edits a window", async () => {
    const at = (date: [number, number, number], time: string) => {
      const [hh, mm] = time.split(":").map(Number);
      return new Date(date[0], date[1] - 1, date[2], hh, mm).toISOString();
    };
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { lines: twoLines() });
    routes.GET["/api/planning/leg"] = () => ({
      rows: [
        { id: "leg-1", orderLineId: "l1", position: 1, concurrencyStamp: "ls", origin: { name: "Acme", plannedDate: "2026-10-25" }, destination: { name: "Beta", plannedDate: "2026-10-26" } },
        { id: "leg-2", orderLineId: "l2", position: 2, origin: { name: "Acme" }, destination: { name: "Gamma" } },
      ],
      totalCount: 2,
      filteredCount: 2,
    });
    routes.PUT["/api/planning/leg/{id}"] = () => ({ id: "leg-1" });
    renderPage();
    await loaded();
    openItem(0);
    const panel = itemTab("Orders:StopsAndLegs");
    expect(panel.querySelectorAll("[data-slot=stops-editor] [data-slot=mock-line]")).toHaveLength(2);
    await waitFor(() => expect(panel.querySelectorAll("[data-slot=item-legs] [data-slot=mock-grid-row]")).toHaveLength(1));
    expect(panel.querySelector("[data-slot=item-legs] [data-slot=mock-grid-row]")).toHaveAttribute("data-id", "leg-1");
    fireEvent.click(within(panel.querySelector<HTMLElement>("[data-slot=item-legs]")!).getByText("Edit"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(dialog.querySelector("#leg-destination-window-from")!, { target: { value: "7:15" } });
    fireEvent.change(dialog.querySelector("#leg-destination-window-till")!, { target: { value: "11:00" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/planning/leg/{id}", expect.anything()));
    expect((lastCall(PUT, "/api/planning/leg/{id}")!.body as Record<string, unknown>).destinationWindow).toEqual({ from: at([2026, 10, 26], "07:15"), till: at([2026, 10, 26], "11:00") });
  });

  it("before Confirm the legs part says the legs are created on Confirm", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    openItem(0);
    expect(itemTab("Orders:StopsAndLegs").querySelector("[data-slot=legs-after-confirm]")).toHaveTextContent("Orders:LegsAfterConfirm");
    expect(calls(GET, "/api/planning/leg")).toHaveLength(0);
  });
});

describe("OrderPage – pricing per item, order-level lines and totals", () => {
  it("the item's Pricing shows its own rate lines and total; Financial terms the order-level lines; Totals the sums", async () => {
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("PL-NL")).toBeInTheDocument());
    expect(within(pricing).queryByText("Handling")).toBeNull();
    expect(within(pricing.querySelector<HTMLElement>("[data-slot=totals-block]")!).getByText("200.00")).toBeInTheDocument();

    const terms = section("Orders:FinancialTerms");
    expect(within(terms.querySelector<HTMLElement>("[data-slot=order-level-rate-lines]")!).getByText("Handling")).toBeInTheDocument();
    expect(within(terms).getByText("Financial:SetFixedPrice")).toBeInTheDocument();

    const totals = section("Orders:Totals").querySelector<HTMLElement>("[data-slot=totals-block]")!;
    const value = (label: string) => within(totals).getByText(label).nextElementSibling?.textContent;
    expect(value("Orders:Lines")).toBe("200.00");
    expect(value("Orders:OrderLevelLines")).toBe("50.00");
    expect(value("Financial:Total")).toBe("250.00");
    expect(within(totals).getByText("EUR")).toBeInTheDocument();
  });

  it("a manual line added on the item's Pricing tab belongs to the item", async () => {
    routes.POST["/api/financial/rate-line/manual"] = () => ({ id: "r3" });
    renderPage();
    await loaded();
    openItem(0);
    fireEvent.click(within(itemTab("Orders:Pricing")).getByText("Financial:AddManualLine"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/^Financial:UnitPrice/), { target: { value: "15" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(calls(POST, "/api/financial/rate-line/manual")).toHaveLength(1));
    expect(lastCall(POST, "/api/financial/rate-line/manual")!.body).toEqual(expect.objectContaining({ orderId: "o1", orderLineId: "l1", unitPrice: 15, kind: 2 }));
  });

  it("previews an unsaved change through preview-prices after the debounce, in the item's Pricing and the totals", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => ({ currencyCode: "EUR", linesAmount: 300, discountsAndSurchargesAmount: 0, totalAmount: 300, lines: [{ orderLineId: "l1", kind: 1, amount: 300 }], warnings: [] });
    renderPage();
    await loaded();
    fireEvent.change(quantityCell(), { target: { value: "3" } });
    expect(POST).not.toHaveBeenCalled();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(1));
    const body = lastCall(POST, "/api/orders/transport-order/{id}/preview-prices")!.body as { lines: { quantity: number }[]; customerRelationId: string };
    expect(body.lines[0].quantity).toBe(3);
    expect(body.customerRelationId).toBe("rel-1");
    await waitFor(() => expect(within(section("Orders:Totals")).getAllByText("300.00")).toHaveLength(2));
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    expect(pricing.querySelector("[data-slot=price-preview]")).not.toBeNull();
    expect(within(pricing.querySelector<HTMLElement>("[data-slot=totals-block]")!).getByText("300.00")).toBeInTheDocument();
    vi.useRealTimers();
  });

  it("does not preview an incomplete item (no transport unit): a quiet note instead, never a toast", async () => {
    vi.mocked(toast.error).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    renderPage();
    await loaded();
    fireEvent.change(within(itemRows()[0]).getByLabelText("Orders:TransportUnit *"), { target: { value: "" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(0);
    expect(toast.error).not.toHaveBeenCalled();
    openItem(0);
    expect(within(itemTab("Orders:Pricing")).getByText("Orders:PricePreviewIncomplete")).toBeInTheDocument();
    vi.useRealTimers();
  });

  it("a refused preview never toasts: the item's Pricing shows a quiet note and the stored lines", async () => {
    vi.mocked(toast.error).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => fail("Your request is not valid!");
    renderPage();
    await loaded();
    fireEvent.change(quantityCell(), { target: { value: "3" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(1));
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("Orders:PricePreviewUnavailable")).toBeInTheDocument());
    expect(within(pricing).getByText("PL-NL")).toBeInTheDocument();
    expect(toast.error).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("without Financial.RateLines.Update the rate lines are read-only", async () => {
    grants = { ...ALL, "Financial.RateLines.Update": false };
    renderPage();
    await loaded();
    const terms = section("Orders:FinancialTerms");
    await waitFor(() => expect(within(terms).getByText("Handling")).toBeInTheDocument());
    expect(terms.querySelector("[data-slot=order-level-rate-lines] [data-slot=mock-lines-grid]")).toHaveAttribute("data-readonly", "true");
  });
});

describe("OrderPage – one call for header and terms, and a save that stops part-way", () => {
  const changeReference = (value: string) => fireEvent.change(referenceField(), { target: { value } });
  const changePaymentTerm = (value: string) => typeIn(section("Orders:FinancialTerms"), /^Orders:PaymentTermDays/, value);
  const detailReads = () => calls(GET, "/api/orders/transport-order/{id}").length;

  it("a header edit together with a terms edit is one header call that carries the terms", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    changeReference("REF-2");
    changePaymentTerm("45");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-1", customerReference: "REF-2", financialTerms: expect.objectContaining({ currencyCode: "EUR", paymentTermDays: 45 }) }));
    expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(0);
  });

  it("a terms-only edit keeps the terms command and sends no header", async () => {
    routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    changePaymentTerm("45");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-1", paymentTermDays: 45 }));
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(0);
  });

  it("an invalid term in a closed Financial terms section: Save opens it, marks its caption and shows the message", async () => {
    renderPage();
    await loaded();
    changePaymentTerm("x");
    const terms = closedSection("Orders:FinancialTerms");
    fireEvent.click(within(terms).getByRole("button", { name: /Orders:FinancialTerms/ }));
    expect(terms).toHaveAttribute("data-state", "closed");
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(terms).toHaveAttribute("data-state", "open");
    expect(within(terms).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
    expect(terms.querySelector("h3")).toHaveClass("text-destructive");
  });

  it("stops at the first failure, names the saved and the unsaved sections, keeps the rest dirty and retries only those with the new stamp", async () => {
    vi.mocked(toast.success).mockClear();
    let server = order(2);
    routes.GET["/api/orders/transport-order/{id}"] = () => server;
    routes.PUT["/api/orders/transport-order/{id}"] = () => (server = order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" }));
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => fail("Line 1 has no transport unit.");
    renderPage();
    await loaded();
    changeReference("REF-2");
    fireEvent.change(quantityCell(), { target: { value: "3" } });
    const readsBefore = detailReads();
    await click(ribbonButton("Save")!);

    expect(await screen.findByText("Line 1 has no transport unit.")).toBeInTheDocument();
    const report = document.querySelector("[data-slot=save-report]") as HTMLElement;
    expect(report).toHaveTextContent("Orders:SectionsSaved Orders:OrderDetails");
    expect(report).toHaveTextContent("Orders:SectionsNotSaved Orders:Lines");
    expect(report).not.toHaveTextContent("Orders:Attributes");
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-2" }));
    expect(toast.success).not.toHaveBeenCalled();
    await waitFor(() => expect(detailReads()).toBeGreaterThan(readsBefore));
    expect(quantityCell().value).toBe("3");

    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => (server = order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-3" }));
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(2));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-2", lines: [expect.objectContaining({ id: "l1", quantity: 3 })] }));
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved"));
    expect(document.querySelector("[data-slot=save-report]")).toBeNull();
  });

  it("a section left unsaved keeps the page dirty: Close asks first", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => fail("Refused.");
    renderPage();
    await loaded();
    changeReference("REF-2");
    fireEvent.change(quantityCell(), { target: { value: "3" } });
    await click(ribbonButton("Save")!);
    expect(await screen.findByText("Refused.")).toBeInTheDocument();
    await click(ribbonButton("Close")!);
    expect(navigateMock).not.toHaveBeenCalled();
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("UnsavedChanges");
  });

  it("a concurrency refusal while only the stamp changed (a recalculation, no one's content) runs the step again once with the newest stamp", async () => {
    vi.mocked(toast.success).mockClear();
    let attempts = 0;
    routes.PUT["/api/orders/transport-order/{id}"] = () => (++attempts === 1 ? fail("The data you have submitted has already changed by another user.", 409) : order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-10" }));
    renderPage();
    await loaded();
    changeReference("REF-2");
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { concurrencyStamp: "stamp-9", summary: { revenueAmount: 300 } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(2));
    expect(calls(PUT, "/api/orders/transport-order/{id}")[0][1]!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-1" }));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-9", customerReference: "REF-2" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved"));
    expect(document.querySelector("[data-slot=order-conflict]")).toBeNull();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });
});

describe("OrderPage – changed by someone else (concurrency conflict, no overwrite)", () => {
  const theirs = () => order(2, { customerReference: "THEIRS", concurrencyStamp: "stamp-other" });

  it("a 409 after someone else changed the order: the prompt, no retry with their stamp; Keep editing keeps the input and blocks Save and Ctrl+S", async () => {
    vi.mocked(toast.success).mockClear();
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("The data you have submitted has already changed by another user.", 409);
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);

    const prompt = await screen.findByRole("alertdialog");
    expect(prompt).toHaveTextContent("Orders:TransportOrderChanged");
    expect(prompt).toHaveTextContent("Orders:OrderChangedByOther");
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Orders:KeepEditing"));
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1);

    await click(within(prompt).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(referenceField().value).toBe("MINE");
    expect(document.querySelector("[data-slot=order-conflict]")).toHaveTextContent("Orders:OrderChangedByOtherBanner");
    expect(ribbonButton("Save")).toHaveAttribute("aria-disabled", "true");
    await click(ribbonButton("Save")!);
    await key(referenceField(), { key: "s", ctrlKey: true });
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1);
  });

  it("Reload discards the unsaved changes and loads theirs; saving works again with their stamp", async () => {
    vi.mocked(toast.success).mockClear();
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("Changed by another user.", 409);
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:Reload" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await waitFor(() => expect(referenceField().value).toBe("THEIRS"));
    expect(document.querySelector("[data-slot=order-conflict]")).toBeNull();
    expect(ribbonButton("Save")).not.toHaveAttribute("aria-disabled");
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderReloaded");

    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "AGAIN", concurrencyStamp: "stamp-next" });
    fireEvent.change(referenceField(), { target: { value: "AGAIN" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(2));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-other", customerReference: "AGAIN" }));
  });

  it("the banner's Reload asks again before it discards", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("Changed by another user.", 409);
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await click(within(document.querySelector<HTMLElement>("[data-slot=order-conflict]")!).getByRole("button", { name: /Orders:Reload/ }));
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("Orders:OrderChangedByOther");
    expect(referenceField().value).toBe("MINE");
  });

  it("a newer version with other content that arrived while editing is not saved over: the prompt, without any call", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    renderPage(client);
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await act(async () => {
      await client.refetchQueries({ queryKey: ["orders", "transportOrders", "detail", "o1"] });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(referenceField().value).toBe("MINE");
    await click(ribbonButton("Save")!);
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("Orders:OrderChangedByOther");
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(0);
  });

  it("a 409 after part of the save went through keeps the saved section and names the rest in the banner", async () => {
    let server = order(2);
    routes.GET["/api/orders/transport-order/{id}"] = () => server;
    routes.PUT["/api/orders/transport-order/{id}"] = () => (server = order(2, { customerReference: "MINE", concurrencyStamp: "stamp-2" }));
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => {
      server = order(2, { customerReference: "MINE", concurrencyStamp: "stamp-3", lines: [{ ...order(2).lines[0], quantity: 9 }] });
      return fail("Changed by another user.", 409);
    };
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    fireEvent.change(quantityCell(), { target: { value: "3" } });
    await click(ribbonButton("Save")!);
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1);
    const report = document.querySelector("[data-slot=order-conflict] [data-slot=save-report]") as HTMLElement;
    expect(report).toHaveTextContent("Orders:SectionsSaved Orders:OrderDetails");
    expect(report).toHaveTextContent("Orders:SectionsNotSaved Orders:Lines");
    expect(quantityCell().value).toBe("3");
  });

  it("a business-rule 409 (with its error code) is no conflict: the error shows and the partial-save behaviour stays", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => ({ data: undefined, error: { error: { code: "OpenTms.Orders:InvalidTransition", message: "The order cannot be changed in this status." } }, response: new Response(null, { status: 409 }) });
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "MINE" } });
    await click(ribbonButton("Save")!);
    expect(await screen.findByText("The order cannot be changed in this status.")).toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(document.querySelector("[data-slot=order-conflict]")).toBeNull();
    expect(ribbonButton("Save")).not.toHaveAttribute("aria-disabled");
  });
});

describe("OrderPage – Tevens trip (Q9; per freight item)", () => {
  const trip = () => section("Orders:TripRequest");
  const field = (suffix: string) => trip().querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-trip-${suffix}']`)!;
  const request = (extra: Record<string, unknown> = {}) => ({ transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureRelationName: "Acme", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalRelationName: "Beta", arrivalAddressId: "adr-2", plannedDeparture: "2026-10-25T06:00:00Z", vehicleIds: [], driverIds: [], lineIds: null, ...extra });

  it("reveals the trip fields behind the checkbox on a Draft; an incomplete request is not sent", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    expect(trip().querySelector("[data-slot=trip-request-fields]")).toBeNull();
    fireEvent.click(within(trip()).getByRole("checkbox", { name: "Orders:TripRequestHint" }));
    expect(trip().querySelector("[data-slot=trip-request-fields]")).not.toBeNull();
    expect(within(trip()).getByText("Orders:Advanced")).toBeInTheDocument();
    fireEvent.change(field("transportTripTypeId"), { target: { value: "" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(trip()).getByText("Orders:TripRequestIncomplete")).toBeInTheDocument();
  });

  it("ticking it prefills type, departure (first stop, its date and window start) and arrival (last stop); the user can change them", async () => {
    const lines = [order(1).lines[0], { id: "l2", position: 2, transportUnitId: "unit-1", quantity: 1, paymentInstruction: 0, stops: [
      { id: "s3", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationName: "Acme", relationAddressId: "adr-1", date: "2026-10-26" },
      { id: "s4", position: 2, waypointTypeId: "wp-unload", relationId: "rel-3", relationName: "Gamma", relationAddressId: "adr-3", date: "2026-10-27" },
    ] }];
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1, { lines });
    routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }, { id: "tt-2", name: "Charter" }];
    routes.GET["/api/planning/transport-trip-type"] = () => ({ rows: [{ id: "tt-2", name: "Charter", isDefault: true }], totalCount: 2, filteredCount: 1 });
    routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { lines, concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    trip();
    await waitFor(() => expect(GET.mock.calls.some(([p]) => p === "/api/planning/transport-trip-type")).toBe(true));
    await act(async () => {
      await Promise.resolve();
    });
    fireEvent.click(within(trip()).getByRole("checkbox", { name: "Orders:TripRequestHint" }));
    expect(field("transportTripTypeId").value).toBe("tt-2");
    expect(field("departureRelation").value).toBe("rel-1");
    expect(field("arrivalRelation").value).toBe("rel-3");
    expect(field("departureDate").value).toBe("2026-10-25");
    expect(field("departureTime").value).toBe("08:00");
    fireEvent.change(field("departureTime"), { target: { value: "06:15" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    const body = (lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body as { tripRequest: Record<string, unknown> }).tripRequest;
    expect(body).toMatchObject({ transportTripTypeId: "tt-2", departureRelationId: "rel-1", arrivalRelationId: "rel-3", lineIds: null });
    const departure = new Date(body.plannedDeparture as string);
    expect([departure.getFullYear(), departure.getMonth() + 1, departure.getDate(), departure.getHours(), departure.getMinutes()]).toEqual([2026, 10, 25, 6, 15]);
  });

  it("a checkbox per freight item, all ticked; an unticked item is left off: lineIds names the others, and reads back", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1, { lines: twoLines(), tripRequest: request() });
    routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { lines: twoLines(), tripRequest: request({ lineIds: ["l2"] }), concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    const boxes = () => within(trip().querySelector<HTMLElement>("[data-slot=trip-request-items]")!).getAllByRole("checkbox");
    expect(boxes()).toHaveLength(2);
    for (const box of boxes()) expect(box).toHaveAttribute("data-checked");
    fireEvent.click(boxes()[0]);
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body).toEqual(expect.objectContaining({ tripRequest: expect.objectContaining({ lineIds: ["l2"] }) }));
    expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(0);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved"));
    expect(boxes()[0]).not.toHaveAttribute("data-checked");
    expect(boxes()[1]).toHaveAttribute("data-checked");
  });

  it("a stored empty selection shows nothing ticked and asks to choose; it is never posted back as []", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1, { lines: twoLines(), tripRequest: request({ lineIds: [] }) });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    const items = trip().querySelector<HTMLElement>("[data-slot=trip-request-items]")!;
    for (const box of within(items).getAllByRole("checkbox")) expect(box).not.toHaveAttribute("data-checked");
    expect(trip().querySelector("[data-slot=trip-request-no-items]")).toHaveTextContent("Orders:TripRequestNoItems");
    fireEvent.change(referenceField(), { target: { value: "REF-9" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(trip()).getByText("Orders:TripRequestNoItems")).toBeInTheDocument();
  });

  it("the agreed purchase price belongs to an outsourced trip: it waits for a carrier and is cleared with it", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1, { tripRequest: request({ carrierRelationId: "car-1", carrierRelationName: "Charter", agreedPurchaseAmount: 400, agreedPurchaseCurrencyCode: "EUR" }) });
    routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { concurrencyStamp: "stamp-2" });
    lookupItems.current = { "*-trip-carrier": [{ id: "car-1", name: "Charter" }] };
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    const amount = () => within(trip()).getByLabelText(/^Orders:AgreedPurchaseAmount/) as HTMLInputElement;
    expect(amount().value).toBe("400");
    expect(amount()).toBeEnabled();
    fireEvent.change(field("carrier"), { target: { value: "" } });
    expect(amount().value).toBe("");
    expect(amount()).toBeDisabled();
    expect(within(trip()).getByText("Orders:AgreedPurchaseNeedsCarrier")).toBeInTheDocument();
    fireEvent.change(field("carrier"), { target: { value: "car-1" } });
    expect(amount()).toBeEnabled();
    fireEvent.change(amount(), { target: { value: "350" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body).toEqual(expect.objectContaining({ tripRequest: expect.objectContaining({ carrierRelationId: "car-1", agreedPurchaseAmount: 350, agreedPurchaseCurrencyCode: null }) }));
  });

  it("shows the created trip as a link and no editable fields once the order is confirmed", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { tripRequest: request({ tripId: "trip-9", tripNumber: "TR-9" }) });
    renderPage();
    await loaded();
    fireEvent.click(within(trip()).getByRole("button", { name: /Orders:Trip TR-9/ }));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/trips/trip-9" }));
    expect(within(trip()).getByRole("checkbox", { name: "Orders:TripRequestHint" })).toHaveAttribute("data-disabled");
  });

  it("a trip request warning is a header badge that opens the Tevens trip section, with the warning in it", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(2, { tripRequest: request({ warningCode: "OpenTms.Orders:TripRequestNotHonoured", warningField: "TransportTripType", warningAt: "2026-10-25T09:00:00Z" }) });
    renderPage();
    await loaded();
    const badge = header().querySelector<HTMLElement>("[data-slot=trip-request-warning-badge]")!;
    expect(badge).toHaveTextContent("Orders:TripNotCreated");
    const box = closedSection("Orders:TripRequest");
    fireEvent.click(within(box).getByRole("button", { name: /Orders:TripRequest/ }));
    expect(box).toHaveAttribute("data-state", "closed");
    fireEvent.click(badge);
    expect(box).toHaveAttribute("data-state", "open");
    expect(box.querySelector("[data-slot=trip-request-warning]")).toHaveTextContent("OpenTms.Orders:TripRequestNotHonoured");
  });

  it("shows no badge and no warning without one", async () => {
    renderPage();
    await loaded();
    expect(header().querySelector("[data-slot=trip-request-warning-badge]")).toBeNull();
    expect(trip().querySelector("[data-slot=trip-request-warning]")).toBeNull();
  });
});

describe("OrderPage – documents and close", () => {
  it("Print CMR saves first and opens the wizard; Close goes back to the list", async () => {
    renderPage();
    await loaded();
    await click(ribbonButton("Orders:PrintConsignmentNote")!);
    expect(await screen.findByTestId("print-wizard")).toBeInTheDocument();
    await click(ribbonButton("Close")!);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders" }));
    expect(GET).toHaveBeenCalledWith("/api/financial/rate-line", expect.objectContaining({ params: { query: { OrderId: "o1" } } }));
  });

  it("a dirty form asks before closing (UI-DLG-008)", async () => {
    renderPage();
    await loaded();
    fireEvent.change(referenceField(), { target: { value: "changed" } });
    await click(ribbonButton("Close")!);
    expect(navigateMock).not.toHaveBeenCalled();
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("UnsavedChanges");
  });
});

describe("TemplatePage – the same page in template mode", () => {
  const template = (extra: Record<string, unknown> = {}) => ({
    id: "tpl-1",
    name: "Weekly Acme",
    transportOrderTypeId: "type-1",
    customerRelationId: "rel-1",
    customerName: "Acme",
    isActive: true,
    concurrencyStamp: "tstamp-1",
    defaultStops: [],
    lines: [],
    schedule: { recurrence: 1, interval: 1, daysOfWeek: 1, horizonDays: 5, validFrom: "2026-10-25" },
    ...extra,
  });
  const renderTemplate = () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    return render(
      <QueryClientProvider client={client}>
        <TemplatePage id="tpl-1" />
      </QueryClientProvider>,
    );
  };
  beforeEach(() => {
    grants = { ...ALL, "Orders.TransportOrderTemplates": true, "Orders.TransportOrderTemplates.Update": true };
  });

  it("a template without items cannot create orders yet; with one it can; Schedule is open and there are no totals", async () => {
    routes.GET["/api/orders/transport-order-template/{id}"] = () => template();
    const { unmount } = renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("aria-disabled", "true");
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("title", "Orders:TemplateHasNoLines");
    expect(closedSection("Orders:Schedule")).toHaveAttribute("data-state", "open");
    expect(document.querySelector("[data-slot=form-section][aria-label='Orders:Totals']")).toBeNull();
    unmount();

    routes.GET["/api/orders/transport-order-template/{id}"] = () => template({ lines: [{ id: "tl1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 1, paymentInstruction: 0, stops: [] }] });
    renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    expect(ribbonButton("Orders:Materialise")).not.toHaveAttribute("aria-disabled");
  });

  it("the entry row takes a day offset; the save posts the items with stops as day offsets and the trip's items by posted id", async () => {
    routes.GET["/api/orders/transport-order-template/{id}"] = () =>
      template({ lines: [{ id: "tl1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 1, paymentInstruction: 0, stops: [] }], tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalAddressId: "adr-2", departureDayOffset: 0, vehicleIds: [], driverIds: [], lineIds: null } });
    routes.PUT["/api/orders/transport-order-template/{id}"] = () => template({ concurrencyStamp: "tstamp-2" });
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => [{ id: `${String(init?.params?.path?.id)}-main`, addressTypeId: null, city: "X" }];
    lookupItems.current = { "*-entry-from": [{ id: "rel-1", name: "Acme" }], "*-entry-to": [{ id: "rel-2", name: "Beta" }] };
    renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    const entry = document.querySelector<HTMLElement>("[data-slot=freight-item-entry]")!;
    const entryField = (suffix: string) => entry.querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-entry-${suffix}']`)!;
    expect(entry.querySelector("[id$='-entry-date']")).toBeNull();
    /* The order type (its default waypoints) is there before the user types. */
    await waitFor(() => expect(calls(GET, "/api/orders/transport-order-type/{id}").length).toBeGreaterThan(0));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    fireEvent.change(entryField("from"), { target: { value: "rel-1" } });
    fireEvent.change(entryField("to"), { target: { value: "rel-2" } });
    fireEvent.change(entryField("dayOffset"), { target: { value: "1" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-1" } });
    await key(entryField("dayOffset"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    /* The first item leaves the trip: the new one goes on it by the id the save posts for it. */
    const items = section("Orders:TripRequest").querySelector<HTMLElement>("[data-slot=trip-request-items]")!;
    fireEvent.click(within(items).getAllByRole("checkbox")[0]);
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order-template/{id}")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order-template/{id}")!.body as { lines: { id: string; stops: { dayOffset: number; relationAddressId: string }[] }[]; tripRequest: { lineIds: string[] } };
    expect(body.lines[0].id).toBe("tl1");
    expect(body.lines[1].id).toMatch(/^[0-9a-f-]{36}$/);
    expect(body.lines[1].stops.map((s) => [s.dayOffset, s.relationAddressId])).toEqual([[1, "rel-1-main"], [1, "rel-2-main"]]);
    expect(body.tripRequest.lineIds).toEqual([body.lines[1].id]);
  });
});
