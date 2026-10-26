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
import { tripPickerPage } from "@/domains/planning-execution/trips";
import { barButton, gridRows, lookupItems, panel, ribbonButton, ribbonLabels, stubGlobals } from "./transport-test-mocks";
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
  lookupItems.current = {};
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

  it("exports the list (UI-GRID-013): the list's filters, the visible columns with CO2, the server's file name; a refusal stays in the dialog", async () => {
    gridRows.current = tripRows;
    const downloads: string[] = [];
    vi.stubGlobal("URL", { ...URL, createObjectURL: vi.fn(() => "blob:x"), revokeObjectURL: vi.fn() });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(this.download);
    });
    routes.POST["/api/planning/transport-trip/export"] = () => ({ data: undefined, error: { error: { code: "OpenTms.PlanningExecution:ExportUnknownColumn", message: "The list has no column \"x\" to export." } }, response: new Response(null, { status: 400 }) });
    renderWith(<TripsView filter="open" />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByRole("checkbox", { name: "PlanningExecution:HideEmpty" }));
    fireEvent.click(screen.getByText("grid-export"));
    let dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Grid:Export" }));
    expect(await within(dialog).findByText(/has no column/)).toBeInTheDocument();
    expect(downloads).toEqual([]);

    routes.POST["/api/planning/transport-trip/export"] = () => ({ data: new Blob(["x"]), error: undefined, response: new Response(null, { headers: { "Content-Disposition": "attachment; filename=transport-trips-2026-10-26.xlsx" } }) });
    dialog = screen.getByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Grid:Export" }));
    await waitFor(() => expect(downloads).toEqual(["transport-trips-2026-10-26.xlsx"]));
    const body = lastCall(POST, "/api/planning/transport-trip/export")?.body as { columns: { field: string; header: string }[]; allRows: boolean; view: string; isArchived: boolean; hideEmpty: boolean };
    expect(body).toMatchObject({ view: "Open", isArchived: false, hideEmpty: true, allRows: false });
    expect(body.columns.map((c) => c.field)).toContain("co2Kg");
    expect(body.columns.map((c) => c.field)).not.toContain("isArchived");
    expect(body.columns.find((c) => c.field === "plannedDistanceM")?.header).toBe("PlanningExecution:Distance (km)");
    click.mockRestore();
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

