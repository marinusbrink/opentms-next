import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The three wizards of design 61 (UI-WIZ-001 to -005): Print CMR (the default profile preselected, the box overrides,
 * the lines to include, one print per order with the chosen lines, the Download / Print buttons per document),
 * New from template / Copy order (the dates per weekday over a range, one materialise call per date with the copies)
 * and Archive trips (open trips skipped and named, one archive call per trip). Risk class: Medium. */

/* Count texts (finding 6) carry their placeholders, so a test sees the count and whether the singular key was picked. */
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (k: string) => (/^(Orders:(PrintCmrOutputHint|PrintCmrOutputTripHint|OrdersCreated|DatesSelected)|PlanningExecution:(ArchiveTripsIntro|ArchiveTripsSkipped|TripsArchived))(\.One)?$/.test(k) ? `${k} {0}${k.includes("Skipped") ? " {1}" : ""}` : k),
    culture: "en",
    isLoading: false,
  }),
}));
let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { features: { values: features }, auth: { grantedPolicies: grants }, currentTenant: { id: "t-1" } } }) }));
const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders", search: {} }) }));
/* The New template dialog (its own tests cover its fields): a stub that hands back the template its save created. */
vi.mock("@/apps/transport/components/NewTemplateDialog", () => ({
  NewTemplateDialog: ({ initialName, onCreated, onOpenChange }: { initialName?: string; onCreated?: (template: { id: string; name: string }) => void; onOpenChange: (open: boolean) => void }) => (
    <div data-testid="new-template-dialog" data-initial-name={initialName ?? ""}>
      <button
        type="button"
        onClick={() => {
          onOpenChange(false);
          onCreated?.({ id: "tpl-new", name: initialName ?? "" });
        }}
      >
        stub-save-template
      </button>
    </div>
  ),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./transport-test-mocks")).MockLinesGrid }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { ArchiveTripsWizard } from "@/apps/transport/wizards/ArchiveTripsWizard";
import { MaterialiseWizard } from "@/apps/transport/wizards/MaterialiseWizard";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import { toast } from "sonner";
import type { TransportTripRow } from "@/domains/planning-execution/trips";
import { stubGlobals } from "./transport-test-mocks";
import { POST, calls, resetClient, routes } from "./test-setup-mocks";

beforeAll(stubGlobals);
beforeEach(() => {
  grants = {};
  features = {};
  navigateMock.mockReset();
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  resetClient();
  routes.GET["/api/orders/consignment-note-profile"] = () => ({ rows: [{ id: "p-other", name: "Minimal", isDefault: false }, { id: "p-default", name: "Standard", isDefault: true }], totalCount: 2, filteredCount: 2 });
  routes.GET["/api/orders/consignment-note-profile/{id}"] = (init) => ({ id: init?.params?.path?.id, name: "Standard", copies: 4, showBorders: true, showQrCode: false, groupByLeg: false, box1Sender: 1, box2Consignee: 3, box16Carrier: 5 });
  routes.GET["/api/orders/transport-order-template/lookup"] = () => [{ id: "tpl-1", name: "Weekly Acme" }];
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const footer = () => document.querySelector("[data-slot=entity-dialog-footer]") as HTMLElement;
const footerButton = (name: string) => within(footer()).getByRole("button", { name });
const next = async () =>
  act(async () => {
    fireEvent.click(footerButton("Wizard:Next"));
    await Promise.resolve();
  });
const finish = async (verb: string) =>
  act(async () => {
    fireEvent.click(footerButton(verb));
    await Promise.resolve();
  });

describe("PrintCmrWizard", () => {
  it("names one consignment note in the singular on the output step (finding 6)", async () => {
    renderWith(<PrintCmrWizard open onOpenChange={() => undefined} orders={[{ id: "o9", number: "TO-9", lines: [{ id: "l9", position: 1, label: "Pallet" }] }]} />);
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect((within(dialog).getByLabelText(/^Orders:ConsignmentNoteProfile/) as HTMLSelectElement).value).toBe("p-default"));
    await next();
    await next();
    expect(within(dialog).getByText("Orders:PrintCmrOutputHint.One 1")).toBeInTheDocument();
  });


  const orders = [
    { id: "o1", number: "TO-1", lines: [{ id: "l1", position: 1, label: "Pallet" }, { id: "l2", position: 2, label: "Box" }] },
    { id: "o2", number: "TO-2", lines: [{ id: "l3", position: 1, label: "Container" }] },
  ];

  it("preselects the default profile with its settings, lets the user drop a line, prints per order and offers the documents", async () => {
    routes.POST["/api/orders/consignment-note/print"] = (init) => {
      const body = init?.body as { orderLineIds: string[] };
      return [{ id: `doc-${body.orderLineIds[0]}`, number: `CMR-${body.orderLineIds[0]}`, orderNumber: "TO", fileName: "cmr.pdf" }];
    };
    renderWith(<PrintCmrWizard open onOpenChange={() => undefined} orders={orders} />);
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect((within(dialog).getByLabelText(/^Orders:ConsignmentNoteProfile/) as HTMLSelectElement).value).toBe("p-default"));
    await waitFor(() => expect((within(dialog).getByLabelText(/^Orders:Copies/) as HTMLInputElement).value).toBe("4"));
    expect((within(dialog).getByLabelText(/^Orders:Box1Sender/) as HTMLSelectElement).value).toBe("1");
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Box1Sender/), { target: { value: "4" } });
    expect(within(dialog).getByRole("checkbox", { name: "Orders:ShowQrCode" })).not.toBeChecked();

    await next();
    expect(within(dialog).getByText("Orders:TransportOrder TO-1")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("checkbox", { name: /Orders:Line 2: Box/ }));
    await next();
    expect(within(dialog).getByText("Orders:PrintCmrOutputHint 2")).toBeInTheDocument();
    expect(footerButton("Orders:Print")).toBeEnabled();
    await finish("Orders:Print");

    await waitFor(() => expect(calls(POST, "/api/orders/consignment-note/print")).toHaveLength(2));
    const bodies = calls(POST, "/api/orders/consignment-note/print").map(([, init]) => init!.body as { orderLineIds: string[]; profileId: string; overrides: { box1Sender: number; copies: number } });
    expect(bodies[0]).toMatchObject({ orderLineIds: ["l1"], profileId: "p-default", overrides: { box1Sender: 4, copies: 4 } });
    expect(bodies[1].orderLineIds).toEqual(["l3"]);
    await waitFor(() => expect(dialog.querySelectorAll("[data-slot=printed-documents] li")).toHaveLength(2));
    expect(within(dialog).getAllByRole("button", { name: /Orders:Download/ })).toHaveLength(2);
    expect(footerButton("Close")).toBeEnabled();
    expect(toast.success).toHaveBeenCalledWith("Orders:ConsignmentNotePrinted");
  });

  it("a trip prints as a whole and a failed print stays in the list with its reason (UI-WIZ-004)", async () => {
    routes.POST["/api/orders/consignment-note/print"] = () => ({ data: undefined, error: { error: { message: "No consignment note template" } }, response: new Response(null, { status: 409 }) });
    renderWith(<PrintCmrWizard open onOpenChange={() => undefined} trip={{ id: "t1", number: "TR-1" }} />);
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect((within(dialog).getByLabelText(/^Orders:ConsignmentNoteProfile/) as HTMLSelectElement).value).toBe("p-default"));
    await next();
    expect(within(dialog).getByText("Orders:PrintCmrTripHint")).toBeInTheDocument();
    await next();
    /* A trip prints one note per order on it: no count of "1" (finding 6). */
    expect(within(dialog).getByText("Orders:PrintCmrOutputTripHint TR-1")).toBeInTheDocument();
    await finish("Orders:Print");
    await waitFor(() => expect(within(dialog).getByText("No consignment note template")).toBeInTheDocument());
    expect(calls(POST, "/api/orders/consignment-note/print")[0][1]!.body).toMatchObject({ tripId: "t1", orderLineIds: null });
    expect(toast.success).not.toHaveBeenCalled();
  });
});

