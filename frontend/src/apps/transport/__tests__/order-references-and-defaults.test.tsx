import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The order page's editable references and line measurements, and the customer change on a stored order (design 58,
 * build note 7): the references are a LinesGrid saved through PUT references (type and value required, the server's
 * lengths); the line's individual weighings and volumes (up to three of each) are saved with the lines; a customer
 * change asks "Take over the financial defaults of <customer>?": Take over reads the customer's settings
 * (by-relation) and sends them as the terms after the header, Cancel sends the current terms after the header (so a
 * server that copies the defaults on a customer change by itself is overruled). Risk class: Medium (UI flow over the
 * server's commands, which own the rules). */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } }),
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
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));


import { OrderPage } from "@/apps/transport/pages/OrderPage";
import { lookupItems, panel, ribbonButton, stubGlobals } from "./transport-test-mocks";
import { GET, PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const ORDER_GRANTS = { "Orders.TransportOrders": true, "Orders.TransportOrders.Update": true, "PlanningExecution.TransportTrips": true, "Financial.RateLines.Update": true };

const order = (status: number, extra: Record<string, unknown> = {}) => ({
  id: "o1",
  number: "TO-2026-00001",
  status,
  transportOrderTypeId: "type-1",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  concurrencyStamp: "stamp-1",
  defaultStops: [],
  references: [{ type: "PO", value: "4711", description: null }],
  lines: [
    {
      id: "l1",
      position: 1,
      transportUnitId: "unit-1",
      transportUnitName: "Pallet",
      quantity: 2,
      paymentInstruction: 0,
      measurements: [
        { kind: 1, position: 1, value: 410 },
        { kind: 2, position: 1, value: 1.2 },
      ],
      stops: [],
    },
  ],
  financialTerms: { currencyCode: "EUR", vatPercentageId: "vat-1", paymentTermDays: 30, priceListIds: ["pl-1"], multipleMatches: 1, discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 2 }], sendFreightDocumentsWithInvoice: false },
  ...extra,
});

/* The new customer's settings (design 57): two revenue lists out of position order, one discount valid on the order date and one expired. */
const DEFAULTS = {
  relationId: "rel-2",
  currencyCode: "USD",
  vatPercentageId: "vat-2",
  paymentTermDays: 14,
  revenueMultipleMatches: 2,
  revenuePriceLists: [
    { priceListId: "pl-b", position: 2 },
    { priceListId: "pl-a", position: 1 },
  ],
  discountsOrSurcharges: [
    { discountOrSurchargeId: "ds-valid", validFrom: "2026-01-01", validTill: null },
    { discountOrSurchargeId: "ds-expired", validFrom: "2025-01-01", validTill: "2025-12-31" },
  ],
  sendFreightDocumentsWithInvoice: true,
};

const CUSTOMERS = [
  { id: "rel-1", code: "ACME", name: "Acme", city: null, countryCode: null, roles: 1 },
  { id: "rel-2", code: "BETA", name: "Beta", city: null, countryCode: null, roles: 1 },
];

