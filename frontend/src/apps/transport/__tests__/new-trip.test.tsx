import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* New trip on the Trips list (design 61 "Trip type defaults" 4 and 5, build note 3; PO 2026-10-29 superseding "no New"):
 * the ribbon New only with PlanningExecution.TransportTrips.Create; the dialog preselects the default type and presets
 * its defaults (execution, stop composition and first/last stop rule too); trip date today, the planned departure date
 * following the trip date until changed; another type switches the fields that still hold the previous preset and
 * keeps the user's; the save posts the designed body to POST /api/planning/transport-trip and opens the trip page.
 * The generated client is the path dispatcher; the pickers are native stand-ins. Risk class: Medium. */

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
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()), EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/tree-select", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/tree-select")>()), TreeSelect: (await import("./transport-test-mocks")).MockTreeSelect }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({ ...(await importOriginal<typeof import("@/components/ui/date-picker")>()), DatePicker: (await import("./transport-test-mocks")).MockDatePicker }));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/apps/transport/wizards/PrintCmrWizard", () => ({ PrintCmrWizard: () => null }));
vi.mock("@/apps/transport/wizards/ArchiveTripsWizard", () => ({ ArchiveTripsWizard: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { TripsView } from "@/apps/transport/views/TripsView";
import { toast } from "sonner";
import { toIsoDate } from "@/lib/format";
import { barButton, fail, gridRows, lookupItems, ribbonButton, stubGlobals } from "./transport-test-mocks";
import { POST, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const VIEW = "PlanningExecution.TransportTrips";
const CREATE = "PlanningExecution.TransportTrips.Create";
const FEATURES = { "Apps.Transport": "true", "Apps.Transport.Trips": "true" };

/* tt-2 is the tenant's default type. */
const TYPES: Record<string, Record<string, unknown>> = {
  "tt-2": {
    id: "tt-2",
    name: "Charter",
    defaultCarrierRelationId: "car-2",
    defaultCarrierRelationName: "Carrier Two",
    defaultCarrierContactPersonId: "cp-2",
    defaultVehicleIds: ["v1"],
    defaultVehicles: [{ id: "v1", name: "DAF 1", kind: 1 }],
    defaultDriverIds: ["d1"],
    defaultDrivers: [{ id: "d1", name: "Jan" }],
    defaultDepartureRelationId: "dep-1",
    defaultDepartureRelationName: "Depot One",
    defaultDepartureAddressId: "adr-dep-1",
    defaultDepartureTime: "06:30:00",
    defaultArrivalRelationId: "arr-1",
    defaultArrivalRelationName: "Hub One",
    defaultArrivalAddressId: "adr-arr-1",
    defaultExecution: 2,
    defaultStopCompositionMode: 2,
    defaultFirstLastStopRule: 3,
  },
  "tt-1": {
    id: "tt-1",
    name: "Own fleet",
    defaultVehicleCombinationId: "combi-1",
    defaultVehicleCombinationName: "Combi 1",
    defaultVehicleIds: [],
    defaultDriverIds: ["d2"],
    defaultDrivers: [{ id: "d2", name: "Ali" }],
    defaultDepartureRelationId: "dep-2",
    defaultDepartureRelationName: "Depot Two",
    defaultDepartureAddressId: "adr-dep-2",
    defaultDepartureTime: "08:00:00",
    defaultExecution: 1,
    defaultStopCompositionMode: 1,
    defaultFirstLastStopRule: 0,
  },
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  vi.mocked(toast.success).mockReset();
  grants = { [VIEW]: true, [CREATE]: true };
  features = { ...FEATURES };
  gridRows.current = [];
  resetClient();
  routes.GET["/api/planning/transport-trip"] = () => ({ rows: [], totalCount: 0, filteredCount: 0 });
  routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }, { id: "tt-2", name: "Charter" }];
  routes.GET["/api/planning/transport-trip-type"] = () => ({ rows: [{ id: "tt-2", name: "Charter", isDefault: true }], totalCount: 2, filteredCount: 2 });
  routes.GET["/api/planning/transport-trip-type/{id}"] = (init) => TYPES[init?.params?.path?.id as string];
  routes.GET["/api/master-data/relation/{id}"] = (init) => ({ id: init?.params?.path?.id, contactPersons: [{ id: "cp-2", firstName: "Kees", lastName: "Vos" }] });
  routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => [{ id: `adr-${init?.params?.path?.id}`, street: "Kade", postalCode: "5000 AA", city: "Tilburg" }];
  routes.GET["/api/master-data/vehicle-combination/lookup"] = () => [{ id: "combi-1", name: "Combi 1" }];
  routes.POST["/api/planning/transport-trip"] = () => ({ id: "t-new", number: "TR-9" });
  lookupItems.current = {
    "*-carrierRelation": [{ id: "car-2", name: "Carrier Two" }, { id: "car-3", name: "Carrier Three" }],
    "*-departureRelation": [{ id: "dep-1", name: "Depot One" }, { id: "dep-2", name: "Depot Two" }],
    "*-arrivalRelation": [{ id: "arr-1", name: "Hub One" }],
    "*-vehicleIds": [{ id: "v1", name: "DAF 1" }, { id: "v2", name: "Trailer 2" }],
    "*-driverIds": [{ id: "d1", name: "Jan" }, { id: "d2", name: "Ali" }, { id: "d3", name: "Piet" }],
  } as never;
});

function renderView() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <TripsView filter="today" />
    </QueryClientProvider>,
  );
}

