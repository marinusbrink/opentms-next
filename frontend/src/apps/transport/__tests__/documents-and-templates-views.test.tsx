import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Documents view (the tenant's printed CMRs with download) and the Templates list (design 61): the gates, the
 * designed columns, the filters sent to the API, the download through the authenticated client and the templates'
 * command bar. Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/documents", search: {} }) }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/opentms-grid-lazy", async () => ({ LazyOpenTmsGrid: (await import("./transport-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: ({ open, source }: { open: boolean; source?: { id: string } }) => (open ? <div data-testid="materialise-wizard" data-source={source?.id} /> : null) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { DocumentsView } from "@/apps/transport/views/DocumentsView";
import { TemplatesView } from "@/apps/transport/views/TemplatesView";
import { gridRows, stubGlobals } from "./transport-test-mocks";
import { GET, lastCall, resetClient, routes } from "./test-setup-mocks";

const FEATURES = { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Orders.Templates": "true", "Apps.Transport.Documents": "true" };

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  features = { ...FEATURES };
  gridRows.current = [];
  resetClient();
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const columns = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span")).map((el) => `${el.getAttribute("data-column")}${el.getAttribute("data-hidden") === "true" ? " (hidden)" : ""}`);

describe("DocumentsView", () => {
  const documents = [{ id: "d1", number: "CMR-2026-000001", printedAt: "2026-10-25T10:00:00Z", orderId: "o1", orderNumber: "TO-1", lineIds: ["l1", "l2"], profileName: "Standard", fileName: "cmr.pdf", sizeBytes: 2048 }];

  it("gates on Orders.TransportOrders and the Documents feature", () => {
    grants = {};
    const { unmount } = renderWith(<DocumentsView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();
    grants = { "Orders.TransportOrders": true };
    features = { ...FEATURES, "Apps.Transport.Documents": "false" };
    renderWith(<DocumentsView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });

  it("has the designed columns, sends the printed range, downloads through the client and opens the order", async () => {
    grants = { "Orders.TransportOrders": true };
    gridRows.current = documents;
    routes.GET["/api/orders/consignment-note/document-list"] = () => ({ rows: documents, totalCount: 1, filteredCount: 1 });
    routes.GET["/api/orders/consignment-note/{id}/download"] = () => ({ data: new Blob(["pdf"]), error: undefined, response: new Response(null, { headers: { "content-disposition": 'attachment; filename="cmr.pdf"' } }) });
    const createObjectURL = vi.fn(() => "blob:x");
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL: vi.fn() });
    renderWith(<DocumentsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("orders.transport.consignmentNotes");
    expect(columns()).toEqual(["number", "printedAt", "orderNumber", "tripId (hidden)", "lines", "profileName", "printedByUserId (hidden)", "fileName", "sizeBytes (hidden)"]);
    fireEvent.change(document.getElementById("documents-from")!, { target: { value: "2026-10-01" } });
    await waitFor(() => expect(lastCall(GET, "/api/orders/consignment-note/document-list")?.params?.query?.PrintedFrom).toBe("2026-10-01"));

    const row = document.querySelector("[data-slot=mock-grid-row]") as HTMLElement;
    expect(within(row).getByText("2")).toBeInTheDocument();
    fireEvent.click(within(row).getByText("Orders:Download"));
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/orders/consignment-note/{id}/download", expect.objectContaining({ params: { path: { id: "d1" } }, parseAs: "blob" })));
    await waitFor(() => expect(createObjectURL).toHaveBeenCalled());
    fireEvent.click(within(row).getByText("open-row"));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o1" }));
  });
});

describe("TemplatesView", () => {
  const templates = [
    { id: "tpl-1", name: "Weekly Acme", transportOrderTypeName: "Standard", customerName: "Acme", recurrence: 1, daysOfWeek: 1 | 4, lineCount: 2, isActive: true },
    { id: "tpl-2", name: "Monthly Beta", recurrence: 2, dayOfMonth: 15, isActive: false },
  ];

  it("gates on Orders.TransportOrderTemplates and the Templates feature", () => {
    grants = {};
    const { unmount } = renderWith(<TemplatesView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();
    grants = { "Orders.TransportOrderTemplates": true };
    features = { ...FEATURES, "Apps.Transport.Orders.Templates": "false" };
    renderWith(<TemplatesView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });

  it("has the designed columns, the inactive filter, New with Create and Create orders for an active template only", async () => {
    grants = { "Orders.TransportOrderTemplates": true, "Orders.TransportOrderTemplates.Create": true, "Orders.TransportOrders.Create": true };
    gridRows.current = templates;
    routes.GET["/api/orders/transport-order-template"] = () => ({ rows: templates, totalCount: 2, filteredCount: 2 });
    renderWith(<TemplatesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("orders.transport.transportOrderTemplates");
    expect(columns()).toEqual([
      "name", "transportOrderTypeName", "customerName", "recurrence", "days", "lastMaterialisedDate", "lineCount", "isActive",
      "interval (hidden)", "horizonDays (hidden)", "validFrom (hidden)", "validTill (hidden)", "hasTripRequest (hidden)", "description (hidden)",
    ]);
    await waitFor(() => expect(lastCall(GET, "/api/orders/transport-order-template")?.params?.query?.IncludeInactive).toBe(false));
    fireEvent.click(screen.getByRole("checkbox", { name: "Orders:ShowInactive" }));
    await waitFor(() => expect(lastCall(GET, "/api/orders/transport-order-template")?.params?.query?.IncludeInactive).toBe(true));

    const rows = document.querySelectorAll("[data-slot=mock-grid-row]");
    expect(within(rows[0] as HTMLElement).getByText("Orders:TemplateRecurrence.Weekly")).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText("15")).toBeInTheDocument();
    expect(within(rows[0] as HTMLElement).getByText("Orders:Materialise")).toBeEnabled();
    expect(within(rows[1] as HTMLElement).getByText("Orders:Materialise")).toBeDisabled();
    expect(screen.getByText("Orders:NewTransportOrderTemplate")).toBeInTheDocument();

    fireEvent.click(within(rows[0] as HTMLElement).getByText("Orders:Materialise"));
    expect(screen.getByTestId("materialise-wizard")).toHaveAttribute("data-source", "tpl-1");
    fireEvent.click(within(rows[0] as HTMLElement).getByText("open-row"));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/templates/tpl-1" }));
  });

  it("Create orders is blocked for an active template without lines, with the reason (it would create empty orders)", async () => {
    grants = { "Orders.TransportOrderTemplates": true, "Orders.TransportOrders.Create": true };
    const empty = [{ id: "tpl-3", name: "New Gamma", recurrence: 0, lineCount: 0, isActive: true }];
    gridRows.current = empty;
    routes.GET["/api/orders/transport-order-template"] = () => ({ rows: empty, totalCount: 1, filteredCount: 1 });
    renderWith(<TemplatesView />);
    await screen.findByTestId("mock-grid");
    const row = document.querySelector("[data-slot=mock-grid-row]") as HTMLElement;
    const action = await waitFor(() => within(row).getByText("Orders:Materialise"));
    expect(action).toBeDisabled();
    expect(action).toHaveAttribute("title", "Orders:TemplateHasNoLines");
  });

  it("hides New and Delete without their permissions", async () => {
    grants = { "Orders.TransportOrderTemplates": true };
    renderWith(<TemplatesView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("Orders:NewTransportOrderTemplate")).toBeNull();
    expect(screen.queryByText("Delete")).toBeNull();
  });
});
