import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The trip type's presets in the order's Tevens trip tab (design 61 "Trip type defaults" 5, build note 2): ticking
 * "Create a trip" with the default type presets the type's carrier and contact, drivers, departure (relation, address,
 * time) and fills what the type leaves empty from the stops (arrival from the last stop, the date from the first);
 * choosing another type switches the fields still holding the previous preset (with the stops' fallback) and keeps the
 * user's; a saved request counts as holding its type's preset; a switch that removes the carrier takes the agreed price
 * with it; the save sends the preset. The generated client is the path dispatcher; pickers are native stand-ins.
 * Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Trips": "true" } } } }),
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
vi.mock("@/apps/transport/wizards/PrintCmrWizard", () => ({ PrintCmrWizard: () => null }));
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { OrderPage } from "@/apps/transport/pages/OrderPage";
import { lookupItems, ribbonButton, section, stubGlobals } from "./transport-test-mocks";
import { GET, PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const ALL = { "Orders.TransportOrders": true, "PlanningExecution.TransportTrips": true, "Orders.TransportOrders.Update": true, "Orders.TransportOrders.Confirm": true, "Financial.RateLines.Update": true };

const stops = [
  { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationName: "Acme", relationAddressId: "adr-1", city: "Utrecht", date: "2026-10-25", windowFrom: "08:00:00" },
  { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationName: "Beta", relationAddressId: "adr-2", city: "Gouda", date: "2026-10-26" },
];
const order = (extra: Record<string, unknown> = {}) => ({
  id: "o1",
  number: null,
  status: 1,
  transportOrderTypeId: "type-1",
  transportOrderTypeName: "Standard",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-25T08:00:00Z",
  defaultStops: [],
  lines: [{ id: "l1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 2, paymentInstruction: 0, stops }],
  financialTerms: { currencyCode: "EUR", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] },
  ...extra,
});

/* tt-2 is the tenant's default type: carrier, drivers and departure (with a time), no arrival. tt-1: a combination and
 * an arrival only. */
const TYPES: Record<string, Record<string, unknown>> = {
  "tt-2": {
    id: "tt-2",
    defaultCarrierRelationId: "car-t",
    defaultCarrierRelationName: "Trucker",
    defaultCarrierContactPersonId: "cp-t",
    defaultDriverIds: ["d1"],
    defaultDrivers: [{ id: "d1", name: "Jan" }],
    defaultDepartureRelationId: "dep-t",
    defaultDepartureRelationName: "Depot T",
    defaultDepartureAddressId: "adr-dep-t",
    defaultDepartureTime: "06:00:00",
    defaultExecution: 2,
  },
  "tt-1": {
    id: "tt-1",
    defaultVehicleCombinationId: "combi-1",
    defaultVehicleCombinationName: "Combi 1",
    defaultArrivalRelationId: "arr-1",
    defaultArrivalRelationName: "Hub One",
    defaultArrivalAddressId: "adr-arr-1",
    defaultExecution: 1,
  },
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  grants = { ...ALL };
  lookupItems.current = { "*-trip-drivers": [{ id: "d1", name: "Jan" }, { id: "d2", name: "Ali" }] } as never;
  resetClient();
  routes.GET["/api/orders/transport-order/{id}"] = () => order();
  routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 1, defaultWaypoints: [] });
  routes.GET["/api/financial/rate-line"] = () => ({ currencyCode: "EUR", lines: [], totals: { total: 0 }, state: { status: 2 } });
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", name: "Pallet" }];
  routes.GET["/api/orders/waypoint-type/lookup"] = () => [{ id: "wp-load", name: "Load" }, { id: "wp-unload", name: "Unload" }];
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }];
  routes.GET["/api/planning/transport-trip-type/lookup"] = () => [{ id: "tt-1", name: "Own fleet" }, { id: "tt-2", name: "Charter" }];
  routes.GET["/api/planning/transport-trip-type"] = () => ({ rows: [{ id: "tt-2", name: "Charter", isDefault: true }], totalCount: 2, filteredCount: 2 });
  routes.GET["/api/planning/transport-trip-type/{id}"] = (init) => TYPES[init?.params?.path?.id as string];
  routes.GET["/api/master-data/relation/{id}"] = (init) => ({ id: init?.params?.path?.id, contactPersons: [{ id: "cp-t", firstName: "Kees", lastName: "Vos" }] });
  routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => [{ id: String(init?.params?.path?.id).replace(/^(rel-)?/, "adr-"), street: "Kade", postalCode: "5000 AA", city: "Tilburg", addressTypeId: "typed" }];
  routes.GET["/api/master-data/vehicle-combination/lookup"] = () => [{ id: "combi-1", name: "Combi 1" }];
  routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order({ concurrencyStamp: "stamp-2" });
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <OrderPage id="o1" />
    </QueryClientProvider>,
  );
}

const trip = () => section("Orders:TripRequest");
const field = (suffix: string) => trip().querySelector<HTMLInputElement | HTMLSelectElement>(`[id$='-trip-${suffix}']`)!;
const checkedDrivers = () => Array.from(field("drivers").querySelectorAll<HTMLInputElement>("input[type=checkbox]")).filter((b) => b.checked).map((b) => b.getAttribute("aria-label"));
const flush = () =>
  act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
const saveRequest = async () => {
  await act(async () => {
    fireEvent.click(ribbonButton("Save")!);
    await Promise.resolve();
  });
  await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
  return (lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body as { tripRequest: Record<string, unknown> }).tripRequest;
};
const localParts = (iso: unknown) => {
  const d = new Date(iso as string);
  return [d.getFullYear(), d.getMonth() + 1, d.getDate(), d.getHours(), d.getMinutes()];
};

