import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The five smaller Master data editors of the Transport run (design 61): the CMR fixed text (name and text required),
 * the order attribute (target, key, one name in the user's language, position), the trip attribute (the server's key
 * shape), the via-depot rule (the picker the scope needs, the depot's addresses from Master Data, the DTO) and the CMR
 * profile (the party boxes as enum numbers, the checkbox groups, the fixed texts, the report definition of purpose
 * ConsignmentNote, Set as default from the ribbon, no Delete on the default). The domain hooks are real; the generated
 * client is the mock; the pickers and grids are native stand-ins. Risk class: Medium; the server tests prove the rules. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: {} }, localization: { languages: [] } } }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => ({ ReferenceCombobox: (await import("./run2-test-mocks")).MockReferenceCombobox }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("./run2-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/entity-lookup", async () => ({ EntityLookup: (await import("./transport-test-mocks")).MockEntityLookup }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { ConsignmentNoteFixedDescriptionEditor } from "@/apps/master-data/editors/ConsignmentNoteFixedDescriptionEditor";
import { ConsignmentNoteProfileEditor } from "@/apps/master-data/editors/ConsignmentNoteProfileEditor";
import { OrderAttributeDefinitionEditor } from "@/apps/master-data/editors/OrderAttributeDefinitionEditor";
import { TripAttributeDefinitionEditor } from "@/apps/master-data/editors/TripAttributeDefinitionEditor";
import { ViaDepotRuleEditor } from "@/apps/master-data/editors/ViaDepotRuleEditor";
import { editorDialog, fieldIn, ok, panel, queryFieldIn, ribbonButton, submitEditor, tab, typeIn } from "./platform-test-helpers";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

const stubBrowser = () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
};

beforeAll(stubBrowser);
afterEach(() => {
  vi.unstubAllGlobals();
  stubBrowser();
});

const GENERAL = "General";
const WAYPOINT_TYPES = [
  { id: "w-load", code: "LOAD", name: "Load" },
  { id: "w-unload", code: "UNLOAD", name: "Unload" },
];
const REGIONS = [{ id: "r-1", name: "Randstad" }];
const FIXED_TEXTS = [
  { id: "ft-1", name: "Fragile" },
  { id: "ft-2", name: "Keep cool" },
];
const ADDRESSES = [{ id: "a-1", street: "Kade", houseNumber: "7", postalCode: "5000 AA", city: "Tilburg", countryId: "c-nl", addressTypeName: "Warehouse" }];

let profile: Record<string, unknown>;

beforeEach(() => {
  grants = {};
  profile = {
    id: "p1", name: "Standard", copies: 3, showBorders: true, showQrCode: true, groupByLeg: false,
    box1Sender: 1, box2Consignee: 3, box3DeliveryPlace: 3, box4TakingOverPlace: 2, box16Carrier: 5, box17SuccessiveCarrier: 9, box21EstablishedBy: 4,
    box6Columns: [4, 5], box13Items: [2], box18Items: [1, 2], fixedDescriptionIds: ["ft-1"], fixedDescriptionNames: ["Fragile"],
    reportDefinitionId: "rd-1", reportDefinitionName: "CMR nl", isDefault: false, isArchived: false, description: null, concurrencyStamp: "stamp-p1", creationTime: "2026-10-25T10:00:00Z",
  };
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/orders/waypoint-type/lookup":
        return ok(WAYPOINT_TYPES);
      case "/api/master-data/geographical-region/lookup":
        return ok(REGIONS);
      case "/api/orders/consignment-note-fixed-description/lookup":
        return ok(FIXED_TEXTS);
      case "/api/platform/report-definition":
        return ok({ rows: [{ id: "rd-1", name: "CMR nl" }, { id: "rd-2", name: "CMR en" }], totalCount: 2, filteredCount: 2 });
      case "/api/master-data/relation/{id}/addresses":
        return ok(ADDRESSES);
      case "/api/orders/consignment-note-profile/{id}":
        return ok(profile);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async () => ok({ id: "new" }));
  PUT.mockReset().mockImplementation(async () => ok(profile));
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const bodyOf = (mock: Mock<ClientCall>) => mock.mock.calls[0][1]!.body as Record<string, unknown>;
const noop = () => undefined;
/* Ribbon buttons carry their state as aria-disabled (the tooltip explains it, UI-PERM-005). */
const isRibbonDisabled = (label: string) => ribbonButton(label)?.getAttribute("aria-disabled") === "true";

describe("ConsignmentNoteFixedDescriptionEditor", () => {
  it("needs a name and a text, then posts them trimmed", async () => {
    renderWith(<ConsignmentNoteFixedDescriptionEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "MasterData:Name", "Fragile ");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("MasterData:Required").length).toBeGreaterThan(0);

    typeIn(GENERAL, "Orders:Text", " Handle with care ");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/consignment-note-fixed-description", expect.anything()));
    expect(bodyOf(POST)).toEqual({ name: "Fragile", text: "Handle with care", isArchived: false, description: null });
  });
});

describe("OrderAttributeDefinitionEditor", () => {
  it("posts the target, the key, one name in the user's language and the position", async () => {
    renderWith(<OrderAttributeDefinitionEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    fireEvent.change(fieldIn(GENERAL, "Orders:AttributeTarget"), { target: { value: "2" } });
    typeIn(GENERAL, "Orders:AttributeKey", "seal_number");
    typeIn(GENERAL, "MasterData:Name", "Seal number");
    typeIn(GENERAL, "Orders:Position", "5");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/order-attribute-definition", expect.anything()));
    expect(bodyOf(POST)).toEqual({ target: 2, key: "seal_number", names: { en: "Seal number" }, position: 5, isArchived: false, description: null });
  });

  it("refuses a position outside 1–1000", async () => {
    renderWith(<OrderAttributeDefinitionEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "Orders:AttributeKey", "k");
    typeIn(GENERAL, "MasterData:Name", "Key");
    typeIn(GENERAL, "Orders:Position", "0");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
  });
});

describe("TripAttributeDefinitionEditor", () => {
  it("refuses a key outside the server's shape and posts a valid one", async () => {
    renderWith(<TripAttributeDefinitionEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "PlanningExecution:Key", "1-bad key");
    typeIn(GENERAL, "MasterData:Name", "Seal");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("PlanningExecution:AttributeKeyShapeHint").length).toBeGreaterThan(0);

    typeIn(GENERAL, "PlanningExecution:Key", "seal_1");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/planning/trip-attribute-definition", expect.anything()));
    expect(bodyOf(POST)).toEqual({ key: "seal_1", names: { en: "Seal" }, position: 1, isArchived: false, description: null });
  });
});

describe("ViaDepotRuleEditor", () => {
  it("asks for a relation or a region by scope, lists the depot's addresses and posts the rule", async () => {
    renderWith(<ViaDepotRuleEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    /* The arrival and the departure picker both list the waypoint types. */
    await waitFor(() => expect(within(editorDialog()).getAllByRole("option", { name: "Unload" })).toHaveLength(2));
    expect(queryFieldIn(GENERAL, "PlanningExecution:Relation")).not.toBeNull();
    expect(queryFieldIn(GENERAL, "PlanningExecution:GeographicalRegion")).toBeNull();

    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:Scope"), { target: { value: "3" } });
    expect(queryFieldIn(GENERAL, "PlanningExecution:Relation")).toBeNull();
    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:GeographicalRegion"), { target: { value: "r-1" } });

    /* No depot yet: the address picker waits for it. */
    expect(fieldIn(GENERAL, "PlanningExecution:DepotAddress")).toBeDisabled();
    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:DepotRelation"), { target: { value: "rel-2" } });
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/relation/{id}/addresses", { params: { path: { id: "rel-2" } } }));
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "Kade 7, 5000 AA Tilburg, Warehouse" })).toBeInTheDocument());
    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:DepotAddress"), { target: { value: "a-1" } });
    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:ArrivalWaypointType"), { target: { value: "w-unload" } });
    fireEvent.click(within(editorDialog()).getByRole("checkbox", { name: "PlanningExecution:Overnight" }));
    typeIn(GENERAL, "PlanningExecution:Position", "2");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/planning/via-depot-rule", expect.anything()));
    expect(bodyOf(POST)).toEqual({
      scope: 3,
      relationId: null,
      geographicalRegionId: "r-1",
      depotRelationId: "rel-2",
      depotAddressId: "a-1",
      arrivalWaypointTypeId: "w-unload",
      departureWaypointTypeId: null,
      overnight: true,
      position: 2,
      isArchived: false,
      description: null,
    });
  });

  it("blocks saving without the depot, its address and the arrival activity", async () => {
    renderWith(<ViaDepotRuleEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    fireEvent.change(fieldIn(GENERAL, "PlanningExecution:Relation"), { target: { value: "rel-1" } });
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("MasterData:Required")).toHaveLength(3);
  });
});

