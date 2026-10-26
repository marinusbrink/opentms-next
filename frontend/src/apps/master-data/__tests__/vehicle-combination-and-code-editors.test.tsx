import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The vehicle combination editor and the product relation code editor (design 48, UI design). Combination: the
 * member picker is restricted by the position it fills (motorised kinds for position 1, trailers after), the members
 * are renumbered in position order, and the rules (2 to 4 members, the first motorised, the others trailers) are
 * checked on submit before anything is sent. Product relation code: at least one of destination, product and
 * transport unit, the destination EntityLookup over every relation. The domain hooks and the EntityLookup are real;
 * the generated client is the mock. Risk class: Medium (UI flow); the server tests prove the same rules. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: {} },
      features: { values: {} },
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
vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { ProductRelationCodeEditor } from "@/domains/master-data/components/ProductRelationCodeEditor";
import { VehicleCombinationEditor } from "@/domains/master-data/components/VehicleCombinationEditor";

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

const TRACTOR = { id: "t1", name: "Tractor 01", licencePlate: "12-ABC-3", kind: 1, vehicleTypeName: "Tractor 4x2" };
const TRAILER_A = { id: "tr1", name: "Trailer A", licencePlate: null, kind: 2, vehicleTypeName: "Curtainsider" };
const TRAILER_B = { id: "tr2", name: "Trailer B", licencePlate: null, kind: 2, vehicleTypeName: "Curtainsider" };

const member = (v: typeof TRACTOR | typeof TRAILER_A, position: number) => ({ vehicleId: v.id, name: v.name, kind: v.kind, licencePlate: v.licencePlate, position });

let combination: Record<string, unknown> = {};
let code: Record<string, unknown> = {};

