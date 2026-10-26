import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Orders list (design 61 "Orders list"): the view permission and feature gates, the grid id and the designed
 * columns (hidden ones after the visible), the URL filter mapped onto the API's view (a date range for All), the
 * status filter, the selection-aware command bar (New per type with Create; Copy / Print / Approve / Cancel / Delete
 * per permission; disabled without a selection), the row actions, opening a record through useOpenRecord, and the
 * bulk delete through the API. Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders", search: { filter: "today" } }) }));
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
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/tree-select", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/tree-select")>()), TreeSelect: (await import("./transport-test-mocks")).MockTreeSelect }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/apps/transport/wizards/PrintCmrWizard", () => ({ PrintCmrWizard: ({ open }: { open: boolean }) => (open ? <div data-testid="print-wizard" /> : null) }));
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: ({ open, source }: { open: boolean; source?: { kind: string } }) => (open ? <div data-testid="materialise-wizard" data-kind={source?.kind ?? "template"} /> : null) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { OrdersView } from "@/apps/transport/views/OrdersView";
import { toast } from "sonner";
import { barButton, exportRequest, gridRows, stubGlobals } from "./transport-test-mocks";
import { GET, POST, lastCall, resetClient, routes } from "./test-setup-mocks";

const ALL_ON = { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Orders.Templates": "true" };
const VIEW = "Orders.TransportOrders";
const rows = [
  { id: "o1", number: "TO-1", status: 2, orderDate: "2026-10-25", customerName: "Acme", lineCount: 1, revenueAmount: 100, executionProgress: 1 },
  { id: "o2", number: null, status: 1, orderDate: "2026-10-25", customerName: "Beta", lineCount: 0, revenueAmount: null, executionProgress: 1 },
];

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { [VIEW]: true };
  features = { ...ALL_ON };
  gridRows.current = rows;
  resetClient();
  routes.GET["/api/orders/transport-order"] = () => ({ rows, totalCount: 2, filteredCount: 2 });
  routes.GET["/api/orders/transport-order-type"] = () => ({ rows: [{ id: "type-1", name: "Standard", isDefault: true }, { id: "type-2", name: "Express", isDefault: false }], totalCount: 2, filteredCount: 2 });
});

function renderView(filter = "today") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <OrdersView filter={filter} />
    </QueryClientProvider>,
  );
}

const columns = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span")).map((el) => `${el.getAttribute("data-column")}${el.getAttribute("data-hidden") === "true" ? " (hidden)" : ""}`);
const commandBar = () => screen.getByRole("button", { name: /Orders:NewTransportOrder|Delete/ }).closest("div.flex.h-10") as HTMLElement;

describe("OrdersView – gates (UI-PAGE-005, UI-PERM-006)", () => {
  it("shows PermissionDenied without the view permission and fetches nothing", () => {
    grants = {};
    renderView();
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(GET).not.toHaveBeenCalledWith("/api/orders/transport-order", expect.anything());
  });

  it("shows FeatureUnavailable when the Orders node is off", () => {
    features = { ...ALL_ON, "Apps.Transport.Orders": "false" };
    renderView();
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });
});

