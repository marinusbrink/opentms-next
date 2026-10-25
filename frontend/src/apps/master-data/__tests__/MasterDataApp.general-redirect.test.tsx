import { render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The domain group path /master-data/general (PO decision 2026-10-16, docs/designs/tenants-and-editions.md D15)
 * redirects to the first list under General that is on for this tenant; the list routes did not move. Risk class:
 * Critical (Platform change; feature gating of every screen). */

const navigateMock = vi.fn();
let pathname = "/master-data/general";
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
  "VehiclesView", "VehicleCombinationsView", "ProductRelationCodesView",
]) {
  vi.doMock(`@/apps/master-data/views/${view}`, () => ({ [view]: () => <div data-testid={view} /> }));
}

const { MasterDataApp } = await import("@/apps/master-data/index");
const { APPS } = await import("@/app/apps.config");

const masterData = APPS.find((a) => a.id === "master-data")!;

function allOn(): Record<string, string> {
  const result: Record<string, string> = { "Apps.MasterData": "true" };
  const walk = (views: typeof masterData.views) =>
    views?.forEach((view) => {
      if (view.feature) result[view.feature] = "true";
      walk(view.children);
    });
  walk(masterData.views);
  return result;
}

beforeEach(() => {
  navigateMock.mockReset();
  values = allOn();
});

const go = (path: string) => {
  pathname = path;
  render(<MasterDataApp />);
};

describe("MasterDataApp – the General domain group path", () => {
  it("/master-data/general goes to the first list that is on", () => {
    go("/master-data/general");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/relations", replace: true });
  });

  it("/master-data/general/ (trailing slash) redirects too", () => {
    go("/master-data/general/");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/relations", replace: true });
  });

  it("skips groups and lists that are off", () => {
    values = {
      ...allOn(),
      "Apps.MasterData.General.Relations": "false",
      "Apps.MasterData.General.Transport.Products": "false",
    };
    go("/master-data/general");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/transport-units", replace: true });
  });

  it("reaches a list in the last group when it is the only one on", () => {
    values = {
      "Apps.MasterData": "true",
      "Apps.MasterData.General": "true",
      "Apps.MasterData.General.Reference": "true",
      "Apps.MasterData.General.Reference.GeographicalRegions": "true",
    };
    go("/master-data/general");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/geographical-regions", replace: true });
  });

  it("does not navigate when General is off (the shell shows FeatureUnavailable)", () => {
    values = { ...allOn(), "Apps.MasterData.General": "false" };
    go("/master-data/general");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the bare app path does not navigate when every domain group is off", () => {
    values = { ...allOn(), "Apps.MasterData.General": "false", "Apps.MasterData.Orders": "false", "Apps.MasterData.PlanningExecution": "false", "Apps.MasterData.Financial": "false" };
    go("/master-data");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("a group path does not navigate when General is off even though the group and its lists are on", () => {
    values = { ...allOn(), "Apps.MasterData.General": "false" };
    go("/master-data/fleet");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the list routes did not move under /general", () => {
    const paths: string[] = [];
    const walk = (views: typeof masterData.views) =>
      views?.forEach((view) => {
        if (!view.children) paths.push(view.path);
        walk(view.children);
      });
    walk(masterData.views);
    /* Fifteen General lists (designs 41–48) and nine of Orders, Planning & Execution and Financial (designs 51–53). */
    expect(paths).toHaveLength(24);
    expect(paths.every((p) => /^\/master-data\/[a-z-]+$/.test(p) && !p.startsWith("/master-data/general"))).toBe(true);
  });
});