describe("MaterialiseWizard", () => {
  it("creates the orders of the weekdays in the range, one call per date with the copies", async () => {
    routes.POST["/api/orders/transport-order-template/{id}/materialise"] = () => ({ createdOrderIds: ["x", "y"], skippedCount: 0 });
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} source={{ kind: "template", id: "tpl-1", label: "Weekly Acme" }} />);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Weekly Acme")).toBeInTheDocument();
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Financial:From/), { target: { value: "2026-10-26" } }); // a Monday
    fireEvent.change(within(dialog).getByLabelText(/^Financial:To/), { target: { value: "2026-11-01" } }); // the Sunday
    fireEvent.click(within(dialog).getByRole("checkbox", { name: "Orders:RecurrenceDays.Wednesday" })); // Mon-Fri preselected: drop Wednesday
    expect(dialog.querySelector("[data-slot=materialise-dates]")).toHaveTextContent("Orders:DatesSelected 4");
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Copies/), { target: { value: "2" } });
    await next();
    expect(within(dialog).getByText("Weekly Acme")).toBeInTheDocument();
    await finish("Orders:Materialise");
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order-template/{id}/materialise")).toHaveLength(4));
    const dates = calls(POST, "/api/orders/transport-order-template/{id}/materialise").map(([, init]) => (init!.body as { dates: string[]; copies: number }).dates[0]);
    expect(dates).toEqual(["2026-10-26", "2026-10-27", "2026-10-29", "2026-10-30"]);
    expect((calls(POST, "/api/orders/transport-order-template/{id}/materialise")[0][1]!.body as { copies: number }).copies).toBe(2);
    expect(toast.success).toHaveBeenCalledWith("Orders:OrdersCreated 8");
  });

  it("copies an order through copy-order and refuses more than the maximum copies", async () => {
    routes.POST["/api/orders/transport-order-template/copy-order/{orderId}"] = () => ({ createdOrderIds: ["x"], skippedCount: 0 });
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} source={{ kind: "order", id: "o1", label: "Orders:TransportOrder TO-1" }} />);
    const dialog = await screen.findByRole("dialog");
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Financial:From/), { target: { value: "2026-10-27" } });
    fireEvent.change(within(dialog).getByLabelText(/^Financial:To/), { target: { value: "2026-10-27" } });
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Copies/), { target: { value: "99" } });
    expect(footerButton("Wizard:Next")).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Copies/), { target: { value: "3" } });
    await next();
    await finish("Orders:Materialise");
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order-template/copy-order/{orderId}", { params: { path: { orderId: "o1" } }, body: { dates: ["2026-10-27"], copies: 3 } }));
  });

  it("Details: per order (date × copy) the reference and the lines' quantity and weight, prefilled from the template; only changes are sent", async () => {
    routes.GET["/api/orders/transport-order-template/{id}"] = () => ({
      id: "tpl-1",
      name: "Weekly Acme",
      customerReference: "WEEK",
      lines: [
        { id: "tl-2", position: 2, transportUnitName: "Box", quantity: 2, weightKg: null, transportUnitId: "u" },
        { id: "tl-1", position: 1, transportUnitName: "Pallet", productName: "Beer", quantity: 10, weightKg: 500, transportUnitId: "u" },
      ],
    });
    routes.POST["/api/orders/transport-order-template/{id}/materialise"] = () => ({ createdOrderIds: ["x"], skippedCount: 0 });
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} source={{ kind: "template", id: "tpl-1", label: "Weekly Acme" }} />);
    const dialog = await screen.findByRole("dialog");
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Financial:From/), { target: { value: "2026-10-26" } });
    fireEvent.change(within(dialog).getByLabelText(/^Financial:To/), { target: { value: "2026-10-27" } });
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Copies/), { target: { value: "2" } });
    /* Four orders: two dates × two copies; the first is shown, prefilled from the template. */
    const list = await within(dialog).findByRole("list", { name: "Orders:OrdersToCreate" });
    expect(within(list).getAllByRole("button")).toHaveLength(4);
    const reference = within(dialog).getByLabelText(/^Orders:CustomerReference/) as HTMLInputElement;
    expect(reference.value).toBe("WEEK");
    const lines = () => Array.from(dialog.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
    expect(lines().map((l) => l.getAttribute("data-key"))).toEqual(["tl-1", "tl-2"]);
    expect((within(lines()[0]).getByLabelText("Orders:Quantity") as HTMLInputElement).value).toBe("10");
    expect(within(lines()[0]).queryByText("Remove")).toBeNull();

    /* The second copy of the 27th: another reference and the first line's weight. */
    fireEvent.click(within(list).getAllByRole("button")[3]);
    expect(dialog.querySelector("[data-slot=materialise-detail]")).toHaveAttribute("data-key", "2026-10-27|1");
    fireEvent.change(within(dialog).getByLabelText(/^Orders:CustomerReference/), { target: { value: "SPECIAL" } });
    fireEvent.change(within(lines()[0]).getByLabelText("Orders:WeightKg"), { target: { value: "450" } });
    expect(within(list).getAllByRole("button")[3].querySelector("[data-slot=detail-changed]")).not.toBeNull();
    expect(within(list).getAllByRole("button")[0].querySelector("[data-slot=detail-changed]")).toBeNull();

    /* A value that is no number blocks Next. */
    fireEvent.change(within(lines()[1]).getByLabelText("Orders:Quantity"), { target: { value: "abc" } });
    expect(within(dialog).getByText("Orders:DetailsInvalidNumber")).toBeInTheDocument();
    expect(footerButton("Wizard:Next")).toBeDisabled();
    fireEvent.change(within(lines()[1]).getByLabelText("Orders:Quantity"), { target: { value: "2" } });
    expect(footerButton("Wizard:Next")).toBeEnabled();

    await next();
    expect(dialog.querySelector("[data-slot=materialise-changed-count]")).toHaveTextContent("1");
    await finish("Orders:Materialise");
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order-template/{id}/materialise")).toHaveLength(2));
    const bodies = calls(POST, "/api/orders/transport-order-template/{id}/materialise").map(([, init]) => init!.body);
    expect(bodies[0]).toEqual({ dates: ["2026-10-26"], copies: 2 });
    expect(bodies[1]).toEqual({ dates: ["2026-10-27"], copies: 2, overrides: [{ date: "2026-10-27", copyIndex: 1, customerReference: "SPECIAL", lines: [{ lineId: "tl-1", quantity: null, weightKg: 450 }] }] });
  });

  it("Details of Copy order name the copied order's line ids", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => ({ id: "o1", number: "TO-1", customerReference: null, lines: [{ id: "ol-1", position: 1, transportUnitName: "Pallet", quantity: 4, weightKg: 100 }] });
    routes.POST["/api/orders/transport-order-template/copy-order/{orderId}"] = () => ({ createdOrderIds: ["x"], skippedCount: 0 });
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} source={{ kind: "order", id: "o1", label: "Orders:TransportOrder TO-1" }} />);
    const dialog = await screen.findByRole("dialog");
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Financial:From/), { target: { value: "2026-10-27" } });
    fireEvent.change(within(dialog).getByLabelText(/^Financial:To/), { target: { value: "2026-10-27" } });
    await next();
    const line = await waitFor(() => {
      const found = dialog.querySelector<HTMLElement>("[data-slot=mock-line][data-key=ol-1]");
      if (!found) throw new Error("no line");
      return found;
    });
    fireEvent.change(within(line).getByLabelText("Orders:Quantity"), { target: { value: "6" } });
    await next();
    await finish("Orders:Materialise");
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/orders/transport-order-template/copy-order/{orderId}", {
        params: { path: { orderId: "o1" } },
        body: { dates: ["2026-10-27"], copies: 1, overrides: [{ date: "2026-10-27", copyIndex: 0, customerReference: null, lines: [{ lineId: "ol-1", quantity: 6, weightKg: null }] }] },
      }),
    );
  });

  it("without a preselected source the first step picks a template and Next waits for it", async () => {
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} />);
    const dialog = await screen.findByRole("dialog");
    expect(footerButton("Wizard:Next")).toBeDisabled();
    await waitFor(() => expect(within(dialog).getByText("Weekly Acme")).toBeInTheDocument());
    fireEvent.change(dialog.querySelector("#materialise-template")!, { target: { value: "tpl-1" } });
    expect(footerButton("Wizard:Next")).toBeEnabled();
  });

  it("a template made with Add new … opens on its page (it has no lines yet) instead of continuing the wizard", async () => {
    grants = { "Orders.TransportOrderTemplates": true, "Orders.TransportOrderTemplates.Create": true };
    features = { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Orders.Templates": "true" };
    const onOpenChange = vi.fn();
    renderWith(<MaterialiseWizard open onOpenChange={onOpenChange} />);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Orders:NewTemplateOpensItsPage")).toBeInTheDocument();
    fireEvent.click(dialog.querySelector("[data-create-for=materialise-template]")!);
    expect(screen.getByTestId("new-template-dialog")).toHaveAttribute("data-initial-name", "Typed name");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-template" }));
      await Promise.resolve();
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/templates/tpl-new" }));
    expect(calls(POST, "/api/orders/transport-order-template/{id}/materialise")).toHaveLength(0);
  });

  it("offers no Add new … row and no hint without the templates' Create permission", async () => {
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} />);
    const dialog = await screen.findByRole("dialog");
    expect(dialog.querySelector("[data-create-for=materialise-template]")).toBeNull();
    expect(within(dialog).queryByText("Orders:NewTemplateOpensItsPage")).toBeNull();
  });
});

