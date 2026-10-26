import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The vehicle editor (design 48, UI design): a new vehicle starts with the own company as owner and the tenant's
 * default country; choosing a type on a new vehicle fills the empty dimension fields from `type-defaults` and keeps
 * what the user typed; the type's characteristics are read-only "from type" rows and only the vehicle's own rows can
 * be removed; the plate, country and VIN rules are checked on submit; a Dutch plate takes its sidecode dashes on
 * blur. The domain hooks and the EntityLookup are real; the generated client is the mock. Risk class: Medium (UI
 * flow); the server tests prove the same rules where they are enforced. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") || k.endsWith("FromTypeHint") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
/* With the characteristics node's chain: "Add new transport characteristic…" needs it besides the permission (UI-PERM-006). */
const DEFAULT_FEATURES = {
  "Apps.MasterData.General.Fleet.Vehicles": "true",
  "Apps.MasterData": "true", "Apps.MasterData.General": "true",
  "Apps.MasterData.General.Fleet": "true",
  "Apps.MasterData.General.Fleet.TransportCharacteristics": "true",
};
let features: Record<string, string> = { ...DEFAULT_FEATURES };
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: grants },
      features: { values: features },
      localization: { languages: [{ cultureName: "nl", displayName: "Nederlands" }] },
    },
  }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/lines-grid-lazy", async () => {
  const mocks = await import("./run2-test-mocks");
  return { LazyLinesGrid: mocks.MockLinesGrid };
});
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});
vi.mock("@/components/ui/multi-select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { MultiSelect: mocks.MockMultiSelect };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { VehicleEditor } from "@/domains/master-data/components/VehicleEditor";
import { VehicleTypeEditor } from "@/domains/master-data/components/VehicleTypeEditor";
import type { VehicleDto } from "@/domains/master-data/entities";

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

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

const TRACTOR_DEFAULTS = {
  vehicleTypeId: "vt-tractor",
  kind: 1,
  lengthCm: 600,
  widthCm: 255,
  heightCm: 400,
  grossVehicleWeightKg: 18000,
  maxEpq: null,
  maxLoadingMeters: null,
  characteristics: [{ id: "c-adr", code: "ADR", name: "Dangerous goods", origin: 1 }],
};

const TRAILER_DEFAULTS = {
  vehicleTypeId: "vt-trailer",
  kind: 2,
  lengthCm: 1360,
  widthCm: 248,
  heightCm: 400,
  grossVehicleWeightKg: 39000,
  maxEpq: 33,
  maxLoadingMeters: 13.6,
  characteristics: [],
};

const stored: VehicleDto = {
  id: "v1",
  name: "Truck 01",
  vehicleTypeId: "vt-tractor",
  vehicleTypeName: "Tractor 4x2",
  kind: 1,
  licencePlate: "12ABC3",
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
  characteristics: [
    { id: "c-adr", code: "ADR", name: "Dangerous goods", origin: 1 },
    { id: "c-cool", code: "COOL", name: "Cooling", origin: 2 },
  ],
  defaultDrivers: [],
  organizationUnitIds: [],
  externalIdentifiers: [],
  combinationName: null,
  description: null,
  concurrencyStamp: "stamp-v1",
  creationTime: "2026-10-15T10:00:00Z",
  lastModificationTime: null,
} as unknown as VehicleDto;

let detail: VehicleDto = stored;

beforeEach(() => {
  vi.stubGlobal("IntersectionObserver", ObserverStub);
  grants = {};
  features = { ...DEFAULT_FEATURES };
  detail = stored;
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/master-data/vehicle/{id}":
        return ok(detail);
      case "/api/master-data/vehicle/type-defaults/{vehicleTypeId}":
        return ok(init?.params?.path?.vehicleTypeId === "vt-trailer" ? TRAILER_DEFAULTS : TRACTOR_DEFAULTS);
      case "/api/master-data/relation/lookup-page":
        return init?.params?.query?.roles === 16
          ? ok({ items: [{ id: "own", code: "OWN", name: "Own Transport", city: "Utrecht", countryCode: "NL", roles: 16 }], totalCount: 1 })
          : ok({ items: [{ id: "car", code: "CAR", name: "Charter Carrier", city: "Delft", countryCode: "NL", roles: 2 }], totalCount: 1 });
      case "/api/master-data/country/default":
        return ok({ id: "nl", isoAlpha2: "NL" });
      case "/api/master-data/country/lookup":
        return ok([{ id: "nl", code: "NL", name: "Netherlands" }, { id: "be", code: "BE", name: "Belgium" }]);
      case "/api/master-data/vehicle-type/lookup":
        return ok([{ id: "vt-tractor", name: "Tractor 4x2" }, { id: "vt-trailer", name: "Curtainsider" }]);
      case "/api/master-data/transport-characteristics/lookup-page":
        return ok({ items: [{ id: "c-tl", code: "TL", name: "Tail lift", description: null }], totalCount: 1 });
      case "/api/platform/administration/organization-units/all":
        return ok([]);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => (path === "/api/master-data/vehicle" ? ok({ ...stored, id: "v-new" }) : ok(undefined)));
  PUT.mockReset().mockResolvedValue(ok(stored));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

function renderEditor(props: Partial<React.ComponentProps<typeof VehicleEditor>> = {}) {
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <VehicleEditor mode="create" readOnly={false} open onClose={onClose} onSaved={onSaved} {...props} />
    </QueryClientProvider>,
  );
  return { onClose, onSaved };
}

const editorDialog = () => document.querySelectorAll<HTMLElement>("[role=dialog]")[0];
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const field = (label: string) =>
  within(editorDialog()).getByLabelText(new RegExp(`^${escape(label)}`), { selector: "input, select, textarea" }) as HTMLInputElement;
const typeInto = (label: string, value: string) => fireEvent.change(field(label), { target: { value } });
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const typeDefaultCalls = () => GET.mock.calls.filter(([path]) => path === "/api/master-data/vehicle/type-defaults/{vehicleTypeId}");
const postedVehicle = () => POST.mock.calls.find(([path]) => path === "/api/master-data/vehicle")![1]!.body as Record<string, unknown>;

const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

/** The create editor renders once the own company and the default country are known. */
const ready = () => waitFor(() => expect(field("MasterData:Name")).toBeInTheDocument());

describe("VehicleEditor — a new vehicle", () => {
  it("starts with the own company as owner and the default country, and posts them", async () => {
    renderEditor();
    await ready();
    await waitFor(() => expect(field("MasterData:Owner")).toHaveValue("Own Transport"));
    expect(field("MasterData:RegistrationCountry")).toHaveValue("nl");

    typeInto("MasterData:Name", "Truck 09");
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-tractor" } });
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle", expect.anything()));
    expect(postedVehicle()).toEqual(expect.objectContaining({ name: "Truck 09", vehicleTypeId: "vt-tractor", ownerRelationId: "own", registrationCountryId: "nl", licencePlate: null, vin: null }));
  });

  it("choosing a type fills the empty dimension fields from type-defaults and keeps what the user typed", async () => {
    renderEditor();
    await ready();
    typeInto("MasterData:Name", "Truck 09");
    tab("MasterData:DimensionsAndCapacity");
    typeInto("MasterData:LengthCm", "610");
    tab("General");

    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-tractor" } });
    await waitFor(() => expect(typeDefaultCalls()).toHaveLength(1));
    expect(typeDefaultCalls()[0][1]).toEqual({ params: { path: { vehicleTypeId: "vt-tractor" } } });

    tab("MasterData:DimensionsAndCapacity");
    await waitFor(() => expect(field("MasterData:WidthCm")).toHaveValue("255"));
    expect(field("MasterData:LengthCm")).toHaveValue("610");
    expect(field("MasterData:HeightCm")).toHaveValue("400");
    expect(field("MasterData:GrossVehicleWeightKg")).toHaveValue("18000");
    expect(field("MasterData:EmptyWeightKg")).toHaveValue("");
    expect(field("MasterData:MaxPayloadKg")).toHaveValue("");

    /* A second type fills only what is still empty. */
    tab("General");
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-trailer" } });
    await waitFor(() => expect(typeDefaultCalls()).toHaveLength(2));
    tab("MasterData:DimensionsAndCapacity");
    await waitFor(() => expect(field("MasterData:MaxEpq")).toHaveValue("33"));
    expect(field("MasterData:WidthCm")).toHaveValue("255");
    expect(field("MasterData:MaxLoadingMeters")).toHaveValue("13.6");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle", expect.anything()));
    expect(postedVehicle()).toEqual(
      expect.objectContaining({ vehicleTypeId: "vt-trailer", lengthCm: 610, widthCm: 255, heightCm: 400, grossVehicleWeightKg: 18000, maxEpq: 33, maxLoadingMeters: 13.6, emptyWeightKg: null, maxPayloadKg: null }),
    );
  });

  it("an existing vehicle does not take type defaults when its type changes", async () => {
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-trailer" } });
    tab("MasterData:DimensionsAndCapacity");
    expect(field("MasterData:MaxEpq")).toHaveValue("");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ vehicleTypeId: "vt-trailer", maxEpq: null, lengthCm: 600, concurrencyStamp: "stamp-v1" }));
  });
});

