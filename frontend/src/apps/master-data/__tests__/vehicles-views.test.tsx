import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The three lists of design 48 (UI design): Vehicles, Vehicle combinations and Product relation codes — the view
 * permission, the grid id and the designed columns (hidden ones after the visible), the filters as list request
 * parameters, and the confirmed Archive row action. The domain hooks are real; the generated client is the mock.
 * Risk class: Medium (UI flows). */

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
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/entity-lookup", () => ({
  EntityLookup: (props: { id: string; placeholder?: string; onChange: (value: unknown) => void }) => (
    <button type="button" id={props.id} data-testid="owner-filter" onClick={() => props.onChange({ id: "car", code: "CAR", name: "Carrier", city: null, countryCode: null, roles: 2 })}>
      {props.placeholder}
    </button>
  ),
}));
vi.mock("@/domains/master-data/components/VehicleEditor", () => ({ VehicleEditor: () => <div data-testid="vehicle-editor" /> }));
vi.mock("@/domains/master-data/components/VehicleCombinationEditor", () => ({ VehicleCombinationEditor: () => <div data-testid="combination-editor" /> }));
vi.mock("@/domains/master-data/components/ProductRelationCodeEditor", () => ({ ProductRelationCodeEditor: () => <div data-testid="code-editor" /> }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { ProductRelationCodesView } from "@/apps/master-data/views/ProductRelationCodesView";
import { VehicleCombinationsView } from "@/apps/master-data/views/VehicleCombinationsView";
import { VehiclesView } from "@/apps/master-data/views/VehiclesView";
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

describe("VehiclesView", () => {
  it("needs MasterData.Vehicles and fetches nothing without it", () => {
    renderView(<VehiclesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(listCalls("/api/master-data/vehicle")).toHaveLength(0);
  });

  it("renders masterData.vehicles with the designed columns and sends the kind, owner and archived filters", async () => {
    grants = { "MasterData.Vehicles": true };
    renderView(<VehiclesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.vehicles");
    expect(columns()).toEqual([
      "name", "licencePlate", "vehicleTypeName", "kind", "ownerName", "homeDepotName", "combinationName",
      "fuelType (hidden)", "euroClass (hidden)", "vin (hidden)", "isArchived (hidden)",
    ]);
    await waitFor(() => expect(listCalls("/api/master-data/vehicle")).toHaveLength(1));
    expect(lastListQuery("/api/master-data/vehicle").includeArchived).toBe(false);
    expect(lastListQuery("/api/master-data/vehicle").kinds).toBeUndefined();
    expect(lastListQuery("/api/master-data/vehicle").ownerRelationId).toBeUndefined();

    const kind = screen.getByRole("combobox", { name: "MasterData:Kind" });
    expect(within(kind).getAllByRole("option").map((o) => o.textContent)).toEqual(
      expect.arrayContaining(["MasterData:AllKinds", "MasterData:VehicleTypeKind.Tractor", "MasterData:VehicleTypeKind.Trailer"]),
    );
    fireEvent.change(kind, { target: { value: "2" } });
    await waitFor(() => expect(lastListQuery("/api/master-data/vehicle").kinds).toBe(2));

    fireEvent.click(screen.getByTestId("owner-filter"));
    await waitFor(() => expect(lastListQuery("/api/master-data/vehicle").ownerRelationId).toBe("car"));

    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/master-data/vehicle").includeArchived).toBe(true));
  });

  it("Archive is a confirmed row action with Update and posts to the archive endpoint", async () => {
    grants = { "MasterData.Vehicles": true, "MasterData.Vehicles.Update": true };
    gridRows.current = [{ id: "v1", name: "Truck 01", isArchived: false }];
    renderView(<VehiclesView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("MasterData:Archive"));
    const confirm = screen.getByRole("alertdialog");
    expect(within(confirm).getByText(/Truck 01/)).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle/{id}/archive", { params: { path: { id: "v1" } } }));
  });

  it("offers no Archive without Update", async () => {
    grants = { "MasterData.Vehicles": true };
    gridRows.current = [{ id: "v1", name: "Truck 01", isArchived: false }];
    renderView(<VehiclesView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:Archive")).toBeNull();
  });
});

describe("VehicleCombinationsView", () => {
  it("renders masterData.vehicleCombinations with the designed columns and unarchives an archived row after confirmation", async () => {
    grants = { "MasterData.VehicleCombinations": true, "MasterData.VehicleCombinations.Update": true };
    gridRows.current = [{ id: "c1", name: "Night shift", members: "T1 + TR1", motorisedVehicleName: "T1", isArchived: true }];
    renderView(<VehicleCombinationsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.vehicleCombinations");
    expect(columns()).toEqual(["name", "members", "motorisedVehicleName", "isArchived (hidden)"]);

    fireEvent.click(screen.getByText("MasterData:Unarchive"));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "MasterData:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/vehicle-combination/{id}/unarchive", { params: { path: { id: "c1" } } }));
  });

  it("needs MasterData.VehicleCombinations", () => {
    renderView(<VehicleCombinationsView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});

describe("ProductRelationCodesView", () => {
  it("renders masterData.productRelationCodes with the designed columns and archives a row after confirmation", async () => {
    grants = { "MasterData.ProductRelationCodes": true, "MasterData.ProductRelationCodes.Update": true };
    gridRows.current = [{ id: "p1", code: "ART-1", relationName: "DC", relationCity: "Utrecht", isArchived: false }];
    renderView(<ProductRelationCodesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.productRelationCodes");
    expect(columns()).toEqual(["code", "relationName", "productName", "transportUnitName", "isArchived (hidden)"]);
    await waitFor(() => expect(listCalls("/api/master-data/product-relation-code")).toHaveLength(1));
    expect(lastListQuery("/api/master-data/product-relation-code")).toEqual(expect.objectContaining({ includeArchived: false }));

    fireEvent.click(screen.getByText("MasterData:Archive"));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/product-relation-code/{id}/archive", { params: { path: { id: "p1" } } }));
  });

  it("needs MasterData.ProductRelationCodes", () => {
    renderView(<ProductRelationCodesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});