const dialog = () => screen.getByRole("dialog");
const control = <T extends HTMLElement = HTMLSelectElement>(field: string) => {
  const found = document.querySelector<T>(`[data-slot=new-trip-form] [id$='-${field}']`);
  if (!found) throw new Error(`no control ${field}`);
  return found;
};
const set = (field: string, value: string) => fireEvent.change(control(field), { target: { value } });
const box = (field: string, name: string) => control<HTMLElement>(field).querySelector<HTMLInputElement>(`input[aria-label='${name}']`)!;
const checkedIn = (field: string) => Array.from(control<HTMLElement>(field).querySelectorAll<HTMLInputElement>("input[type=checkbox]")).filter((b) => b.checked).map((b) => b.getAttribute("aria-label"));
const save = async () =>
  act(async () => {
    fireEvent.click(ribbonButton("Save")!);
    await Promise.resolve();
  });

async function openNewTrip() {
  renderView();
  await screen.findByTestId("mock-grid");
  fireEvent.click(barButton("PlanningExecution:NewTrip"));
  await screen.findByRole("dialog");
  /* the default type's detail arrived and was applied */
  await waitFor(() => expect(control("carrierRelation").value).toBe("car-2"));
}

describe("TripsView – New (design 61 \"Trip type defaults\" 4)", () => {
  it("is offered only with the Create permission", async () => {
    grants = { [VIEW]: true };
    const { unmount } = renderView();
    await screen.findByTestId("mock-grid");
    expect(() => barButton("PlanningExecution:NewTrip")).toThrow();
    unmount();

    grants = { [VIEW]: true, [CREATE]: true };
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(barButton("PlanningExecution:NewTrip"));
    expect(await screen.findByRole("dialog")).toHaveTextContent("PlanningExecution:NewTransportTrip");
  });
});

