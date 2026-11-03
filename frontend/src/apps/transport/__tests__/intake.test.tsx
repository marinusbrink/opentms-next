import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 72 "Review" and "UI design", Transport app → Intake: the list (gates; opens on Needs review and the last six
 * months; row actions Review, Open order, Reject with its permission), the review page /transport/intake/{id} (the
 * resolution per field: green resolved, amber ambiguous with candidates, red unresolved; a pick or a candidate makes a
 * field resolved by the user; a corrected received value travels as the reviewed payload and is validated; Create order
 * sends confirm=false and needs only Orders.OrderIntakes.Resolve, Create and confirm needs Orders.TransportOrders.Confirm
 * too (build note 3); Reject with a required reason; View source only with Integrations.InboundMessages and the
 * Received node, navigating to the document trace; read-only once decided) and the order page's source chip. The
 * domain hooks are real; the generated client is a path dispatcher. Risk class: Medium (UI flows) on top of the High
 * order chain (resolver and auto-confirm are backend tests). */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname: "/transport/intake", search: {} }),
  Link: ({ to, children, className }: { to: string; children?: ReactNode; className?: string }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } }),
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
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";

import { IntakeSourceChip } from "@/apps/transport/components/IntakeSourceChip";
import { IntakePage } from "@/apps/transport/pages/IntakePage";
import { IntakeView } from "@/apps/transport/views/IntakeView";
import { GET, POST, calls, lastCall, resetClient, routes } from "./test-setup-mocks";
import { fail, panel, ribbonButton, stubGlobals } from "./transport-test-mocks";

const FEATURES = {
  "Integrations.OrderIntake": "true",
  "Apps.Transport": "true",
  "Apps.Transport.Intake": "true",
  "Apps.Transport.Orders": "true",
  "Apps.Integrations": "true",
  "Apps.Integrations.Intake": "true",
  "Apps.Integrations.Intake.Received": "true",
};
const VIEW = { "Orders.OrderIntakes": true };
const RESOLVE = { ...VIEW, "Orders.OrderIntakes.Resolve": true };

const ROWS = [
  { id: "i-open", channel: "Gmail", senderAddress: "planning@acme.test", subject: "Order PO-4711", customerName: "Acme", status: 1, openFieldCount: 2, receivedAt: "2026-11-03T08:00:00Z" },
  { id: "i-done", channel: "Api", senderAddress: null, fileName: "orders.json", customerName: "Beta", status: 2, openFieldCount: 0, transportOrderId: "o-9", orderNumber: "TO-9", receivedAt: "2026-11-02T08:00:00Z" },
];