describe("ArchiveTripsWizard", () => {
  it("skips open trips, names them, and archives the rest one by one", async () => {
    routes.POST["/api/planning/transport-trip/{id}/archive"] = (init) => ({ id: init?.params?.path?.id, isArchived: true });
    const trips: TransportTripRow[] = [
      { id: "t1", number: "TR-1", status: 3 },
      { id: "t2", number: "TR-2", status: 1 },
      { id: "t3", number: "TR-3", status: 4 },
    ];
    renderWith(<ArchiveTripsWizard open onOpenChange={() => undefined} trips={trips} />);
    const dialog = await screen.findByRole("dialog");
    expect(dialog.querySelectorAll("[data-slot=archive-selection] li")).toHaveLength(2);
    /* One trip skipped, two archived: the singular and the plural count text. */
    expect(within(dialog).getByText("PlanningExecution:ArchiveTripsSkipped.One 1 TR-2")).toBeInTheDocument();
    expect(within(dialog).getByText("PlanningExecution:ArchiveTripsIntro 2")).toBeInTheDocument();
    await finish("PlanningExecution:Archive");
    await waitFor(() => expect(calls(POST, "/api/planning/transport-trip/{id}/archive")).toHaveLength(2));
    expect(calls(POST, "/api/planning/transport-trip/{id}/archive").map(([, init]) => init!.params!.path!.id)).toEqual(["t1", "t3"]);
    await waitFor(() => expect(footerButton("Close")).toBeEnabled());
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:TripsArchived 2");
  });

  it("cannot start without an archivable trip", async () => {
    renderWith(<ArchiveTripsWizard open onOpenChange={() => undefined} trips={[{ id: "t2", number: "TR-2", status: 1 }]} />);
    await screen.findByRole("dialog");
    expect(footerButton("PlanningExecution:Archive")).toBeDisabled();
  });
});