describe("VehicleEditor — characteristics", () => {
  it("shows the type's characteristics read-only and lets only the vehicle's own be removed", async () => {
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    tab("MasterData:Characteristics");

    const rows = lines();
    expect(rows.map((r) => r.getAttribute("data-key"))).toEqual(["c-adr", "c-cool"]);
    expect(within(rows[0]).queryByText("Remove")).toBeNull();
    expect(within(rows[1]).getByText("Remove")).toBeInTheDocument();

    fireEvent.click(within(rows[1]).getByText("Remove"));
    expect(lines().map((r) => r.getAttribute("data-key"))).toEqual(["c-adr"]);

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ transportCharacteristicIds: [] }));
  });

  it("on a new vehicle the chosen type's characteristics appear as non-removable rows and are not posted as the vehicle's own", async () => {
    renderEditor();
    await ready();
    typeInto("MasterData:Name", "Truck 09");
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-tractor" } });
    tab("MasterData:Characteristics");
    await waitFor(() => expect(lines().map((r) => r.getAttribute("data-key"))).toEqual(["c-adr"]));
    expect(within(lines()[0]).queryByText("Remove")).toBeNull();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle", expect.anything()));
    expect(postedVehicle().transportCharacteristicIds).toEqual([]);
  });
});

describe("VehicleEditor — Add new transport characteristic… (own dialog, UI-LOOK-004 / UI-PERM-006)", () => {
  const openCharacteristicLookup = async () => {
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    tab("MasterData:Characteristics");
    const input = within(editorDialog()).getByLabelText("MasterData:AddTransportCharacteristic", { selector: "input" });
    fireEvent.pointerDown(input);
    fireEvent.mouseDown(input);
    fireEvent.click(input);
    await screen.findByText("Tail lift");
  };

  it("the row with TransportCharacteristics.Create and the chain on", async () => {
    grants = { "MasterData.TransportCharacteristics.Create": true };
    await openCharacteristicLookup();
    expect(screen.getByText("MasterData:AddNewTransportCharacteristics")).toBeInTheDocument();
  });

  it.each(["Apps.MasterData.General.Fleet.TransportCharacteristics", "Apps.MasterData.General.Fleet"])("no row with the permission when %s is off", async (off) => {
    grants = { "MasterData.TransportCharacteristics.Create": true };
    features = { ...DEFAULT_FEATURES, [off]: "false" };
    await openCharacteristicLookup();
    expect(screen.queryByText("MasterData:AddNewTransportCharacteristics")).toBeNull();
  });
});