describe("ConsignmentNoteProfileEditor", () => {
  const BOXES = "Orders:Boxes";

  it("creates with the boxes as enum numbers, the checkbox groups, the fixed texts and the report definition", async () => {
    renderWith(<ConsignmentNoteProfileEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "MasterData:Name", "Container");
    typeIn(GENERAL, "Orders:Copies", "4");
    await waitFor(() => expect(within(editorDialog()).getByRole("option", { name: "CMR en" })).toBeInTheDocument());
    fireEvent.change(fieldIn(GENERAL, "Orders:ReportDefinition"), { target: { value: "rd-2" } });
    expect(GET).toHaveBeenCalledWith("/api/platform/report-definition", expect.objectContaining({ params: { query: expect.objectContaining({ Purpose: 6, IncludeArchived: false }) } }));

    tab(BOXES);
    fireEvent.change(fieldIn(BOXES, "Orders:Box1Sender"), { target: { value: "6" } });
    fireEvent.change(fieldIn(BOXES, "Orders:Box17SuccessiveCarrier"), { target: { value: "5" } });
    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrGoodsColumn.Quantity" }));
    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrGoodsColumn.WeightKg" }));
    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrInstructionItem.FixedDescriptions" }));
    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrRemarkItem.TripNumbers" }));
    await waitFor(() => expect(within(panel(BOXES)).getByRole("checkbox", { name: "Keep cool" })).toBeInTheDocument());
    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Keep cool" }));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/consignment-note-profile", expect.anything()));
    expect(bodyOf(POST)).toEqual({
      name: "Container",
      description: null,
      copies: 4,
      showBorders: true,
      showQrCode: true,
      groupByLeg: false,
      box1Sender: 6,
      box2Consignee: 3,
      box3DeliveryPlace: 3,
      box4TakingOverPlace: 2,
      box16Carrier: 5,
      box17SuccessiveCarrier: 5,
      box21EstablishedBy: 4,
      box6Columns: [4, 9],
      box13Items: [2],
      box18Items: [4],
      fixedDescriptionIds: ["ft-2"],
      reportDefinitionId: "rd-2",
      isArchived: false,
    });
  });

  it("refuses more than five copies", async () => {
    renderWith(<ConsignmentNoteProfileEditor mode="create" readOnly={false} open onClose={noop} onSaved={noop} />);
    typeIn(GENERAL, "MasterData:Name", "Many");
    typeIn(GENERAL, "Orders:Copies", "6");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
  });

  it("loads a profile, keeps its boxes and updates with the stamp; Set as default waits for a save", async () => {
    grants = { "Orders.ConsignmentNoteProfiles.Delete": true };
    renderWith(<ConsignmentNoteProfileEditor mode="edit" id="p1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Name").value).toBe("Standard"));
    tab(BOXES);
    expect(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrGoodsColumn.Quantity" })).toBeChecked();
    expect(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrGoodsColumn.Barcode" })).not.toBeChecked();
    expect((fieldIn(BOXES, "Orders:Box21EstablishedBy") as unknown as HTMLSelectElement).value).toBe("4");

    expect(ribbonButton("MasterData:SetAsDefault")).not.toBeNull();
    expect(isRibbonDisabled("MasterData:SetAsDefault")).toBe(false);
    expect(ribbonButton("Delete")).not.toBeNull();

    fireEvent.click(within(panel(BOXES)).getByRole("checkbox", { name: "Orders:CmrGoodsColumn.Barcode" }));
    expect(isRibbonDisabled("MasterData:SetAsDefault")).toBe(true);

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalled());
    expect(bodyOf(PUT)).toMatchObject({ name: "Standard", box6Columns: [4, 5, 12], fixedDescriptionIds: ["ft-1"], concurrencyStamp: "stamp-p1" });
  });

  it("sets the saved profile as the default from the ribbon and offers no Delete on the default", async () => {
    grants = { "Orders.ConsignmentNoteProfiles.Delete": true };
    const first = renderWith(<ConsignmentNoteProfileEditor mode="edit" id="p1" readOnly={false} open onClose={noop} onSaved={noop} />);
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Name").value).toBe("Standard"));
    fireEvent.click(ribbonButton("MasterData:SetAsDefault")!);
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/orders/consignment-note-profile/{id}/set-default", { params: { path: { id: "p1" } } }));
    first.unmount();

    profile = { ...profile, isDefault: true };
    renderWith(<ConsignmentNoteProfileEditor mode="edit" id="p1" readOnly={false} open onClose={noop} onSaved={noop} />);
    /* Loaded: the badge after the title (the ribbon's group caption carries the same text before the load). */
    await waitFor(() => expect(fieldIn(GENERAL, "MasterData:Name").value).toBe("Standard"));
    expect(screen.getAllByText("MasterData:IsDefault").length).toBeGreaterThan(1);
    expect(ribbonButton("Delete")).toBeNull();
    expect(isRibbonDisabled("MasterData:SetAsDefault")).toBe(true);
  });
});
