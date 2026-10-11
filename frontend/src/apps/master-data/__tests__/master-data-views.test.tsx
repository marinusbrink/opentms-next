import { render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The list pages of the Master data app (design 41, UI design): the four states through the
 * shared view, the feature flag and the view permission, and the command bar per permission.
 * Risk class: Medium (UI flows). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants }, features: { values: features } } }),
}));

vi.mock("@/domains/master-data/entities", () => {
  const fetcher = vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 });
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: false });
  const hooks = (entity: string) => ({
    entity,
    useGridFetcher: () => fetcher,
    useDelete: mutation,
    useBulkDelete: mutation,
    useLookup: () => ({ data: [], isLoading: false, error: null }),
    useDetail: () => ({ data: undefined, isLoading: false, error: null }),
    useCreate: mutation,
    useUpdate: mutation,
  });
  return {
    currencies: hooks("currencies"),
    countries: hooks("countries"),
    addressTypes: hooks("addressTypes"),
    relationCategories: hooks("relationCategories"),
    transportCharacteristics: hooks("transportCharacteristics"),
    temperatures: hooks("temperatures"),
    products: hooks("products"),
    transportUnits: hooks("transportUnits"),
    vehicleTypes: hooks("vehicleTypes"),
    geographicalRegions: hooks("geographicalRegions"),
    addressTypeKindKey: () => "MasterData:AddressTypeKind.Other",
    vehicleTypeKindKey: () => "",
    useSetDefaultCurrency: mutation,
  };
});

vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: { gridId: string; filters?: React.ReactNode }) => (
    <div data-testid="mock-grid" data-grid-id={props.gridId}>
      {props.filters}
    </div>
  ),
}));

vi.mock("@/components/ui/reference-combobox", () => ({
  ReferenceCombobox: () => <div data-testid="country-filter" />,
}));

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

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

beforeEach(() => {
  grants = {};
  features = { "MasterData.FirstRun": "true" };
});

import { CurrenciesView } from "@/apps/master-data/views/CurrenciesView";
import { ProductsView } from "@/apps/master-data/views/ProductsView";
import { GeographicalRegionsView } from "@/apps/master-data/views/GeographicalRegionsView";
import { AddressTypesView } from "@/apps/master-data/views/AddressTypesView";
import { CountriesView } from "@/apps/master-data/views/CountriesView";
import { RelationCategoriesView } from "@/apps/master-data/views/RelationCategoriesView";
import { TransportCharacteristicsView } from "@/apps/master-data/views/TransportCharacteristicsView";
import { TemperaturesView } from "@/apps/master-data/views/TemperaturesView";
import { TransportUnitsView } from "@/apps/master-data/views/TransportUnitsView";
import { VehicleTypesView } from "@/apps/master-data/views/VehicleTypesView";

describe("Master data list views", () => {
  it("shows the permission-denied panel while the per-tenant feature is off (constitution rule 4)", () => {
    features = {};
    grants = { "MasterData.Currencies": true };
    render(<CurrenciesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
  });

  it("shows the permission-denied panel without the view permission (UI-PAGE-005)", () => {
    render(<CurrenciesView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });

  it("renders the grid with its stable id and the active-only filter for currencies", async () => {
    grants = { "MasterData.Currencies": true };
    render(<CurrenciesView />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.getAttribute("data-grid-id")).toBe("masterData.currencies");
    expect(screen.getByText("MasterData:ActiveOnly")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:NewCurrency")).toBeNull();
  });

  it("offers New only with the Create permission and Delete only with the Delete permission (UI-PERM-002)", async () => {
    grants = { "MasterData.Products": true, "MasterData.Products.Create": true };
    const { unmount } = render(<ProductsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.getByText("MasterData:NewProduct")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:DeleteSelected")).toBeNull();
    unmount();

    grants = { "MasterData.Products": true, "MasterData.Products.Delete": true };
    render(<ProductsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:NewProduct")).toBeNull();
    expect(screen.getByText("MasterData:DeleteSelected")).toBeInTheDocument();
  });

  it("renders the archived filter for the three archivable lists and the country filter for regions", async () => {
    grants = { "MasterData.Products": true, "MasterData.GeographicalRegions": true };
    const { unmount } = render(<ProductsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.getByText("MasterData:ShowArchived")).toBeInTheDocument();
    unmount();

    render(<GeographicalRegionsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.getByTestId("country-filter")).toBeInTheDocument();
  });

  it.each([
    ["MasterData.AddressTypes", "masterData.addressTypes", AddressTypesView],
    ["MasterData.Countries", "masterData.countries", CountriesView],
    ["MasterData.RelationCategories", "masterData.relationCategories", RelationCategoriesView],
    ["MasterData.TransportCharacteristics", "masterData.transportCharacteristics", TransportCharacteristicsView],
    ["MasterData.Temperatures", "masterData.temperatures", TemperaturesView],
    ["MasterData.TransportUnits", "masterData.transportUnits", TransportUnitsView],
    ["MasterData.VehicleTypes", "masterData.vehicleTypes", VehicleTypesView],
  ])("%s renders its grid %s with the view permission", async (permission, gridId, View) => {
    grants = { [permission]: true };
    render(<View />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.getAttribute("data-grid-id")).toBe(gridId);
  });
});
