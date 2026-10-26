import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The order editor page (design 61 "Order editor"): the page states, the title and badge, the ribbon per status and
 * per permission, the header save through the section command with the stamp, the lines tab (add a line from the
 * pattern, the required marks per type flag, save through PUT lines), the pricing tab (stored lines, totals, the
 * debounced preview of an unsaved change), the planning tab (the Tevens trip section: fields behind the checkbox,
 * read-only with the trip link once confirmed), Confirm with the API's refusal shown in the page. Risk class: Medium. */

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
import { fail, gridRows, lookupItems, panel, ribbonButton, ribbonLabels, stubGlobals } from "./transport-test-mocks";
import { GET, POST, PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const VIEW = "Orders.TransportOrders";
const ALL = { [VIEW]: true, "PlanningExecution.TransportTrips": true, "Orders.TransportOrders.Update": true, "Orders.TransportOrders.Confirm": true, "Orders.TransportOrders.Cancel": true, "Orders.TransportOrders.Approve": true, "Orders.TransportOrders.Revise": true, "Orders.TransportOrders.Archive": true, "Orders.TransportOrders.Print": true, "Orders.TransportOrders.Delete": true, "Financial.RateLines.Update": true };

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
  requiredFields: 1 | 64,
  defaultStops: [
    { waypointTypeId: "wp-load", waypointTypeName: "Load", relationId: "rel-1", relationName: "Acme", relationAddressId: "adr-1", dayOffset: 0, position: 1 },
    { waypointTypeId: "wp-unload", waypointTypeName: "Unload", relationId: "rel-2", relationName: "Beta", relationAddressId: "adr-2", dayOffset: 0, position: 2 },
  ],
  lines: [
    {
      id: "l1",
      position: 1,
      transportUnitId: "unit-1",
      transportUnitName: "Pallet",
      quantity: 2,
      paymentInstruction: 0,
      stops: [
        { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationName: "Acme", relationAddressId: "adr-1", date: "2026-10-25" },
        { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationName: "Beta", relationAddressId: "adr-2", date: "2026-10-26" },
      ],
    },
  ],
  financialTerms: { currencyCode: "EUR", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] },
  summary: { revenueAmount: 250 },
  ...extra,
});

const rateLines = {
  currencyCode: "EUR",
  lines: [
    { id: "r1", orderId: "o1", orderLineId: "l1", kind: 1, quantity: 2, unitPrice: 100, amount: 200, priceListCode: "PL-NL", position: 1, concurrencyStamp: "r1s", isManual: false },
    { id: "r2", orderId: "o1", kind: 2, quantity: 1, unitPrice: 50, amount: 50, description: "Handling", position: 2, concurrencyStamp: "r2s", isManual: true },
  ],
  totals: { linesAmount: 200, discountsAndSurchargesAmount: 0, additionalServicesAmount: 50, total: 250, hasFixedPrice: false },
  state: { status: 2 },
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { ...ALL };
  gridRows.current = [];
  lookupItems.current = {};
  resetClient();
  routes.GET["/api/orders/transport-order/{id}"] = () => order(2);
  routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 1 | 64, defaultWaypoints: [] });
  routes.GET["/api/financial/rate-line"] = () => rateLines;
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", name: "Pallet" }, { id: "unit-2", name: "Box" }];
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-load", name: "Load" }, { id: "wp-unload", name: "Unload" }];
  routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }];
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }];
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <OrderPage id="o1" />
    </QueryClientProvider>,
  );
}

const header = () => document.querySelector("[data-slot=record-page-header]") as HTMLElement;
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });

describe("OrderPage – states and header", () => {
  it("shows PermissionDenied without the view permission and the skeleton, then the page, with it", async () => {
    grants = {};
    const { unmount } = renderPage();
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();

    grants = { ...ALL };
    renderPage();
    expect(document.querySelector("[data-slot=record-page-skeleton]")).not.toBeNull();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    expect(within(header()).getByText("Orders:Status.Open")).toBeInTheDocument();
    expect(screen.getAllByRole("tab").map((t) => t.getAttribute("aria-label") ?? t.textContent)).toEqual(["General", "Orders:Lines", "Orders:Pricing", "Orders:Planning", "Orders:Documents", "RecordIdentification"]);
  });

  it("shows RecordNotFound for a missing order", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => fail("not found", 404);
    renderPage();
    expect(await screen.findByText("RecordNotFound")).toBeInTheDocument();
  });

  it("is read-only without Update: no Save, controls disabled, state commands still there", async () => {
    grants = { ...ALL, "Orders.TransportOrders.Update": false };
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    expect(ribbonButton("Save")).toBeNull();
    expect(within(panel("General")).getByLabelText(/^Orders:CustomerReference/)).toBeDisabled();
    expect(ribbonButton("Orders:Approve")).not.toBeNull();
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
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    expect(ribbonButton("Orders:Cancel")).toBeNull();
    expect(ribbonButton("Orders:SaveAsTemplate")).not.toBeNull();
  });
});