describe("OrdersView – grid", () => {
  it("has the designed columns, the hidden ones after the visible, and the grid id", async () => {
    renderView();
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("orders.transport.transportOrders");
    expect(columns()).toEqual([
      "number", "status", "orderDate", "customerName", "customerReference", "lineCount", "firstStop", "firstStopDate", "lastStop", "lastStopDate",
      "revenueAmount", "costOfRevenueAmount", "marginAmount", "co2Kg", "tripNumbers", "executionProgress", "tripRequestWarningCode",
      "transportOrderTypeName (hidden)", "organizationUnitId (hidden)", "currencyCode (hidden)", "stopCount (hidden)", "source (hidden)",
    ]);
    const first = document.querySelectorAll("[data-slot=mock-grid-row]")[0];
    expect(within(first as HTMLElement).getByText("Orders:Status.Open")).toBeInTheDocument();
    expect(within(first as HTMLElement).getByText("Orders:ExecutionProgress.Unplanned")).toBeInTheDocument();
  });

  it.each([
    ["today", 1],
    ["tomorrow", 2],
    ["this-week", 3],
    ["open", 4],
    ["drafts", 5],
    ["all", 0],
  ])("maps the URL filter %s onto the API view %i", async (filter, view) => {
    renderView(filter);
    await waitFor(() => expect(lastCall(GET, "/api/orders/transport-order")?.params?.query?.View).toBe(view));
  });

  it("offers the date range for All only and sends it; the status filter sends Status", async () => {
    const { unmount } = renderView("today");
    await screen.findByTestId("mock-grid");
    expect(document.getElementById("orders-date-from-filter")).toBeNull();
    unmount();

    renderView("all");
    await screen.findByTestId("mock-grid");
    fireEvent.change(document.getElementById("orders-date-from-filter")!, { target: { value: "2026-10-01" } });
    await waitFor(() => expect(lastCall(GET, "/api/orders/transport-order")?.params?.query?.DateFrom).toBe("2026-10-01"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Orders:TransportOrderStatus.Draft" }));
    await waitFor(() => expect(lastCall(GET, "/api/orders/transport-order")?.params?.query?.Status).toEqual([1]));
  });

  it("double click opens the record through useOpenRecord, keeping the search", async () => {
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getAllByText("open-row")[0]);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o1" }));
  });
});

describe("OrdersView – command bar (UI-ACT-001, UI-PERM-002)", () => {
  it("shows only New and no selection commands without the action permissions", async () => {
    grants = { [VIEW]: true, "Orders.TransportOrders.Create": true };
    renderView();
    await screen.findByTestId("mock-grid");
    const bar = commandBar();
    expect(within(bar).getByText("Orders:NewTransportOrder")).toBeInTheDocument();
    expect(within(bar).queryByText("Orders:Approve")).toBeNull();
    expect(within(bar).queryByText("Delete")).toBeNull();
    expect(within(bar).getByText("Orders:CopyOrder").closest("button")).toHaveAttribute("aria-disabled", "true");
  });

  it("hides New without Create and enables the selection commands once a row is selected", async () => {
    grants = { [VIEW]: true, "Orders.TransportOrders.Approve": true, "Orders.TransportOrders.Print": true, "Orders.TransportOrders.Delete": true };
    renderView();
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("Orders:NewTransportOrder")).toBeNull();
    expect(barButton("Orders:Approve")).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(screen.getByText("select-first"));
    expect(barButton("Orders:Approve")).not.toHaveAttribute("aria-disabled");
    expect(barButton("Orders:PrintConsignmentNote")).not.toHaveAttribute("aria-disabled");
  });

  it("New per type: the dropdown lists every type when there is more than one; New from template needs the templates permission", async () => {
    grants = { [VIEW]: true, "Orders.TransportOrders.Create": true, "Orders.TransportOrderTemplates": true };
    renderView();
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(barButton("Express")).toBeInTheDocument());
    expect(barButton("Standard")).toBeInTheDocument();
    fireEvent.click(barButton("Orders:NewFromTemplate"));
    expect(screen.getByTestId("materialise-wizard")).toHaveAttribute("data-kind", "template");
  });

  it("deletes the selection through bulk-delete and refreshes", async () => {
    grants = { [VIEW]: true, "Orders.TransportOrders.Delete": true };
    routes.POST["/api/orders/transport-order/bulk-delete"] = () => ({ deletedCount: 2, skippedRows: [] });
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("select-all"));
    fireEvent.click(barButton("Orders:DeleteN"));
    const confirm = await screen.findByRole("alertdialog");
    await act(async () => {
      fireEvent.click(within(confirm).getByRole("button", { name: "Delete" }));
      await Promise.resolve();
    });
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/bulk-delete", expect.objectContaining({ body: expect.objectContaining({ selection: expect.objectContaining({ mode: "Explicit", explicitIds: ["o1", "o2"] }), view: 1 }) })));
    expect(toast.success).toHaveBeenCalledWith("Orders:OrdersDeleted");
  });

  it("row actions: Print opens the wizard for the row, Cancel is disabled for an order that is not Draft or Open", async () => {
    grants = { [VIEW]: true, "Orders.TransportOrders.Print": true, "Orders.TransportOrders.Cancel": true };
    gridRows.current = [{ ...rows[0], status: 4 }];
    routes.GET["/api/orders/transport-order"] = () => ({ rows: gridRows.current, totalCount: 1, filteredCount: 1 });
    routes.GET["/api/orders/transport-order/{id}"] = () => ({ ...rows[0], lines: [{ id: "l1", position: 1 }] });
    renderView();
    await screen.findByTestId("mock-grid");
    const row = document.querySelector("[data-slot=mock-grid-row]") as HTMLElement;
    expect(within(row).getByText("Orders:Cancel")).toBeDisabled();
    fireEvent.click(within(row).getByText("Orders:PrintConsignmentNote"));
    await screen.findByTestId("print-wizard");
  });
});

