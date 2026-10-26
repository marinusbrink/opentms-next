import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "Add new …" in the Transport app's record pickers (UI-LOOK-004, PO decision 2026-10-16: no exception for an editor
 * another app shows): the order page's customer (a new relation starts with the Customer role), the customer's contact
 * person (the relation editor opens on the picked relation with the contact person dialog), a line's transport unit
 * (grid cell), the terms' price lists (a revenue list joins, a cost list does not), the Tevens trip's vehicles and
 * drivers, and the Print CMR wizard's profile. The editors are stubs that show what they were opened with and save a
 * fixed DTO (the real editors have their own tests); the pickers are native stand-ins whose "Add new …" row is a button.
 * The row needs the entity's Create permission and its navigation node's feature chain (UI-PERM-006). Risk class: Medium. */

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigateMock, useLocation: () => ({ pathname: "/transport/orders/o1", search: {} }) }));
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
vi.mock("@/apps/transport/wizards/MaterialiseWizard", () => ({ MaterialiseWizard: () => null }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

/* The editors the pickers open: stubs that show their props and save the DTO the test put into `saved`. */
const saved: Record<string, unknown> = {};
interface StubProps {
  mode: string;
  id?: string;
  initialName?: string;
  initialTab?: string;
  initialRoles?: number;
  newContactPersonName?: string;
  onSaved: (dto?: unknown) => void;
  onClose: () => void;
}
function stub(name: string) {
  return function StubEditor({ mode, id, initialName, initialTab, initialRoles, newContactPersonName, onSaved, onClose }: StubProps): ReactNode {
    return (
      <div data-stub-editor={name} data-mode={mode} data-record-id={id ?? ""} data-initial-name={initialName ?? ""} data-initial-tab={initialTab ?? ""} data-initial-roles={initialRoles ?? ""} data-new-contact-person={newContactPersonName ?? ""}>
        <button
          type="button"
          onClick={() => {
            onSaved(saved[name]);
            onClose();
          }}
        >
          {`stub-save-${name}`}
        </button>
      </div>
    );
  };
}
vi.mock("@/domains/master-data/components/RelationEditor", () => ({ RelationEditor: stub("Relation") }));
vi.mock("@/domains/master-data/components/VehicleEditor", () => ({ VehicleEditor: stub("Vehicle") }));
vi.mock("@/domains/master-data/components/DriverEditor", () => ({ DriverEditor: stub("Driver") }));
vi.mock("@/domains/master-data/components/CurrencyEditor", () => ({ CurrencyEditor: stub("Currency") }));
vi.mock("@/domains/master-data/components/TransportUnitEditor", () => ({ TransportUnitEditor: stub("TransportUnit") }));
vi.mock("@/domains/financial/components/PriceListEditor", () => ({ PriceListEditor: stub("PriceList") }));
vi.mock("@/domains/orders/components/ConsignmentNoteProfileEditor", () => ({ ConsignmentNoteProfileEditor: stub("ConsignmentNoteProfile") }));

import { featureChain } from "@/app/app-features";
import { OrderPage } from "@/apps/transport/pages/OrderPage";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import { TYPED_TERM, lookupItems, panel, ribbonButton, stubGlobals } from "./transport-test-mocks";
import { PUT, calls, lastCall, resetClient, routes } from "./test-setup-mocks";

const ORDER_GRANTS = { "Orders.TransportOrders": true, "Orders.TransportOrders.Update": true, "Orders.TransportOrders.Print": true, "PlanningExecution.TransportTrips": true, "Financial.RateLines.Update": true };
/* The Create permission and the navigation node of each entity the tests create (create-in-place.tsx). */
const CREATE = {
  Relation: ["MasterData.Relations.Create", "Apps.MasterData.General.Relations.Relations"],
  RelationContactPerson: ["MasterData.Relations.Update", "Apps.MasterData.General.Relations.Relations"],
  Vehicle: ["MasterData.Vehicles.Create", "Apps.MasterData.General.Fleet.Vehicles"],
  Driver: ["MasterData.Drivers.Create", "Apps.MasterData.General.Fleet.Drivers"],
  TransportUnit: ["MasterData.TransportUnits.Create", "Apps.MasterData.General.Transport.TransportUnits"],
  PriceList: ["Financial.PriceLists.Create", "Apps.MasterData.Financial.Pricing.PriceLists"],
  ConsignmentNoteProfile: ["Orders.ConsignmentNoteProfiles.Create", "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles"],
  Currency: ["MasterData.Currencies.Create", "Apps.MasterData.General.Reference.Currencies"],
} as const;
const allowCreate = (...entities: (keyof typeof CREATE)[]) => {
  for (const entity of entities) {
    const [permission, node] = CREATE[entity];
    grants[permission] = true;
    for (const feature of featureChain(node)) features[feature] = "true";
  }
};

const order = (status: number, extra: Record<string, unknown> = {}) => ({
  id: "o1",
  number: status === 1 ? null : "TO-2026-00001",
  status,
  transportOrderTypeId: "type-1",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  concurrencyStamp: "stamp-1",
  defaultStops: [],
  lines: [{ id: "l1", position: 1, transportUnitId: "unit-1", transportUnitName: "Pallet", quantity: 2, paymentInstruction: 0, stops: [] }],
  financialTerms: { currencyCode: "EUR", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] },
  ...extra,
});

beforeAll(stubGlobals);
beforeEach(() => {
  grants = { ...ORDER_GRANTS };
  features = { "Apps.Transport": "true", "Apps.Transport.Orders": "true", "Apps.Transport.Trips": "true" };
  lookupItems.current = {};
  for (const key of Object.keys(saved)) delete saved[key];
  resetClient();
  routes.GET["/api/orders/transport-order/{id}"] = () => order(2);
  routes.GET["/api/orders/transport-order-type/{id}"] = () => ({ id: "type-1", name: "Standard", requiredFields: 0, defaultWaypoints: [] });
  routes.GET["/api/orders/transport-order-type/lookup"] = () => [{ id: "type-1", name: "Standard" }];
  routes.GET["/api/master-data/transport-unit/lookup"] = () => [{ id: "unit-1", name: "Pallet" }];
  routes.GET["/api/master-data/relation/{id}"] = () => ({ id: "rel-1", name: "Acme", contactPersons: [{ id: "cp-1", firstName: "Jan", lastName: "Jansen" }] });
  routes.GET["/api/financial/price-list/lookup"] = () => [{ id: "pl-1", code: "PL1", name: "Benelux" }];
  routes.GET["/api/master-data/currency/lookup"] = () => [{ id: "c-eur", code: "EUR", name: "Euro" }, { id: "c-usd", code: "USD", name: "US dollar" }];
  routes.PUT["/api/orders/transport-order/{id}"] = () => order(2, { concurrencyStamp: "stamp-2" });
  routes.PUT["/api/orders/transport-order/{id}/financial-terms"] = () => order(2, { concurrencyStamp: "stamp-3" });
  routes.PUT["/api/orders/transport-order/{id}/lines"] = () => order(2, { concurrencyStamp: "stamp-2" });
  routes.PUT["/api/orders/transport-order/{id}/trip-request"] = () => order(1, { concurrencyStamp: "stamp-2" });
});

const newClient = () => new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
function renderPage() {
  return render(
    <QueryClientProvider client={newClient()}>
      <OrderPage id="o1" />
    </QueryClientProvider>,
  );
}
const loaded = () => screen.findByRole("heading", { level: 1 });
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const click = async (element: Element) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });
const createFor = (scope: HTMLElement, idSuffix: string) => scope.querySelector<HTMLElement>(`[data-create-for$="${idSuffix}"]`);
const editor = () => document.querySelector<HTMLElement>("[data-stub-editor]");
const saveStub = (name: string) => click(screen.getByRole("button", { name: `stub-save-${name}` }));