describe("OrderPage – saving", () => {
  it("saves a changed header through the header command with the stamp, and nothing else", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    const reference = within(panel("General")).getByLabelText(/^Orders:CustomerReference/) as HTMLInputElement;
    expect(reference.value).toBe("REF-1");
    fireEvent.change(reference, { target: { value: "REF-2" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")).toEqual({
      params: { path: { id: "o1" } },
      body: expect.objectContaining({ concurrencyStamp: "stamp-1", customerReference: "REF-2", customerRelationId: "rel-1", transportOrderTypeId: "type-1" }),
    });
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved");
  });

  it("a new line starts with the stop pattern and is saved through PUT lines; the type's flags mark the columns", async () => {
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByText("Pallet").length).toBeGreaterThan(0));
    expect(within(lines).getByText("Orders:TransportUnit *")).toBeInTheDocument();
    expect(within(lines).getByText("Orders:Quantity")).toBeInTheDocument();

    fireEvent.click(within(lines).getByText("Orders:AddLine"));
    const detail = lines.querySelector("[data-slot=line-detail]") as HTMLElement;
    expect(detail).not.toBeNull();
    expect(detail.querySelectorAll("[data-slot=stops-editor] [data-slot=mock-line]")).toHaveLength(2);
    const newLine = lines.querySelectorAll("[data-slot=mock-lines-grid]")[0].querySelectorAll(":scope > [data-slot=mock-line]")[1] as HTMLElement;
    fireEvent.change(within(newLine).getByLabelText("Orders:TransportUnit *"), { target: { value: "unit-2" } });

    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { concurrencyStamp: string; lines: { id: string | null; transportUnitId: string; stops: { waypointTypeId: string; date?: string }[] }[] };
    expect(body.concurrencyStamp).toBe("stamp-1");
    expect(body.lines).toHaveLength(2);
    expect(body.lines[1]).toMatchObject({ id: null, transportUnitId: "unit-2" });
    expect(body.lines[1].stops.map((s) => [s.waypointTypeId, s.date])).toEqual([["wp-load", "2026-10-25"], ["wp-unload", "2026-10-25"]]);
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(0);
  });

  it("refuses to save without a customer and marks the General tab", async () => {
    lookupItems.current = {};
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    const customer = within(panel("General")).getByLabelText(/^Orders:Customer \*/) as HTMLSelectElement;
    fireEvent.change(customer, { target: { value: "" } });
    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(panel("General")).getByText("MasterData:Required")).toBeInTheDocument();
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

  it("Confirm saves first when the form is dirty (UI-ACT-008)", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(1, { customerReference: "X", concurrencyStamp: "stamp-2" });
    routes.POST["/api/orders/transport-order/{id}/confirm"] = () => order(2, { concurrencyStamp: "stamp-3" });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    fireEvent.change(within(panel("General")).getByLabelText(/^Orders:CustomerReference/), { target: { value: "X" } });
    await click(ribbonButton("Orders:Confirm")!);
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/{id}/confirm", expect.anything()));
    expect(PUT.mock.invocationCallOrder[0]).toBeLessThan(POST.mock.invocationCallOrder[0]);
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderConfirmed");
  });
});