describe("NewTripDialog", () => {
  it("preselects the default type and presets every field it defines; trip date and planned departure date are today", async () => {
    await openNewTrip();
    const today = toIsoDate(new Date());
    expect(control("transportTripTypeId").value).toBe("tt-2");
    expect(control<HTMLInputElement>("tripDate").value).toBe(today);
    expect(control<HTMLInputElement>("departureDate").value).toBe(today);
    expect(control<HTMLInputElement>("departureTime").value).toBe("06:30");
    expect(control("execution").value).toBe("2");
    expect(control("stopCompositionMode").value).toBe("2");
    expect(control("firstLastStopRule").value).toBe("3");
    expect(control("departureRelation").value).toBe("dep-1");
    expect(control("arrivalRelation").value).toBe("arr-1");
    expect(checkedIn("vehicleIds")).toEqual(["DAF 1"]);
    expect(checkedIn("driverIds")).toEqual(["Jan"]);
    await waitFor(() => expect(control("carrierContactPersonId").value).toBe("cp-2"));
    await waitFor(() => expect(control("departureAddressId").value).toBe("adr-dep-1"));
    await waitFor(() => expect(control("arrivalAddressId").value).toBe("adr-arr-1"));
  });

  it("saves the preset with the trip date the planned departure follows, then opens the trip page", async () => {
    await openNewTrip();
    fireEvent.change(control<HTMLInputElement>("tripDate"), { target: { value: "2026-11-05" } });
    expect(control<HTMLInputElement>("departureDate").value).toBe("2026-11-05");
    fireEvent.change(control<HTMLInputElement>("name"), { target: { value: "Morning run" } });
    await save();

    await waitFor(() => expect(calls(POST, "/api/planning/transport-trip")).toHaveLength(1));
    expect(lastCall(POST, "/api/planning/transport-trip")!.body).toEqual({
      transportTripTypeId: "tt-2",
      tripDate: "2026-11-05",
      plannedDeparture: new Date(2026, 10, 5, 6, 30).toISOString(),
      name: "Morning run",
      execution: 2,
      carrierRelationId: "car-2",
      carrierContactPersonId: "cp-2",
      vehicleCombinationId: null,
      vehicleIds: ["v1"],
      driverIds: ["d1"],
      departureRelationId: "dep-1",
      departureAddressId: "adr-dep-1",
      arrivalRelationId: "arr-1",
      arrivalAddressId: "adr-arr-1",
      stopCompositionMode: 2,
      firstLastStopRule: 3,
    });
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/trips/t-new" })));
    expect(toast.success).toHaveBeenCalledWith("PlanningExecution:TransportTripSaved");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("a planned departure date the user changed no longer follows the trip date", async () => {
    await openNewTrip();
    fireEvent.change(control<HTMLInputElement>("departureDate"), { target: { value: "2026-11-06" } });
    fireEvent.change(control<HTMLInputElement>("tripDate"), { target: { value: "2026-11-05" } });
    expect(control<HTMLInputElement>("departureDate").value).toBe("2026-11-06");
  });

  it("another type switches the fields still holding the previous preset and keeps the user's", async () => {
    await openNewTrip();
    /* the user changes the drivers and the time; the rest stays the default type's preset */
    fireEvent.click(box("driverIds", "Piet"));
    fireEvent.change(control<HTMLInputElement>("departureTime"), { target: { value: "07:00" } });
    set("transportTripTypeId", "tt-1");
    await waitFor(() => expect(control("vehicleCombinationId").value).toBe("combi-1"));
    expect(control("carrierRelation").value).toBe("");
    expect(control("carrierContactPersonId").value).toBe("");
    expect(checkedIn("vehicleIds")).toEqual([]);
    expect(checkedIn("driverIds")).toEqual(["Jan", "Piet"]);
    expect(control<HTMLInputElement>("departureTime").value).toBe("07:00");
    expect(control("departureRelation").value).toBe("dep-2");
    expect(control("arrivalRelation").value).toBe("");
    expect(control("execution").value).toBe("1");
    expect(control("stopCompositionMode").value).toBe("1");
    expect(control("firstLastStopRule").value).toBe("0");

    await save();
    await waitFor(() => expect(calls(POST, "/api/planning/transport-trip")).toHaveLength(1));
    const today = toIsoDate(new Date());
    const [y, m, d] = today.split("-").map(Number);
    expect(lastCall(POST, "/api/planning/transport-trip")!.body).toEqual({
      transportTripTypeId: "tt-1",
      tripDate: today,
      plannedDeparture: new Date(y, m - 1, d, 7, 0).toISOString(),
      name: null,
      execution: 1,
      carrierRelationId: null,
      carrierContactPersonId: null,
      vehicleCombinationId: "combi-1",
      vehicleIds: [],
      driverIds: ["d1", "d3"],
      departureRelationId: "dep-2",
      departureAddressId: "adr-dep-2",
      arrivalRelationId: null,
      arrivalAddressId: null,
      stopCompositionMode: 1,
      firstLastStopRule: 0,
    });
  });

  it("a carrier preset from the type makes the trip Subcontracted, as the server's fill does (PO 2026-10-29, point 2)", async () => {
    /* A type with a default carrier and the default execution left at Own fleet (a new type's starting value). */
    TYPES["tt-3"] = { ...TYPES["tt-2"], id: "tt-3", defaultExecution: 1 };
    routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-3", name: "Charter (own fleet default)" }];
    routes.GET["/api/planning/transport-trip-type"] = () => ({ rows: [{ id: "tt-3", isDefault: true }], totalCount: 1, filteredCount: 1 });
    try {
      await openNewTrip();
      await save();
      await waitFor(() => expect(calls(POST, "/api/planning/transport-trip")).toHaveLength(1));
      const body = lastCall(POST, "/api/planning/transport-trip")!.body as { carrierRelationId: string | null; execution?: number };
      expect(body.carrierRelationId).toBe("car-2");
      /* Subcontracted, or left to the server (which then makes it Subcontracted); never Own fleet with the type's carrier. */
      expect([2, undefined]).toContain(body.execution);
    } finally {
      delete TYPES["tt-3"];
    }
  });

  it("a carrier the user picked survives a type switch, its contact with it", async () => {
    await openNewTrip();
    set("carrierRelation", "car-3");
    expect(control("carrierContactPersonId").value).toBe("");
    set("transportTripTypeId", "tt-1");
    await waitFor(() => expect(control("vehicleCombinationId").value).toBe("combi-1"));
    expect(control("carrierRelation").value).toBe("car-3");
  });

  it("without a default type and a time nothing is posted and the required fields say so", async () => {
    routes.GET["/api/planning/transport-trip-type"] = () => ({ rows: [{ id: "tt-1", isDefault: false }], totalCount: 2, filteredCount: 2 });
    renderView();
    await screen.findByTestId("mock-grid");
    fireEvent.click(barButton("PlanningExecution:NewTrip"));
    await screen.findByRole("dialog");
    await waitFor(() => expect(control("transportTripTypeId").querySelector("option[value='tt-1']")).not.toBeNull());
    expect(control("transportTripTypeId").value).toBe("");
    await save();
    expect(calls(POST, "/api/planning/transport-trip")).toHaveLength(0);
    expect(within(dialog()).getAllByText("MasterData:Required").length).toBeGreaterThanOrEqual(2);
  });

  it("shows the server's refusal in the dialog and stays open", async () => {
    routes.POST["/api/planning/transport-trip"] = () => fail("The trip type is archived.");
    await openNewTrip();
    await save();
    expect(await within(dialog()).findByText("The trip type is archived.")).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("offers \"Add new …\" on the type picker with the trip type Create permission (UI-LOOK-004)", async () => {
    grants = { [VIEW]: true, [CREATE]: true, "PlanningExecution.TransportTripTypes.Create": true };
    features = { ...FEATURES, "Apps.MasterData": "true", "Apps.MasterData.PlanningExecution": "true", "Apps.MasterData.PlanningExecution.Trips": "true", "Apps.MasterData.PlanningExecution.Trips.TransportTripTypes": "true" };
    await openNewTrip();
    expect(document.querySelector("[data-slot=new-trip-form] [data-create-for$='-transportTripTypeId']")).not.toBeNull();
  });
});
