import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Trips list and the trip editor page (design 61 "Trips list and editor"): the gates, the designed columns and
 * the URL filter mapped onto the API's view, the command bar without New, the ribbon per status, the header save, the
 * stops grid with its row actions (move, record actuals, remove manual only), the legs grid and the cost tab. Risk
 * class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/trips", search: { filter: "today" } }) }));
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
vi.mock("@/apps/transport/wizards/PrintCmrWizard", () => ({ PrintCmrWizard: ({ open, trip }: { open: boolean; trip?: { number: string } }) => (open ? <div data-testid="print-wizard" data-trip={trip?.number} /> : null) }));
vi.mock("@/apps/transport/wizards/ArchiveTripsWizard", () => ({ ArchiveTripsWizard: ({ open, trips }: { open: boolean; trips: { id: string }[] }) => (open ? <div data-testid="archive-wizard" data-count={trips.length} /> : null) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { TripPage } from "@/apps/transport/pages/TripPage";
import { TripsView } from "@/apps/transport/views/TripsView";
import { toast } from "sonner";
import { barButton, gridRows, panel, ribbonButton, ribbonLabels, stubGlobals } from "./transport-test-mocks";
import { GET, POST, PUT, lastCall, resetClient, routes } from "./test-setup-mocks";

const VIEW = "PlanningExecution.TransportTrips";
const ALL = { [VIEW]: true, "Orders.TransportOrders": true, "PlanningExecution.TransportTrips.Update": true, "PlanningExecution.TransportTrips.Start": true, "PlanningExecution.TransportTrips.Complete": true, "PlanningExecution.TransportTrips.Cancel": true, "PlanningExecution.TransportTrips.Archive": true, "PlanningExecution.TransportTrips.AssignLegs": true, "PlanningExecution.TransportTrips.ManageStops": true, "PlanningExecution.Legs.Update": true, "Orders.TransportOrders.Print": true, "Financial.RateLines.Update": true };
const FEATURES = { "Apps.Transport": "true", "Apps.Transport.Trips": "true", "Apps.Transport.Orders": "true" };

const tripRows = [
  { id: "t1", number: "TR-1", status: 1, tripDate: "2026-10-25", name: "Morning", carrierName: null, vehicles: ["DAF 1"], drivers: ["Jan"], stopCount: 3, legCount: 2, customers: ["Acme"], orderNumbers: ["TO-1"], plannedDistanceM: 120000, loadFactor: 0.5, warnings: [{ code: 11 }] },
  { id: "t2", number: "TR-2", status: 3, tripDate: "2026-10-24", isArchived: false, warnings: [] },
];

const trip = (status: number, extra: Record<string, unknown> = {}) => ({
  id: "t1",
  number: "TR-1",
  status,
  isArchived: false,
  tripDate: "2026-10-25",
  name: "Morning",
  transportTripTypeId: "tt-1",
  transportTripTypeName: "Own fleet",
  execution: 1,
  stopCompositionMode: 1,
  firstLastStopRule: 0,
  plannedDeparture: "2026-10-25T06:00:00Z",
  concurrencyStamp: "ts-1",
  creationTime: "2026-10-25T05:00:00Z",
  createdFromOrderId: "o1",
  vehicles: [{ vehicleId: "v1", name: "DAF 1", licencePlate: "AB-12-CD" }],
  drivers: [{ driverId: "d1", name: "Jan" }],
  stops: [
    { id: "st1", position: 1, waypointTypeId: "wp-load", relationName: "Acme", city: "Utrecht", isManual: false, legs: [{ legId: "leg-1" }], plannedArrival: "2026-10-25T07:00:00Z" },
    { id: "st2", position: 2, waypointTypeId: "wp-unload", relationName: "Beta", city: "Breda", isManual: false, legs: [{ legId: "leg-1" }], actualArrival: "2026-10-25T09:00:00Z" },
    { id: "st3", position: 3, waypointTypeId: "wp-rest", relationName: "Depot", city: "Tilburg", isManual: true, legs: [] },
  ],
  warnings: [{ code: 6, stopId: "st2" }],
  costAmount: 400,
  revenueAmount: 650,
  ...extra,
});

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { ...ALL };
  features = { ...FEATURES };
  gridRows.current = [];
  resetClient();
  routes.GET["/api/planning/transport-trip"] = () => ({ rows: tripRows, totalCount: 2, filteredCount: 2 });
  routes.GET["/api/planning/transport-trip/{id}"] = () => trip(1);
  routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", orderId: "o1", orderNumber: "TO-1", tripPosition: 1, origin: { name: "Acme", city: "Utrecht" }, destination: { name: "Beta", city: "Breda" }, distanceM: 90000, allocatedCostAmount: 400, allocatedRevenueAmount: 650 }], totalCount: 1, filteredCount: 1 });
  routes.GET["/api/financial/rate-line"] = () => ({ currencyCode: "EUR", lines: [{ id: "c1", tripId: "t1", legId: "leg-1", kind: 7, quantity: 90, unitPrice: 1.2, amount: 108, position: 1, concurrencyStamp: "c1s" }], totals: { total: 108 }, state: { status: 2 } });
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-load", name: "Load" }, { id: "wp-unload", name: "Unload" }, { id: "wp-rest", name: "Rest" }];
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const columns = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span")).map((el) => `${el.getAttribute("data-column")}${el.getAttribute("data-hidden") === "true" ? " (hidden)" : ""}`);
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });

describe("TripsView", () => {
  it("gates on the permission and the feature", () => {
    grants = {};
    const { unmount } = renderWith(<TripsView filter="today" />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    unmount();
    grants = { ...ALL };
    features = { ...FEATURES, "Apps.Transport.Trips": "false" };
    renderWith(<TripsView filter="today" />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
  });

  it("has the designed columns, maps the filter onto the view and hides archived trips", async () => {
    gridRows.current = tripRows;
    renderWith(<TripsView filter="this-week" />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("planning.transport.transportTrips");
    expect(columns()).toEqual([
      "number", "status", "tripDate", "name", "transportTripTypeName", "carrierName", "vehicles", "drivers", "plannedDeparture", "plannedArrival", "stopCount", "legCount", "customers", "orders",
      "plannedDistanceM", "loadFactor", "co2Kg", "costAmount", "revenueAmount", "warnings",
      "isArchived (hidden)", "execution (hidden)", "route (hidden)", "actualDeparture (hidden)", "actualArrival (hidden)",
    ]);
    await waitFor(() => expect(lastCall(GET, "/api/planning/transport-trip")?.params?.query).toMatchObject({ View: "ThisWeek", IsArchived: false }));
    const first = document.querySelector("[data-slot=mock-grid-row]") as HTMLElement;
    expect(within(first).getByText("PlanningExecution:Status.Planned")).toBeInTheDocument();
    expect(within(first).getByRole("img", { name: /PlanningExecution:Warnings/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox", { name: "PlanningExecution:HideEmpty" }));
    await waitFor(() => expect(lastCall(GET, "/api/planning/transport-trip")?.params?.query?.HideEmpty).toBe(true));
  });

  it("has no New; Print, Archive, Complete and Cancel follow the selection and the status", async () => {
    gridRows.current = tripRows;
    renderWith(<TripsView filter="today" />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText(/NewTransportTrip/)).toBeNull();
    expect(barButton("PlanningExecution:ArchiveTrips")).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(screen.getByText("select-first"));
    expect(barButton("PlanningExecution:Complete")).not.toHaveAttribute("aria-disabled");
    fireEvent.click(barButton("PlanningExecution:ArchiveTrips"));
    expect(screen.getByTestId("archive-wizard")).toHaveAttribute("data-count", "1");
  });

  it("Complete from the command bar confirms and posts", async () => {
    gridRows.current = tripRows;
    routes.POST["/api/planning/transport-trip/{id}/complete"] = () => trip(3);
    renderWith(<TripsView filter="today" />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("select-first"));
    fireEvent.click(barButton("PlanningExecution:Complete"));
    const confirm = await screen.findByRole("alertdialog");
    await click(within(confirm).getByRole("button", { name: "PlanningExecution:Complete" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/planning/transport-trip/{id}/complete", expect.objectContaining({ params: { path: { id: "t1" } } })));
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:TransportTripCompleted");
  });

  it("opens a trip through useOpenRecord", async () => {
    gridRows.current = tripRows;
    renderWith(<TripsView filter="today" />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getAllByText("open-row")[0]);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/trips/t1" }));
  });
});

describe("TripPage – ribbon and header", () => {
  it.each([
    [1, false, ["Save", "Close", "PlanningExecution:Start", "PlanningExecution:Complete", "PlanningExecution:CancelTrip", "Orders:PrintConsignmentNote"]],
    [2, false, ["Save", "Close", "PlanningExecution:Complete", "PlanningExecution:CancelTrip", "Orders:PrintConsignmentNote"]],
    [3, false, ["Close", "PlanningExecution:Archive", "Orders:PrintConsignmentNote"]],
    [3, true, ["Close", "PlanningExecution:Unarchive", "Orders:PrintConsignmentNote"]],
    [4, false, ["Close", "PlanningExecution:Archive", "Orders:PrintConsignmentNote"]],
  ])("status %i archived=%s shows %j", async (status, archived, expected) => {
    routes.GET["/api/planning/transport-trip/{id}"] = () => trip(status, { isArchived: archived });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    await waitFor(() => expect(ribbonLabels()).toEqual(expected));
  });

  it("saves the header through PUT with the stamp and the planned departure", async () => {
    routes.PUT["/api/planning/transport-trip/{id}"] = () => trip(1, { name: "Evening", concurrencyStamp: "ts-2" });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    fireEvent.change(within(panel("General")).getByLabelText(/^Orders:Name/), { target: { value: "Evening" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(lastCall(PUT, "/api/planning/transport-trip/{id}")!.body).toMatchObject({ concurrencyStamp: "ts-1", name: "Evening", tripDate: "2026-10-25", execution: 1 });
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:TransportTripSaved");
  });

  it("Start posts the command and shows the API's refusal in the page", async () => {
    routes.POST["/api/planning/transport-trip/{id}/start"] = () => ({ data: undefined, error: { error: { message: "'Start' is not possible" } }, response: new Response(null, { status: 409 }) });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    await click(ribbonButton("PlanningExecution:Start")!);
    expect(await screen.findByText("'Start' is not possible")).toBeInTheDocument();
  });

  it("resources are editable for a Planned trip created from an order; a Planboard link otherwise", async () => {
    const { unmount } = renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    expect(within(panel("General")).getByRole("button", { name: "Edit PlanningExecution:Carrier" })).toBeInTheDocument();
    unmount();
    routes.GET["/api/planning/transport-trip/{id}"] = () => trip(1, { createdFromOrderId: null });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    expect(within(panel("General")).queryByRole("button", { name: "Edit PlanningExecution:Carrier" })).toBeNull();
    expect(within(panel("General")).getByText("PlanningExecution:OpenPlanboard")).toBeInTheDocument();
  });
});

describe("TripPage – stops, legs, cost", () => {
  it("lists the stops with their warnings; Remove only for a manual stop, move blocked by actuals; Record actuals posts", async () => {
    routes.POST["/api/planning/transport-trip/{id}/record-stop-actuals/{stopId}"] = () => trip(1, { concurrencyStamp: "ts-2" });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:Stops");
    const stops = panel("PlanningExecution:Stops");
    const rows = await within(stops).findAllByTestId("mock-grid");
    expect(rows[0].getAttribute("data-grid-id")).toBe("planning.transport.tripStops");
    const stopRows = stops.querySelectorAll("[data-slot=mock-grid-row]");
    expect(stopRows).toHaveLength(3);
    expect(within(stopRows[0] as HTMLElement).getByText("PlanningExecution:RemoveStop")).toBeDisabled();
    expect(within(stopRows[2] as HTMLElement).getByText("PlanningExecution:RemoveStop")).toBeEnabled();
    expect(within(stopRows[1] as HTMLElement).getByText("Orders:MoveUp")).toBeDisabled();
    expect(within(stopRows[1] as HTMLElement).getByRole("img", { name: /PlanningExecution:Warnings/ })).toBeInTheDocument();

    fireEvent.click(within(stopRows[0] as HTMLElement).getByText("PlanningExecution:RecordActuals"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/^PlanningExecution:ActualArrival/), { target: { value: "2026-10-25" } });
    fireEvent.change(within(dialog).getAllByLabelText(/^Orders:Time/)[0], { target: { value: "07:15" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/planning/transport-trip/{id}/record-stop-actuals/{stopId}", expect.objectContaining({ params: { path: { id: "t1", stopId: "st1" } } })));
    const body = lastCall(POST, "/api/planning/transport-trip/{id}/record-stop-actuals/{stopId}")!.body as { actualArrival: string | null };
    expect(body.actualArrival).not.toBeNull();
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:ActualsRecorded");
  });

  it("the legs grid shows the trip's legs with the order link; Unassign posts the leg ids", async () => {
    routes.POST["/api/planning/transport-trip/{id}/unassign-legs"] = () => trip(1, { concurrencyStamp: "ts-2" });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:Legs");
    const legs = panel("PlanningExecution:Legs");
    await waitFor(() => expect(legs.querySelectorAll("[data-slot=mock-grid-row]")).toHaveLength(1));
    expect(within(legs).getByText("TO-1")).toBeInTheDocument();
    fireEvent.click(within(legs).getByText("PlanningExecution:UnassignLegs"));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/planning/transport-trip/{id}/unassign-legs", { params: { path: { id: "t1" } }, body: { legIds: ["leg-1"] } }));
    fireEvent.click(within(legs).getByText("PlanningExecution:OpenOrder"));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o1" }));
  });

  it("the cost tab shows the cost lines, the totals and the allocation per leg", async () => {
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:CostAndRevenue");
    const costs = panel("PlanningExecution:CostAndRevenue");
    await waitFor(() => expect(within(costs).getByText("Financial:RateLineKind.OwnFleetDistance")).toBeInTheDocument());
    const totals = costs.querySelector("[data-slot=totals-block]") as HTMLElement;
    expect(within(totals).getByText("108.00")).toBeInTheDocument();
    expect(within(totals).getByText("650.00")).toBeInTheDocument();
    expect(within(costs).getByRole("table")).toHaveTextContent("TO-1");
    expect(GET).toHaveBeenCalledWith("/api/financial/rate-line", expect.objectContaining({ params: { query: { TripId: "t1" } } }));
  });
});