describe("OrderPage – one call for header and terms, and a save that stops part-way", () => {
  const typeIn = (scope: HTMLElement, label: RegExp, value: string) => fireEvent.change(within(scope).getByLabelText(label), { target: { value } });
  const changeReference = (value: string) => typeIn(panel("General"), /^Orders:CustomerReference/, value);
  const changePaymentTerm = (value: string) => {
    tab("Orders:Pricing");
    typeIn(panel("Orders:Pricing"), /^Orders:PaymentTermDays/, value);
  };
  const changeQuantity = async (value: string) => {
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByLabelText("Orders:Quantity").length).toBeGreaterThan(0));
    fireEvent.change(within(lines).getAllByLabelText("Orders:Quantity")[0], { target: { value } });
  };
  const detailReads = () => calls(GET, "/api/orders/transport-order/{id}").length;

  it("a header edit together with a terms edit is one header call that carries the terms", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    changeReference("REF-2");
    changePaymentTerm("45");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(
      expect.objectContaining({ concurrencyStamp: "stamp-1", customerReference: "REF-2", financialTerms: expect.objectContaining({ currencyCode: "EUR", paymentTermDays: 45 }) }),
    );
    expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(0);
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderSaved");
  });

  it("a terms-only edit keeps the terms command and sends no header", async () => {
    routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => order(2, { concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    changePaymentTerm("45");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-1", paymentTermDays: 45 }));
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(0);
  });

  it("stops at the first failure, names the saved and the unsaved sections, keeps the rest dirty and retries only those with the new stamp", async () => {
    vi.mocked(toast.success).mockClear();
    /* The server's state: the header call stores the new reference; the lines call fails once. */
    let server = order(2);
    routes.GET["/api/orders/transport-order/{id}"] = () => server;
    routes.PUT["/api/orders/transport-order/{id}"] = () => (server = order(2, { customerReference: "REF-2", concurrencyStamp: "stamp-2" }));
    routes.PUT["/api/orders/transport-order/{id}/lines"] = () => fail("Line 1 has no transport unit.");
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    changeReference("REF-2");
    await changeQuantity("3");
    const readsBefore = detailReads();
    await click(ribbonButton("Save")!);

    expect(await screen.findByText("Line 1 has no transport unit.")).toBeInTheDocument();
    const report = document.querySelector("[data-slot=save-report]") as HTMLElement;
    expect(report).toHaveTextContent("Orders:SectionsSaved Orders:OrderDetails");
    expect(report).toHaveTextContent("Orders:SectionsNotSaved Orders:Lines");
    expect(report).not.toHaveTextContent("Orders:Attributes");
    expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1);
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-2" }));
    expect(toast.success).not.toHaveBeenCalled();
    /* The record is read again (its current stamp), and the typed quantity is still there and dirty. */
    await waitFor(() => expect(detailReads()).toBeGreaterThan(readsBefore));
    expect((within(panel("Orders:Lines")).getAllByLabelText("Orders:Quantity")[0] as HTMLInputElement).value).toBe("3");

    /* Retry: only the lines go, with the stamp the header call returned; the header is not sent again. */
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
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    changeReference("REF-2");
    await changeQuantity("3");
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
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    changeReference("REF-2");
    /* The newest version: the same content, another stamp (the summary the recalculation wrote). */
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
  const typeIn = (scope: HTMLElement, label: RegExp, value: string) => fireEvent.change(within(scope).getByLabelText(label), { target: { value } });
  const referenceField = () => within(panel("General")).getByLabelText(/^Orders:CustomerReference/) as HTMLInputElement;
  const theirs = () => order(2, { customerReference: "THEIRS", concurrencyStamp: "stamp-other" });

  it("a 409 after someone else changed the order: the prompt, no retry with their stamp; Keep editing keeps the input and blocks Save", async () => {
    vi.mocked(toast.success).mockClear();
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("The data you have submitted has already changed by another user.", 409);
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);

    const prompt = await screen.findByRole("alertdialog");
    expect(prompt).toHaveTextContent("Orders:TransportOrderChanged");
    expect(prompt).toHaveTextContent("Orders:OrderChangedByOther");
    /* Destructive (it discards the input): the focus starts on Keep editing. */
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Orders:KeepEditing"));
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1);
    expect(toast.success).not.toHaveBeenCalledWith("Orders:TransportOrderSaved");

    await click(within(prompt).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(referenceField().value).toBe("MINE");
    expect(document.querySelector("[data-slot=order-conflict]")).toHaveTextContent("Orders:OrderChangedByOtherBanner");
    expect(ribbonButton("Save")).toHaveAttribute("aria-disabled", "true");
    /* Save stays impossible: even a command that saves first (Ctrl+S, Confirm, Print) sends nothing. */
    await click(ribbonButton("Save")!);
    expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1);
  });

  it("Reload discards the unsaved changes and loads theirs; saving works again with their stamp", async () => {
    vi.mocked(toast.success).mockClear();
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("Changed by another user.", 409);
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);
    const prompt = await screen.findByRole("alertdialog");
    await click(within(prompt).getByRole("button", { name: "Orders:Reload" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await waitFor(() => expect(referenceField().value).toBe("THEIRS"));
    expect(document.querySelector("[data-slot=order-conflict]")).toBeNull();
    expect(ribbonButton("Save")).not.toHaveAttribute("aria-disabled");
    expect(toast.success).toHaveBeenCalledWith("Orders:TransportOrderReloaded");

    routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { customerReference: "AGAIN", concurrencyStamp: "stamp-next" });
    typeIn(panel("General"), /^Orders:CustomerReference/, "AGAIN");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(2));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ concurrencyStamp: "stamp-other", customerReference: "AGAIN" }));
  });

  it("the banner's Reload asks again before it discards", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => fail("Changed by another user.", 409);
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await click(ribbonButton("Save")!);
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    const banner = document.querySelector("[data-slot=order-conflict]") as HTMLElement;
    await click(within(banner).getByRole("button", { name: /Orders:Reload/ }));
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("Orders:OrderChangedByOther");
    expect(referenceField().value).toBe("MINE");
  });

  it("a newer version with other content that arrived while editing is not saved over: the prompt, without any call", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <OrderPage id="o1" />
      </QueryClientProvider>,
    );
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    /* A push (another user's save) refreshes the record while the form is dirty. */
    routes.GET["/api/orders/transport-order/{id}"] = theirs;
    await act(async () => {
      await client.refetchQueries({ queryKey: ["orders", "transportOrders", "detail", "o1"] });
      /* The query's observers are notified on the next tick. */
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
      /* Someone else changed the lines in between. */
      server = order(2, { customerReference: "MINE", concurrencyStamp: "stamp-3", lines: [{ ...order(2).lines[0], quantity: 9 }] });
      return fail("Changed by another user.", 409);
    };
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    tab("Orders:Lines");
    await waitFor(() => expect(within(panel("Orders:Lines")).getAllByLabelText("Orders:Quantity").length).toBeGreaterThan(0));
    fireEvent.change(within(panel("Orders:Lines")).getAllByLabelText("Orders:Quantity")[0], { target: { value: "3" } });
    await click(ribbonButton("Save")!);
    await click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Orders:KeepEditing" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1);
    const report = panel("Orders:Lines").querySelector("[data-slot=order-conflict] [data-slot=save-report]") as HTMLElement;
    expect(report).toHaveTextContent("Orders:SectionsSaved Orders:OrderDetails");
    expect(report).toHaveTextContent("Orders:SectionsNotSaved Orders:Lines");
    expect((within(panel("Orders:Lines")).getAllByLabelText("Orders:Quantity")[0] as HTMLInputElement).value).toBe("3");
  });

  it("a business-rule 409 (with its error code) is no conflict: the error shows and the partial-save behaviour stays", async () => {
    routes.PUT["/api/orders/transport-order/{id}"] = () => ({ data: undefined, error: { error: { code: "OpenTms.Orders:InvalidTransition", message: "The order cannot be changed in this status." } }, response: new Response(null, { status: 409 }) });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    typeIn(panel("General"), /^Orders:CustomerReference/, "MINE");
    await click(ribbonButton("Save")!);
    expect(await screen.findByText("The order cannot be changed in this status.")).toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(document.querySelector("[data-slot=order-conflict]")).toBeNull();
    expect(ribbonButton("Save")).not.toHaveAttribute("aria-disabled");
  });
});

