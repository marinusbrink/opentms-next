import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The order page (design 61 "Order page, legacy tab structure", PO 2026-10-26 and 2026-10-27): the main tabs General,
 * Financial, Tevens trip, Schedule (template) and the icon tabs Documents, Remarks and Record identification; under
 * General the header and the nested tabs Freight items, Additional services, Remarks for the planner, Other
 * references and Custom fields. Covered: the page states and the tab strip; the ribbon per status and permission;
 * the section saves with the stamp, the save that stops part-way and the concurrency conflict (unchanged rules); a
 * failed submit opens the tab of the first error and marks it; the entry of a new item (the stop strip seeded from the
 * type's default waypoints, Enter adds with a copy of the stops, the strip kept, the freight fields cleared but the
 * unit, the cursor back in the last stop's relation; a stop without an address named); the freight items grid
 * (columns, footer total, Delete asks); Edit freight item (its tabs, Apply keeps, Close restores, a save while open);
 * the item's rate lines, the Financial tab's blocks and order-level lines and the revenue overview with the preview;
 * Additional services on a new and a stored order; the Tevens trip with the items on it (lineIds); the legs per item;
 * the Remarks tab; read-only after confirm; the shortcuts; template mode. New orders and paste are in
 * order-page-new.test.tsx. Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
/* The save report's keys carry their {0}, so a test sees which section names were filled in. */
/* The stop and item names carry their placeholders, so a test sees which stop a message names. */
const TEXTS: Record<string, string> = { "Orders:StopNamed": "Stop {0} ({1})", "Orders:StopNumbered": "Stop {0}", "Orders:ItemProblem": "Item {0} · {1}", "Orders:DeleteFreightItemQuestion": "Delete freight item {0}?" };
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
import { TemplatePage } from "@/apps/transport/pages/TemplatePage";
import { toast } from "sonner";
import { fail, gridRows, itemList, itemRows, itemTab, lookupItems, openItem, openNested, openTab, panel, ribbonButton, ribbonLabels, section, stubGlobals, tabLabels } from "./transport-test-mocks";
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
const loaded = () => screen.findByText("Orders:TransportOrder TO-2026-00001");
/** A main tab's trigger in the page's strip. */
const tabTrigger = (label: string) => Array.from(document.querySelectorAll<HTMLElement>("[data-editor-tab-strip] [role=tab]")).find((t) => t.textContent?.trim() === label || t.getAttribute("aria-label") === label)!;
const activeTab = () => document.querySelector<HTMLElement>("[data-slot=record-page-panel][data-state=active]")?.getAttribute("aria-label");
const activeNested = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=nested-tabs] [role=tab]")).find((t) => t.getAttribute("aria-selected") === "true")?.textContent?.trim();
const dialog = () => document.querySelector<HTMLElement>("[role=dialog][data-variant=editor]");