describe("VehicleEditor — registration", () => {
  it("refuses a plate with other characters, a plate without a country and a VIN outside the ISO 3779 shape", async () => {
    renderEditor();
    await ready();
    typeInto("MasterData:Name", "Truck 09");
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-tractor" } });

    typeInto("MasterData:LicencePlate", "12#AB");
    typeInto("MasterData:Vin", "WDB9634031I123456");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:InvalidLicencePlate")).toBeInTheDocument();
    expect(screen.getByText("MasterData:InvalidVin")).toBeInTheDocument();

    typeInto("MasterData:LicencePlate", "12-ABC-3");
    fireEvent.change(field("MasterData:RegistrationCountry"), { target: { value: "" } });
    typeInto("MasterData:Vin", "wdb9634031l123456");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:RegistrationCountryRequired")).toBeInTheDocument();

    fireEvent.change(field("MasterData:RegistrationCountry"), { target: { value: "nl" } });
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle", expect.anything()));
    expect(postedVehicle()).toEqual(expect.objectContaining({ registrationCountryId: "nl", vin: "WDB9634031L123456" }));
    /* The server normalises; the display form (with or without dashes) is the formatter's concern, tested on its own. */
    expect(String(postedVehicle().licencePlate).replace(/-/g, "")).toBe("12ABC3");
  });

  it("a Dutch plate takes its sidecode dashes on blur; a Belgian one stays as typed in upper case", async () => {
    renderEditor();
    await ready();
    const plate = field("MasterData:LicencePlate");
    fireEvent.change(plate, { target: { value: "12abc3" } });
    expect(plate).toHaveValue("12ABC3");
    fireEvent.blur(plate);
    expect(field("MasterData:LicencePlate")).toHaveValue("12-ABC-3");

    fireEvent.change(field("MasterData:RegistrationCountry"), { target: { value: "be" } });
    expect(field("MasterData:LicencePlate")).toHaveValue("12ABC3");
  });

  it("an edited vehicle shows its stored plate in the display form of its country", async () => {
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    expect(field("MasterData:LicencePlate")).toHaveValue("12-ABC-3");
    expect(editorDialog().querySelector("[data-slot=entity-dialog-header]")).toHaveTextContent("MasterData:VehicleTypeKind.Tractor");
  });
});

