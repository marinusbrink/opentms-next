import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The transport trip type editor (design 53) on the Platform reference-data hooks (design 50): validation, the DTO
 * sent on create and update (upper-cased code, the localized-name map, the numbering, the next number, the stamp),
 * the numbering preview, the default badge and no Delete for the default. Design 61 "Trip type defaults" 1/3 and
 * build note 4: the tab Defaults for new trips (carrier and contact, combination or vehicles, drivers, departure
 * relation/address/time, arrival relation/address, stop composition mode, first/last stop rule), the pair rules on
 * screen (a combination clears vehicles and back, clearing a carrier clears its contact, clearing a relation its
 * address), no default-address preselect for a stored type, the defaults in the save body, and "Add new …" on every
 * record picker (UI-LOOK-004). The trip type's own hooks are mocked; the pickers' Master Data hooks are real over the
 * mocked client; the pickers are native stand-ins. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()),
  EntityLookup: (await import("../../transport/__tests__/transport-test-mocks")).MockEntityLookup,
}));
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("../../transport/__tests__/transport-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("../../transport/__tests__/transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});

const createMutateAsync = vi.fn();
const updateMutateAsync = vi.fn();
const deleteMutateAsync = vi.fn();
const setDefaultMutateAsync = vi.fn();
let detail: { data: unknown; isLoading: boolean; error: unknown } = { data: undefined, isLoading: false, error: null };
vi.mock("@/domains/planning-execution/reference-data", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/domains/planning-execution/reference-data")>()),
  transportTripTypes: {
    entity: "transportTripTypes",
    useDetail: () => detail,
    useLookup: () => ({ data: [], isLoading: false, error: null }),
    useCreate: () => ({ mutateAsync: createMutateAsync, isPending: false }),
    useUpdate: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useDelete: () => ({ mutateAsync: deleteMutateAsync, isPending: false }),
  },
  useSetDefaultTransportTripType: () => ({ mutateAsync: setDefaultMutateAsync, isPending: false }),
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { TransportTripTypeEditor } from "@/domains/planning-execution/components/TransportTripTypeEditor";
import { lookupItems, stubGlobals } from "../../transport/__tests__/transport-test-mocks";
import { resetClient, routes } from "../../transport/__tests__/test-setup-mocks";
import { ALL_CREATE_FEATURES } from "./create-in-place-test-mocks";

const CREATE_GRANTS = {
  "MasterData.Relations.Create": true,
  "MasterData.Relations.Update": true,
  "MasterData.Vehicles.Create": true,
  "MasterData.VehicleCombinations.Create": true,
  "MasterData.Drivers.Create": true,
};

const CONTACTS: Record<string, { id: string; firstName: string; lastName: string }[]> = {
  "car-1": [{ id: "cp-1", firstName: "Piet", lastName: "Jansen" }],
  "car-2": [{ id: "cp-2", firstName: "Kees", lastName: "Vos" }],
};
const ADDRESSES: Record<string, { id: string; street: string; city: string; postalCode: string; addressTypeId?: string | null }[]> = {
  "dep-1": [{ id: "adr-dep-1", street: "Kade", postalCode: "5000 AA", city: "Tilburg", addressTypeId: null }],
  "dep-2": [{ id: "adr-dep-2", street: "Weg", postalCode: "6000 BB", city: "Venlo", addressTypeId: null }],
  "arr-1": [{ id: "adr-arr-1", street: "Haven", postalCode: "3000 CC", city: "Rotterdam", addressTypeId: null }],
};

beforeAll(stubGlobals);

beforeEach(() => {
  createMutateAsync.mockReset().mockResolvedValue({});
  updateMutateAsync.mockReset().mockResolvedValue({});
  deleteMutateAsync.mockReset();
  setDefaultMutateAsync.mockReset();
  grants = { "PlanningExecution.TransportTripTypes.Delete": true, ...CREATE_GRANTS };
  features = { ...ALL_CREATE_FEATURES };
  detail = { data: undefined, isLoading: false, error: null };
  resetClient();
  routes.GET["/api/master-data/relation/{id}"] = (init) => {
    const id = init?.params?.path?.id as string;
    return { id, name: id, contactPersons: CONTACTS[id] ?? [] };
  };
  routes.GET["/api/master-data/relation/{id}/addresses"] = (init) => ADDRESSES[init?.params?.path?.id as string] ?? [];
  routes.GET["/api/master-data/vehicle-combination/lookup"] = () => [{ id: "combi-1", name: "Combi 1" }];
  lookupItems.current = {
    "*-defaultCarrierRelation": [{ id: "car-1", name: "Carrier One" }, { id: "car-2", name: "Carrier Two" }],
    "*-defaultDepartureRelation": [{ id: "dep-1", name: "Depot One" }, { id: "dep-2", name: "Depot Two" }],
    "*-defaultArrivalRelation": [{ id: "arr-1", name: "Hub One" }],
    "*-defaultVehicles": [{ id: "v1", name: "DAF 1" }, { id: "v2", name: "Trailer 2" }],
    "*-defaultDrivers": [{ id: "d1", name: "Jan" }, { id: "d2", name: "Ali" }],
  } as never;
});

let invalidateQueries: ReturnType<typeof vi.spyOn>;
function renderEditor(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  invalidateQueries = vi.spyOn(client, "invalidateQueries");
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const stored = {
  id: "t1",
  code: "STD",
  name: "Standard",
  names: { en: "Standard", nl: "Standaard" },
  defaultExecution: 1,
  sendToFleetManagementSystem: true,
  numbering: { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 },
  nextNumber: 42,
  nextNumberPreview: "TR-2026-00042",
  isDefault: false,
  isArchived: false,
  description: null,
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-22T10:00:00Z",
};

/** The defaults a new type sends when its Defaults tab is left alone (design 61 "Trip type defaults" 1: all optional;
 *  stop composition MergeByLocationAndActivity, first/last stop rule None). */
const NO_DEFAULTS = {
  defaultCarrierRelationId: null,
  defaultCarrierContactPersonId: null,
  defaultVehicleCombinationId: null,
  defaultVehicleIds: [],
  defaultDriverIds: [],
  defaultDepartureRelationId: null,
  defaultDepartureAddressId: null,
  defaultDepartureTime: null,
  defaultArrivalRelationId: null,
  defaultArrivalAddressId: null,
  defaultStopCompositionMode: 1,
  defaultFirstLastStopRule: 0,
};

const inputByLabel = (label: string) => screen.getByLabelText(new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`)) as HTMLInputElement;
/** A control of the editor by its field name (the form prefixes ids with its instance). */
const control = <T extends HTMLElement = HTMLSelectElement>(field: string) => {
  const found = document.querySelector<T>(`[role=dialog] [id$='-${field}']`);
  if (!found) throw new Error(`no control ${field}`);
  return found;
};
const pick = (field: string, value: string) => fireEvent.change(control(field), { target: { value } });
const option = (field: string, value: string) => control(field).querySelector(`option[value='${value}']`);
const vehicleBox = (name: string) => control<HTMLElement>("defaultVehicles").querySelector<HTMLInputElement>(`input[aria-label='${name}']`)!;
const driverBox = (name: string) => control<HTMLElement>("defaultDrivers").querySelector<HTMLInputElement>(`input[aria-label='${name}']`)!;
const defaultsTab = () => fireEvent.click(screen.getByRole("tab", { name: "PlanningExecution:DefaultsForNewTrips" }));

const submit = () =>
  act(async () => {
    fireEvent.submit(document.querySelector("form")!);
    await Promise.resolve();
  });

describe("TransportTripTypeEditor", () => {
  it("refuses to save without a code and a name, and sends nothing", async () => {
    renderEditor(<TransportTripTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getAllByText("MasterData:Required").length).toBeGreaterThanOrEqual(2);
  });

  it("creates with the upper-cased code, the name map and the default numbering of a new type", async () => {
    const onSaved = vi.fn();
    renderEditor(<TransportTripTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={onSaved} />);
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "cht" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Charter" } });
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toEqual({
      code: "CHT",
      names: { en: "Charter" },
      defaultExecution: 1,
      sendToFleetManagementSystem: true,
      numbering: { prefix: "TR", includeYear: true, includeMonth: false, separator: "-", digits: 5 },
      nextNumber: 1,
      isArchived: false,
      description: null,
      ...NO_DEFAULTS,
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["masterData", "transportTripTypes"] });
  });

  it("updates with the stamp, keeps the other languages' names and refuses a next number below the current one", async () => {
    detail = { data: stored, isLoading: false, error: null };
    renderEditor(<TransportTripTypeEditor mode="edit" id="t1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Standard trip" } });
    fireEvent.click(screen.getByRole("tab", { name: "MasterData:Numbering" }));
    fireEvent.change(inputByLabel("MasterData:Numbering.NextNumber"), { target: { value: "41" } });
    await submit();
    expect(updateMutateAsync).not.toHaveBeenCalled();

    fireEvent.change(inputByLabel("MasterData:Numbering.NextNumber"), { target: { value: "500" } });
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    const { id, dto } = updateMutateAsync.mock.calls[0][0];
    expect(id).toBe("t1");
    expect(dto.concurrencyStamp).toBe("stamp-1");
    expect(dto.names).toEqual({ en: "Standard trip", nl: "Standaard" });
    expect(dto.nextNumber).toBe(500);
  });

  it("shows the default badge and offers no Delete for the default type", () => {
    detail = { data: { ...stored, isDefault: true }, isLoading: false, error: null };
    renderEditor(<TransportTripTypeEditor mode="edit" id="t1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    expect(screen.getByText("MasterData:IsDefault")).toBeInTheDocument();
    expect(screen.queryByText("Delete")).toBeNull();
  });
});

describe("TransportTripTypeEditor – Defaults for new trips (design 61 \"Trip type defaults\" 3)", () => {
  const open = () => {
    renderEditor(<TransportTripTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    defaultsTab();
  };

  it("has every designed field", () => {
    open();
    for (const field of [
      "defaultCarrierRelation",
      "defaultCarrierContactPersonId",
      "defaultVehicleCombinationId",
      "defaultVehicles",
      "defaultDrivers",
      "defaultDepartureRelation",
      "defaultDepartureAddressId",
      "defaultDepartureTime",
      "defaultArrivalRelation",
      "defaultArrivalAddressId",
      "defaultStopCompositionMode",
      "defaultFirstLastStopRule",
    ])
      expect(control<HTMLElement>(field)).toBeInTheDocument();
    /* a new type starts as the server's defaults */
    expect(control("defaultStopCompositionMode").value).toBe("1");
    expect(control("defaultFirstLastStopRule").value).toBe("0");
    /* a contact waits for its carrier, an address for its relation */
    expect(control("defaultCarrierContactPersonId")).toBeDisabled();
    expect(control("defaultDepartureAddressId")).toBeDisabled();
    expect(control("defaultArrivalAddressId")).toBeDisabled();
  });

  it("offers \"Add new …\" on every record picker (UI-LOOK-004); a contact and an address once their relation is chosen", async () => {
    open();
    const createRow = (field: string) => document.querySelector(`[data-create-for$='-${field}']`);
    for (const field of ["defaultCarrierRelation", "defaultVehicleCombinationId", "defaultVehicles", "defaultDrivers", "defaultDepartureRelation", "defaultArrivalRelation"]) expect(createRow(field)).not.toBeNull();
    expect(createRow("defaultCarrierContactPersonId")).toBeNull();
    expect(createRow("defaultDepartureAddressId")).toBeNull();
    pick("defaultCarrierRelation", "car-1");
    pick("defaultDepartureRelation", "dep-1");
    pick("defaultArrivalRelation", "arr-1");
    for (const field of ["defaultCarrierContactPersonId", "defaultDepartureAddressId", "defaultArrivalAddressId"]) expect(createRow(field)).not.toBeNull();
  });

  it("without the Create permissions the pickers offer no \"Add new …\"", () => {
    grants = {};
    open();
    expect(document.querySelectorAll("[role=dialog] [data-create-for]")).toHaveLength(0);
  });

  it("a combination clears the vehicles, and vehicles clear the combination", async () => {
    open();
    await waitFor(() => expect(option("defaultVehicleCombinationId", "combi-1")).not.toBeNull());
    fireEvent.click(vehicleBox("DAF 1"));
    expect(vehicleBox("DAF 1").checked).toBe(true);
    pick("defaultVehicleCombinationId", "combi-1");
    expect(control("defaultVehicleCombinationId").value).toBe("combi-1");
    expect(vehicleBox("DAF 1").checked).toBe(false);
    fireEvent.click(vehicleBox("Trailer 2"));
    expect(vehicleBox("Trailer 2").checked).toBe(true);
    expect(control("defaultVehicleCombinationId").value).toBe("");
  });

  it("clearing or replacing the carrier clears its contact", async () => {
    open();
    pick("defaultCarrierRelation", "car-1");
    await waitFor(() => expect(option("defaultCarrierContactPersonId", "cp-1")).not.toBeNull());
    pick("defaultCarrierContactPersonId", "cp-1");
    expect(control("defaultCarrierContactPersonId").value).toBe("cp-1");
    pick("defaultCarrierRelation", "");
    expect(control("defaultCarrierContactPersonId").value).toBe("");
    expect(control("defaultCarrierContactPersonId")).toBeDisabled();

    pick("defaultCarrierRelation", "car-1");
    await waitFor(() => expect(option("defaultCarrierContactPersonId", "cp-1")).not.toBeNull());
    pick("defaultCarrierContactPersonId", "cp-1");
    pick("defaultCarrierRelation", "car-2");
    await waitFor(() => expect(option("defaultCarrierContactPersonId", "cp-2")).not.toBeNull());
    expect(control("defaultCarrierContactPersonId").value).toBe("");
  });

  it("clearing or replacing a relation clears its address (departure and arrival)", async () => {
    open();
    pick("defaultDepartureRelation", "dep-1");
    await waitFor(() => expect(option("defaultDepartureAddressId", "adr-dep-1")).not.toBeNull());
    /* build note 4: no default-address preselect, even for a relation with one default address */
    expect(control("defaultDepartureAddressId").value).toBe("");
    pick("defaultDepartureAddressId", "adr-dep-1");
    pick("defaultDepartureRelation", "dep-2");
    await waitFor(() => expect(option("defaultDepartureAddressId", "adr-dep-2")).not.toBeNull());
    expect(control("defaultDepartureAddressId").value).toBe("");

    pick("defaultArrivalRelation", "arr-1");
    await waitFor(() => expect(option("defaultArrivalAddressId", "adr-arr-1")).not.toBeNull());
    pick("defaultArrivalAddressId", "adr-arr-1");
    pick("defaultArrivalRelation", "");
    expect(control("defaultArrivalAddressId").value).toBe("");
    expect(control("defaultArrivalAddressId")).toBeDisabled();
  });

  it("refuses an invalid departure time and sends nothing", async () => {
    open();
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "cht" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Charter" } });
    fireEvent.change(control<HTMLInputElement>("defaultDepartureTime"), { target: { value: "25:00" } });
    await submit();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("Orders:InvalidTime")).toBeInTheDocument();
  });

  it("the save body carries every default", async () => {
    open();
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "cht" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Charter" } });
    pick("defaultCarrierRelation", "car-1");
    await waitFor(() => expect(option("defaultCarrierContactPersonId", "cp-1")).not.toBeNull());
    pick("defaultCarrierContactPersonId", "cp-1");
    fireEvent.click(vehicleBox("DAF 1"));
    fireEvent.click(vehicleBox("Trailer 2"));
    fireEvent.click(driverBox("Ali"));
    fireEvent.click(driverBox("Jan"));
    pick("defaultDepartureRelation", "dep-1");
    await waitFor(() => expect(option("defaultDepartureAddressId", "adr-dep-1")).not.toBeNull());
    pick("defaultDepartureAddressId", "adr-dep-1");
    fireEvent.change(control<HTMLInputElement>("defaultDepartureTime"), { target: { value: "6:30" } });
    pick("defaultArrivalRelation", "arr-1");
    await waitFor(() => expect(option("defaultArrivalAddressId", "adr-arr-1")).not.toBeNull());
    pick("defaultArrivalAddressId", "adr-arr-1");
    pick("defaultStopCompositionMode", "2");
    pick("defaultFirstLastStopRule", "3");
    await submit();

    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    expect(createMutateAsync.mock.calls[0][0]).toMatchObject({
      code: "CHT",
      defaultCarrierRelationId: "car-1",
      defaultCarrierContactPersonId: "cp-1",
      defaultVehicleCombinationId: null,
      defaultVehicleIds: ["v1", "v2"],
      defaultDriverIds: ["d2", "d1"],
      defaultDepartureRelationId: "dep-1",
      defaultDepartureAddressId: "adr-dep-1",
      defaultDepartureTime: "06:30:00",
      defaultArrivalRelationId: "arr-1",
      defaultArrivalAddressId: "adr-arr-1",
      defaultStopCompositionMode: 2,
      defaultFirstLastStopRule: 3,
    });
  });

  it("a combination is sent without vehicles", async () => {
    open();
    fireEvent.change(inputByLabel("MasterData:Code"), { target: { value: "cht" } });
    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Charter" } });
    await waitFor(() => expect(option("defaultVehicleCombinationId", "combi-1")).not.toBeNull());
    pick("defaultVehicleCombinationId", "combi-1");
    await submit();
    expect(createMutateAsync.mock.calls[0][0]).toMatchObject({ defaultVehicleCombinationId: "combi-1", defaultVehicleIds: [] });
  });

  it("a stored type opens with its defaults, no address preselected, and an update sends them back unchanged", async () => {
    detail = {
      data: {
        ...stored,
        defaultCarrierRelationId: "car-1",
        defaultCarrierRelationName: "Carrier One",
        defaultCarrierContactPersonId: "cp-1",
        defaultVehicleIds: ["v1"],
        defaultVehicles: [{ id: "v1", name: "DAF 1", kind: 1 }],
        defaultDriverIds: ["d1"],
        defaultDrivers: [{ id: "d1", name: "Jan" }],
        defaultDepartureRelationId: "dep-1",
        defaultDepartureRelationName: "Depot One",
        defaultDepartureAddressId: null,
        defaultDepartureTime: "05:45:00",
        defaultArrivalRelationId: "arr-1",
        defaultArrivalRelationName: "Hub One",
        defaultArrivalAddressId: "adr-arr-1",
        defaultStopCompositionMode: 2,
        defaultFirstLastStopRule: 1,
      },
      isLoading: false,
      error: null,
    };
    renderEditor(<TransportTripTypeEditor mode="edit" id="t1" readOnly={false} open onClose={() => {}} onSaved={() => {}} />);
    defaultsTab();
    expect(control("defaultCarrierRelation").value).toBe("car-1");
    await waitFor(() => expect(control("defaultCarrierContactPersonId").value).toBe("cp-1"));
    expect(vehicleBox("DAF 1").checked).toBe(true);
    expect(driverBox("Jan").checked).toBe(true);
    expect(control<HTMLInputElement>("defaultDepartureTime").value).toBe("05:45");
    await waitFor(() => expect(option("defaultDepartureAddressId", "adr-dep-1")).not.toBeNull());
    expect(control("defaultDepartureAddressId").value).toBe("");
    await waitFor(() => expect(control("defaultArrivalAddressId").value).toBe("adr-arr-1"));
    expect(control("defaultStopCompositionMode").value).toBe("2");
    expect(control("defaultFirstLastStopRule").value).toBe("1");

    fireEvent.change(inputByLabel("MasterData:Name"), { target: { value: "Standard trip" } });
    await submit();
    expect(updateMutateAsync).toHaveBeenCalledTimes(1);
    expect(updateMutateAsync.mock.calls[0][0].dto).toMatchObject({
      defaultCarrierRelationId: "car-1",
      defaultCarrierContactPersonId: "cp-1",
      defaultVehicleCombinationId: null,
      defaultVehicleIds: ["v1"],
      defaultDriverIds: ["d1"],
      defaultDepartureRelationId: "dep-1",
      defaultDepartureAddressId: null,
      defaultDepartureTime: "05:45:00",
      defaultArrivalRelationId: "arr-1",
      defaultArrivalAddressId: "adr-arr-1",
      defaultStopCompositionMode: 2,
      defaultFirstLastStopRule: 1,
      concurrencyStamp: "stamp-1",
    });
  });
});
