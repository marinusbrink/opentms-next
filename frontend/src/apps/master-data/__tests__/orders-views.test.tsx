import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The three Orders lists of design 51 (UI design): Transport order types, Waypoint types and Time slots — the view
 * permission, the grid id and the designed columns (hidden ones after the visible; the columns the server cannot sort
 * are not sortable), the archived filter as a list request parameter and Set as default on a non-default order type.
 * The domain hooks are real; the generated client is the mock. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants }, features: { values: {} }, localization: { languages: [] } } }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/opentms-grid", async () => {
  const mocks = await import("./run2-test-mocks");
  return { OpenTmsGrid: mocks.MockOpenTmsGrid };
});
vi.mock("@/apps/master-data/editors/TransportOrderTypeEditor", () => ({ TransportOrderTypeEditor: () => <div data-testid="order-type-editor" /> }));
vi.mock("@/apps/master-data/editors/WaypointTypeEditor", () => ({ WaypointTypeEditor: () => <div data-testid="waypoint-type-editor" /> }));
vi.mock("@/apps/master-data/editors/TimeSlotEditor", () => ({ TimeSlotEditor: () => <div data-testid="time-slot-editor" /> }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { TimeSlotsView } from "@/apps/master-data/views/TimeSlotsView";
import { TransportOrderTypesView } from "@/apps/master-data/views/TransportOrderTypesView";
import { WaypointTypesView } from "@/apps/master-data/views/WaypointTypesView";
import { gridRows } from "./run2-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;

const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

beforeEach(() => {
  grants = {};
  gridRows.current = [];
  GET.mockReset().mockResolvedValue(ok({ rows: [], totalCount: 0, filteredCount: 0 }));
  POST.mockReset().mockResolvedValue(ok(undefined));
});

function renderView(view: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{view}</QueryClientProvider>);
}

const listCalls = (path: string) => GET.mock.calls.filter(([p]) => p === path);
const lastListQuery = (path: string) => listCalls(path).at(-1)![1]!.params!.query!;
const columns = () =>
  Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span")).map((el) => `${el.getAttribute("data-column")}${el.getAttribute("data-hidden") === "true" ? " (hidden)" : ""}`);
const unsortable = () =>
  Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span[data-sortable=false]")).map((el) => el.getAttribute("data-column"));

describe("TransportOrderTypesView", () => {
  it("needs Orders.TransportOrderTypes and fetches nothing without it", () => {
    renderView(<TransportOrderTypesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(listCalls("/api/orders/transport-order-type")).toHaveLength(0);
  });

  it("renders the designed columns, sends the archived filter and moves the default", async () => {
    grants = { "Orders.TransportOrderTypes": true, "Orders.TransportOrderTypes.Update": true };
    gridRows.current = [{ id: "o2", code: "EXP", name: "Express", isDefault: false, isArchived: false }];
    renderView(<TransportOrderTypesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.transportOrderTypes");
    expect(columns()).toEqual(["code", "name", "numberingExample", "defaultWaypoints", "isDefault", "isArchived (hidden)", "description (hidden)"]);
    expect(unsortable()).toEqual(["numberingExample", "defaultWaypoints", "description"]);
    await waitFor(() => expect(lastListQuery("/api/orders/transport-order-type").includeArchived).toBe(false));

    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/orders/transport-order-type").includeArchived).toBe(true));

    fireEvent.click(screen.getByRole("button", { name: "MasterData:SetAsDefault" }));
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/orders/transport-order-type/{id}/set-default", { params: { path: { id: "o2" } } }),
    );
  });

  it("offers no Set as default without Update", async () => {
    grants = { "Orders.TransportOrderTypes": true };
    gridRows.current = [{ id: "o2", code: "EXP", name: "Express", isDefault: false, isArchived: false }];
    renderView(<TransportOrderTypesView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByRole("button", { name: "MasterData:SetAsDefault" })).toBeNull();
  });
});

describe("WaypointTypesView", () => {
  it("renders the designed columns with the Master Data names hidden and not sortable", async () => {
    grants = { "Orders.WaypointTypes": true };
    renderView(<WaypointTypesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.waypointTypes");
    expect(columns()).toEqual([
      "code", "name", "activity", "successiveWaypointTypeName", "defaultDurationMinutes",
      "relationCategoryName (hidden)", "defaultRelationName (hidden)", "isArchived (hidden)", "description (hidden)",
    ]);
    expect(unsortable()).toEqual(["relationCategoryName", "defaultRelationName", "description"]);
    await waitFor(() => expect(listCalls("/api/orders/waypoint-type")).toHaveLength(1));
  });

  it("needs Orders.WaypointTypes", () => {
    renderView(<WaypointTypesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});

describe("TimeSlotsView", () => {
  it("renders the designed columns", async () => {
    grants = { "Orders.TimeSlots": true };
    renderView(<TimeSlotsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.timeSlots");
    expect(columns()).toEqual(["code", "name", "startTime", "endTime", "isOvernight", "isArchived (hidden)", "description (hidden)"]);
    await waitFor(() => expect(listCalls("/api/orders/time-slot")).toHaveLength(1));
  });

  it("needs Orders.TimeSlots", () => {
    renderView(<TimeSlotsView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});