/* QP10: the own-fleet cost price per kilometre on the type and on the vehicle; the server uses the vehicle's own value,
 * else its type's, so an empty vehicle field names the type's value in its hint. */
describe("Cost price per km (QP10)", () => {
  const costField = () => field("MasterData:CostPricePerKm");
  const hintOf = (input: HTMLInputElement) => document.getElementById(input.getAttribute("aria-describedby") ?? "")?.textContent ?? null;

  it("an empty vehicle field shows the type's value as its hint and posts null", async () => {
    GET.mockImplementation(async (path, init) => {
      if (path === "/api/master-data/vehicle/type-defaults/{vehicleTypeId}")
        return ok(init?.params?.path?.vehicleTypeId === "vt-trailer" ? { ...TRAILER_DEFAULTS, costPricePerKm: null } : { ...TRACTOR_DEFAULTS, costPricePerKm: 1.4 });
      if (path === "/api/master-data/vehicle/{id}") return ok(detail);
      if (path === "/api/master-data/vehicle-type/lookup") return ok([{ id: "vt-tractor", name: "Tractor 4x2" }, { id: "vt-trailer", name: "Curtainsider" }]);
      if (path === "/api/master-data/country/lookup") return ok([{ id: "nl", code: "NL", name: "Netherlands" }]);
      return ok([]);
    });
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    tab("MasterData:DimensionsAndCapacity");
    expect(costField()).toHaveValue("");
    await waitFor(() => expect(hintOf(costField())).toBe("MasterData:CostPricePerKmFromTypeHint 1.40"));

    /* A type without a value says so; a typed value of its own drops the hint. */
    tab("General");
    fireEvent.change(field("MasterData:VehicleType"), { target: { value: "vt-trailer" } });
    tab("MasterData:DimensionsAndCapacity");
    await waitFor(() => expect(hintOf(costField())).toBe("MasterData:CostPricePerKmNoTypeValueHint"));
    typeInto("MasterData:CostPricePerKm", "1.25");
    expect(hintOf(costField())).toBeNull();

    typeInto("MasterData:CostPricePerKm", "");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ vehicleTypeId: "vt-trailer", costPricePerKm: null }));
  });

  it("the vehicle's own value is loaded and saved, and a negative one is refused on submit", async () => {
    detail = { ...stored, costPricePerKm: 1.2345 } as VehicleDto;
    renderEditor({ mode: "edit", id: "v1" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Truck 01"));
    tab("MasterData:DimensionsAndCapacity");
    expect(costField()).toHaveValue("1.2345");
    expect(hintOf(costField())).toBeNull();

    typeInto("MasterData:CostPricePerKm", "-1");
    await submitEditor();
    expect(PUT).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:InvalidNumber")).toBeInTheDocument();

    typeInto("MasterData:CostPricePerKm", "1.5");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ costPricePerKm: 1.5 }));
  });
});

describe("VehicleTypeEditor — cost price per km (QP10)", () => {
  function renderTypeEditor(props: Partial<React.ComponentProps<typeof VehicleTypeEditor>> = {}) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <VehicleTypeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} {...props} />
      </QueryClientProvider>,
    );
  }

  it("a new type posts its cost price per km", async () => {
    POST.mockImplementation(async () => ok({ id: "vt-new", name: "Tractor 6x2" }));
    renderTypeEditor({ initialName: "Tractor 6x2" });
    await waitFor(() => expect(field("MasterData:Name")).toHaveValue("Tractor 6x2"));
    typeInto("MasterData:CostPricePerKm", "1.2345");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle-type", expect.anything()));
    expect(POST.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ name: "Tractor 6x2", costPricePerKm: 1.2345 }));
  });

  it("an existing type shows its value and clears it to null", async () => {
    GET.mockImplementation(async (path) =>
      path === "/api/master-data/vehicle-type/{id}"
        ? ok({ id: "vt-tractor", name: "Tractor 4x2", kind: 1, costPricePerKm: 0.95, transportCharacteristics: [], concurrencyStamp: "stamp-vt" })
        : ok([]),
    );
    renderTypeEditor({ mode: "edit", id: "vt-tractor" });
    await waitFor(() => expect(field("MasterData:CostPricePerKm")).toHaveValue("0.95"));
    typeInto("MasterData:CostPricePerKm", "");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ costPricePerKm: null, concurrencyStamp: "stamp-vt" }));
  });
});