const INTAKE = {
  id: "i-open",
  status: 1,
  mappingConfidence: 0.8,
  creationTime: "2026-11-03T08:00:00Z",
  sourceSummary: { channel: "Gmail", senderAddress: "planning@acme.test", subject: "Order PO-4711", fileName: "order-4711.pdf", sourceDocumentId: "doc-1", intakeIndex: 0, source: 5 },
  payload: {
    customerReference: "PO-4711",
    orderTypeName: "Standaard",
    customer: { name: "ACME B.V.", emailAddress: "planning@acme.test" },
    lines: [
      {
        transportUnitName: "Europallet",
        quantity: 2,
        weightKg: 800,
        stops: [
          { role: 1, date: "2026-11-04", address: { name: "Acme DC", street: "Dam", houseNumber: "1", postalCode: "1012 AB", city: "Amsterdam", countryCode: "NL" } },
          { role: 2, date: "2026-11-05", address: { name: "Beta", postalCode: "3011 AA", city: "Rotterdam", countryCode: "NL" } },
        ],
      },
    ],
  },
  resolution: [
    { fieldPath: "order.customer", status: 3, method: null, candidates: [{ id: "r-acme", name: "Acme", detail: "Amsterdam" }, { id: "r-acme2", name: "Acme Logistics", detail: "Utrecht" }] },
    { fieldPath: "order.orderType", status: 1, method: 2, targetId: "tt-1", targetName: "Standard" },
    { fieldPath: "lines[0].transportUnit", status: 2, method: null, candidates: [{ id: "tu-eur", name: "Europallet" }, { id: "tu-eur2", name: "Euro pallet (block)" }] },
    { fieldPath: "lines[0].stops[0].address", status: 1, method: 3, targetId: "a-1", targetName: "Acme DC, Dam 1, Amsterdam" },
    { fieldPath: "lines[0].stops[0].waypointType", status: 1, method: 5, targetId: "wp-load", targetName: "Load" },
    { fieldPath: "lines[0].stops[1].address", status: 1, method: 2, targetId: "a-2", targetName: "Beta, Rotterdam" },
    { fieldPath: "lines[0].stops[1].waypointType", status: 1, method: 5, targetId: "wp-unload", targetName: "Unload" },
  ],
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  vi.mocked(toast.success).mockClear();
  grants = { ...VIEW };
  features = { ...FEATURES };
  resetClient();
  routes.GET["/api/orders/order-intake"] = () => ({ rows: ROWS, totalCount: 2, filteredCount: 2 });
  routes.GET["/api/orders/order-intake/{id}"] = () => INTAKE;
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const gridRows = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-grid-row]"));
const rowActions = (index: number) => Array.from(gridRows()[index].querySelectorAll<HTMLElement>("[data-row-action]")).map((b) => b.getAttribute("data-row-action"));
const resolutionRow = (path: string) => document.querySelector<HTMLElement>(`[data-slot=intake-review] [data-path="${path}"]`)!;
const resolvedBody = () => lastCall(POST, "/api/orders/order-intake/{id}/resolve")?.body as { picks: Record<string, string>; reviewedPayload?: { lines?: { weightKg?: number }[] }; confirm: boolean };
const waitForReview = () => waitFor(() => expect(resolutionRow("order.customer")).not.toBeNull());