describe("Transport order page — Add new … in its pickers", () => {
  it("customer: no row without the relations' Create permission and node", async () => {
    renderPage();
    await loaded();
    expect(createFor(panel("General"), "-customer")).toBeNull();
  });

  it("customer: the new relation is picked, the defaults question is kept off with Cancel, and the save sends the new customer", async () => {
    allowCreate("Relation");
    saved.Relation = { id: "rel-new", code: "NEW", name: "New customer", roles: 1, addresses: [] };
    renderPage();
    await loaded();
    const row = createFor(panel("General"), "-customer")!;
    expect(row).toHaveTextContent("MasterData:AddNewRelation");
    await click(row);
    expect(editor()).toHaveAttribute("data-stub-editor", "Relation");
    expect(editor()).toHaveAttribute("data-mode", "create");
    expect(editor()).toHaveAttribute("data-initial-name", TYPED_TERM);
    expect(editor()).toHaveAttribute("data-initial-roles", "1");
    await saveStub("Relation");
    expect(editor()).toBeNull();
    const customer = within(panel("General")).getByLabelText(/^Orders:Customer \*/) as HTMLSelectElement;
    expect(customer.value).toBe("rel-new");
    /* A stored order asks about the new customer's financial defaults (design 58 build note 7); Cancel keeps the terms. */
    await click(await screen.findByRole("button", { name: "Cancel" }));
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ customerRelationId: "rel-new", customerContactPersonId: null }));
  });

  it("contact person: the relation editor opens on the picked customer with the contact person dialog; the contact its save added is picked", async () => {
    allowCreate("RelationContactPerson");
    saved.Relation = { id: "rel-1", name: "Acme", contactPersons: [{ id: "cp-1" }, { id: "cp-new" }] };
    renderPage();
    await loaded();
    const general = panel("General");
    await waitFor(() => expect(within(general).getByRole("option", { name: "Jan Jansen" })).toBeInTheDocument());
    const row = createFor(general, "-customerContactPersonId")!;
    expect(row).toHaveTextContent("MasterData:AddNewContactPerson");
    await click(row);
    expect(editor()).toHaveAttribute("data-mode", "edit");
    expect(editor()).toHaveAttribute("data-record-id", "rel-1");
    expect(editor()).toHaveAttribute("data-initial-tab", "contactPersons");
    expect(editor()).toHaveAttribute("data-new-contact-person", TYPED_TERM);
    routes.GET["/api/master-data/relation/{id}"] = () => ({ id: "rel-1", name: "Acme", contactPersons: [{ id: "cp-1", firstName: "Jan", lastName: "Jansen" }, { id: "cp-new", firstName: "Piet", lastName: "Smit" }] });
    await saveStub("Relation");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}")!.body).toEqual(expect.objectContaining({ customerRelationId: "rel-1", customerContactPersonId: "cp-new" }));
  });

  it("a line's transport unit (grid cell): the new unit lands in that line and is saved with the lines", async () => {
    allowCreate("TransportUnit");
    saved.TransportUnit = { id: "unit-new", code: null, name: "Box pallet" };
    renderPage();
    await loaded();
    tab("Orders:Lines");
    const lines = panel("Orders:Lines");
    const row = await waitFor(() => lines.querySelector<HTMLElement>("[data-create-cell=transportUnitId]")!);
    expect(row).toHaveTextContent("MasterData:AddNewTransportUnit");
    await click(row);
    expect(editor()).toHaveAttribute("data-stub-editor", "TransportUnit");
    expect(editor()).toHaveAttribute("data-initial-name", TYPED_TERM);
    await saveStub("TransportUnit");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/lines")).toHaveLength(1));
    const body = lastCall(PUT, "/api/orders/transport-order/{id}/lines")!.body as { lines: { id: string | null; transportUnitId: string }[] };
    expect(body.lines[0]).toMatchObject({ id: "l1", transportUnitId: "unit-new" });
  });

  it("price lists: a new revenue list joins the terms; a cost list made there does not", async () => {
    allowCreate("PriceList");
    renderPage();
    await loaded();
    tab("Orders:Pricing");
    const pricing = panel("Orders:Pricing");
    saved.PriceList = { id: "pl-cost", code: "C1", name: "Cost list", side: 2 };
    await click(createFor(pricing, "-terms-priceListIds")!);
    await saveStub("PriceList");
    saved.PriceList = { id: "pl-new", code: "PL2", name: "Benelux 2027", side: 1 };
    await click(createFor(pricing, "-terms-priceListIds")!);
    expect(editor()).toHaveAttribute("data-initial-name", TYPED_TERM);
    await saveStub("PriceList");
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect((lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body as { priceListIds: string[] }).priceListIds).toEqual(["pl-new"]);
  });

  it("Tevens trip: a new vehicle and a new driver join the request (Draft) and are saved with it", async () => {
    allowCreate("Vehicle", "Driver");
    routes.GET["/api/orders/transport-order/{id}"] = () => order(1, { tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureRelationName: "Acme", departureAddressId: "adr-1", arrivalRelationId: "rel-1", arrivalRelationName: "Acme", arrivalAddressId: "adr-1", plannedDeparture: "2026-10-25T06:00:00Z", vehicleIds: [], driverIds: [] } });
    saved.Vehicle = { id: "veh-new", name: "Truck 42", licencePlate: "AB-12-CD", kind: 1 };
    saved.Driver = { id: "drv-new", code: "D9", fullName: "Jan Smit" };
    renderPage();
    await loaded();
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    fireEvent.click(within(planning).getByText("Orders:Advanced"));
    await click(createFor(planning, "-trip-vehicles")!);
    expect(editor()).toHaveAttribute("data-stub-editor", "Vehicle");
    await saveStub("Vehicle");
    await click(createFor(planning, "-trip-drivers")!);
    await saveStub("Driver");
    expect(within(planning).getByText("Truck 42")).toBeInTheDocument();
    expect(within(planning).getByText("Jan Smit")).toBeInTheDocument();
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body).toEqual(expect.objectContaining({ tripRequest: expect.objectContaining({ vehicleIds: ["veh-new"], driverIds: ["drv-new"] }) }));
  });

  it("terms currency: a picker over the currencies that stores the ISO code; Add new … picks the new currency's code", async () => {
    allowCreate("Currency");
    saved.Currency = { id: "c-chf", isoCode: "CHF", name: "Swiss franc" };
    renderPage();
    await loaded();
    tab("Orders:Pricing");
    const pricing = panel("Orders:Pricing");
    const currency = pricing.querySelector<HTMLSelectElement>("select[id$='-terms-currencyCode']")!;
    await waitFor(() => expect(within(currency).getByRole("option", { name: "USD US dollar" })).toBeInTheDocument());
    expect(currency.value).toBe("EUR");
    fireEvent.change(currency, { target: { value: "USD" } });
    expect(currency.value).toBe("USD");

    const row = createFor(pricing, "-terms-currencyCode")!;
    expect(row).toHaveTextContent("MasterData:AddNewCurrency");
    await click(row);
    expect(editor()).toHaveAttribute("data-stub-editor", "Currency");
    expect(editor()).toHaveAttribute("data-initial-name", TYPED_TERM);
    await saveStub("Currency");
    expect(currency.value).toBe("CHF");
    expect(within(currency).getByRole("option", { name: "CHF Swiss franc" })).toBeInTheDocument();

    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(1));
    expect((lastCall(PUT, "/api/orders/transport-order/{id}/financial-terms")!.body as { currencyCode: string }).currencyCode).toBe("CHF");
  });

  it("terms currency: a stored code the lookup no longer has stays selected; no row without the currencies' Create permission", async () => {
    routes.GET["/api/orders/transport-order/{id}"] = () => order(2, { financialTerms: { currencyCode: "GBP", priceListIds: [], multipleMatches: 1, discountsOrSurcharges: [] } });
    renderPage();
    await loaded();
    tab("Orders:Pricing");
    const pricing = panel("Orders:Pricing");
    const currency = pricing.querySelector<HTMLSelectElement>("select[id$='-terms-currencyCode']")!;
    await waitFor(() => expect(within(currency).getByRole("option", { name: "USD US dollar" })).toBeInTheDocument());
    expect(currency.value).toBe("GBP");
    expect(createFor(pricing, "-terms-currencyCode")).toBeNull();
    /* Untouched: nothing to save. */
    expect(ribbonButton("Save")).not.toBeNull();
    await click(ribbonButton("Save")!);
    expect(calls(PUT, "/api/orders/transport-order/{id}/financial-terms")).toHaveLength(0);
  });

  it("Tevens trip: the agreed purchase currency is the same picker and is saved as the ISO code", async () => {
    allowCreate("Currency");
    routes.GET["/api/orders/transport-order/{id}"] = () =>
      order(1, { tripRequest: { transportTripTypeId: "tt-1", departureRelationId: "rel-1", departureRelationName: "Acme", departureAddressId: "adr-1", arrivalRelationId: "rel-1", arrivalRelationName: "Acme", arrivalAddressId: "adr-1", plannedDeparture: "2026-10-25T06:00:00Z", carrierRelationId: "car-1", carrierRelationName: "Charter", agreedPurchaseAmount: 400, vehicleIds: [], driverIds: [] } });
    renderPage();
    await loaded();
    tab("Orders:Planning");
    const planning = panel("Orders:Planning");
    const currency = planning.querySelector<HTMLSelectElement>("select[id$='-trip-agreedPurchaseCurrencyCode']")!;
    await waitFor(() => expect(within(currency).getByRole("option", { name: "USD US dollar" })).toBeInTheDocument());
    expect(currency.value).toBe("");
    expect(createFor(planning, "-trip-agreedPurchaseCurrencyCode")).toHaveTextContent("MasterData:AddNewCurrency");
    fireEvent.change(currency, { target: { value: "USD" } });
    await click(ribbonButton("Save")!);
    await waitFor(() => expect(calls(PUT, "/api/orders/transport-order/{id}/trip-request")).toHaveLength(1));
    expect(lastCall(PUT, "/api/orders/transport-order/{id}/trip-request")!.body).toEqual(expect.objectContaining({ tripRequest: expect.objectContaining({ agreedPurchaseAmount: 400, agreedPurchaseCurrencyCode: "USD" }) }));
  });

  it("a read-only order (no Update) offers no row, even with every Create permission", async () => {
    allowCreate("Relation", "PriceList", "TransportUnit", "Currency");
    grants["Orders.TransportOrders.Update"] = false;
    renderPage();
    await loaded();
    expect(document.querySelector("[data-create-for]")).toBeNull();
  });
});