describe("OrderPage – states and layout", () => {
  it("shows PermissionDenied without the view permission and the skeleton, then the legacy tabs: General first, the icon tabs last", async () => {
    grants = {};
    const { unmount } = renderPage();
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();

    grants = { ...ALL };
    renderPage();
    expect(document.querySelector("[data-slot=record-page-skeleton]")).not.toBeNull();
    await loaded();
    expect(within(header()).getByText("Orders:Status.Open")).toBeInTheDocument();
    /* UI-FORM-008: end-aligned at the ribbon row; General, Financial, Tevens trip; then Documents, Remarks and Record
     * identification as icon-only tabs (their names as tooltips / aria-labels). No Schedule outside template mode. */
    expect(tabLabels()).toEqual(["General", "Orders:Financial", "Orders:TripRequestTab", "Orders:Documents", "Remarks", "RecordIdentification"]);
    expect(document.querySelector("[data-editor-tab-strip]")).toHaveClass("justify-end");
    for (const icon of ["Orders:Documents", "Remarks", "RecordIdentification"]) {
      expect(tabTrigger(icon).textContent?.trim()).toBe("");
      expect(tabTrigger(icon).querySelector("svg")).not.toBeNull();
    }
    expect(activeTab()).toBe("General");
    /* General: the header on top, then the nested tabs (start-aligned, text only); no Custom fields without attributes. */
    const general = panel("General");
    expect(general.querySelector("[data-slot=order-header]")).not.toBeNull();
    const nested = Array.from(general.querySelectorAll<HTMLElement>("[data-slot=nested-tabs] [role=tab]")).map((t) => t.textContent?.trim());
    expect(nested).toEqual(["Orders:Lines", "Financial:AdditionalServices", "Orders:PlannerRemarks", "Orders:OtherReferences"]);
    expect(general.querySelector("[data-slot=nested-tabs] [role=tablist]")).toHaveClass("justify-start");
    expect(activeNested()).toBe("Orders:Lines");
    /* The header shows the number read-only; there is no stop pattern on the order any more. */
    expect(within(section("Orders:OrderDetails")).getByLabelText("Orders:Number")).toHaveValue("TO-2026-00001");
    expect(within(section("Orders:OrderDetails")).getByLabelText("Orders:Number")).toBeDisabled();
    expect(screen.queryByText("Orders:DefaultStops")).toBeNull();
  });

  it("Custom fields is a nested tab once the orders have attribute definitions", async () => {
    routes.GET["/api/orders/order-attribute-definition"] = (init) => ({ rows: init?.params?.query?.Target === 1 ? [{ id: "d1", key: "dock", name: "Dock", position: 1 }] : [], totalCount: 1, filteredCount: 1 });
    renderPage();
    await loaded();
    await waitFor(() => expect(Array.from(document.querySelectorAll("[data-slot=nested-tabs] [role=tab]")).map((t) => t.textContent?.trim())).toContain("Orders:CustomFields"));
    expect(openNested("Orders:CustomFields").querySelector("[data-slot=order-custom-fields]")).not.toBeNull();
  });

  it("the Remarks tab lists the order's dated remarks, newest first, read-only", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { remarks: [{ at: "2026-10-20T08:00:00Z", kind: 1, text: "First reason" }, { at: "2026-10-26T08:00:00Z", kind: 1, text: "Second reason" }] });
    renderPage();
    await loaded();
    const remarks = openTab("Remarks");
    const items = Array.from(remarks.querySelectorAll("[data-slot=order-remark]")).map((r) => r.textContent);
    expect(items).toHaveLength(2);
    expect(items[0]).toContain("Second reason");
    expect(items[0]).toContain("Orders:OrderRemarkKind.Revision");
    expect(remarks.querySelector("textarea, input")).toBeNull();
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

  it("the freight items grid shows position, from → to, date/time, unit, quantity, positions, weight, loading metres, reference, price and trip; the footer the total", async () => {
    routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", orderLineId: "l1", tripId: "trip-9", tripNumber: "TR-9", position: 1 }], totalCount: 1, filteredCount: 1 });
    renderPage();
    await loaded();
    await waitFor(() => expect(within(itemRows()[0]).getByText("200.00")).toBeInTheDocument());
    const columns = Array.from(itemList().querySelectorAll("[data-slot=mock-lines-grid-columns] > span")).map((c) => c.getAttribute("data-column"));
    expect(columns).toEqual(["problem", "position", "route", "when", "transportUnitId", "quantity", "positions", "weightKg", "loadingMeters", "reference", "price", "trips"]);
    const footer = document.querySelector<HTMLElement>("[data-slot=freight-items] > [data-slot=totals-block]")!;
    expect(within(footer).getByText("Orders:FreightItemsTotal").nextElementSibling).toHaveTextContent("200.00");
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

  it("an item without a transport unit is marked in the grid; Save refuses, names it and opens General → Freight items", async () => {
    renderPage();
    await loaded();
    fireEvent.change(within(itemRows()[0]).getByLabelText("Orders:TransportUnit *"), { target: { value: "" } });
    expect(itemRows()[0].querySelector("[data-slot=item-problem]")).not.toBeNull();
    openTab("Orders:Financial");
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(activeTab()).toBe("General");
    expect(activeNested()).toBe("Orders:Lines");
    expect(document.querySelector("[data-slot=items-message]")).toHaveTextContent("Item 1 · Orders:LineNeedsTransportUnit");
    /* UI-FORM-007: the tab and the nested tab with the error are marked. */
    expect(tabTrigger("General")).toHaveClass("text-destructive");
    expect(Array.from(document.querySelectorAll<HTMLElement>("[data-slot=nested-tabs] [role=tab]")).find((t) => t.textContent?.trim() === "Orders:Lines")).toHaveClass("text-destructive");
  });

  it("a stop without a usable address names the stop and its item: \"Stop 2 (Unload): …\"", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { lines: [{ ...order(2).lines[0], stops: [stops("2026-10-25", "2026-10-26")[0], { ...stops("2026-10-25", "2026-10-26")[1], waypointTypeName: "Unload", relationAddressId: null }] }] });
    renderPage();
    await loaded();
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(document.querySelector("[data-slot=items-message]")).toHaveTextContent("Item 1 · Stop 2 (Unload): Orders:StopNeedsAddress");
    /* In the item's dialog the message stands under the stop's address, and its Stops tab is marked. */
    const edit = openItem(0);
    const stopsTab = Array.from(edit.querySelectorAll<HTMLElement>("[data-editor-tab-strip] [role=tab]")).find((t) => t.textContent?.includes("Orders:StopsAndLegs"))!;
    expect(stopsTab).toHaveClass("text-destructive");
    const rows = itemTab("Orders:StopsAndLegs").querySelectorAll<HTMLElement>("[data-slot=stop-row]");
    expect(within(rows[1]).getByText("Stop 2 (Unload): Orders:StopNeedsAddress")).toBeInTheDocument();
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

describe("OrderPage – the entry of a new freight item (the stop strip, keyboard first)", () => {
  const entry = () => document.querySelector<HTMLElement>("[data-slot=freight-item-entry]")!;
  const stopField = (index: number, name: string) => entry().querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-entry-strip-stop-${index}-${name}']`)!;
  const entryField = (suffix: string) => entry().querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-entry-${suffix}']`)!;
  const stripRows = () => Array.from(entry().querySelectorAll<HTMLElement>("[data-slot=stop-row]"));
  beforeEach(() => {
    lookupItems.current = {
      "*-entry-strip-stop-0-relation": [{ id: "rel-1", name: "Acme" }],
      "*-entry-strip-stop-1-relation": [{ id: "rel-2", name: "Beta" }, { id: "rel-3", name: "Gamma" }],
      "*-entry-strip-stop-2-relation": [{ id: "rel-3", name: "Gamma" }],
    };
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => {
      const id = String(init?.params?.path?.id);
      return [{ id: `${id}-dock`, addressTypeId: "type-dock", street: "Dock", city: "Elsewhere" }, { id: `${id}-main`, addressTypeId: null, street: "Main", city: `City of ${id}` }];
    };
  });

  it("the strip starts with the order type's default waypoints on the order date", async () => {
    routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 1 | 64, defaultWaypoints: [...LOAD_UNLOAD, { waypointTypeId: "wp-unload", name: "Unload", position: 3 }] });
    renderPage();
    await loaded();
    await waitFor(() => expect(stripRows()).toHaveLength(3));
    expect([0, 1, 2].map((i) => stopField(i, "activity").value)).toEqual(["wp-load", "wp-unload", "wp-unload"]);
    expect(stopField(0, "date").value).toBe("2026-10-25");
  });

  it("a strip typed in before the order type arrived keeps its stops and gets the type's activities", async () => {
    let release: () => void = () => undefined;
    const typeLoaded = new Promise<void>((resolve) => (release = resolve));
    const typeRoute = routes.GET["/api/orders/transport-order-type/{id}"];
    GET.mockImplementation(async (path, init) => {
      if (path === "/api/orders/transport-order-type/{id}") {
        await typeLoaded;
        return { data: typeRoute(init), error: undefined, response: new Response() };
      }
      const handler = routes.GET[path];
      return { data: handler ? handler(init) : /\/(lookup|all|states|addresses)$/.test(path) ? [] : { rows: [], totalCount: 0, filteredCount: 0 }, error: undefined, response: new Response() };
    });
    renderPage();
    await loaded();
    await waitFor(() => expect(stripRows()).toHaveLength(2));
    fireEvent.change(stopField(0, "relation"), { target: { value: "rel-1" } });
    expect(stopField(1, "activity").value).toBe("");
    await act(async () => {
      release();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    await waitFor(() => expect(stopField(1, "activity").value).toBe("wp-unload"));
    expect(stopField(0, "activity").value).toBe("wp-load");
    expect(stopField(0, "relation").value).toBe("rel-1");
  });

  it("Enter adds the item with a copy of the strip's stops (default addresses); the strip keeps them, the fields clear but the unit, the cursor goes to the last stop's relation", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    await waitFor(() => expect(stopField(1, "activity").value).toBe("wp-unload"));
    fireEvent.change(stopField(0, "relation"), { target: { value: "rel-1" } });
    fireEvent.change(stopField(0, "from"), { target: { value: "07:30" } });
    fireEvent.change(stopField(0, "reference"), { target: { value: "L-77" } });
    fireEvent.change(stopField(1, "relation"), { target: { value: "rel-2" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-2" } });
    fireEvent.change(entryField("weightKg"), { target: { value: "120" } });
    fireEvent.change(entryField("loadingMeters"), { target: { value: "2.4" } });
    await key(entryField("loadingMeters"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    expect(itemRows()[1].querySelector("[data-field=route]")?.textContent).toBe("Acme, City of rel-1 → Beta, City of rel-2");
    /* The strip keeps its stops; the freight fields clear except the unit; the cursor is in the last stop's relation. */
    expect(stopField(0, "relation").value).toBe("rel-1");
    expect(stopField(1, "relation").value).toBe("rel-2");
    expect(stopField(0, "reference").value).toBe("L-77");
    expect(entryField("unit").value).toBe("unit-2");
    expect(entryField("weightKg").value).toBe("");
    expect(entryField("loadingMeters").value).toBe("");
    expect(document.activeElement).toBe(stopField(1, "relation"));

    /* The next one goes elsewhere: only its last stop changes, Enter on it. */
    fireEvent.change(stopField(1, "relation"), { target: { value: "rel-3" } });
    await key(stopField(1, "relation"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(3));

    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { lines: { id: string | null; transportUnitId: string; quantity: number; weightKg: number | null; loadingMeters: number | null; stops: Record<string, unknown>[] }[] };
    expect(body.lines).toHaveLength(3);
    expect(body.lines[1]).toMatchObject({ id: null, transportUnitId: "unit-2", quantity: 1, weightKg: 120, loadingMeters: 2.4 });
    expect(body.lines[1].stops).toEqual([
      expect.objectContaining({ waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "rel-1-main", date: "2026-10-25", windowFrom: "07:30:00", reference: "L-77" }),
      expect.objectContaining({ waypointTypeId: "wp-unload", relationId: "rel-2", relationAddressId: "rel-2-main", date: "2026-10-25", reference: null }),
    ]);
    /* The second item has stops of its own: the same first stop, its own last one; the unit kept, the weight not. */
    expect(body.lines[2].stops.map((s) => s.relationId)).toEqual(["rel-1", "rel-3"]);
    expect(body.lines[2].stops[0]).toEqual(expect.objectContaining({ reference: "L-77", id: null }));
    expect(body.lines[2]).toMatchObject({ transportUnitId: "unit-2", weightKg: null });
  });

  it("+ adds a stop to the strip and the item gets it; − is not offered below two stops", async () => {
    renderPage();
    await loaded();
    await waitFor(() => expect(stopField(1, "activity").value).toBe("wp-unload"));
    expect(stripRows()).toHaveLength(2);
    for (const row of stripRows()) expect(within(row).getByRole("button", { name: "Orders:RemoveStop" })).toBeDisabled();
    fireEvent.click(within(stripRows()[1]).getByRole("button", { name: "Orders:AddStop" }));
    expect(stripRows()).toHaveLength(3);
    fireEvent.change(stopField(0, "relation"), { target: { value: "rel-1" } });
    fireEvent.change(stopField(1, "relation"), { target: { value: "rel-2" } });
    fireEvent.change(stopField(2, "relation"), { target: { value: "rel-3" } });
    fireEvent.change(stopField(2, "activity"), { target: { value: "wp-unload" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-1" } });
    await key(entryField("unit"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
    expect(itemRows()[1].querySelector("[data-field=route]")?.textContent).toBe("Acme, City of rel-1 → Gamma, City of rel-3");
    expect(document.activeElement).toBe(stopField(2, "relation"));
  });

  it("Enter with a stop or the unit missing adds nothing: the message names the stop, the cursor goes there; Enter in an open list never adds", async () => {
    renderPage();
    await loaded();
    await waitFor(() => expect(stopField(1, "activity").value).toBe("wp-unload"));
    fireEvent.change(stopField(0, "relation"), { target: { value: "rel-1" } });
    await key(stopField(0, "relation"), { key: "Enter" });
    expect(itemRows()).toHaveLength(1);
    expect(within(stripRows()[1]).getByText("Stop 2 (Unload): Orders:StopNeedsRelation")).toBeInTheDocument();
    expect(document.activeElement).toBe(stopField(1, "relation"));

    fireEvent.change(stopField(1, "relation"), { target: { value: "rel-2" } });
    await key(stopField(1, "relation"), { key: "Enter" });
    expect(itemRows()).toHaveLength(1);
    expect(within(entry()).getByText("MasterData:Required")).toBeInTheDocument();
    expect(document.activeElement).toBe(entryField("unit"));

    fireEvent.change(entryField("unit"), { target: { value: "unit-1" } });
    /* A lookup whose list is open takes Enter itself (it selects). */
    stopField(1, "relation").setAttribute("aria-expanded", "true");
    await key(stopField(1, "relation"), { key: "Enter" });
    expect(itemRows()).toHaveLength(1);
    stopField(1, "relation").removeAttribute("aria-expanded");
    await key(stopField(1, "relation"), { key: "Enter" });
    await waitFor(() => expect(itemRows()).toHaveLength(2));
  });

  it("Delete asks \"Delete freight item {n}?\" and removes the item only on Delete", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { lines: twoLines() });
    renderPage();
    await loaded();
    fireEvent.click(within(itemRows()[1]).getByRole("button", { name: "Remove" }));
    const confirm = await screen.findByRole("alertdialog");
    expect(confirm).toHaveTextContent("Orders:DeleteFreightItem");
    expect(confirm).toHaveTextContent("Delete freight item 2?");
    expect(itemRows()).toHaveLength(2);
    await click(within(confirm).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(itemRows()).toHaveLength(1));
    expect(itemRows()[0]).toHaveAttribute("data-key");
    expect(itemRows()[0].querySelector("[data-field=position]")?.textContent).toBe("1");
  });
});

describe("OrderPage – Edit freight item (a dialog with tabs of its own)", () => {
  it("a double click opens it with Freight details, Stops and legs and Pricing; details hold cargo, then the nested tabs", async () => {
    renderPage();
    await loaded();
    expect(dialog()).toBeNull();
    const edit = openItem(0);
    expect(edit).toHaveAttribute("data-size", "full");
    expect(within(edit).getByText("Orders:Line 1")).toBeInTheDocument();
    expect(Array.from(edit.querySelectorAll("[data-editor-tab-strip] [role=tab]")).map((t) => t.textContent)).toEqual(["Orders:FreightDetails", "Orders:StopsAndLegs", "Orders:Pricing"]);
    const details = itemTab("Orders:FreightDetails");
    expect(details.querySelector("[data-slot=freight-details] > [data-slot=form-section]")).toHaveAttribute("aria-label", "Orders:Cargo");
    expect(Array.from(details.querySelectorAll("[data-slot=nested-tabs] [role=tab]")).map((t) => t.textContent?.trim())).toEqual(["Orders:Container", "Orders:Measurements", "Orders:Texts"]);
    expect((within(details).getByLabelText(/^Orders:WeightKg/) as HTMLInputElement).value).toBe("500");
    /* Apply and Close in the ribbon (UI-DLG-005). */
    expect(Array.from(edit.querySelectorAll("[data-slot=ribbon-button]")).map((b) => b.textContent?.trim())).toEqual(["Orders:Apply", "Close"]);
  });

  it("Apply keeps the change in the order (saved with the items); Close on a changed item asks and restores it", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    /* Close: the dirty guard, then the item as it was. */
    typeIn(openItem(0), /^Orders:Positions/, "9");
    await click(within(dialog()!).getAllByRole("button", { name: "Close" })[0]);
    const discard = await screen.findByRole("alertdialog");
    expect(discard).toHaveTextContent("UnsavedChanges");
    await click(within(discard).getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(dialog()).toBeNull());
    expect(within(itemRows()[0]).getByLabelText("Orders:Positions")).toHaveValue("");
    expect(ribbonButton("Save")).not.toBeNull();

    /* Apply: kept, the page is dirty, and the order's Save stores it. */
    typeIn(openItem(0), /^Orders:Positions/, "4");
    await click(within(dialog()!).getByText("Orders:Apply"));
    await waitFor(() => expect(dialog()).toBeNull());
    expect(within(itemRows()[0]).getByLabelText("Orders:Positions")).toHaveValue("4");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body).toEqual(expect.objectContaining({ lines: [expect.objectContaining({ id: "l1", positions: 4 })] }));
  });

  it("Ctrl/⌘+S inside the dialog saves the order; the dialog stays open and Close then restores nothing", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2", lines: [{ ...order(2).lines[0], positions: 4 }] });
    renderPage();
    await loaded();
    const edit = openItem(0);
    typeIn(edit, /^Orders:Positions/, "4");
    await key(within(edit).getByLabelText(/^Orders:Positions/), { key: "s", ctrlKey: true });
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved"));
    expect(dialog()).not.toBeNull();
    await click(within(dialog()!).getAllByRole("button", { name: "Close" })[0]);
    await waitFor(() => expect(dialog()).toBeNull());
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(within(itemRows()[0]).getByLabelText("Orders:Positions")).toHaveValue("4");
  });

  it("Stops and legs: the item's stops in the row editor and, after Confirm, only its own legs; the leg dialog edits a window", async () => {
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
    const stopsPanel = itemTab("Orders:StopsAndLegs");
    expect(stopsPanel.querySelectorAll("[data-slot=stop-strip] [data-slot=stop-row]")).toHaveLength(2);
    expect(within(stopsPanel.querySelectorAll<HTMLElement>("[data-slot=stop-row]")[0]).getByRole("button", { name: "Orders:MoveDown" })).toBeInTheDocument();
    await waitFor(() => expect(stopsPanel.querySelectorAll("[data-slot=item-legs] [data-slot=mock-grid-row]")).toHaveLength(1));
    expect(stopsPanel.querySelector("[data-slot=item-legs] [data-slot=mock-grid-row]")).toHaveAttribute("data-id", "leg-1");
    fireEvent.click(within(stopsPanel.querySelector<HTMLElement>("[data-slot=item-legs]")!).getByText("Edit"));
    await waitFor(() => expect(document.querySelector("#leg-destination-window-from")).not.toBeNull());
    const legDialog = document.querySelector<HTMLElement>("#leg-destination-window-from")!.closest<HTMLElement>("[role=dialog]")!;
    fireEvent.change(legDialog.querySelector("#leg-destination-window-from")!, { target: { value: "7:15" } });
    fireEvent.change(legDialog.querySelector("#leg-destination-window-till")!, { target: { value: "11:00" } });
    await act(async () => {
      fireEvent.submit(legDialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/planning/leg/{id}", expect.anything()));
    expect((lastCall(PUT, "/api/planning/leg/{id}")!.body as Record<string, unknown>).destinationWindow).toEqual({ from: at([2026, 10, 26], "07:15"), till: at([2026, 10, 26], "11:00") });
  });

  it("a stop moved in the dialog is saved in its new order", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    openItem(0);
    const stopsPanel = itemTab("Orders:StopsAndLegs");
    fireEvent.click(within(stopsPanel.querySelectorAll<HTMLElement>("[data-slot=stop-row]")[0]).getByRole("button", { name: "Orders:MoveDown" }));
    await click(within(dialog()!).getByText("Orders:Apply"));
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { lines: { stops: { relationId: string }[] }[] };
    expect(body.lines[0].stops.map((s) => s.relationId)).toEqual(["rel-2", "rel-1"]);
  });

  it("before Confirm the legs part says the legs are created on Confirm", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    openItem(0);
    expect(itemTab("Orders:StopsAndLegs").querySelector("[data-slot=legs-after-confirm]")).toHaveTextContent("Orders:LegsAfterConfirm");
    expect(calls(GET, "/api/planning/leg")).toHaveLength(0);
  });

  it("a read-only order still opens the item, without Apply and without editable fields", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(3);
    renderPage();
    await loaded();
    const edit = openItem(0);
    expect(Array.from(edit.querySelectorAll("[data-slot=ribbon-button]")).map((b) => b.textContent?.trim())).toEqual(["Close"]);
    expect(within(itemTab("Orders:FreightDetails")).getByLabelText(/^Orders:WeightKg/)).toBeDisabled();
  });
});

describe("OrderPage – pricing per item, order-level lines and totals", () => {
  it("the item's Pricing shows its own rate lines and total; Financial the order-level lines and the revenue overview", async () => {
    renderPage();
    await loaded();
    openItem(0);
    const pricing = itemTab("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("PL-NL")).toBeInTheDocument());
    expect(within(pricing).queryByText("Handling")).toBeNull();
    expect(within(pricing.querySelector<HTMLElement>("[data-slot=totals-block]")!).getByText("200.00")).toBeInTheDocument();
    await click(within(dialog()!).getByText("Orders:Apply"));

    const financial = openTab("Orders:Financial");
    /* The legacy blocks, in order. */
    expect(Array.from(financial.querySelectorAll("[data-slot=form-section]")).map((b) => b.getAttribute("aria-label"))).toEqual(["Orders:PriceCalculation", "Orders:RevenueOverview", "Orders:Invoicing", "Orders:InvoicingRemarksBlock", "Orders:OrderLevelLines"]);
    expect(within(financial.querySelector<HTMLElement>("[data-slot=order-level-rate-lines]")!).getByText("Handling")).toBeInTheDocument();
    expect(within(financial).getByText("Financial:SetFixedPrice")).toBeInTheDocument();
    /* Additional services are a tab of General now. */
    expect(within(financial).queryByText("Financial:AddAdditionalService")).toBeNull();

    const totals = section("Orders:RevenueOverview").querySelector<HTMLElement>("[data-slot=totals-block]")!;
    const value = (label: string) => within(totals).getByText(label).nextElementSibling?.textContent;
    expect(value("Orders:Lines")).toBe("200.00");
    expect(value("Orders:OrderLevelLines")).toBe("50.00");
    expect(value("Financial:Total")).toBe("250.00");
    expect(within(totals).getByText("EUR")).toBeInTheDocument();
  });

  it("Price calculation: per freight item clears the fixed price; fixed asks for the amount; Remarks for invoicing is saved with the header", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { financialTerms: { ...order(2).financialTerms, fixedPriceAmount: 900 } });
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await loaded();
    const block = section("Orders:PriceCalculation");
    const amount = within(block).getByLabelText(/^Orders:FixedPriceAmount/) as HTMLInputElement;
    expect(amount.value).toBe("900");
    expect(within(block).getByRole("radio", { name: "Orders:FixedPriceForOrder" })).toHaveAttribute("aria-checked", "true");
    fireEvent.click(within(block).getByRole("radio", { name: "Orders:PricedPerFreightItem" }));
    expect(amount.value).toBe("");
    expect(amount).toBeDisabled();
    fireEvent.click(within(block).getByRole("radio", { name: "Orders:FixedPriceForOrder" }));
    expect(amount).toBeEnabled();
    fireEvent.change(amount, { target: { value: "750" } });
    typeIn(section("Orders:InvoicingRemarksBlock"), /^Orders:InvoicingRemarks/, "Invoice with the CMR");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ invoicingRemarks: "Invoice with the CMR", financialTerms: expect.objectContaining({ fixedPriceAmount: 750 }) }));
  });

  it("Additional services (General): a stored order lists its service lines and adds one; a new order says it comes after the save", async () => {
    routes.GET["/api/financial/rate-line"] = () => ({ ...rateLines, lines: [...rateLines.lines, { id: "r9", orderId: "o1", kind: 5, quantity: 1, unitPrice: 30, amount: 30, additionalServiceName: "Tail lift", position: 3, concurrencyStamp: "r9s", isManual: true }] });
    renderPage();
    await loaded();
    const services = openNested("Financial:AdditionalServices");
    await waitFor(() => expect(within(services).getByText("Tail lift")).toBeInTheDocument());
    expect(within(services).queryByText("Handling")).toBeNull();
    fireEvent.click(within(services).getByText("Financial:AddAdditionalService"));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("a manual line added on the item's Pricing tab belongs to the item", async () => {
    routes.POST["/api/financial/rate-line/manual"] = () => ({ id: "r3" });
    renderPage();
    await loaded();
    openItem(0);
    fireEvent.click(within(itemTab("Orders:Pricing")).getByText("Financial:AddManualLine"));
    await waitFor(() => expect(document.querySelector("[role=dialog][data-variant=action]")).not.toBeNull());
    const action = document.querySelector<HTMLElement>("[role=dialog][data-variant=action]")!;
    fireEvent.change(within(action).getByLabelText(/^Financial:UnitPrice/), { target: { value: "15" } });
    await act(async () => {
      fireEvent.submit(action.querySelector("form")!);
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
    await waitFor(() => expect(within(section("Orders:RevenueOverview")).getAllByText("300.00")).toHaveLength(2));
    openTab("General");
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
    const lines = section("Orders:OrderLevelLines");
    await waitFor(() => expect(within(lines).getByText("Handling")).toBeInTheDocument());
    expect(lines.querySelector("[data-slot=order-level-rate-lines] [data-slot=mock-lines-grid]")).toHaveAttribute("data-readonly", "true");
  });
});

describe("OrderPage – one call for header and terms, and a save that stops part-way", () => {
  const changeReference = (value: string) => fireEvent.change(referenceField(), { target: { value } });
  const changePaymentTerm = (value: string) => typeIn(section("Orders:Invoicing"), /^Orders:PaymentTermDays/, value);
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

  it("an invalid term while General is open: Save opens Financial, marks its tab and shows the message", async () => {
    renderPage();
    await loaded();
    changePaymentTerm("x");
    openTab("General");
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(activeTab()).toBe("Orders:Financial");
    expect(tabTrigger("Orders:Financial")).toHaveClass("text-destructive");
    expect(tabTrigger("General")).not.toHaveClass("text-destructive");
    expect(within(panel("Orders:Financial")).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
  });

  it("the refusal of a save shows above the tab that is open", async () => {
    routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => fail("Terms refused.");
    renderPage();
    await loaded();
    changePaymentTerm("45");
    await click(ribbonButton("Save")!);
    expect(await within(panel("Orders:Financial")).findByText("Terms refused.")).toBeInTheDocument();
    expect(within(panel("General")).queryByText("Terms refused.")).toBeNull();
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

  it("a trip request warning is a header badge that opens the Tevens trip tab, with the warning in it", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(2, { tripRequest: request({ warningCode: "OpenTms.Orders:TripRequestNotHonoured", warningField: "TransportTripType", warningAt: "2026-10-25T09:00:00Z" }) });
    renderPage();
    await loaded();
    const badge = header().querySelector<HTMLElement>("[data-slot=trip-request-warning-badge]")!;
    expect(badge).toHaveTextContent("Orders:TripNotCreated");
    expect(activeTab()).toBe("General");
    fireEvent.click(badge);
    expect(activeTab()).toBe("Orders:TripRequestTab");
    expect(panel("Orders:TripRequestTab").querySelector("[data-slot=trip-request-warning]")).toHaveTextContent("OpenTms.Orders:TripRequestNotHonoured");
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

  it("a template without items cannot create orders yet; with one it can; a Schedule tab, no Documents, no Remarks, no revenue", async () => {
    routes.GET["/api/orders/transport-order-template/{id}"] = () => template();
    const { unmount } = renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("aria-disabled", "true");
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("title", "Orders:TemplateHasNoLines");
    expect(tabLabels()).toEqual(["General", "Orders:Financial", "Orders:TripRequestTab", "Orders:Schedule", "RecordIdentification"]);
    expect(within(openTab("Orders:Schedule")).getByLabelText(/^Orders:Interval/)).toHaveValue("1");
    /* The header: the name instead of the number and the date. */
    expect(within(section("Orders:TransportOrderTemplate")).queryByLabelText("Orders:Number")).toBeNull();
    expect(within(section("Orders:TransportOrderTemplate")).getByLabelText(/^Orders:Name/)).toHaveValue("Weekly Acme");
    expect(section("Orders:RevenueOverview").querySelector("[data-slot=revenue-after-save]")).toHaveTextContent("Orders:RevenueOnOrders");
    unmount();

    routes.GET["/api/orders/transport-order-template/{id}"] = () => template({ lines: [{ id: "tl1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 1, paymentInstruction: 0, stops: [] }] });
    renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    expect(ribbonButton("Orders:Materialise")).not.toHaveAttribute("aria-disabled");
  });

  it("the stop strip takes day offsets; the save posts the items with stops as day offsets and the trip's items by posted id", async () => {
    routes.GET["/api/orders/transport-order-template/{id}"] = () =>
      template({ lines: [{ id: "tl1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 1, paymentInstruction: 0, stops: [] }], tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalAddressId: "adr-2", departureDayOffset: 0, vehicleIds: [], driverIds: [], lineIds: null } });
    routes.PUT["/api/orders/transport-order-template/{id}"] = () => template({ concurrencyStamp: "tstamp-2" });
    routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => [{ id: `${String(init?.params?.path?.id)}-main`, addressTypeId: null, city: "X" }];
    lookupItems.current = { "*-entry-strip-stop-0-relation": [{ id: "rel-1", name: "Acme" }], "*-entry-strip-stop-1-relation": [{ id: "rel-2", name: "Beta" }] };
    renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    const entry = document.querySelector<HTMLElement>("[data-slot=freight-item-entry]")!;
    const entryField = (suffix: string) => entry.querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-entry-${suffix}']`)!;
    expect(entry.querySelector("[id$='-stop-0-date']")).toBeNull();
    /* The order type (its default waypoints) is there before the user types. */
    await waitFor(() => expect(entryField("strip-stop-1-activity").value).toBe("wp-unload"));
    fireEvent.change(entryField("strip-stop-0-relation"), { target: { value: "rel-1" } });
    fireEvent.change(entryField("strip-stop-1-relation"), { target: { value: "rel-2" } });
    fireEvent.change(entryField("strip-stop-0-dayOffset"), { target: { value: "1" } });
    fireEvent.change(entryField("strip-stop-1-dayOffset"), { target: { value: "1" } });
    fireEvent.change(entryField("unit"), { target: { value: "unit-1" } });
    await key(entryField("strip-stop-1-dayOffset"), { key: "Enter" });
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