beforeEach(() => {
  vi.stubGlobal("IntersectionObserver", ObserverStub);
  combination = {
    id: "c1", name: "Night shift", isArchived: false, description: null, concurrencyStamp: "stamp-c1",
    members: [member(TRACTOR, 1), member(TRAILER_A, 2), member(TRAILER_B, 3)], creationTime: "2026-10-15T10:00:00Z",
  };
  code = {
    id: "p1", code: "ART-4711", isArchived: false, relationId: "rel-1", relationCode: "DC", relationName: "Distribution Centre", relationCity: "Utrecht",
    productId: "prod-1", productName: "Bananas", transportUnitId: null, description: null, concurrencyStamp: "stamp-p1", creationTime: "2026-10-15T10:00:00Z",
  };
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/master-data/vehicle-combination/{id}":
        return ok(combination);
      case "/api/master-data/product-relation-code/{id}":
        return ok(code);
      case "/api/master-data/vehicle/lookup-page": {
        const kinds = (init?.params?.query?.kinds as number[] | undefined) ?? [];
        const items = [TRACTOR, TRAILER_A, TRAILER_B].filter((v) => kinds.length === 0 || kinds.includes(v.kind));
        return ok({ items, totalCount: items.length });
      }
      case "/api/master-data/relation/lookup-page":
        return ok({ items: [{ id: "rel-1", code: "DC", name: "Distribution Centre", city: "Utrecht", countryCode: "NL", roles: 1 }], totalCount: 1 });
      case "/api/master-data/product/lookup":
        return ok([{ id: "prod-1", name: "Bananas" }, { id: "prod-2", name: "Apples" }]);
      case "/api/master-data/transport-unit/lookup":
        return ok([{ id: "tu-1", name: "Euro pallet" }]);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => ok(path.includes("combination") ? { ...combination, id: "c-new" } : { ...code, id: "p-new" }));
  PUT.mockReset().mockImplementation(async (path) => ok(path.includes("combination") ? combination : code));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const editorDialog = () => document.querySelectorAll<HTMLElement>("[role=dialog]")[0];
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const field = (label: string) =>
  within(editorDialog()).getByLabelText(new RegExp(`^${escape(label)}`), { selector: "input, select, textarea" }) as HTMLInputElement;
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const vehicleLookups = () => GET.mock.calls.filter(([path]) => path === "/api/master-data/vehicle/lookup-page");
const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

async function pickVehicle(name: string) {
  openPopup(field("MasterData:AddMember"));
  const option = await screen.findByText(name);
  fireEvent.click(option.closest("[role=option]")!);
}

describe("VehicleCombinationEditor — members", () => {
  it("the picker offers motorised vehicles for position 1 and trailers after, and the members post in position order", async () => {
    const onSaved = vi.fn();
    renderWith(<VehicleCombinationEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={onSaved} />);

    await pickVehicle("Tractor 01");
    expect(vehicleLookups()[0][1]).toEqual({ params: { query: { term: undefined, skip: 0, take: 25, kinds: [1, 3, 4], ownerRelationId: undefined } } });
    await waitFor(() => expect(lines()).toHaveLength(1));

    await pickVehicle("Trailer A");
    expect(vehicleLookups().at(-1)![1]!.params!.query!.kinds).toEqual([2]);
    await waitFor(() => expect(lines()).toHaveLength(2));
    expect(lines().map((l) => l.getAttribute("data-key"))).toEqual(["t1", "tr1"]);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle-combination", expect.anything()));
    expect(POST.mock.calls[0][1]!.body).toEqual({
      name: null,
      isArchived: false,
      description: null,
      members: [
        { vehicleId: "t1", position: 1 },
        { vehicleId: "tr1", position: 2 },
      ],
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it("refuses fewer than two members and sends nothing", async () => {
    renderWith(<VehicleCombinationEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await submitEditor();
    expect(screen.getByText("MasterData:CombinationMembersRequired")).toBeInTheDocument();

    await pickVehicle("Tractor 01");
    await waitFor(() => expect(lines()).toHaveLength(1));
    await submitEditor();
    expect(screen.getByText("MasterData:CombinationMembersRequired")).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
  });

  it("refuses a trailer in position 1 after the positions were edited, and sends nothing", async () => {
    renderWith(<VehicleCombinationEditor mode="edit" id="c1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(lines()).toHaveLength(3));

    /* Trailer B moved before the tractor. */
    fireEvent.change(within(lines()[2]).getByLabelText("MasterData:Position"), { target: { value: "0" } });
    await submitEditor();
    expect(screen.getByText("MasterData:CombinationMotorisedFirst")).toBeInTheDocument();
    expect(PUT).not.toHaveBeenCalled();
  });

  it("refuses a combination whose motorised vehicle was removed, then saves it renumbered once fixed", async () => {
    renderWith(<VehicleCombinationEditor mode="edit" id="c1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(lines()).toHaveLength(3));
    /* Three members: the picker is there. */
    expect(within(editorDialog()).getByLabelText("MasterData:AddMember")).toBeInTheDocument();

    fireEvent.click(within(lines()[0]).getByText("Remove"));
    await submitEditor();
    expect(screen.getByText("MasterData:CombinationMotorisedFirst")).toBeInTheDocument();
    expect(PUT).not.toHaveBeenCalled();

    /* The picker now asks for a motorised vehicle and puts it first. */
    await pickVehicle("Tractor 01");
    expect(vehicleLookups().at(-1)![1]!.params!.query!.kinds).toEqual([1, 3, 4]);
    await waitFor(() => expect(lines().map((l) => l.getAttribute("data-key"))).toEqual(["t1", "tr1", "tr2"]));

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(
      expect.objectContaining({
        concurrencyStamp: "stamp-c1",
        name: "Night shift",
        members: [
          { vehicleId: "t1", position: 1 },
          { vehicleId: "tr1", position: 2 },
          { vehicleId: "tr2", position: 3 },
        ],
      }),
    );
  });

  it("offers no picker at four members", async () => {
    combination = { ...combination, members: [member(TRACTOR, 1), member(TRAILER_A, 2), member(TRAILER_B, 3), member({ ...TRAILER_B, id: "tr3", name: "Trailer C" }, 4)] };
    renderWith(<VehicleCombinationEditor mode="edit" id="c1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(lines()).toHaveLength(4));
    expect(within(editorDialog()).queryByLabelText("MasterData:AddMember")).toBeNull();
  });
});

describe("ProductRelationCodeEditor", () => {
  it("refuses a code that maps to nothing and one without a code, and sends nothing", async () => {
    renderWith(<ProductRelationCodeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await submitEditor();
    expect(screen.getByText("MasterData:CodeRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:ProductRelationCodeTargetRequired")).toBeInTheDocument();

    fireEvent.change(field("MasterData:Code"), { target: { value: "ART-1" } });
    await submitEditor();
    expect(screen.getByText("MasterData:ProductRelationCodeTargetRequired")).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
  });

  it.each([
    ["the product", "MasterData:Product", "prod-2", { productId: "prod-2", relationId: null, transportUnitId: null }],
    ["the transport unit", "MasterData:TransportUnit", "tu-1", { productId: null, relationId: null, transportUnitId: "tu-1" }],
  ])("one target is enough: %s", async (_name, label, value, expected) => {
    renderWith(<ProductRelationCodeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} initialName=" art-1 " />);
    expect(field("MasterData:Code")).toHaveValue(" art-1 ");
    await waitFor(() => expect(within(field(label)).getAllByRole("option").length).toBeGreaterThan(1));
    fireEvent.change(field(label), { target: { value } });

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/product-relation-code", expect.anything()));
    expect(POST.mock.calls[0][1]!.body).toEqual({ code: "art-1", isArchived: false, description: null, ...expected });
  });

  it("the destination lookup searches every relation and the pick travels as relationId", async () => {
    renderWith(<ProductRelationCodeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(field("MasterData:Code"), { target: { value: "ART-2" } });
    openPopup(field("MasterData:Destination"));
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/relation/lookup-page", { params: { query: { term: undefined, skip: 0, take: 25, roles: undefined } } }));
    fireEvent.click((await screen.findByText("Distribution Centre")).closest("[role=option]")!);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/product-relation-code", expect.anything()));
    expect(POST.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ code: "ART-2", relationId: "rel-1", productId: null, transportUnitId: null }));
  });

  it("loads a code with its destination and product and updates with the concurrency stamp; clearing every target is refused", async () => {
    renderWith(<ProductRelationCodeEditor mode="edit" id="p1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(field("MasterData:Code")).toHaveValue("ART-4711"));
    expect(field("MasterData:Destination")).toHaveValue("Distribution Centre");
    await waitFor(() => expect(field("MasterData:Product")).toHaveValue("prod-1"));

    fireEvent.change(field("MasterData:Product"), { target: { value: "" } });
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(PUT.mock.calls[0][1]!.body).toEqual(expect.objectContaining({ code: "ART-4711", relationId: "rel-1", productId: null, concurrencyStamp: "stamp-p1" }));
  });
});