describe("OrdersView – trip request warning", () => {
  it("marks a row whose trip request Planning did not honour with a warning icon, and only that row", async () => {
    const warned = [{ ...rows[0], tripRequestWarningCode: "OpenTms.Orders:TripRequestNotHonoured" }, rows[1]];
    routes.GET["/api/orders/transport-order"] = () => ({ rows: warned, totalCount: 2, filteredCount: 2 });
    renderView();
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(document.querySelectorAll("[data-slot=trip-request-warning-cell]")).toHaveLength(1));
    const [first, second] = Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-grid-row]"));
    expect(within(first).getByRole("img", { name: "Orders:TripNotCreated" })).toBeInTheDocument();
    expect(within(second).queryByRole("img", { name: "Orders:TripNotCreated" })).toBeNull();
  });
});

describe("OrdersView – export (UI-GRID-013)", () => {
  const xlsx = () => ({ data: new Blob(["xlsx"]), error: undefined, response: new Response(null, { headers: { "Content-Disposition": 'attachment; filename="transport-orders-2026-10-26.xlsx"', "Content-Type": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }) });
  let createObjectURL: ReturnType<typeof vi.fn>;
  let downloads: string[];
  let anchorClick: { mockRestore: () => void } | undefined;
  afterEach(() => anchorClick?.mockRestore());
  beforeEach(() => {
    createObjectURL = vi.fn(() => "blob:x");
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL: vi.fn() });
    downloads = [];
    anchorClick = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(this.download);
    });
    exportRequest.search = "";
    exportRequest.sort = null;
  });

  it("sends the list's request, the visible columns with their headers (CO2 included) and saves the server's file", async () => {
    routes.POST["/api/orders/transport-order/export"] = xlsx;
    exportRequest.search = "acme";
    exportRequest.sort = { colId: "orderDate", sort: "desc" };
    renderView("all");
    await screen.findByTestId("mock-grid");
    fireEvent.change(document.getElementById("orders-date-from-filter")!, { target: { value: "2026-10-01" } });
    fireEvent.click(screen.getByText("grid-export"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Grid:ExportTitle")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole("button", { name: "Grid:Export" }));
      await Promise.resolve();
    });
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/export", expect.objectContaining({ parseAs: "blob" })));
    const body = lastCall(POST, "/api/orders/transport-order/export")?.body as { columns: { field: string; header: string }[]; allRows: boolean; view: number; dateFrom: string; wildcardSearch: string; sortBy: string; sortDir: string };
    expect(body).toMatchObject({ view: 0, dateFrom: "2026-10-01", wildcardSearch: "acme", sortBy: "orderDate", sortDir: "desc", allRows: false });
    expect(body.columns.map((c) => c.field)).toEqual(["number", "status", "orderDate", "customerName", "customerReference", "lineCount", "firstStop", "firstStopDate", "lastStop", "lastStopDate", "revenueAmount", "costOfRevenueAmount", "marginAmount", "co2Kg", "tripNumbers", "executionProgress"]);
    expect(body.columns[0]).toEqual({ field: "number", header: "Orders:Number" });
    await waitFor(() => expect(downloads).toEqual(["transport-orders-2026-10-26.xlsx"]));
    expect(createObjectURL).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith("Grid:ExportDownloaded");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("sends all columns and all rows when chosen", async () => {
    routes.POST["/api/orders/transport-order/export"] = xlsx;
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("grid-export"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("radio", { name: /^Grid:ExportAllColumns/ }));
    fireEvent.click(within(dialog).getByRole("radio", { name: /^Grid:ExportAllRows/ }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Grid:Export" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order/export", expect.anything()));
    const body = lastCall(POST, "/api/orders/transport-order/export")?.body as { columns: { field: string }[]; allRows: boolean };
    expect(body.allRows).toBe(true);
    expect(body.columns.map((c) => c.field)).toContain("source");
    expect(body.columns.map((c) => c.field)).toContain("co2Kg");
    expect(body.columns.map((c) => c.field)).not.toContain("tripRequestWarningCode");
  });

  it("shows ExportTooManyRows inside the dialog, which stays open, and downloads nothing", async () => {
    routes.POST["/api/orders/transport-order/export"] = () => ({ data: undefined, error: { error: { code: "OpenTms.Orders:ExportTooManyRows", message: "The export would hold 60000 rows; at most 50000 are allowed. Filter the list first." } }, response: new Response(null, { status: 400 }) });
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("grid-export"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Grid:Export" }));
    expect(await within(dialog).findByText(/at most 50000 are allowed/)).toBeInTheDocument();
    expect(downloads).toEqual([]);
    expect(toast.error).not.toHaveBeenCalled();
  });
});
