import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Orders master data editors (design 51, UI design): the transport order type (default stops as lines in position
 * order, the required-field flags as one checkbox per flag, the numbering), the waypoint type (the next type without
 * itself, the Master Data defaults, the fleet-management codes, one per vendor) and the time slot (HH:mm, the "ends
 * the next day" hint, zero length refused). The domain hooks are real; the generated client is the mock.
 * Risk class: Medium; the server tests prove the same rules. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: { "Orders.TransportOrderTypes.Delete": true } },
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
import { TimeSlotEditor } from "@/apps/master-data/editors/TimeSlotEditor";
import { TransportOrderTypeEditor } from "@/apps/master-data/editors/TransportOrderTypeEditor";
import { WaypointTypeEditor } from "@/apps/master-data/editors/WaypointTypeEditor";
import { parseTimeOfDay, timeOfDay } from "@/domains/orders/reference-data";

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

const stubBrowser = () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("IntersectionObserver", ObserverStub);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
};

beforeAll(stubBrowser);

const WAYPOINT_TYPES = [
  { id: "w-load", code: "LOAD", name: "Load" },
  { id: "w-unload", code: "UNLOAD", name: "Unload" },
  { id: "w-via", code: "VIA", name: "Via" },
];

let orderType: Record<string, unknown> = {};
let waypointType: Record<string, unknown> = {};
let timeSlot: Record<string, unknown> = {};

beforeEach(() => {
  orderType = {
    id: "o1", code: "STD", name: "Standard", names: { en: "Standard", nl: "Standaard" }, requiredFields: 1 | 2048,
    defaultWaypoints: [
      { waypointTypeId: "w-load", name: "Load", position: 1 },
      { waypointTypeId: "w-unload", name: "Unload", position: 2 },
    ],
    numbering: { prefix: "TO", includeYear: true, includeMonth: false, separator: "-", digits: 5 }, nextNumber: 7, nextNumberPreview: "TO-2026-00007",
    isDefault: true, isArchived: false, description: null, concurrencyStamp: "stamp-o1", creationTime: "2026-10-22T10:00:00Z",
  };
  waypointType = {
    id: "w-load", code: "LOAD", name: "Load", names: { en: "Load", nl: "Laden" }, activity: 1,
    successiveWaypointTypeId: "w-unload", successiveWaypointTypeName: "Unload",
    relationCategoryId: "cat-gone", relationCategoryName: "(not found)", defaultRelationId: "rel-1", defaultRelationName: "Own depot",
    defaultDurationMinutes: 30, fleetManagementCodes: [{ vendor: "Transics", code: "L" }],
    isArchived: false, description: null, concurrencyStamp: "stamp-w1", creationTime: "2026-10-22T10:00:00Z",
  };
  timeSlot = {
    id: "s1", code: "NIGHT", name: "Night", names: { en: "Night", nl: "Nacht" }, startTime: "22:00:00", endTime: "06:00:00", isOvernight: true,
    isArchived: false, description: null, concurrencyStamp: "stamp-s1", creationTime: "2026-10-22T10:00:00Z",
  };
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/orders/transport-order-type/{id}":
        return ok(orderType);
      case "/api/orders/waypoint-type/{id}":
        return ok(waypointType);
      case "/api/orders/time-slot/{id}":
        return ok(timeSlot);
      case "/api/orders/waypoint-type/lookup":
        return ok(WAYPOINT_TYPES);
      case "/api/master-data/relation-category/lookup":
        return ok([{ id: "cat-1", name: "Warehouses" }]);
      case "/api/master-data/relation/lookup-page":
        return ok({ items: [{ id: "rel-1", code: "DEPOT", name: "Own depot", city: "Venlo", countryCode: "NL", roles: 8 }], totalCount: 1 });
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async () => ok({ id: "new" }));
  PUT.mockReset().mockImplementation(async (path) => ok(path.includes("order-type") ? orderType : path.includes("waypoint") ? waypointType : timeSlot));
});

afterEach(() => {
  vi.unstubAllGlobals();
  stubBrowser();
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
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });
const bodyOf = (mock: Mock<ClientCall>) => mock.mock.calls[0][1]!.body as Record<string, unknown>;