async function tickWithDefaultType() {
  renderPage();
  await screen.findByRole("heading", { level: 1 });
  trip();
  /* the default type's detail is loaded before the tick, so the tick presets at once */
  await waitFor(() => expect(GET.mock.calls.some(([p, init]) => p === "/api/planning/transport-trip-type/{id}" && init?.params?.path?.id === "tt-2")).toBe(true));
  await flush();
  fireEvent.click(within(trip()).getByRole("checkbox", { name: "Orders:TripRequestHint" }));
  await waitFor(() => expect(field("departureRelation").value).toBe("dep-t"));
}

describe("Tevens trip – the trip type's presets (design 61 \"Trip type defaults\" 5)", () => {
  it("ticking presets the default type's fields; the type's departure and time win, the stops fill the arrival and the date", async () => {
    await tickWithDefaultType();
    expect(field("transportTripTypeId").value).toBe("tt-2");
    expect(field("departureRelation").value).toBe("dep-t");
    await waitFor(() => expect(field("departureAddress").value).toBe("adr-dep-t"));
    expect(field("arrivalRelation").value).toBe("rel-2");
    await waitFor(() => expect(field("arrivalAddress").value).toBe("adr-2"));
    expect(field("departureDate").value).toBe("2026-10-25");
    expect(field("departureTime").value).toBe("06:00");
    /* a preset carrier or driver opens Advanced so it is seen */
    expect(field("carrier").value).toBe("car-t");
    await waitFor(() => expect(field("carrierContact").value).toBe("cp-t"));
    expect(checkedDrivers()).toEqual(["Jan"]);

    const body = await saveRequest();
    expect(body).toMatchObject({
      transportTripTypeId: "tt-2",
      departureRelationId: "dep-t",
      departureAddressId: "adr-dep-t",
      arrivalRelationId: "rel-2",
      arrivalAddressId: "adr-2",
      carrierRelationId: "car-t",
      carrierContactPersonId: "cp-t",
      vehicleCombinationId: null,
      driverIds: ["d1"],
    });
    expect(localParts(body.plannedDeparture)).toEqual([2026, 10, 25, 6, 0]);
  });

  it("another type switches what still holds the previous preset (the stops' fallback where it has none) and keeps the user's time", async () => {
    await tickWithDefaultType();
    fireEvent.change(field("departureTime"), { target: { value: "07:15" } });
    fireEvent.change(field("transportTripTypeId"), { target: { value: "tt-1" } });
    await waitFor(() => expect(field("arrivalRelation").value).toBe("arr-1"));
    expect(field("transportTripTypeId").value).toBe("tt-1");
    expect(field("departureRelation").value).toBe("rel-1");
    expect(field("departureTime").value).toBe("07:15");
    expect(field("departureDate").value).toBe("2026-10-25");
    expect(field("carrier").value).toBe("");
    expect(field("vehicleCombinationId").value).toBe("combi-1");
    expect(checkedDrivers()).toEqual([]);

    const body = await saveRequest();
    expect(body).toMatchObject({
      transportTripTypeId: "tt-1",
      departureRelationId: "rel-1",
      departureAddressId: "adr-1",
      arrivalRelationId: "arr-1",
      arrivalAddressId: "adr-arr-1",
      carrierRelationId: null,
      carrierContactPersonId: null,
      vehicleCombinationId: "combi-1",
      vehicleIds: [],
      driverIds: [],
    });
    expect(localParts(body.plannedDeparture)).toEqual([2026, 10, 25, 7, 15]);
  });

  it("a saved request counts as holding its type's preset; a switch that removes the carrier clears its agreed price", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order({
        tripRequest: {
          transportTripTypeId: "tt-2",
          carrierRelationId: "car-t",
          carrierRelationName: "Trucker",
          carrierContactPersonId: "cp-t",
          driverIds: ["d2"],
          vehicleIds: [],
          departureRelationId: "dep-t",
          departureRelationName: "Depot T",
          departureAddressId: "adr-dep-t",
          arrivalRelationId: "rel-2",
          arrivalRelationName: "Beta",
          arrivalAddressId: "adr-2",
          plannedDeparture: new Date(2026, 9, 25, 6, 0).toISOString(),
          agreedPurchaseAmount: 400,
          agreedPurchaseCurrencyCode: "EUR",
          lineIds: null,
        },
      });
    renderPage();
    await screen.findByRole("heading", { level: 1 });
    trip();
    await waitFor(() => expect(GET.mock.calls.some(([p, init]) => p === "/api/planning/transport-trip-type/{id}" && init?.params?.path?.id === "tt-2")).toBe(true));
    await flush();
    expect(field("departureRelation").value).toBe("dep-t");
    fireEvent.change(field("transportTripTypeId"), { target: { value: "tt-1" } });
    await waitFor(() => expect(field("arrivalRelation").value).toBe("arr-1"));
    /* held tt-2's preset → switched; the user's drivers (d2, not tt-2's d1) stay */
    expect(field("departureRelation").value).toBe("rel-1");
    expect(field("departureTime").value).toBe("08:00");
    expect(field("carrier").value).toBe("");
    expect(checkedDrivers()).toEqual(["Ali"]);
    expect((within(trip()).getByLabelText(/^Orders:AgreedPurchaseAmount/) as HTMLInputElement).value).toBe("");
  });
});
