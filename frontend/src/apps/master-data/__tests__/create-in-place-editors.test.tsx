import { act, cleanup, fireEvent, render, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* UI-LOOK-004 wired into the editors (PO decision 2026-10-16): a representative set of record pickers shows
 * "Add new …" with the entity's Create permission, not without it and not in a read-only form; the nested editor
 * opens with the typed text, and after it saves the field holds the new id (multi-value fields: appended to what
 * was selected). Covered: VehicleEditor vehicle type (with the type's dimensions fetched as for a picked type) and
 * organisation units (multi); RelationEditor categories (multi) and currency; CountryEditor default currency;
 * DriverEditor user account (EntityLookup); TransportUnitEditor the load-factor vehicle-type cell
 * (ReferenceCellEditor). Every row also needs the feature chain of the target entity's navigation node
 * (UI-PERM-006), including the rows that keep their own create dialog (the relation pickers of the Vehicle, Driver
 * and ProductRelationCode editors; the VehicleType editor's characteristics picker), which ask useCanCreateInPlace.
 * The Vehicle and Relation editors' characteristics pickers sit in a grid toolbar this file's grid stand-in does not
 * render: vehicle-editor.test.tsx and relation-editor.test.tsx cover them. The domain hooks and useCreateInPlace are real; the generated client is the mock;
 * the pickers are native stand-ins with an Add new button; the nested editors are stubs that save a fixed DTO.
 * Risk class: Medium (UI flow). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      currentTenant: { id: "t-1" },
      auth: { grantedPolicies: grants },
      features: { values: features },
      localization: { languages: [{ cultureName: "nl", displayName: "Nederlands" }, { cultureName: "en", displayName: "English" }] },
    },
  }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/lines-grid-lazy", async () => ({ LazyLinesGrid: (await import("./create-in-place-test-mocks")).GridWithCellEditors }));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./create-in-place-test-mocks")).PickerReferenceCombobox }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./create-in-place-test-mocks")).PickerMultiSelect }));
vi.mock("@/components/ui/entity-lookup", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/ui/entity-lookup")>()),
  EntityLookup: (await import("./create-in-place-test-mocks")).PickerEntityLookup,
}));
vi.mock("@/components/ui/tree-select", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/ui/tree-select")>()),
  TreeSelect: (await import("./create-in-place-test-mocks")).PickerTreeSelect,
}));

/* The nested editors the covered pickers open. */
vi.mock("@/apps/master-data/editors/VehicleTypeEditor", async () => ({ VehicleTypeEditor: (await import("./create-in-place-test-mocks")).stubEditor("VehicleType") }));
vi.mock("@/apps/master-data/editors/CurrencyEditor", async () => ({ CurrencyEditor: (await import("./create-in-place-test-mocks")).stubEditor("Currency") }));
vi.mock("@/apps/master-data/editors/RelationCategoryEditor", async () => ({
  RelationCategoryEditor: (await import("./create-in-place-test-mocks")).stubEditor("RelationCategory"),
}));
vi.mock("@/domains/platform/components/OrganizationUnitFormDialog", async () => ({
  OrganizationUnitFormDialog: (await import("./create-in-place-test-mocks")).stubDialog("OrganizationUnit"),
}));
vi.mock("@/domains/platform/components/UserFormDialog", async () => ({ UserFormDialog: (await import("./create-in-place-test-mocks")).stubDialog("User") }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";

import { CountryEditor } from "@/apps/master-data/editors/CountryEditor";
import { DriverEditor } from "@/apps/master-data/editors/DriverEditor";
import { ProductRelationCodeEditor } from "@/apps/master-data/editors/ProductRelationCodeEditor";
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import { TransportUnitEditor } from "@/apps/master-data/editors/TransportUnitEditor";
import { VehicleEditor } from "@/apps/master-data/editors/VehicleEditor";
import { apiClient } from "@/lib/api/client";

import { ALL_CREATE_FEATURES, resetSavedDtos, TYPED_TERM } from "./create-in-place-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;

const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });

