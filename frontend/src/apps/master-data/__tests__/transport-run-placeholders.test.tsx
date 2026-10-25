import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The seven Master data lists of the Transport run (design 61) until their screens arrive with the backend modules:
 * each shows PermissionDenied without its view permission (UI-PAGE-005) and the page skeleton with it; none reads its
 * feature (the shell gates the node, AppShell.features.test.tsx). Risk class: Medium (permission gating). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: {} } } }),
}));

import { ConsignmentNoteFixedDescriptionsView } from "@/apps/master-data/views/ConsignmentNoteFixedDescriptionsView";
import { ConsignmentNoteProfilesView } from "@/apps/master-data/views/ConsignmentNoteProfilesView";
import { OrderAttributeDefinitionsView } from "@/apps/master-data/views/OrderAttributeDefinitionsView";
import { PriceListsView } from "@/apps/master-data/views/PriceListsView";
import { RelationFinancialSettingsView } from "@/apps/master-data/views/RelationFinancialSettingsView";
import { TripAttributeDefinitionsView } from "@/apps/master-data/views/TripAttributeDefinitionsView";
import { ViaDepotRulesView } from "@/apps/master-data/views/ViaDepotRulesView";

beforeEach(() => {
  grants = {};
});

describe.each([
  ["CMR profiles", ConsignmentNoteProfilesView, "Orders.ConsignmentNoteProfiles"],
  ["CMR fixed texts", ConsignmentNoteFixedDescriptionsView, "Orders.ConsignmentNoteFixedDescriptions"],
  ["Order attributes", OrderAttributeDefinitionsView, "Orders.OrderAttributeDefinitions"],
  ["Via depot rules", ViaDepotRulesView, "PlanningExecution.ViaDepotRules"],
  ["Trip attributes", TripAttributeDefinitionsView, "PlanningExecution.TripAttributeDefinitions"],
  ["Price lists", PriceListsView, "Financial.PriceLists"],
  ["Relation financial settings", RelationFinancialSettingsView, "Financial.RelationFinancialSettings"],
])("%s list", (_name, View, permission) => {
  it(`shows PermissionDenied without ${permission}`, () => {
    render(<View />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
  });

  it("shows the page skeleton with it, without a title (UI-PAGE-002), and no feature check of its own", () => {
    grants = { [permission]: true };
    render(<View />);
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("heading")).toBeNull();
    expect(document.querySelector('[data-slot="view-placeholder"]')).not.toBeNull();
    expect(screen.getByText("Shell:EmptyAppTitle")).toBeInTheDocument();
  });
});