describe("TripPage – carrier and agreed price per execution (own fleet vs outsourced)", () => {
  const agreedLines = { currencyCode: "EUR", lines: [{ id: "a1", tripId: "t1", kind: 6, quantity: 1, unitPrice: 400, amount: 400, currencyCode: "EUR", position: 1, concurrencyStamp: "a1s" }], totals: { total: 400 }, state: { status: 2 } };
  const carrierText = () => panel("General").querySelector("[data-slot=trip-carrier]")?.textContent;
  const agreedText = () => panel("General").querySelector("[data-slot=trip-agreed-price]")?.textContent ?? null;

  it("an outsourced trip shows its carrier and the agreed purchase price from its cost lines", async () => {
    routes.GET["/api/planning/transport-trip/{id}"] = () => trip(1, { execution: 2, carrierRelationId: "car-1", carrierName: "Charter BV" });
    routes.GET["/api/financial/rate-line"] = () => agreedLines;
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    expect(carrierText()).toBe("Charter BV");
    await waitFor(() => expect(agreedText()).toBe("400.00 EUR"));
  });

  it("an outsourced trip without a carrier says none is chosen yet, never the own company", async () => {
    routes.GET["/api/planning/transport-trip/{id}"] = () => trip(1, { execution: 2, carrierRelationId: null, carrierName: null });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    expect(carrierText()).toBe("PlanningExecution:NoCarrierChosen");
  });

  it("an own-fleet trip shows the own company and no agreed price row; switching the execution follows the form", async () => {
    routes.GET["/api/financial/rate-line"] = () => agreedLines;
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    expect(carrierText()).toBe("PlanningExecution:OwnCompany");
    /* The cost lines hold an agreed price, but an own-fleet trip shows no agreed price row. */
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/financial/rate-line", expect.anything()));
    expect(agreedText()).toBeNull();

    fireEvent.change(within(panel("General")).getByLabelText(/^PlanningExecution:Execution/), { target: { value: "2" } });
    expect(carrierText()).toBe("PlanningExecution:NoCarrierChosen");
    await waitFor(() => expect(agreedText()).toBe("400.00 EUR"));
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

  it("the leg dialog edits the time windows: prefilled as HH:mm, from after till refused, the changed window sent as instants", async () => {
    const at = (time: string) => {
      const [hh, mm] = time.split(":").map(Number);
      return new Date(2026, 9, 25, hh, mm).toISOString();
    };
    routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", orderId: "o1", orderNumber: "TO-1", tripPosition: 1, concurrencyStamp: "leg-s1", origin: { name: "Acme", plannedDate: "2026-10-25", windowFrom: at("08:00"), windowTill: at("12:00") }, destination: { name: "Beta", plannedDate: "2026-10-26" } }], totalCount: 1, filteredCount: 1 });
    routes.PUT["/api/planning/leg/{id}"] = () => ({ id: "leg-1" });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:Legs");
    const legs = panel("PlanningExecution:Legs");
    await waitFor(() => expect(legs.querySelectorAll("[data-slot=mock-grid-row]")).toHaveLength(1));
    fireEvent.click(within(legs).getByText("Edit"));
    const dialog = await screen.findByRole("dialog");
    const from = dialog.querySelector<HTMLInputElement>("#trip-leg-origin-window-from")!;
    const till = dialog.querySelector<HTMLInputElement>("#trip-leg-origin-window-till")!;
    expect([from.value, till.value]).toEqual(["08:00", "12:00"]);
    expect(dialog.querySelector<HTMLInputElement>("#trip-leg-destination-window-from")!.value).toBe("");

    fireEvent.change(from, { target: { value: "13:00" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    expect(within(dialog).getByText("PlanningExecution:WindowEndsBeforeStart")).toBeInTheDocument();
    expect(PUT).not.toHaveBeenCalledWith("/api/planning/leg/{id}", expect.anything());

    fireEvent.change(from, { target: { value: "09:30" } });
    fireEvent.change(till, { target: { value: "14:00" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(PUT).toHaveBeenCalledWith("/api/planning/leg/{id}", expect.objectContaining({ params: { path: { id: "leg-1" } } })));
    const body = lastCall(PUT, "/api/planning/leg/{id}")!.body as Record<string, unknown>;
    expect(body).toMatchObject({ concurrencyStamp: "leg-s1", originPlannedDate: "2026-10-25", destinationPlannedDate: "2026-10-26", originWindow: { from: at("09:30"), till: at("14:00") } });
    expect(body.destinationWindow).toBeUndefined();
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:LegSaved");
  });

  it("the leg dialog shows the server's InvalidLegWindow refusal and stays open", async () => {
    routes.GET["/api/planning/leg"] = () => ({ rows: [{ id: "leg-1", orderId: "o1", orderNumber: "TO-1", origin: { plannedDate: "2026-10-25" }, destination: { plannedDate: "2026-10-26" } }], totalCount: 1, filteredCount: 1 });
    routes.PUT["/api/planning/leg/{id}"] = () => ({ data: undefined, error: { error: { code: "OpenTms.PlanningExecution:InvalidLegWindow", message: "The time window ends before it starts (destination)." } }, response: new Response(null, { status: 400 }) });
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:Legs");
    const legs = panel("PlanningExecution:Legs");
    await waitFor(() => expect(legs.querySelectorAll("[data-slot=mock-grid-row]")).toHaveLength(1));
    fireEvent.click(within(legs).getByText("Edit"));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(dialog.querySelector("#trip-leg-destination-window-from")!, { target: { value: "10:00" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    expect(await within(dialog).findByText("The time window ends before it starts (destination).")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("Outsource origin picks the target trip in a trip picker (not an id field) and posts its id", async () => {
    routes.POST["/api/planning/transport-trip/{id}/outsource-origin"] = () => ({ id: "leg-9" });
    lookupItems.current = { "outsource-hub": [{ id: "hub-1", name: "Hub Tilburg" }], "outsource-trip": [{ id: "t9", number: "TR-9", name: "Evening" } as never] };
    renderWith(<TripPage id="t1" />);
    await screen.findByText("PlanningExecution:TransportTrip TR-1");
    tab("PlanningExecution:Legs");
    const legs = panel("PlanningExecution:Legs");
    await waitFor(() => expect(legs.querySelectorAll("[data-slot=mock-grid-row]")).toHaveLength(1));
    fireEvent.click(within(legs).getByText("PlanningExecution:OutsourceOrigin"));
    const dialog = await screen.findByRole("dialog");
    const picker = dialog.querySelector<HTMLSelectElement>("#outsource-trip")!;
    expect(picker.tagName).toBe("SELECT");
    expect(within(dialog).getByRole("option", { name: "TR-9 · Evening" })).toBeInTheDocument();
    fireEvent.change(dialog.querySelector("#outsource-hub")!, { target: { value: "hub-1" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    expect(within(dialog).getByText("MasterData:Required")).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalledWith("/api/planning/transport-trip/{id}/outsource-origin", expect.anything());
    fireEvent.change(picker, { target: { value: "t9" } });
    await act(async () => {
      fireEvent.submit(dialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/planning/transport-trip/{id}/outsource-origin", { params: { path: { id: "t1" } }, body: { legId: "leg-1", hubRelationId: "hub-1", hubAddressId: null, toTripId: "t9" } }),
    );
  });

  it("the trip picker asks the picker endpoint with the search, the page and the leg's own trip excluded", async () => {
    routes.GET["/api/planning/transport-trip/picker"] = () => ({ rows: [{ id: "t9", number: "TR-9", status: 1 }, { id: null, number: "broken" }], totalCount: 7, filteredCount: 7 });
    const page = await tripPickerPage("t1")({ term: "eve", skip: 25, take: 25 });
    expect(GET).toHaveBeenCalledWith("/api/planning/transport-trip/picker", { params: { query: { StartRow: 25, EndRow: 50, WildcardSearch: "eve", ExcludeTripId: "t1" } } });
    expect(page).toEqual({ items: [{ id: "t9", number: "TR-9", status: 1 }], totalCount: 7 });
    await tripPickerPage("t1")({ term: "", skip: 0, take: 25 });
    expect(lastCall(GET, "/api/planning/transport-trip/picker")?.params?.query?.WildcardSearch).toBeUndefined();
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