class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}
class ObserverStub {
  observe() {}
  disconnect() {}
  unobserve() {}
}
const stubGlobals = () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("IntersectionObserver", ObserverStub);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
};
beforeAll(stubGlobals);
afterEach(() => {
  vi.unstubAllGlobals();
  stubGlobals();
});

/** Every Create permission the covered pickers check. */
const CREATE_ALL = {
  "MasterData.VehicleTypes.Create": true,
  "MasterData.Countries.Create": true,
  "MasterData.Currencies.Create": true,
  "MasterData.Drivers.Create": true,
  "MasterData.RelationCategories.Create": true,
  "MasterData.GeographicalRegions.Create": true,
  "MasterData.Relations.Create": true,
  "MasterData.TransportCharacteristics.Create": true,
  "MasterData.TransportUnits.Create": true,
  "Platform.Administration.OrganizationUnits.Create": true,
  "Platform.Administration.Users.Create": true,
};

const VEHICLE = {
  id: "v1",
  name: "Truck 01",
  vehicleTypeId: "vt-1",
  vehicleTypeName: "Tractor",
  kind: 1,
  licencePlate: null,
  registrationCountryId: "nl",
  registrationCountryCode: "NL",
  vin: null,
  ownerRelationId: "own",
  ownerName: "Own Transport",
  homeDepotRelationId: null,
  homeDepotName: null,
  lengthCm: 600,
  widthCm: 255,
  heightCm: 400,
  grossVehicleWeightKg: 18000,
  emptyWeightKg: null,
  maxEpq: null,
  maxLoadingMeters: null,
  maxPayloadKg: null,
  isArchived: false,
  characteristics: [],
  defaultDrivers: [],
  organizationUnitIds: ["ou-1"],
  externalIdentifiers: [],
  combinationName: null,
  description: null,
  concurrencyStamp: "stamp-v1",
  creationTime: "2026-10-15T10:00:00Z",
  lastModificationTime: null,
};

const COUNTRY = { id: "be", isoAlpha2: "BE", isoAlpha3: "BEL", isoNumeric: 56, names: { en: "Belgium" }, defaultCurrencyId: "cur-eur", primaryTimeZoneId: "Europe/Brussels", isDefault: false, concurrencyStamp: "s" };

const TRANSPORT_UNIT = {
  id: "tu1",
  name: "Euro pallet",
  description: null,
  isArchived: false,
  isStackable: false,
  isExchangeable: true,
  loadFactors: [{ id: "lf1", vehicleTypeId: "vt-1", loadingCapacityFactor: 1, maxCapacity: 33 }],
  incompatibleTransportUnits: [],
  concurrencyStamp: "stamp-tu",
};