beforeAll(stubGlobals);
beforeEach(() => {
  grants = { ...ORDER_GRANTS };
  features = { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Trips": "true" };
  /* The customer lookup (its id carries the form's instance prefix) offers both relations. */
  lookupItems.current = new Proxy({} as Record<string, { id: string; name?: string }[]>, { get: (_, key) => (String(key).endsWith("-customer") ? CUSTOMERS : undefined) });
  resetClient();
  routes.GET["/api/orders/transport-order/{id}"] = () => order(2);
  routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 0, defaultWaypoints: [] });
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }];
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", name: "Pallet" }];
  routes.GET["/api/financial/relation-financial-settings/by-relation/{relationId}"] = () => DEFAULTS;
  routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerRelationId: "rel-2", concurrencyStamp: "stamp-2" });
  routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => order(2, { concurrencyStamp: "stamp-3" });
  routes.PUT["/api/orders/transport-order/{id}/references"] = () => order(2, { concurrencyStamp: "stamp-2" });
  routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <OrderPage id="o1" />
    </QueryClientProvider>,
  );
}
const loaded = () => screen.findByRole("heading", { level: 1 });
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const click = async (element: Element) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const referencesSection = () => within(panel("General")).getByText("Orders:References").closest("section, [data-slot=form-section]") as HTMLElement;
const referenceLines = () => Array.from(referencesSection().querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const pickCustomer = (id: string) => fireEvent.change(within(panel("General")).getByLabelText(/^Orders:Customer \*/), { target: { value: id } });

describe("Order references (design 58)", () => {
  it("shows the stored references, adds one and saves them through PUT references with the stamp, and nothing else", async () => {
    renderPage();
    await loaded();
    expect(referenceLines()).toHaveLength(1);
    expect((within(referenceLines()[0]).getByLabelText("Orders:ReferenceValue *") as HTMLInputElement).value).toBe("4711");
    fireEvent.click(within(referencesSection()).getByText("Orders:AddReference"));
    const added = referenceLines()[1];
    fireEvent.change(within(added).getByLabelText("Orders:ReferenceType *"), { target: { value: " CMR " } });
    fireEvent.change(within(added).getByLabelText("Orders:ReferenceValue *"), { target: { value: "C-9" } });
    fireEvent.change(within(added).getByLabelText("Orders:Description"), { target: { value: "Pallet note" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/references")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/references")).toEqual({
      params: { path: { id: "o1" } },
      body: {
        concurrencyStamp: "stamp-1",
        references: [
          { type: "PO", value: "4711", description: null },
          { type: "CMR", value: "C-9", description: "Pallet note" },
        ],
      },
    });
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(0);
    expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(0);
  });

  it("removes a reference, and refuses one without a value (General marked, nothing sent)", async () => {
    renderPage();
    await loaded();
    fireEvent.click(within(referencesSection()).getByText("Orders:AddReference"));
    fireEvent.change(within(referenceLines()[1]).getByLabelText("Orders:ReferenceType *"), { target: { value: "CMR" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(referencesSection()).getByText("Orders:ReferenceInvalid")).toBeInTheDocument();

    fireEvent.click(within(referenceLines()[1]).getByText("Remove"));
    fireEvent.click(within(referenceLines()[0]).getByText("Remove"));
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/references")).toHaveLength(1));
    expect((lastCall(PUT, "/api/orders/transport-order/{id}/references")!.body as { references: unknown[] }).references).toEqual([]);
  });
});

describe("Line measurements (design 58: up to three weighings and volumes per line)", () => {
  it("shows the stored ones in their slots and saves the edited ones with the lines", async () => {
    renderPage();
    await loaded();
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByText("Edit").length).toBeGreaterThan(0));
    fireEvent.click(within(lines).getAllByText("Edit")[0]);
    const detail = lines.querySelector("[data-slot=line-detail]") as HTMLElement;
    const weighing = (n: number) => within(detail).getAllByLabelText("Orders:WeightMeasurement")[n - 1] as HTMLInputElement;
    const volume = (n: number) => within(detail).getAllByLabelText("Orders:VolumeMeasurement")[n - 1] as HTMLInputElement;
    expect(weighing(1).value).toBe("410");
    expect(weighing(2).value).toBe("");
    expect(volume(1).value).toBe("1.2");

    fireEvent.change(weighing(2), { target: { value: "395,5" } });
    fireEvent.change(volume(1), { target: { value: "" } });
    fireEvent.change(volume(3), { target: { value: "0.8" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { lines: { measurements: unknown[] }[] };
    expect(body.lines[0].measurements).toEqual([
      { kind: 1, position: 1, value: 410 },
      { kind: 1, position: 2, value: 395.5 },
      { kind: 2, position: 3, value: 0.8 },
    ]);
  });

  it("refuses a measurement that is not a number (Lines marked, nothing sent)", async () => {
    renderPage();
    await loaded();
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByText("Edit").length).toBeGreaterThan(0));
    fireEvent.click(within(lines).getAllByText("Edit")[0]);
    const detail = lines.querySelector("[data-slot=line-detail]") as HTMLElement;
    fireEvent.change(within(detail).getAllByLabelText("Orders:WeightMeasurement")[0], { target: { value: "heavy" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(lines).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
  });
});

describe("Customer change on a stored order: take over the financial defaults? (design 58, build note 7)", () => {
  it("Take over: reads the new customer's settings and sends them as the terms after the header (lists by position, only the discounts valid on the order date)", async () => {
    renderPage();
    await loaded();
    pickCustomer("rel-2");
    const question = await screen.findByRole("alertdialog");
    expect(question).toHaveTextContent("Orders:TakeOverFinancialDefaultsQuestion".replace("{0}", "Beta"));
    await click(within(question).getByRole("button", { name: "Orders:TakeOver" }));
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/relation-financial-settings/by-relation/{relationId}", { params: { path: { relationId: "rel-2" } } }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ customerRelationId: "rel-2" }));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")).toEqual({
      params: { path: { id: "o1" } },
      body: {
        concurrencyStamp: "stamp-2",
        currencyCode: "USD",
        vatPercentageId: "vat-2",
        paymentTermDays: 14,
        priceListIds: ["pl-a", "pl-b"],
        multipleMatches: 2,
        discountsOrSurcharges: [{ discountOrSurchargeId: "ds-valid", source: 1 }],
        fixedPriceAmount: null,
        sendFreightDocumentsWithInvoice: true,
      },
    });
    const header = PUT.mock.calls.findIndex(([path]) => path === "/api/orders/transport-order/{id}");
    const terms = PUT.mock.calls.findIndex(([path]) => path === "/api/orders/transport-order/{id}/financial-terms");
    expect(header).toBeLessThan(terms);
  });

  it("Take over for a customer without settings: the module's default terms (EUR, no lists, no discounts)", async () => {
    routes.GET["/api/financial/relation-financial-settings/by-relation/{relationId}"] = () => ({ data: undefined, error: { error: { message: "not found" } }, response: new Response(null, { status: 404 }) });
    renderPage();
    await loaded();
    pickCustomer("rel-2");
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:TakeOver" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body).toEqual({
      concurrencyStamp: "stamp-2",
      currencyCode: "EUR",
      vatPercentageId: null,
      paymentTermDays: null,
      priceListIds: [],
      multipleMatches: 1,
      discountsOrSurcharges: [],
      fixedPriceAmount: null,
      sendFreightDocumentsWithInvoice: false,
    });
  });

  it("Cancel: keeps the current terms and sends them after the header, so the new customer's defaults are not copied", async () => {
    renderPage();
    await loaded();
    pickCustomer("rel-2");
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Cancel" }));
    expect(GET).not.toHaveBeenCalledWith("/api/financial/relation-financial-settings/by-relation/{relationId}", expect.anything());
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ customerRelationId: "rel-2" }));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body).toEqual({
      concurrencyStamp: "stamp-2",
      currencyCode: "EUR",
      vatPercentageId: "vat-1",
      paymentTermDays: 30,
      priceListIds: ["pl-1"],
      multipleMatches: 1,
      discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 2 }],
      fixedPriceAmount: null,
      sendFreightDocumentsWithInvoice: false,
    });
    expect(PUT.mock.calls.findIndex(([path]) => path === "/api/orders/transport-order/{id}")).toBeLessThan(PUT.mock.calls.findIndex(([path]) => path === "/api/orders/transport-order/{id}/financial-terms"));
  });

  it("no question and no terms command when the customer stays", async () => {
    renderPage();
    await loaded();
    fireEvent.change(within(panel("General")).getByLabelText(/^Orders:CustomerReference/), { target: { value: "REF-9" } });
    expect(screen.queryByRole("alertdialog")).toBeNull();
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1));
    expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(0);
  });
});