describe("time of day helpers", () => {
  it("parses HH:mm in its usual spellings and refuses anything else", () => {
    expect(parseTimeOfDay("8:05")).toBe("08:05:00");
    expect(parseTimeOfDay("0805")).toBe("08:05:00");
    expect(parseTimeOfDay(" 22:00 ")).toBe("22:00:00");
    expect(parseTimeOfDay("24:00")).toBeNull();
    expect(parseTimeOfDay("12:60")).toBeNull();
    expect(parseTimeOfDay("noon")).toBeNull();
    expect(timeOfDay("06:00:00")).toBe("06:00");
    expect(timeOfDay(null)).toBe("");
  });
});

describe("TransportOrderTypeEditor", () => {
  it("creates with the stops in position order, the flags and the default numbering", async () => {
    renderWith(<TransportOrderTypeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(field("MasterData:Code"), { target: { value: "exp" } });
    fireEvent.change(field("MasterData:Name"), { target: { value: "Express" } });

    tab("Orders:DefaultWaypoints");
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "Unload" })).toBeInTheDocument());
    fireEvent.change(field("Orders:AddDefaultWaypoint"), { target: { value: "w-load" } });
    fireEvent.change(field("Orders:AddDefaultWaypoint"), { target: { value: "w-load" } });
    fireEvent.change(field("Orders:AddDefaultWaypoint"), { target: { value: "w-unload" } });
    expect(lines()).toHaveLength(3);

    tab("Orders:RequiredFields");
    fireEvent.click(within(editorDialog()).getByRole("checkbox", { name: "Orders:RequiredField.CustomerReference" }));
    fireEvent.click(within(editorDialog()).getByRole("checkbox", { name: "Orders:RequiredField.WeightPerLine" }));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/transport-order-type", expect.anything()));
    expect(bodyOf(POST)).toEqual({
      code: "EXP",
      names: { en: "Express" },
      requiredFields: 1 | 256,
      defaultWaypoints: [
        { waypointTypeId: "w-load", position: 1 },
        { waypointTypeId: "w-load", position: 2 },
        { waypointTypeId: "w-unload", position: 3 },
      ],
      numbering: { prefix: "TO", includeYear: true, includeMonth: false, separator: "-", digits: 5 },
      nextNumber: 1,
      isArchived: false,
      description: null,
    });
  });

  it("updates with the stamp, keeps the stops, clears a flag and shows the default without Delete", async () => {
    renderWith(<TransportOrderTypeEditor mode="edit" id="o1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(field("MasterData:Code").value).toBe("STD"));
    expect(within(editorDialog()).getAllByText("MasterData:IsDefault").length).toBeGreaterThan(0);
    expect(within(editorDialog()).queryByRole("button", { name: "Delete" })).toBeNull();

    tab("Orders:RequiredFields");
    const temperature = within(editorDialog()).getByRole("checkbox", { name: "Orders:RequiredField.Temperature" });
    expect(temperature).toBeChecked();
    fireEvent.click(temperature);

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalled());
    const body = bodyOf(PUT);
    expect(body.concurrencyStamp).toBe("stamp-o1");
    expect(body.requiredFields).toBe(1);
    expect(body.defaultWaypoints).toEqual([
      { waypointTypeId: "w-load", position: 1 },
      { waypointTypeId: "w-unload", position: 2 },
    ]);
    expect(body.names).toEqual({ en: "Standard", nl: "Standaard" });
  });
});