beforeEach(() => {
  grants = { ...CREATE_ALL };
  features = { ...ALL_CREATE_FEATURES };
  resetSavedDtos();
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/master-data/vehicle/{id}":
        return ok(VEHICLE);
      case "/api/master-data/vehicle/type-defaults/{vehicleTypeId}":
        return ok({ vehicleTypeId: init?.params?.path?.vehicleTypeId, kind: 2, lengthCm: 1360, widthCm: 248, heightCm: 400, grossVehicleWeightKg: 39000, maxEpq: 33, maxLoadingMeters: 13.6, characteristics: [] });
      case "/api/master-data/relation/lookup-page":
        return ok({ items: [{ id: "own", code: "OWN", name: "Own Transport", city: "Utrecht", countryCode: "NL", roles: 16 }], totalCount: 1 });
      case "/api/master-data/country/default":
        return ok({ id: "nl", isoAlpha2: "NL", primaryTimeZoneId: "Europe/Amsterdam" });
      case "/api/master-data/country/{id}":
        return ok(init?.params?.path?.id === "be" ? COUNTRY : { id: "nl", isoAlpha2: "NL", code: "NL", primaryTimeZoneId: "Europe/Amsterdam" });
      case "/api/master-data/country/lookup":
        return ok([{ id: "nl", code: "NL", name: "Netherlands" }, { id: "be", code: "BE", name: "Belgium" }]);
      case "/api/master-data/vehicle-type/lookup":
        return ok([{ id: "vt-1", code: null, name: "Tractor" }]);
      case "/api/master-data/currency/lookup":
        return ok([{ id: "cur-eur", code: "EUR", name: "Euro" }]);
      case "/api/master-data/currency":
        return ok({ rows: [], lastRow: 0 });
      case "/api/master-data/relation-category/lookup":
        return ok([{ id: "cat-1", code: null, name: "Cat A" }]);
      case "/api/master-data/geographical-region/lookup":
        return ok([]);
      case "/api/master-data/address-type/lookup":
        return ok([]);
      case "/api/master-data/transport-unit/{id}":
        return ok(TRANSPORT_UNIT);
      case "/api/master-data/transport-unit/lookup":
        return ok([{ id: "tu1", code: null, name: "Euro pallet" }]);
      case "/api/platform/administration/organization-units/all":
        return ok([{ id: "ou-1", displayName: "Unit 1", parentId: null }]);
      case "/api/master-data/transport-characteristics/lookup-page":
      case "/api/master-data/driver/user-lookup-page":
        return ok({ items: [], totalCount: 0 });
      default:
        return ok(undefined);
    }
  });
  POST.mockReset().mockResolvedValue(ok({ id: "posted" }));
  PUT.mockReset().mockImplementation(async (_path, init) => ok({ id: "put", ...(init?.body as object) }));
});

function renderWith(node: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
}
const common = { open: true, onClose: () => undefined, onSaved: () => undefined };

/* The pickers are found by their form id suffix (form.id(field) = `${instance}-${field}`). */
const createFor = (field: string) => document.querySelector<HTMLButtonElement>(`[data-create-for$="-${field}"]`);
const selectFor = (field: string) => document.querySelector<HTMLSelectElement>(`select[id$="-${field}"]`);
const valueFor = (field: string) => document.querySelector<HTMLElement>(`[data-value-for$="-${field}"]`);
const stub = () => document.querySelector<HTMLElement>("[data-stub-editor]");
const allCreateRows = () => document.querySelectorAll("[data-create-for]");

async function addNewAndSave(open: () => void, entity: string) {
  open();
  await waitFor(() => expect(stub()).toHaveAttribute("data-stub-editor", entity));
  expect(stub()).toHaveAttribute("data-mode", "create");
  expect(stub()).toHaveAttribute("data-initial-name", TYPED_TERM);
  await act(async () => {
    /* The stub renders beside the host's modal dialog, which marks it inert for role queries; the real nested
     * editor is a modal of its own on top. */
    fireEvent.click(within(stub()!).getByRole("button", { name: `stub-save-${entity}`, hidden: true }));
  });
  expect(stub()).toBeNull();
}

/* ── VehicleEditor ────────────────────────────────────────────────────────────────────────────────────────── */