describe("Print CMR wizard — Add new profile …", () => {
  const renderWizard = () =>
    render(
      <QueryClientProvider client={newClient()}>
        <PrintCmrWizard open onOpenChange={() => undefined} orders={[{ id: "o1", number: "TO-1", lines: [{ id: "l1", position: 1, label: "Pallet" }] }]} />
      </QueryClientProvider>,
    );

  it("needs the profiles' Create permission and node; the new profile is picked and named at once", async () => {
    routes.GET["/api/orders/consignment-note-profile"] = () => ({ rows: [{ id: "p-1", name: "Standard", isDefault: true }], totalCount: 1, filteredCount: 1 });
    const { unmount } = renderWizard();
    await waitFor(() => expect((document.getElementById("cmr-profile") as HTMLSelectElement).value).toBe("p-1"));
    expect(document.querySelector("[data-create-for=cmr-profile]")).toBeNull();
    unmount();

    allowCreate("ConsignmentNoteProfile");
    saved.ConsignmentNoteProfile = { id: "p-new", name: "Container" };
    renderWizard();
    await waitFor(() => expect((document.getElementById("cmr-profile") as HTMLSelectElement).value).toBe("p-1"));
    const row = document.querySelector<HTMLElement>("[data-create-for=cmr-profile]")!;
    expect(row).toHaveTextContent("Orders:AddNewConsignmentNoteProfile");
    await click(row);
    expect(editor()).toHaveAttribute("data-stub-editor", "ConsignmentNoteProfile");
    expect(editor()).toHaveAttribute("data-initial-name", TYPED_TERM);
    await saveStub("ConsignmentNoteProfile");
    const select = document.getElementById("cmr-profile") as HTMLSelectElement;
    expect(select.value).toBe("p-new");
    expect(within(select).getByRole("option", { name: "Container" })).toBeInTheDocument();
  });
});
