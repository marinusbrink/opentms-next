import { render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Master data app's index redirects (PO 2026-10-14, "App features"): the bare app path and a group path go to the
 * first list that is on for this tenant. Before the change the groups redirected to fixed lists (/master-data/products,
 * /vehicle-types, /countries); now the target follows the features. Risk class: High (feature gating of every screen). */

const navigateMock = vi.fn();
let pathname = "/master-data";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  useNavigate: () => navigateMock,
}));

let values: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, features: { values }, auth: { grantedPolicies: {} } } }),
}));

for (const view of [
  "AddressTypesView", "CountriesView", "CurrenciesView", "DriversView", "GeographicalRegionsView", "ProductsView",
  "RelationCategoriesView", "RelationsView", "TemperaturesView", "TransportCharacteristicsView", "TransportUnitsView", "VehicleTypesView",
]) {
  vi.doMock(`@/apps/master-data/views/${view}`, () => ({ [view]: () => <div data-testid={view} /> }));
}

const { MasterDataApp } = await import("@/apps/master-data/index");

const ALL_ON = Object.fromEntries(
  [
    "Apps.MasterData", "Apps.MasterData.General",
    "Apps.MasterData.General.Relations", "Apps.MasterData.General.Relations.Relations", "Apps.MasterData.General.Relations.AddressTypes", "Apps.MasterData.General.Relations.RelationCategories",
    "Apps.MasterData.General.Transport", "Apps.MasterData.General.Transport.Products", "Apps.MasterData.General.Transport.TransportUnits", "Apps.MasterData.General.Transport.Temperatures",
    "Apps.MasterData.General.Fleet", "Apps.MasterData.General.Fleet.Drivers", "Apps.MasterData.General.Fleet.VehicleTypes", "Apps.MasterData.General.Fleet.TransportCharacteristics",
    "Apps.MasterData.General.Reference", "Apps.MasterData.General.Reference.Countries", "Apps.MasterData.General.Reference.Currencies", "Apps.MasterData.General.Reference.GeographicalRegions",
  ].map((f) => [f, "true"]),
);

beforeEach(() => {
  navigateMock.mockReset();
  values = { ...ALL_ON };
});

const go = (path: string) => {
  pathname = path;
  render(<MasterDataApp />);
};

describe("MasterDataApp – redirects follow the features", () => {
  it("the bare app path goes to the first list that is on", () => {
    go("/master-data");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/relations", replace: true });
  });

  it("the bare app path skips lists that are off", () => {
    values = { ...ALL_ON, "Apps.MasterData.General.Relations": "false" };
    go("/master-data/");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/products", replace: true });
  });

  it.each([
    ["/master-data/transport", "/master-data/products"],
    ["/master-data/fleet", "/master-data/drivers"],
    ["/master-data/reference", "/master-data/countries"],
  ])("the group path %s goes to its first list that is on (%s)", (group, first) => {
    go(group);
    expect(navigateMock).toHaveBeenCalledWith({ to: first, replace: true });
  });

  it("a group path skips its lists that are off", () => {
    values = { ...ALL_ON, "Apps.MasterData.General.Fleet.Drivers": "false" };
    go("/master-data/fleet");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/vehicle-types", replace: true });
  });

  it("does not navigate from a list path", () => {
    go("/master-data/countries");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("does not navigate when nothing is on (the shell shows FeatureUnavailable)", () => {
    values = { "Apps.MasterData": "true" };
    go("/master-data");
    expect(navigateMock).not.toHaveBeenCalled();
  });
});