describe("VehicleEditor", () => {
  it("vehicle type: the row with the permission; the new type is selected and its dimensions are fetched", async () => {
    renderWith(<VehicleEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("vehicleTypeId")).not.toBeNull());
    expect(createFor("vehicleTypeId")).toHaveTextContent("MasterData:AddNewVehicleType");

    await addNewAndSave(() => fireEvent.click(createFor("vehicleTypeId")!), "VehicleType");
    await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("new-type"));
    expect(within(selectFor("vehicleTypeId")!).getByRole("option", { name: "Low loader" })).toBeInTheDocument();
    await waitFor(() =>
      expect(GET).toHaveBeenCalledWith("/api/master-data/vehicle/type-defaults/{vehicleTypeId}", { params: { path: { vehicleTypeId: "new-type" } } }),
    );
  });

  it("organisation units: the new unit is added to what was selected", async () => {
    renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
    await waitFor(() => expect(valueFor("organizationUnitIds")).toHaveTextContent("ou-1"));
    expect(createFor("organizationUnitIds")).toHaveTextContent("Administration:AddNewOrganizationUnit");

    await addNewAndSave(() => fireEvent.click(createFor("organizationUnitIds")!), "OrganizationUnit");
    await waitFor(() => expect(valueFor("organizationUnitIds")).toHaveTextContent("ou-1,new-ou"));
    /* The unit list the picker reads has the new unit at once, so it shows as a checked option. */
    expect(document.querySelector('[data-option="new-ou"]')).toBeChecked();
  });

  it("no rows without the Create permissions", async () => {
    grants = {};
    renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
    await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("vt-1"));
    expect(createFor("vehicleTypeId")).toBeNull();
    expect(createFor("organizationUnitIds")).toBeNull();
    expect(createFor("registrationCountryId")).toBeNull();
    expect(allCreateRows()).toHaveLength(0);
  });

  it("no rows in a read-only editor, even with every Create permission", async () => {
    renderWith(<VehicleEditor mode="edit" id="v1" readOnly {...common} />);
    await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("vt-1"));
    expect(allCreateRows()).toHaveLength(0);
  });

  it("no rows when the target nodes' features are off, even with every Create permission", async () => {
    features = {};
    renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
    await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("vt-1"));
    expect(allCreateRows()).toHaveLength(0);
  });

  it("each picker follows its own target's node: vehicle types off hides only the vehicle-type row", async () => {
    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Fleet.VehicleTypes": "false" };
    renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
    await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("vt-1"));
    expect(createFor("vehicleTypeId")).toBeNull();
    expect(createFor("registrationCountryId")).not.toBeNull();
    expect(createFor("organizationUnitIds")).not.toBeNull();
  });

  it.each(["Apps.MasterData.General.Relations.Relations", "Apps.MasterData.General.Relations", "Apps.MasterData"])(
    "owner and home depot (own relation dialog): rows with Relations.Create and the chain on, none with %s off",
    async (off) => {
      renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
      await waitFor(() => expect(createFor("owner")).not.toBeNull());
      expect(createFor("homeDepot")).not.toBeNull();
      cleanup();

      features = { ...ALL_CREATE_FEATURES, [off]: "false" };
      renderWith(<VehicleEditor mode="edit" id="v1" readOnly={false} {...common} />);
      await waitFor(() => expect(selectFor("vehicleTypeId")).toHaveValue("vt-1"));
      expect(createFor("owner")).toBeNull();
      expect(createFor("homeDepot")).toBeNull();
    },
  );
});

/* ── VehicleTypeEditor (the real one; the module is a stub elsewhere in this file) ────────────────────────── */

describe("VehicleTypeEditor — characteristics (own dialog)", () => {
  it.each(["Apps.MasterData.General.Fleet.TransportCharacteristics", "Apps.MasterData.General.Fleet"])("the row with the chain on, none with %s off", async (off) => {
    const { VehicleTypeEditor } = await vi.importActual<typeof import("@/apps/master-data/editors/VehicleTypeEditor")>("@/apps/master-data/editors/VehicleTypeEditor");
    renderWith(<VehicleTypeEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("transportCharacteristicsIds")).not.toBeNull());
    cleanup();

    features = { ...ALL_CREATE_FEATURES, [off]: "false" };
    renderWith(<VehicleTypeEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(valueFor("transportCharacteristicsIds")).not.toBeNull());
    expect(createFor("transportCharacteristicsIds")).toBeNull();
  });
});

/* ── ProductRelationCodeEditor: the relation picker (own dialog) ──────────────────────────────────────────── */

describe("ProductRelationCodeEditor — relation (own dialog)", () => {
  it("the row with the chain on, none with Relations.Relations off, while product and unit keep theirs", async () => {
    grants = { ...CREATE_ALL, "MasterData.Products.Create": true };
    renderWith(<ProductRelationCodeEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("relation")).not.toBeNull());
    cleanup();

    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Relations.Relations": "false" };
    renderWith(<ProductRelationCodeEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("productId")).not.toBeNull());
    expect(createFor("transportUnitId")).not.toBeNull();
    expect(createFor("relation")).toBeNull();
  });
});

/* ── RelationEditor ───────────────────────────────────────────────────────────────────────────────────────── */

