import { render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The domain group path /master-data/general (PO decision 2026-10-16, docs/designs/tenants-and-editions.md D15)
 * redirects to the first list under General that is on for this tenant; the list routes did not move. Risk class:
 * Critical (Platform change; feature gating of every screen). */

const navigateMock = vi.fn();
let pathname = "/master-data/general";
let search: Record<string, unknown> = {};
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname, search }),
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
  /* Design 49: the Platform domain's lists. */
  "EmailTemplatesView", "ReportDefinitionsView", "DocumentTypesView",
  /* Design 61: the Transport run's lists. */
  "ConsignmentNoteProfilesView", "ConsignmentNoteFixedDescriptionsView", "OrderAttributeDefinitionsView",
  "ViaDepotRulesView", "TripAttributeDefinitionsView", "PriceListsView", "RelationFinancialSettingsView",
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
  search = {};
});

const go = (path: string) => {
  pathname = path;
  render(<MasterDataApp />);
};

describe("MasterDataApp – the domain group paths (General; Orders, Planning & Execution, Financial; Platform, design 54)", () => {
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
    values = { ...allOn(), "Apps.MasterData.General": "false", "Apps.MasterData.Orders": "false", "Apps.MasterData.PlanningExecution": "false", "Apps.MasterData.Financial": "false", "Apps.MasterData.Platform": "false" };
    go("/master-data");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the bare app path still goes to General's first list when Platform is off", () => {
    values = { ...allOn(), "Apps.MasterData.Platform": "false" };
    go("/master-data");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/relations", replace: true });
  });

  it("a group path does not navigate when General is off even though the group and its lists are on", () => {
    values = { ...allOn(), "Apps.MasterData.General": "false" };
    go("/master-data/fleet");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the old order-intake addresses go to the same list in the Integrations app, the search kept (PO 2026-11-03)", () => {
    search = { connected: "mb-1" };
    go("/master-data/integrations/mailboxes");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/mailboxes", search: { connected: "mb-1" }, replace: true });
    for (const [from, to] of [
      ["/master-data/integrations/api-clients", "/integrations/api-clients"],
      ["/master-data/integrations/import-profiles", "/integrations/import-profiles"],
      ["/master-data/integrations", "/integrations/intake"],
      ["/master-data/integrations/intake", "/integrations/intake"],
    ]) {
      navigateMock.mockReset();
      search = {};
      go(from);
      expect(navigateMock, from).toHaveBeenCalledWith({ to, search: {}, replace: true });
    }
  });

  it("/master-data/platform goes to the first Platform list that is on", () => {
    go("/master-data/platform");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/email-templates", replace: true });
  });

  it("/master-data/platform skips Platform lists that are off", () => {
    values = { ...allOn(), "Apps.MasterData.Platform.EmailTemplates": "false", "Apps.MasterData.Platform.ReportDefinitions": "false" };
    go("/master-data/platform/");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/master-data/document-types", replace: true });
  });

  it("/master-data/platform does not navigate when Platform is off, even with General on", () => {
    values = { ...allOn(), "Apps.MasterData.Platform": "false" };
    go("/master-data/platform");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it.each([
    ["/master-data/email-templates", "EmailTemplatesView"],
    ["/master-data/report-definitions", "ReportDefinitionsView"],
    ["/master-data/document-types", "DocumentTypesView"],
    /* Design 61: the Transport run's lists under the existing Orders, Planning & Execution and Financial groups. */
    ["/master-data/consignment-note-profiles", "ConsignmentNoteProfilesView"],
    ["/master-data/consignment-note-fixed-descriptions", "ConsignmentNoteFixedDescriptionsView"],
    ["/master-data/order-attribute-definitions", "OrderAttributeDefinitionsView"],
    ["/master-data/via-depot-rules", "ViaDepotRulesView"],
    ["/master-data/trip-attribute-definitions", "TripAttributeDefinitionsView"],
    ["/master-data/price-lists", "PriceListsView"],
    ["/master-data/relation-financial-settings", "RelationFinancialSettingsView"],
  ])("%s renders %s without navigating", (path, view) => {
    go(path);
    expect(document.querySelector(`[data-testid=${view}]`)).not.toBeNull();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("the list routes did not move under /general or /platform", () => {
    const paths: string[] = [];
    const walk = (views: typeof masterData.views) =>
      views?.forEach((view) => {
        if (!view.children) paths.push(view.path);
        walk(view.children);
      });
    walk(masterData.views);
    /* Fifteen General lists (designs 41–48), sixteen of Orders, Planning & Execution and Financial (designs 51–53, 61)
     * and three of Platform (design 54). Design 72's order-intake lists are in the Integrations app (PO 2026-11-03). */
    expect(paths).toHaveLength(34);
    expect(paths.filter((p) => p.startsWith("/master-data/integrations"))).toEqual([]);
    const others = paths;
    expect(others.every((p) => /^\/master-data\/[a-z-]+$/.test(p) && !p.startsWith("/master-data/general") && !p.startsWith("/master-data/platform"))).toBe(true);
  });
});

describe("MasterDataApp – the moved order-intake addresses (design 72 build note 25, PO 2026-11-03)", () => {
  it("a mailbox OAuth return to the old address lands on the Integrations Mailboxes list with its refusal or connection kept", () => {
    search = { connectError: "access_denied" };
    go("/master-data/integrations/mailboxes");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/mailboxes", search: { connectError: "access_denied" }, replace: true });
    expect(document.querySelector("[data-testid]")).toBeNull();
  });

  it("deeper and trailing-slash old addresses go to their list; an unknown old list goes to the Order intake group", () => {
    for (const [from, to] of [
      ["/master-data/integrations/api-clients/", "/integrations/api-clients"],
      ["/master-data/integrations/import-profiles/p-1", "/integrations/import-profiles"],
      ["/master-data/integrations/mailboxes/anything/else", "/integrations/mailboxes"],
      ["/master-data/integrations/unknown-list", "/integrations/intake"],
    ]) {
      navigateMock.mockReset();
      go(from);
      expect(navigateMock, from).toHaveBeenCalledWith({ to, search: {}, replace: true });
    }
  });

  it("a Master data path that only starts with the same letters is not moved", () => {
    go("/master-data/integrations-archive");
    expect(navigateMock).not.toHaveBeenCalledWith(expect.objectContaining({ to: expect.stringMatching(/^\/integrations/) }));
  });

  it("the old addresses redirect even with every Master data list off: the move does not depend on the old nodes", () => {
    values = { "Apps.MasterData": "true" };
    go("/master-data/integrations/mailboxes");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/mailboxes", search: {}, replace: true });
  });
});
