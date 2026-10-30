import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The seven Master data lists of the Transport run (design 61): the view permission (UI-PAGE-005), the grid id, the
 * designed columns (hidden ones after the visible; the columns the server cannot sort are not sortable), the list
 * filters as request parameters (side, target, archived), Set as default on a non-default CMR profile, the default
 * profile's blocked Delete (UI-PERM-005), and the relation financial settings list that needs Update for New (the
 * settings are created on the first edit) and opens its editor by relation. The domain hooks are real; the generated
 * client is the mock. Risk class: Medium. */

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
vi.mock("@/domains/financial/components/PriceListEditor", () => ({ PriceListEditor: () => <div data-testid="price-list-editor" /> }));
vi.mock("@/apps/master-data/editors/RelationFinancialSettingsEditor", () => ({
  RelationFinancialSettingsEditor: (props: { mode: string; relationId?: string; readOnly: boolean }) => <div data-testid="settings-editor" data-mode={props.mode} data-relation-id={props.relationId ?? ""} data-readonly={String(props.readOnly)} />,
}));
vi.mock("@/domains/orders/components/ConsignmentNoteProfileEditor", () => ({ ConsignmentNoteProfileEditor: () => <div data-testid="profile-editor" /> }));
vi.mock("@/domains/orders/components/ConsignmentNoteFixedDescriptionEditor", () => ({ ConsignmentNoteFixedDescriptionEditor: () => <div data-testid="fixed-text-editor" /> }));
vi.mock("@/apps/master-data/editors/OrderAttributeDefinitionEditor", () => ({ OrderAttributeDefinitionEditor: () => <div data-testid="order-attribute-editor" /> }));
vi.mock("@/apps/master-data/editors/TripAttributeDefinitionEditor", () => ({ TripAttributeDefinitionEditor: () => <div data-testid="trip-attribute-editor" /> }));
vi.mock("@/apps/master-data/editors/ViaDepotRuleEditor", () => ({ ViaDepotRuleEditor: () => <div data-testid="via-depot-editor" /> }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { ConsignmentNoteFixedDescriptionsView } from "@/apps/master-data/views/ConsignmentNoteFixedDescriptionsView";
import { ConsignmentNoteProfilesView } from "@/apps/master-data/views/ConsignmentNoteProfilesView";
import { OrderAttributeDefinitionsView } from "@/apps/master-data/views/OrderAttributeDefinitionsView";
import { PriceListsView } from "@/apps/master-data/views/PriceListsView";
import { RelationFinancialSettingsView } from "@/apps/master-data/views/RelationFinancialSettingsView";
import { TripAttributeDefinitionsView } from "@/apps/master-data/views/TripAttributeDefinitionsView";
import { ViaDepotRulesView } from "@/apps/master-data/views/ViaDepotRulesView";
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
const unsortable = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span[data-sortable=false]")).map((el) => el.getAttribute("data-column"));

describe.each([
  ["CMR profiles", ConsignmentNoteProfilesView, "Orders.ConsignmentNoteProfiles"],
  ["CMR fixed texts", ConsignmentNoteFixedDescriptionsView, "Orders.ConsignmentNoteFixedDescriptions"],
  ["Order attributes", OrderAttributeDefinitionsView, "Orders.OrderAttributeDefinitions"],
  ["Via depot rules", ViaDepotRulesView, "PlanningExecution.ViaDepotRules"],
  ["Trip attributes", TripAttributeDefinitionsView, "PlanningExecution.TripAttributeDefinitions"],
  ["Price lists", PriceListsView, "Financial.PriceLists"],
  ["Relation financial settings", RelationFinancialSettingsView, "Financial.RelationFinancialSettings"],
])("%s list", (_name, View, permission) => {
  it(`shows PermissionDenied without ${permission} and fetches nothing`, () => {
    renderView(<View />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(GET).not.toHaveBeenCalled();
  });
});

describe("PriceListsView", () => {
  it("renders the designed columns, sends the side and archived filters", async () => {
    grants = { "Financial.PriceLists": true };
    renderView(<PriceListsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.priceLists");
    expect(columns()).toEqual(["code", "name", "side", "currencyCode", "basis", "validFrom", "validTill", "ruleCount", "isArchived (hidden)", "description (hidden)"]);
    expect(unsortable()).toEqual(["ruleCount", "description"]);
    await waitFor(() => expect(lastListQuery("/api/financial/price-list").includeArchived).toBe(false));
    expect(lastListQuery("/api/financial/price-list").side).toBeUndefined();

    fireEvent.change(screen.getByLabelText("Financial:RateSide"), { target: { value: "2" } });
    await waitFor(() => expect(lastListQuery("/api/financial/price-list").side).toBe(2));
    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/financial/price-list").includeArchived).toBe(true));
  });
});

describe("RelationFinancialSettingsView", () => {
  it("renders the designed columns with the looked-up names not sortable and lists with the flat query", async () => {
    grants = { "Financial.RelationFinancialSettings": true };
    renderView(<RelationFinancialSettingsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.relationFinancialSettings");
    expect(columns()).toEqual(["relationName", "revenuePriceListCount", "costPriceListCount", "vatPercentageName", "paymentTermDays", "chargeFixedPriceByDefault", "currencyCode"]);
    expect(unsortable()).toEqual(["revenuePriceListCount", "costPriceListCount", "vatPercentageName", "chargeFixedPriceByDefault"]);
    await waitFor(() => expect(listCalls("/api/financial/relation-financial-settings")).toHaveLength(1));
    expect(lastListQuery("/api/financial/relation-financial-settings")).toMatchObject({ StartRow: 0, EndRow: 50 });
  });

  it("offers New only with Update (the settings are created on the first edit) and never Delete", async () => {
    grants = { "Financial.RelationFinancialSettings": true };
    const { unmount } = renderView(<RelationFinancialSettingsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByRole("button", { name: /Financial:NewRelationFinancialSettings/ })).toBeNull();
    unmount();

    grants = { "Financial.RelationFinancialSettings": true, "Financial.RelationFinancialSettings.Update": true };
    renderView(<RelationFinancialSettingsView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByRole("button", { name: /Financial:NewRelationFinancialSettings/ }));
    expect(screen.getByTestId("settings-editor").getAttribute("data-mode")).toBe("create");
    expect(screen.queryByRole("button", { name: /MasterData:DeleteSelected/ })).toBeNull();
  });

  it("opens a row's editor by its relation, read-only without Update", async () => {
    grants = { "Financial.RelationFinancialSettings": true, "Financial.RelationFinancialSettings.Update": true };
    GET.mockResolvedValue(ok({ rows: [{ id: "s1", relationId: "rel-1", relationName: "Acme" }], totalCount: 1, filteredCount: 1 }));
    gridRows.current = [{ id: "s1", relationId: "rel-1", relationName: "Acme" }];
    renderView(<RelationFinancialSettingsView />);
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls("/api/financial/relation-financial-settings")).toHaveLength(1));
    fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    const editor = screen.getByTestId("settings-editor");
    expect(editor.getAttribute("data-mode")).toBe("edit");
    expect(editor.getAttribute("data-relation-id")).toBe("rel-1");
    expect(editor.getAttribute("data-readonly")).toBe("false");
  });
});

describe("ConsignmentNoteProfilesView", () => {
  it("renders the designed columns, moves the default and blocks deleting the default profile", async () => {
    grants = { "Orders.ConsignmentNoteProfiles": true, "Orders.ConsignmentNoteProfiles.Update": true, "Orders.ConsignmentNoteProfiles.Delete": true };
    gridRows.current = [
      { id: "p1", name: "Standard", isDefault: true, isArchived: false },
      { id: "p2", name: "Container", isDefault: false, isArchived: false },
    ];
    renderView(<ConsignmentNoteProfilesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.consignmentNoteProfiles");
    expect(columns()).toEqual(["name", "copies", "showBorders", "showQrCode", "groupByLeg", "reportDefinitionName", "isDefault", "isArchived (hidden)", "description (hidden)"]);
    expect(unsortable()).toEqual(["showBorders", "showQrCode", "groupByLeg", "reportDefinitionName", "description"]);

    const rows = document.querySelectorAll("[data-slot=mock-grid-row]");
    const defaultRowDelete = Array.from(rows[0].querySelectorAll("button")).find((b) => b.textContent === "Delete")!;
    expect(defaultRowDelete).toBeDisabled();
    expect(defaultRowDelete.getAttribute("title")).toBe("Orders:DefaultProfileCannotBeDeleted");
    expect(Array.from(rows[0].querySelectorAll("button")).some((b) => b.textContent === "MasterData:SetAsDefault")).toBe(false);

    fireEvent.click(Array.from(rows[1].querySelectorAll("button")).find((b) => b.textContent === "MasterData:SetAsDefault")!);
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/consignment-note-profile/{id}/set-default", { params: { path: { id: "p2" } } }));
  });
});

describe("ConsignmentNoteFixedDescriptionsView", () => {
  it("renders the designed columns and sends the archived filter", async () => {
    grants = { "Orders.ConsignmentNoteFixedDescriptions": true };
    renderView(<ConsignmentNoteFixedDescriptionsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.consignmentNoteFixedDescriptions");
    expect(columns()).toEqual(["name", "text", "isArchived (hidden)", "description (hidden)"]);
    await waitFor(() => expect(lastListQuery("/api/orders/consignment-note-fixed-description").includeArchived).toBe(false));
  });
});

describe("OrderAttributeDefinitionsView", () => {
  it("renders the designed columns and sends the target filter", async () => {
    grants = { "Orders.OrderAttributeDefinitions": true };
    renderView(<OrderAttributeDefinitionsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.orderAttributeDefinitions");
    expect(columns()).toEqual(["target", "key", "name", "position", "isArchived (hidden)", "description (hidden)"]);
    await waitFor(() => expect(listCalls("/api/orders/order-attribute-definition")).toHaveLength(1));
    expect(lastListQuery("/api/orders/order-attribute-definition").target).toBeUndefined();

    fireEvent.change(screen.getByLabelText("Orders:AttributeTarget"), { target: { value: "2" } });
    await waitFor(() => expect(lastListQuery("/api/orders/order-attribute-definition").target).toBe(2));
  });
});

describe("TripAttributeDefinitionsView", () => {
  it("renders the designed columns", async () => {
    grants = { "PlanningExecution.TripAttributeDefinitions": true };
    renderView(<TripAttributeDefinitionsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.tripAttributeDefinitions");
    expect(columns()).toEqual(["key", "name", "position", "isArchived (hidden)", "description (hidden)"]);
    await waitFor(() => expect(listCalls("/api/planning/trip-attribute-definition")).toHaveLength(1));
  });
});

describe("ViaDepotRulesView", () => {
  it("renders the designed columns with the Master Data names not sortable", async () => {
    grants = { "PlanningExecution.ViaDepotRules": true };
    GET.mockImplementation(async (path) => (path === "/api/orders/waypoint-type/lookup" ? ok([{ id: "w-unload", code: "UNLOAD", name: "Unload" }]) : ok({ rows: [], totalCount: 0, filteredCount: 0 })));
    renderView(<ViaDepotRulesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("masterData.viaDepotRules");
    /* The stop's activity is named from the waypoint types' lookup. */
    await waitFor(() => expect(listCalls("/api/orders/waypoint-type/lookup")).toHaveLength(1));
    expect(columns()).toEqual([
      "scope", "relationName", "geographicalRegionName", "stopWaypointTypeId", "depotRelationName", "depotAddressCity", "overnight", "position", "isArchived (hidden)", "description (hidden)",
    ]);
    expect(unsortable()).toEqual(["relationName", "geographicalRegionName", "stopWaypointTypeId", "depotRelationName", "depotAddressCity", "description"]);
    await waitFor(() => expect(listCalls("/api/planning/via-depot-rule")).toHaveLength(1));
  });
});