describe("RelationEditor", () => {
  it("categories: the new category is added to the selection", async () => {
    renderWith(<RelationEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(document.querySelector('[data-option="cat-1"]')).not.toBeNull());
    fireEvent.click(document.querySelector('[data-option="cat-1"]')!);
    expect(valueFor("categoryIds")).toHaveTextContent("cat-1");
    expect(createFor("categoryIds")).toHaveTextContent("MasterData:AddNewRelationCategory");

    await addNewAndSave(() => fireEvent.click(createFor("categoryIds")!), "RelationCategory");
    await waitFor(() => expect(valueFor("categoryIds")).toHaveTextContent("cat-1,new-category"));
    expect(document.querySelector('[data-option="new-category"]')).toBeChecked();
  });

  it("currency: the new currency is selected", async () => {
    renderWith(<RelationEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("currencyId")).not.toBeNull());
    expect(createFor("currencyId")).toHaveTextContent("MasterData:AddNewCurrency");
    await addNewAndSave(() => fireEvent.click(createFor("currencyId")!), "Currency");
    await waitFor(() => expect(selectFor("currencyId")).toHaveValue("new-currency"));
  });

  it("no category or currency row without the permissions", async () => {
    grants = {};
    renderWith(<RelationEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(document.querySelector('[data-option="cat-1"]')).not.toBeNull());
    expect(createFor("categoryIds")).toBeNull();
    expect(createFor("currencyId")).toBeNull();
    expect(allCreateRows()).toHaveLength(0);
  });

  it("no category row with Relations.RelationCategories off, no currency row with Apps.MasterData.General.Reference off", async () => {
    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Relations.RelationCategories": "false", "Apps.MasterData.General.Reference": "false" };
    renderWith(<RelationEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(document.querySelector('[data-option="cat-1"]')).not.toBeNull());
    expect(createFor("categoryIds")).toBeNull();
    expect(createFor("currencyId")).toBeNull();
  });

  it("no rows in a read-only editor", async () => {
    renderWith(<RelationEditor mode="create" readOnly {...common} />);
    await waitFor(() => expect(document.querySelector('[data-option="cat-1"]')).not.toBeNull());
    expect(allCreateRows()).toHaveLength(0);
  });
});

/* ── CountryEditor ────────────────────────────────────────────────────────────────────────────────────────── */

describe("CountryEditor", () => {
  it("default currency: the new currency is selected; the time zone picker has no row (fixed list)", async () => {
    renderWith(<CountryEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("defaultCurrencyId")).not.toBeNull());
    expect(createFor("primaryTimeZoneId")).toBeNull();
    await addNewAndSave(() => fireEvent.click(createFor("defaultCurrencyId")!), "Currency");
    await waitFor(() => expect(selectFor("defaultCurrencyId")).toHaveValue("new-currency"));
  });

  it("no row without the permission or when read-only", async () => {
    grants = {};
    renderWith(<CountryEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(selectFor("defaultCurrencyId")).not.toBeNull());
    expect(createFor("defaultCurrencyId")).toBeNull();
  });

  it("no row in a read-only editor", async () => {
    renderWith(<CountryEditor mode="edit" id="be" readOnly {...common} />);
    await waitFor(() => expect(selectFor("defaultCurrencyId")).toHaveValue("cur-eur"));
    expect(allCreateRows()).toHaveLength(0);
  });
});

/* ── DriverEditor ─────────────────────────────────────────────────────────────────────────────────────────── */