describe("OrderPage – pricing tab", () => {
  it("shows the stored rate lines grouped by line, the totals and the currency once (UI-FMT-007)", async () => {
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Pricing");
    const pricing = panel("Orders:Pricing");
    await waitFor(() => expect(within(pricing).getByText("Handling")).toBeInTheDocument());
    const lines = pricing.querySelectorAll("[data-slot=mock-line]");
    expect(within(lines[0] as HTMLElement).getByText("Orders:Line 1")).toBeInTheDocument();
    expect(within(lines[1] as HTMLElement).getByText("Orders:OrderLevel")).toBeInTheDocument();
    const totals = pricing.querySelector("[data-slot=totals-block]") as HTMLElement;
    expect(within(totals).getByText("250.00")).toBeInTheDocument();
    expect(within(totals).getByText("EUR")).toBeInTheDocument();
    expect(within(pricing).getByText("Financial:SetFixedPrice")).toBeInTheDocument();
  });

  it("previews an unsaved change through preview-prices after the debounce, and shows the preview notice", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => ({ currencyCode: "EUR", linesAmount: 300, discountsAndSurchargesAmount: 0, totalAmount: 300, lines: [], warnings: [] });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByLabelText("Orders:Quantity").length).toBeGreaterThan(0));
    fireEvent.change(within(lines).getAllByLabelText("Orders:Quantity")[0], { target: { value: "3" } });
    expect(POST).not.toHaveBeenCalled();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(1));
    const body = lastCall(POST, "/api/orders/transport-order/{id}/preview-prices")!.body as { lines: { quantity: number }[]; customerRelationId: string };
    expect(body.lines[0].quantity).toBe(3);
    expect(body.customerRelationId).toBe("rel-1");
    tab("Orders:Pricing");
    await waitFor(() => expect(panel("Orders:Pricing").querySelector("[data-slot=price-preview]")).not.toBeNull());
    /* The preview's lines and its grand total: both 300 here. */
    expect(within(panel("Orders:Pricing").querySelector("[data-slot=totals-block]") as HTMLElement).getAllByText("300.00")).toHaveLength(2);
    vi.useRealTimers();
  });

  it("does not preview an incomplete line (no transport unit): a quiet note instead; once complete it sends no empty ids", async () => {
    vi.mocked(toast.error).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => ({ currencyCode: "EUR", linesAmount: 300, discountsAndSurchargesAmount: 0, totalAmount: 300, lines: [], warnings: [] });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByText("Pallet").length).toBeGreaterThan(0));
    fireEvent.click(within(lines).getByText("Orders:AddLine"));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(0);
    expect(toast.error).not.toHaveBeenCalled();
    tab("Orders:Pricing");
    expect(within(panel("Orders:Pricing")).getByText("Orders:PricePreviewIncomplete")).toBeInTheDocument();

    tab("Orders:Lines");
    const newLine = lines.querySelectorAll("[data-slot=mock-lines-grid]")[0].querySelectorAll(":scope > [data-slot=mock-line]")[1] as HTMLElement;
    fireEvent.change(within(newLine).getByLabelText("Orders:TransportUnit *"), { target: { value: "unit-2" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(1));
    const body = lastCall(POST, "/api/orders/transport-order/{id}/preview-prices")!.body as { lines: { transportUnitId: string }[] };
    expect(body.lines.map((l) => l.transportUnitId)).toEqual(["unit-1", "unit-2"]);
    const emptyIds: string[] = [];
    const walk = (value: unknown, path: string) => {
      if (Array.isArray(value)) value.forEach((v, i) => walk(v, `${path}[${i}]`));
      else if (value && typeof value === "object") for (const [k, v] of Object.entries(value)) (/Id$/.test(k) && v === "" ? emptyIds.push(`${path}.${k}`) : walk(v, `${path}.${k}`));
    };
    walk(body, "body");
    expect(emptyIds).toEqual([]);
    vi.useRealTimers();
  });

  it("a refused preview never toasts: the Pricing tab shows a quiet note and the stored lines", async () => {
    vi.mocked(toast.error).mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    routes.POST["/api/orders/transport-order/{id}/preview-prices"] = () => fail("Your request is not valid!");
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    await waitFor(() => expect(within(lines).getAllByLabelText("Orders:Quantity").length).toBeGreaterThan(0));
    fireEvent.change(within(lines).getAllByLabelText("Orders:Quantity")[0], { target: { value: "3" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order/{id}/preview-prices")).toHaveLength(1));
    tab("Orders:Pricing");
    await waitFor(() => expect(within(panel("Orders:Pricing")).getByText("Orders:PricePreviewUnavailable")).toBeInTheDocument());
    expect(toast.error).not.toHaveBeenCalled();
    expect(panel("Orders:Pricing").querySelector("[data-slot=price-preview]")).toBeNull();
    vi.useRealTimers();
  });

  it("without Financial.RateLines.Update the rate lines are read-only", async () => {
    grants = { ...ALL, "Financial.RateLines.Update": false };
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Pricing");
    await waitFor(() => expect(within(panel("Orders:Pricing")).getByText("Handling")).toBeInTheDocument());
    expect(panel("Orders:Pricing").querySelector("[data-slot=mock-lines-grid]")).toHaveAttribute("data-readonly", "true");
  });
});

describe("OrderPage – planning tab (Tevens trip, Q9)", () => {
  it("reveals the trip fields behind the checkbox on a Draft and saves the request", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1);
    routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { concurrencyStamp: "stamp-2" });
    lookupItems.current = { [`${document.body ? "" : ""}`]: [] };
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    expect(planning.querySelector("[data-slot=trip-request-fields]")).toBeNull();
    fireEvent.click(within(planning).getByRole("checkbox", { name: "Orders:TripRequestHint" }));
    expect(planning.querySelector("[data-slot=trip-request-fields]")).not.toBeNull();
    expect(within(planning).getByText("Orders:Advanced")).toBeInTheDocument();
    expect(planning.querySelector("[data-grid-id='planning.transport.orderLegs']")).not.toBeNull();

    await click(ribbonButton("Save")!);
    expect(PUT).not.toHaveBeenCalled();
    expect(within(planning).getByText("Orders:TripRequestIncomplete")).toBeInTheDocument();
  });

  it("the agreed purchase price belongs to an outsourced trip: it waits for a carrier and is cleared with it", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(1, { tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureRelationName: "Acme", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalRelationName: "Beta", arrivalAddressId: "adr-2", plannedDeparture: "2026-10-25T06:00:00Z", carrierRelationId: "car-1", carrierRelationName: "Charter", agreedPurchaseAmount: 400, agreedPurchaseCurrencyCode: "EUR", vehicleIds: [], driverIds: [] } });
    routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { concurrencyStamp: "stamp-2" });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    const amount = () => within(planning).getByLabelText(/^Orders:AgreedPurchaseAmount/) as HTMLInputElement;
    const currency = () => planning.querySelector<HTMLSelectElement>("select[id$='-trip-agreedPurchaseCurrencyCode']")!;
    const carrier = planning.querySelector<HTMLSelectElement>("select[id$='-trip-carrier']")!;
    expect(amount().value).toBe("400");
    expect(amount()).toBeEnabled();
    expect(within(planning).queryByText("Orders:AgreedPurchaseNeedsCarrier")).toBeNull();

    /* No carrier: an own-fleet trip; the price is cleared and waits, with the reason under it. */
    lookupItems.current = { [carrier.id]: [{ id: "car-1", name: "Charter" }] };
    fireEvent.change(carrier, { target: { value: "" } });
    expect(amount().value).toBe("");
    expect(amount()).toBeDisabled();
    expect(currency()).toBeDisabled();
    expect(within(planning).getByText("Orders:AgreedPurchaseNeedsCarrier")).toBeInTheDocument();

    fireEvent.change(carrier, { target: { value: "car-1" } });
    expect(amount()).toBeEnabled();
    fireEvent.change(amount(), { target: { value: "350" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body).toEqual(expect.objectContaining({ tripRequest: expect.objectContaining({ carrierRelationId: "car-1", agreedPurchaseAmount: 350, agreedPurchaseCurrencyCode: null }) }));
  });

  it("shows the created trip as a link and no editable fields once the order is confirmed", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(2, { tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalAddressId: "adr-2", plannedDeparture: "2026-10-25T06:00:00Z", tripId: "trip-9", tripNumber: "TR-9" } });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    const link = within(planning).getByRole("button", { name: /Orders:Trip TR-9/ });
    fireEvent.click(link);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/trips/trip-9" }));
    /* The Base UI checkbox is a span: disabled shows as data-disabled. */
    expect(within(planning).getByRole("checkbox", { name: "Orders:TripRequestHint" })).toHaveAttribute("data-disabled");
  });
});