describe("WaypointTypeEditor", () => {
  it("offers every other type as the next type, keeps a Master Data row that is gone, and sends the codes", async () => {
    renderWith(<WaypointTypeEditor mode="edit" id="w-load" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(field("MasterData:Code").value).toBe("LOAD"));
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "Via" })).toBeInTheDocument());

    const successor = field("Orders:SuccessiveWaypointType") as unknown as HTMLSelectElement;
    expect(Array.from(successor.options).map((o) => o.value)).not.toContain("w-load");
    expect(successor.value).toBe("w-unload");

    tab("Orders:Defaults");
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "(not found)" })).toBeInTheDocument());

    tab("Orders:FleetManagementCodes");
    fireEvent.click(within(editorDialog()).getByRole("button", { name: /Orders:AddFleetManagementCode/ }));
    const inputs = within(lines()[1]).getAllByRole("textbox");
    fireEvent.change(inputs[0], { target: { value: "Trimble" } });
    fireEvent.change(inputs[1], { target: { value: "100" } });

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalled());
    const body = bodyOf(PUT);
    expect(body).toMatchObject({
      code: "LOAD",
      activity: 1,
      successiveWaypointTypeId: "w-unload",
      relationCategoryId: "cat-gone",
      defaultRelationId: "rel-1",
      defaultDurationMinutes: 30,
      concurrencyStamp: "stamp-w1",
    });
    expect(body.fleetManagementCodes).toEqual([
      { vendor: "Transics", code: "L" },
      { vendor: "Trimble", code: "100" },
    ]);
  });

  it("refuses a vendor twice and an incomplete code, and sends nothing", async () => {
    renderWith(<WaypointTypeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(field("MasterData:Code"), { target: { value: "x" } });
    fireEvent.change(field("MasterData:Name"), { target: { value: "X" } });

    tab("Orders:FleetManagementCodes");
    const add = () => fireEvent.click(within(editorDialog()).getByRole("button", { name: /Orders:AddFleetManagementCode/ }));
    add();
    add();
    const inputs = (line: HTMLElement) => Array.from(line.querySelectorAll("input"));
    for (const line of lines()) {
      const [vendor, code] = inputs(line);
      fireEvent.change(vendor, { target: { value: "Transics" } });
      fireEvent.change(code, { target: { value: "1" } });
    }

    await submitEditor();
    expect(screen.getByText("Orders:FleetManagementVendorTwice")).toBeInTheDocument();

    fireEvent.change(inputs(lines()[1])[1], { target: { value: "" } });
    await submitEditor();
    expect(screen.getByText("Orders:FleetManagementCodeIncomplete")).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
  });

  it("refuses a duration over a day", async () => {
    renderWith(<WaypointTypeEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(field("MasterData:Code"), { target: { value: "x" } });
    fireEvent.change(field("MasterData:Name"), { target: { value: "X" } });
    fireEvent.change(field("Orders:DefaultDurationMinutes"), { target: { value: "1441" } });
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();

    fireEvent.change(field("Orders:DefaultDurationMinutes"), { target: { value: "45" } });
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/waypoint-type", expect.anything()));
    expect(bodyOf(POST)).toMatchObject({ code: "X", activity: 1, defaultDurationMinutes: 45, successiveWaypointTypeId: null, fleetManagementCodes: [] });
  });
});

describe("TimeSlotEditor", () => {
  it("shows the next-day hint for an overnight slot and sends the times as HH:mm:ss", async () => {
    renderWith(<TimeSlotEditor mode="edit" id="s1" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    await waitFor(() => expect(field("Orders:StartTime").value).toBe("22:00"));
    expect(field("Orders:EndTime").value).toBe("06:00");
    expect(screen.getByText("Orders:EndsNextDay")).toBeInTheDocument();

    fireEvent.change(field("Orders:EndTime"), { target: { value: "2330" } });
    expect(screen.queryByText("Orders:EndsNextDay")).toBeNull();

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalled());
    expect(bodyOf(PUT)).toMatchObject({ code: "NIGHT", startTime: "22:00:00", endTime: "23:30:00", concurrencyStamp: "stamp-s1" });
  });

  it("refuses a time that is not HH:mm and a slot of zero length", async () => {
    renderWith(<TimeSlotEditor mode="create" readOnly={false} open onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(field("MasterData:Code"), { target: { value: "x" } });
    fireEvent.change(field("MasterData:Name"), { target: { value: "X" } });
    fireEvent.change(field("Orders:StartTime"), { target: { value: "25:00" } });
    fireEvent.change(field("Orders:EndTime"), { target: { value: "08:00" } });
    await submitEditor();
    expect(screen.getByText("Orders:InvalidTime")).toBeInTheDocument();

    fireEvent.change(field("Orders:StartTime"), { target: { value: "8:00" } });
    await submitEditor();
    expect(screen.getByText("OpenTms.Orders:EmptyTimeSlot")).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
  });
});