describe("DriverEditor", () => {
  it("user account: the new user is selected with its name", async () => {
    renderWith(<DriverEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("user")).not.toBeNull());
    expect(createFor("user")).toHaveTextContent("Administration:AddNewUser");
    await addNewAndSave(() => fireEvent.click(createFor("user")!), "User");
    await waitFor(() => expect(valueFor("user")).toHaveAttribute("data-id", "new-user"));
    expect(valueFor("user")).toHaveTextContent("Jane Doe");
  });

  it("employer (own relation dialog) and user rows follow their nodes: none with Relations off or Administration.Users off", async () => {
    renderWith(<DriverEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(createFor("employer")).not.toBeNull());
    expect(createFor("user")).not.toBeNull();
    cleanup();

    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Relations": "false", "Apps.Administration.Users": "false" };
    renderWith(<DriverEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(valueFor("user")).not.toBeNull());
    expect(createFor("employer")).toBeNull();
    expect(createFor("user")).toBeNull();
  });

  it("no user row without Users.Create, and none in a read-only editor", async () => {
    grants = { ...CREATE_ALL, "Platform.Administration.Users.Create": false };
    renderWith(<DriverEditor mode="create" readOnly={false} {...common} />);
    await waitFor(() => expect(valueFor("user")).not.toBeNull());
    expect(createFor("user")).toBeNull();
  });

  it("no rows in a read-only editor", async () => {
    renderWith(<DriverEditor mode="create" readOnly {...common} />);
    await waitFor(() => expect(valueFor("user")).not.toBeNull());
    expect(allCreateRows()).toHaveLength(0);
  });
});

/* ── TransportUnitEditor: the load-factor cell ────────────────────────────────────────────────────────────── */

describe("TransportUnitEditor — load-factor vehicle type cell", () => {
  const cell = () => document.querySelector<HTMLElement>('[data-slot=mock-line][data-key="lf1"] [data-cell-editor-for="vehicleTypeId"]');
  /* Inside the host's modal the accessible name of the row is not computed (inert subtree): found by its text. */
  const cellCreate = () => (cell() ? (within(cell()!).queryByText("MasterData:AddNewVehicleType")?.closest("button") ?? null) : null);

  it("the new type goes into that row and is saved with it", async () => {
    renderWith(<TransportUnitEditor mode="edit" id="tu1" readOnly={false} {...common} />);
    await waitFor(() => expect(cell()).not.toBeNull());
    await waitFor(() => expect(within(cell()!).getByRole("option", { selected: true, hidden: true })).toHaveTextContent("Tractor"));

    await addNewAndSave(() => {
      fireEvent.change(cell()!.querySelector("input")!, { target: { value: TYPED_TERM } });
      fireEvent.click(cellCreate()!);
    }, "VehicleType");
    await waitFor(() => expect(within(cell()!).getByRole("option", { selected: true, hidden: true })).toHaveTextContent("Low loader"));

    await act(async () => {
      fireEvent.submit(document.querySelector("form")!);
    });
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    const body = PUT.mock.calls[0][1]!.body as { loadFactors: { id: string | null; vehicleTypeId: string }[] };
    expect(body.loadFactors).toEqual([expect.objectContaining({ id: "lf1", vehicleTypeId: "new-type" })]);
  });

  it("no row in the cell without VehicleTypes.Create", async () => {
    grants = { ...CREATE_ALL, "MasterData.VehicleTypes.Create": false };
    renderWith(<TransportUnitEditor mode="edit" id="tu1" readOnly={false} {...common} />);
    await waitFor(() => expect(cell()).not.toBeNull());
    expect(cellCreate()).toBeNull();
    /* The incompatible-units picker is gated by its own permission. */
    expect(createFor("incompatibleTransportUnitIds")).not.toBeNull();
  });

  it("no row in the cell with Fleet.VehicleTypes off, even with VehicleTypes.Create", async () => {
    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Fleet.VehicleTypes": "false" };
    renderWith(<TransportUnitEditor mode="edit" id="tu1" readOnly={false} {...common} />);
    await waitFor(() => expect(cell()).not.toBeNull());
    expect(cellCreate()).toBeNull();
    expect(createFor("incompatibleTransportUnitIds")).not.toBeNull();
  });

  it("no editor and no rows in a read-only editor", async () => {
    renderWith(<TransportUnitEditor mode="edit" id="tu1" readOnly {...common} />);
    await waitFor(() => expect(document.querySelector('[data-slot=mock-line][data-key="lf1"]')).not.toBeNull());
    expect(cell()).toBeNull();
    expect(allCreateRows()).toHaveLength(0);
  });
});