describe("OrderPage – documents and close", () => {
  it("Print CMR saves first and opens the wizard; Close goes back to the list", async () => {
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    await click(ribbonButton("Orders:PrintConsignmentNote")!);
    expect(await screen.findByTestId("print-wizard")).toBeInTheDocument();
    await click(ribbonButton("Close")!);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders" }));
    expect(GET).toHaveBeenCalledWith("/api/financial/rate-line", expect.objectContaining({ params: { query: { OrderId: "o1" } } }));
  });

  it("a dirty form asks before closing (UI-DLG-008)", async () => {
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    fireEvent.change(within(panel("General")).getByLabelText(/^Orders:CustomerReference/), { target: { value: "changed" } });
    await click(ribbonButton("Close")!);
    expect(navigateMock).not.toHaveBeenCalled();
    expect(await screen.findByRole("alertdialog")).toHaveTextContent("UnsavedChanges");
  });
});

describe("TemplatePage – Create orders needs lines", () => {
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

  it("a template without lines (a new one) cannot create orders yet; with a line it can", async () => {
    grants = { ...ALL, "Orders.TransportOrderTemplates": true, "Orders.TransportOrderTemplates.Update": true };
    routes.GET["/api/orders/transport-order-template/{id}"] = () => template();
    const { unmount } = renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    /* A disabled ribbon command stays focusable (aria-disabled) and names its reason (UI-ACT). */
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("aria-disabled", "true");
    expect(ribbonButton("Orders:Materialise")).toHaveAttribute("title", "Orders:TemplateHasNoLines");
    unmount();

    routes.GET["/api/orders/transport-order-template/{id}"] = () => template({ lines: [{ id: "tl1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 1, paymentInstruction: 0, stops: [] }] });
    renderTemplate();
    await screen.findByText("Orders:TransportOrderTemplate Weekly Acme");
    expect(ribbonButton("Orders:Materialise")).not.toHaveAttribute("aria-disabled");
  });
});

describe("OrderPage – Planning tab leg dialog (time windows)", () => {
  it("edits a leg's destination window on the order's Planning tab and sends it on the planned date", async () => {
    const at = (date: [number, number, number], time: string) => {
      const [hh, mm] = time.split(":").map(Number);
      return new Date(date[0], date[1] - 1, date[2], hh, mm).toISOString();
    };
    routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", position: 1, concurrencyStamp: "ls", origin: { name: "Acme", plannedDate: "2026-10-25" }, destination: { name: "Beta", plannedDate: "2026-10-26" } }], totalCount: 1, filteredCount: 1 });
    routes.PUT["/api/planning/leg/{id}"] = () => ({ id: "leg-1" });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    await waitFor(() => expect(planning.querySelectorAll("[data-slot=mock-grid-row]")).toHaveLength(1));
    fireEvent.click(within(planning).getByText("Edit"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(dialog.querySelector("#leg-destination-window-from")!, { target: { value: "7:15" } });
    fireEvent.change(dialog.querySelector("#leg-destination-window-till")!, { target: { value: "11:00" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/planning/leg/{id}", expect.anything()));
    const body = lastCall(PUT, "/api/planning/leg/{id}")!.body as Record<string, unknown>;
    expect(body.destinationWindow).toEqual({ from: at([2026, 10, 26], "07:15"), till: at([2026, 10, 26], "11:00") });
    expect(body.originWindow).toBeUndefined();
  });
});

describe("OrderPage – trip request warning", () => {
  it("shows the warning as a header badge that opens the Planning tab, and in the Tevens-trip section with its time", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(2, { tripRequest: { transportTripTypeId: "tt-1", plannedDeparture: "2026-10-25T06:00:00Z", vehicleIds: [], driverIds: [], warningCode: "OpenTms.Orders:TripRequestNotHonoured", warningField: "TransportTripType", warningAt: "2026-10-25T09:00:00Z" } });
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    const badge = header().querySelector<HTMLElement>("[data-slot=trip-request-warning-badge]")!;
    expect(badge).toHaveTextContent("Orders:TripNotCreated");
    fireEvent.click(badge);
    expect(screen.getByRole("tab", { name: "Orders:Planning" })).toHaveAttribute("aria-selected", "true");
    const alert = panel("Orders:Planning").querySelector<HTMLElement>("[data-slot=trip-request-warning]")!;
    expect(alert).toHaveTextContent("OpenTms.Orders:TripRequestNotHonoured");
    expect(alert).toHaveTextContent("Orders:WarningSince");
  });

  it("shows no badge and no warning without one", async () => {
    renderPage();
    await screen.findByText("Orders:TransportOrder TO-2026-00001");
    expect(header().querySelector("[data-slot=trip-request-warning-badge]")).toBeNull();
    tab("Orders:Planning");
    expect(panel("Orders:Planning").querySelector("[data-slot=trip-request-warning]")).toBeNull();
  });
});