describe("IntakeView (design 72)", () => {
  it("is not rendered with the tenant feature off or the Intake node off; needs Orders.OrderIntakes", () => {
    features = { ...FEATURES, "Integrations.OrderIntake": "false" };
    const first = renderWith(<IntakeView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    first.unmount();
    features = { ...FEATURES, "Apps.Transport.Intake": "false" };
    const second = renderWith(<IntakeView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    second.unmount();
    features = { ...FEATURES };
    grants = {};
    renderWith(<IntakeView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(calls(GET, "/api/orders/order-intake")).toHaveLength(0);
  });

  it("opens on Needs review (the filter's default) and the last six months", async () => {
    renderWith(<IntakeView />);
    await waitFor(() => expect(calls(GET, "/api/orders/order-intake")).toHaveLength(1));
    const query = lastCall(GET, "/api/orders/order-intake")!.params!.query!;
    expect(query.Status).toEqual([1]);
    expect(query.ReceivedFrom).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(screen.getByRole("checkbox", { name: "Orders:OrderIntakeStatus.NeedsReview" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Orders:OrderIntakeStatus.OrderCreated" })).not.toBeChecked();
    expect(document.querySelector("[data-grid-id='orders.transport.orderIntakes']")).not.toBeNull();

    /* Clearing the filter shows the history too (no Status parameter). */
    fireEvent.click(screen.getByRole("checkbox", { name: "Orders:OrderIntakeStatus.NeedsReview" }));
    await waitFor(() => expect(lastCall(GET, "/api/orders/order-intake")!.params!.query!.Status).toBeUndefined());
  });

  it("row actions: Review always, Open order when there is one, Reject only with its permission and only for Needs review", async () => {
    const first = renderWith(<IntakeView />);
    await waitFor(() => expect(gridRows()).toHaveLength(2));
    expect(rowActions(0)).toEqual(["review"]);
    expect(rowActions(1)).toEqual(["review", "open-order"]);
    first.unmount();

    grants = { ...VIEW, "Orders.OrderIntakes.Reject": true, "Orders.TransportOrders": true };
    renderWith(<IntakeView />);
    await waitFor(() => expect(gridRows()).toHaveLength(2));
    expect(rowActions(0)).toEqual(["review", "reject"]);
    expect(rowActions(1)).toEqual(["review", "open-order"]);
    await click(gridRows()[0].querySelector<HTMLElement>("[data-row-action=review]")!);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/intake/i-open" }));
  });

  it("Reject from the list asks the reason and posts it", async () => {
    grants = { ...VIEW, "Orders.OrderIntakes.Reject": true };
    routes.POST["/api/orders/order-intake/{id}/reject"] = () => ({ ...INTAKE, status: 3 });
    renderWith(<IntakeView />);
    await waitFor(() => expect(gridRows()).toHaveLength(2));
    await click(gridRows()[0].querySelector<HTMLElement>("[data-row-action=reject]")!);
    const dialog = screen.getByRole("dialog");
    fireEvent.change(dialog.querySelector("#reject-intake-reason")!, { target: { value: "  Newsletter, not an order  " } });
    await click(within(dialog).getAllByRole("button", { name: "Orders:Reject" }).at(-1)!);
    await waitFor(() => expect(lastCall(POST, "/api/orders/order-intake/{id}/reject")).toEqual({ params: { path: { id: "i-open" } }, body: { reason: "Newsletter, not an order" } }));
  });
});

describe("IntakePage – the review (design 72)", () => {
  it("is not rendered with the feature off; needs Orders.OrderIntakes; not found for an unknown intake", async () => {
    features = { ...FEATURES, "Integrations.OrderIntake": "false" };
    const first = renderWith(<IntakePage id="i-open" />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    first.unmount();
    features = { ...FEATURES };
    grants = {};
    const second = renderWith(<IntakePage id="i-open" />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    second.unmount();
    grants = { ...VIEW };
    routes.GET["/api/orders/order-intake/{id}"] = () => fail("Not found", 404);
    renderWith(<IntakePage id="other-tenants-intake" />);
    expect(await screen.findByText("RecordNotFound")).toBeInTheDocument();
  });

  it("shows each field's resolution: resolved green with its method and target, ambiguous and unresolved with candidates", async () => {
    grants = RESOLVE;
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(resolutionRow("order.customer")).toHaveAttribute("data-status", "Unresolved");
    expect(resolutionRow("order.orderType")).toHaveAttribute("data-status", "Resolved");
    expect(resolutionRow("lines[0].transportUnit")).toHaveAttribute("data-status", "Ambiguous");
    expect(resolutionRow("lines[0].stops[0].address")).toHaveAttribute("data-status", "Resolved");

    const type = resolutionRow("order.orderType");
    expect(type.textContent).toContain("Orders:IntakeResolutionMethod.Exact");
    expect(type.textContent).toContain("Standard");
    expect(type.textContent).toContain("Standaard");
    expect(within(type).queryByLabelText("Orders:IntakeCandidates")).toBeNull();

    const customer = resolutionRow("order.customer");
    expect(customer.textContent).toContain("ACME B.V.");
    const candidates = within(customer).getByLabelText("Orders:IntakeCandidates");
    expect(within(candidates).getAllByRole("button").map((b) => b.textContent)).toEqual(["AcmeAmsterdam", "Acme LogisticsUtrecht"]);
    expect(resolutionRow("lines[0].transportUnit").textContent).toContain("Euro pallet (block)");
  });

  it("a candidate pick resolves the field by the user; Create order posts the picks with confirm=false and closes", async () => {
    grants = RESOLVE;
    routes.POST["/api/orders/order-intake/{id}/resolve"] = () => ({ ...INTAKE, status: 2, orderNumber: "TO-100", transportOrderId: "o-100" });
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await click(within(resolutionRow("order.customer")).getByRole("button", { name: "AcmeAmsterdam" }));
    expect(resolutionRow("order.customer")).toHaveAttribute("data-status", "Resolved");
    expect(resolutionRow("order.customer").textContent).toContain("Orders:IntakeResolutionMethod.User");
    await click(within(resolutionRow("lines[0].transportUnit")).getByRole("button", { name: "Europallet" }));

    await click(ribbonButton("Orders:IntakeCreateOrder")!);
    await waitFor(() => expect(calls(POST, "/api/orders/order-intake/{id}/resolve")).toHaveLength(1));
    expect(lastCall(POST, "/api/orders/order-intake/{id}/resolve")?.params?.path).toEqual({ id: "i-open" });
    expect(resolvedBody()).toEqual({ picks: { "order.customer": "r-acme", "lines[0].transportUnit": "tu-eur" }, reviewedPayload: undefined, confirm: false });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:IntakeOrderCreated"));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/intake" }));
  });

  it("Create and confirm is hidden without Orders.TransportOrders.Confirm, and sends confirm=true with it", async () => {
    grants = RESOLVE;
    const first = renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(ribbonButton("Orders:IntakeCreateOrder")).not.toBeNull();
    expect(ribbonButton("Orders:IntakeCreateAndConfirm")).toBeNull();
    first.unmount();

    grants = { ...RESOLVE, "Orders.TransportOrders.Confirm": true };
    routes.POST["/api/orders/order-intake/{id}/resolve"] = () => ({ ...INTAKE, status: 2, orderNumber: "TO-101" });
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await click(ribbonButton("Orders:IntakeCreateAndConfirm")!);
    await waitFor(() => expect(resolvedBody()?.confirm).toBe(true));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:IntakeOrderCreatedConfirmed"));
  });

  it("without Orders.OrderIntakes.Resolve the review is read-only: no Create commands, no pickers, no candidates", async () => {
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(screen.getByText("Orders:IntakeReadOnly")).toBeInTheDocument();
    expect(ribbonButton("Orders:IntakeCreateOrder")).toBeNull();
    expect(ribbonButton("Orders:IntakeCreateAndConfirm")).toBeNull();
    expect(within(resolutionRow("order.customer")).queryByLabelText("Orders:IntakeCandidates")).toBeNull();
    expect((document.querySelector("#intake-order-customerReference") as HTMLInputElement).disabled).toBe(true);
  });

  it("a corrected value travels as the reviewed payload; a negative number is refused under the field without a post", async () => {
    grants = RESOLVE;
    routes.POST["/api/orders/order-intake/{id}/resolve"] = () => ({ ...INTAKE, status: 2, orderNumber: "TO-102" });
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    const weight = document.querySelector("#intake-lines-0--weightKg") as HTMLInputElement;
    expect(weight.value).toBe("800");
    fireEvent.change(weight, { target: { value: "-5" } });
    await click(ribbonButton("Orders:IntakeCreateOrder")!);
    expect(calls(POST, "/api/orders/order-intake/{id}/resolve")).toHaveLength(0);

    fireEvent.change(weight, { target: { value: "900" } });
    await click(ribbonButton("Orders:IntakeCreateOrder")!);
    await waitFor(() => expect(calls(POST, "/api/orders/order-intake/{id}/resolve")).toHaveLength(1));
    expect(resolvedBody().reviewedPayload?.lines?.[0]?.weightKg).toBe(900);
    expect(resolvedBody().picks).toEqual({});
  });

  it("an answer that is still open says so and stays on the page", async () => {
    grants = RESOLVE;
    routes.POST["/api/orders/order-intake/{id}/resolve"] = () => INTAKE;
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await click(ribbonButton("Orders:IntakeCreateOrder")!);
    expect(await screen.findByText("Orders:IntakeStillOpen")).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("Reject needs its permission and a reason, then posts it and closes", async () => {
    grants = RESOLVE;
    const first = renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(ribbonButton("Orders:Reject")).toBeNull();
    first.unmount();

    grants = { ...RESOLVE, "Orders.OrderIntakes.Reject": true };
    routes.POST["/api/orders/order-intake/{id}/reject"] = () => ({ ...INTAKE, status: 3, rejectReason: "Duplicate" });
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await click(ribbonButton("Orders:Reject")!);
    const dialog = screen.getAllByRole("dialog").at(-1)!;
    const confirm = within(dialog).getAllByRole("button", { name: "Orders:Reject" }).at(-1)!;
    await click(confirm);
    expect(within(dialog).getByText("MasterData:Required")).toBeInTheDocument();
    expect(calls(POST, "/api/orders/order-intake/{id}/reject")).toHaveLength(0);
    fireEvent.change(dialog.querySelector("#reject-intake-reason")!, { target: { value: "Duplicate" } });
    await click(confirm);
    await waitFor(() => expect(lastCall(POST, "/api/orders/order-intake/{id}/reject")?.body).toEqual({ reason: "Duplicate" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Orders:IntakeRejected"));
  });

  it("View source only with Integrations.InboundMessages and the Received node; it opens the document's trace", async () => {
    const first = renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(ribbonButton("Orders:IntakeViewSource")).toBeNull();
    first.unmount();

    grants = { ...VIEW, "Integrations.InboundMessages": true };
    features = { ...FEATURES, "Apps.Integrations.Intake.Received": "false" };
    const second = renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    expect(ribbonButton("Orders:IntakeViewSource")).toBeNull();
    second.unmount();

    features = { ...FEATURES };
    renderWith(<IntakePage id="i-open" />);
    await waitForReview();
    await click(ribbonButton("Orders:IntakeViewSource")!);
    expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/received/document/doc-1" });
  });

  it("a decided intake is read-only and offers Open order", async () => {
    grants = { ...RESOLVE, "Orders.OrderIntakes.Reject": true, "Orders.TransportOrders": true, "Orders.TransportOrders.Confirm": true };
    routes.GET["/api/orders/order-intake/{id}"] = () => ({ ...INTAKE, status: 2, transportOrderId: "o-100", orderNumber: "TO-100" });
    renderWith(<IntakePage id="i-open" />);
    await waitFor(() => expect(document.querySelector("[data-slot=intake-page]")).toHaveAttribute("data-status", "OrderCreated"));
    expect(ribbonButton("Orders:IntakeCreateOrder")).toBeNull();
    expect(ribbonButton("Orders:IntakeCreateAndConfirm")).toBeNull();
    expect(ribbonButton("Orders:Reject")).toBeNull();
    await click(ribbonButton("Orders:IntakeOpenOrder")!);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o-100" }));
    expect(panel("Orders:IntakeReviewTab").textContent).toContain("TO-100");
  });
});

describe("IntakeSourceChip – the order page's source (design 72)", () => {
  it("an order typed by hand shows nothing", () => {
    grants = { ...VIEW, "Integrations.InboundMessages": true };
    const { container } = renderWith(<IntakeSourceChip order={{ source: 1, sourceIntakeId: null }} />);
    expect(container.textContent).toBe("");
  });

  it("links to the intake and, through it, to the trace of the source document", async () => {
    grants = { ...VIEW, "Integrations.InboundMessages": true };
    renderWith(<IntakeSourceChip order={{ source: 5, sourceIntakeId: "i-open" }} />);
    expect(screen.getByRole("link", { name: "Orders:OrderSource.Import" })).toHaveAttribute("href", "/transport/intake/i-open");
    expect(await screen.findByRole("link", { name: "Orders:IntakeViewSource" })).toHaveAttribute("href", "/integrations/received/document/doc-1");
  });

  it("without the intake permission the chip is plain text and nothing is fetched; without the trace permission no source link", async () => {
    grants = { "Integrations.InboundMessages": true };
    const first = renderWith(<IntakeSourceChip order={{ source: 4, sourceIntakeId: "i-open" }} />);
    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.getByText("Orders:OrderSource.Api")).toBeInTheDocument();
    expect(calls(GET, "/api/orders/order-intake/{id}")).toHaveLength(0);
    first.unmount();

    grants = { ...VIEW };
    renderWith(<IntakeSourceChip order={{ source: 4, sourceIntakeId: "i-open" }} />);
    expect(screen.getByRole("link", { name: "Orders:OrderSource.Api" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Orders:IntakeViewSource" })).toBeNull();
    expect(calls(GET, "/api/orders/order-intake/{id}")).toHaveLength(0);
  });
});
