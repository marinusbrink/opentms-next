import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The three wizards of design 61 (UI-WIZ-001 to -005): Print CMR (the default profile preselected, the box overrides,
 * the lines to include, one print per order with the chosen lines, the Download / Print buttons per document),
 * New from template / Copy order (the dates per weekday over a range, one materialise call per date with the copies)
 * and Archive trips (open trips skipped and named, one archive call per trip). Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { features: { values: {} }, auth: { grantedPolicies: {} } } }) }));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { ArchiveTripsWizard } from "@/apps/transport/wizards/ArchiveTripsWizard";
import { MaterialiseWizard } from "@/apps/transport/wizards/MaterialiseWizard";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import { toast } from "sonner";
import { stubGlobals } from "./transport-test-mocks";
import { POST, calls, resetClient, routes } from "./test-setup-mocks";

beforeAll(stubGlobals);
beforeEach(() => {
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
    expect(dialog.querySelector("[data-slot=materialise-dates]")).toHaveTextContent("Orders:DatesSelected");
    await next();
    fireEvent.change(within(dialog).getByLabelText(/^Orders:Copies/), { target: { value: "2" } });
    await next();
    expect(within(dialog).getByText("Weekly Acme")).toBeInTheDocument();
    await finish("Orders:Materialise");
    await waitFor(() => expect(calls(POST, "/api/orders/transport-order-template/{id}/materialise")).toHaveLength(4));
    const dates = calls(POST, "/api/orders/transport-order-template/{id}/materialise").map(([, init]) => (init!.body as { dates: string[]; copies: number }).dates[0]);
    expect(dates).toEqual(["2026-10-26", "2026-10-27", "2026-10-29", "2026-10-30"]);
    expect((calls(POST, "/api/orders/transport-order-template/{id}/materialise")[0][1]!.body as { copies: number }).copies).toBe(2);
    expect(toast.success).toHaveBeenCalledWith("Orders:OrdersCreated");
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

  it("without a preselected source the first step picks a template and Next waits for it", async () => {
    renderWith(<MaterialiseWizard open onOpenChange={() => undefined} />);
    const dialog = await screen.findByRole("dialog");
    expect(footerButton("Wizard:Next")).toBeDisabled();
    await waitFor(() => expect(within(dialog).getByText("Weekly Acme")).toBeInTheDocument());
    fireEvent.change(within(dialog).getByLabelText(/^Orders:TransportOrderTemplate/), { target: { value: "tpl-1" } });
    expect(footerButton("Wizard:Next")).toBeEnabled();
  });
});

describe("ArchiveTripsWizard", () => {
  it("skips open trips, names them, and archives the rest one by one", async () => {
    routes.POST["/api/planning/transport-trip/{id}/archive"] = (init) => ({ id: init?.params?.path?.id, isArchived: true });
    const trips = [
      { id: "t1", number: "TR-1", status: 3 },
      { id: "t2", number: "TR-2", status: 1 },
      { id: "t3", number: "TR-3", status: 4 },
    ];
    renderWith(<ArchiveTripsWizard open onOpenChange={() => undefined} trips={trips} />);
    const dialog = await screen.findByRole("dialog");
    expect(dialog.querySelectorAll("[data-slot=archive-selection] li")).toHaveLength(2);
    expect(within(dialog).getByText("PlanningExecution:ArchiveTripsSkipped")).toBeInTheDocument();
    await finish("PlanningExecution:Archive");
    await waitFor(() => expect(calls(POST, "/api/planning/transport-trip/{id}/archive")).toHaveLength(2));
    expect(calls(POST, "/api/planning/transport-trip/{id}/archive").map(([, init]) => init!.params!.path!.id)).toEqual(["t1", "t3"]);
    await waitFor(() => expect(footerButton("Close")).toBeEnabled());
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:TripsArchived");
  });

  it("cannot start without an archivable trip", async () => {
    renderWith(<ArchiveTripsWizard open onOpenChange={() => undefined} trips={[{ id: "t2", number: "TR-2", status: 1 }]} />);
    await screen.findByRole("dialog");
    expect(footerButton("PlanningExecution:Archive")).toBeDisabled();
  });
});
