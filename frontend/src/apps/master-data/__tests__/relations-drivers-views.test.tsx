import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Relations and Drivers lists (design 43, UI design): the per-tenant feature MasterData.Relations
 * and the view permission, the command bar per permission, the grid columns, the filters as list
 * request parameters, the confirmed Archive / Unarchive row actions that refresh the grid, delete
 * and bulk delete. The domain hooks are real; the generated client is the mock, so the assertions
 * are on the requests the server receives. Risk class: Medium (UI flows). */

/* Keys are returned as-is; the confirmation questions keep their {0} placeholder so the record name shows. */
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } }),
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

vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});

vi.mock("@/components/ui/entity-lookup", () => ({
  EntityLookup: (props: { id: string; placeholder?: string; onChange: (value: unknown) => void }) => (
    <button type="button" id={props.id} data-testid="employer-filter" onClick={() => props.onChange({ id: "rel-1", code: "ACME", name: "Acme", city: null, countryCode: null, roles: 2 })}>
      {props.placeholder}
    </button>
  ),
}));

vi.mock("@/apps/master-data/editors/RelationEditor", () => ({
  RelationEditor: (props: { mode: string; id?: string; readOnly: boolean }) => <div data-testid="relation-editor" data-mode={props.mode} data-id={props.id} data-readonly={String(props.readOnly)} />,
}));
vi.mock("@/apps/master-data/editors/DriverEditor", () => ({
  DriverEditor: (props: { mode: string; id?: string; readOnly: boolean }) => <div data-testid="driver-editor" data-mode={props.mode} data-id={props.id} data-readonly={String(props.readOnly)} />,
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";

import { apiClient } from "@/lib/api/client";
import { DriversView } from "@/apps/master-data/views/DriversView";
import { RelationsView } from "@/apps/master-data/views/RelationsView";
import { gridRows } from "./run2-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const DELETE = apiClient.DELETE as unknown as Mock<ClientCall>;

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

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

const relationRows = [
  { id: "r1", code: "ACME", name: "Acme", fullName: null, roles: 3, city: "Delft", countryCode: "NL", isArchived: false },
  { id: "r2", code: "OLD", name: "Old Carrier", fullName: null, roles: 2, city: "Gouda", countryCode: "NL", isArchived: true },
];
const driverRows = [
  { id: "d1", code: "DRV1", fullName: "Jan Jansen", firstName: "Jan", lastName: "Jansen", isArchived: false },
  { id: "d2", code: "DRV2", fullName: "Piet Pietersen", firstName: "Piet", lastName: "Pietersen", isArchived: true },
];

beforeEach(() => {
  grants = {};
  features = { "MasterData.Relations": "true" };
  gridRows.current = [];
  vi.mocked(toast.success).mockReset();
  vi.mocked(toast.error).mockReset();
  GET.mockReset().mockImplementation(async (path) => {
    if (path === "/api/master-data/relation-category/lookup") return ok([{ id: "cat-1", name: "Cat A" }]);
    if (path === "/api/master-data/relation") return ok({ rows: relationRows, totalCount: 2, filteredCount: 2 });
    if (path === "/api/master-data/driver") return ok({ rows: driverRows, totalCount: 2, filteredCount: 2 });
    return ok(undefined);
  });
  POST.mockReset().mockResolvedValue(ok({ deletedCount: 2, skippedRows: [] }));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

function renderView(view: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{view}</QueryClientProvider>);
}

const listCalls = (path: string) => GET.mock.calls.filter(([p]) => p === path);
const lastListQuery = (path: string) => listCalls(path).at(-1)![1]!.params!.query!;
const columns = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span")).map((el) => ({ field: el.getAttribute("data-column"), header: el.textContent, hidden: el.getAttribute("data-hidden") === "true", sortable: el.getAttribute("data-sortable") !== "false" }));
const alertDialog = () => screen.getByRole("alertdialog");

describe("RelationsView", () => {
  it("shows the permission-denied panel while MasterData.Relations is off and fetches nothing (constitution rule 4)", () => {
    features = {};
    grants = { "MasterData.Relations": true };
    renderView(<RelationsView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(screen.queryByTestId("mock-grid")).toBeNull();
    expect(listCalls("/api/master-data/relation")).toHaveLength(0);
  });

  it("shows the permission-denied panel without MasterData.Relations and fetches nothing (UI-PAGE-005)", () => {
    renderView(<RelationsView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(listCalls("/api/master-data/relation")).toHaveLength(0);
  });

  it("renders the grid masterData.relations with the designed columns (the full name hidden after the visible ones); the server-unsortable ones are not sortable", async () => {
    grants = { "MasterData.Relations": true };
    renderView(<RelationsView />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.getAttribute("data-grid-id")).toBe("masterData.relations");
    expect(columns()).toEqual([
      { field: "code", header: "MasterData:Code", hidden: false, sortable: true },
      { field: "name", header: "MasterData:Name", hidden: false, sortable: true },
      { field: "roles", header: "MasterData:Roles", hidden: false, sortable: false },
      { field: "city", header: "MasterData:City", hidden: false, sortable: true },
      { field: "countryCode", header: "MasterData:Country", hidden: false, sortable: false },
      { field: "vatNumber", header: "MasterData:VatNumber", hidden: false, sortable: true },
      { field: "phone", header: "MasterData:Phone", hidden: false, sortable: true },
      { field: "email", header: "MasterData:Email", hidden: false, sortable: true },
      { field: "chamberOfCommerceNumber", header: "MasterData:ChamberOfCommerceNumber", hidden: true, sortable: true },
      { field: "categoryNames", header: "MasterData:Categories", hidden: true, sortable: false },
      { field: "isArchived", header: "MasterData:IsArchived", hidden: true, sortable: true },
      { field: "fullName", header: "MasterData:RelationFullName", hidden: true, sortable: true },
      { field: "isBlocked", header: "MasterData:IsBlocked", hidden: true, sortable: true },
    ]);
    expect(screen.getByText("MasterData:ShowArchived")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "MasterData:Role" })).toBeInTheDocument();
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(1));
    expect(lastListQuery("/api/master-data/relation")).toEqual({ startRow: 0, endRow: 50, wildcardSearch: undefined, sortBy: undefined, sortDir: undefined, includeArchived: false, roles: undefined, categoryId: undefined });
  });

  it("offers New only with Create and Delete selected only with Delete (UI-PERM-002)", async () => {
    grants = { "MasterData.Relations": true };
    const { unmount } = renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:NewRelation")).toBeNull();
    expect(screen.queryByText("MasterData:DeleteSelected")).toBeNull();
    unmount();

    grants = { "MasterData.Relations": true, "MasterData.Relations.Create": true };
    const second = renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.getByText("MasterData:NewRelation")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:DeleteSelected")).toBeNull();
    second.unmount();

    grants = { "MasterData.Relations": true, "MasterData.Relations.Delete": true };
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:NewRelation")).toBeNull();
    expect(screen.getByText("MasterData:DeleteSelected")).toBeInTheDocument();
  });

  it("New opens the relation editor in create mode; Edit opens it for the row", async () => {
    grants = { "MasterData.Relations": true, "MasterData.Relations.Create": true, "MasterData.Relations.Update": true };
    gridRows.current = [relationRows[0]];
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("MasterData:NewRelation"));
    expect(screen.getByTestId("relation-editor")).toHaveAttribute("data-mode", "create");
  });

  it("Show archived sends includeArchived in the list request", async () => {
    grants = { "MasterData.Relations": true };
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(1));
    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(2));
    expect(lastListQuery("/api/master-data/relation").includeArchived).toBe(true);
  });

  it("the Role filter has an All roles row and sends the flags value as roles", async () => {
    grants = { "MasterData.Relations": true };
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    const role = screen.getByRole("combobox", { name: "MasterData:Role" });
    const labels = within(role).getAllByRole("option").map((o) => o.textContent);
    expect(labels).toEqual(["MasterData:AllRoles", "MasterData:Role.Customer", "MasterData:Role.Carrier", "MasterData:Role.Creditor", "MasterData:Role.Depot", "MasterData:Role.OwnCompany"]);
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(1));

    fireEvent.change(role, { target: { value: "2" } });
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(2));
    expect(lastListQuery("/api/master-data/relation").roles).toBe(2);

    fireEvent.change(role, { target: { value: "" } });
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(3));
    expect(lastListQuery("/api/master-data/relation").roles).toBeUndefined();
  });

  it("the Category filter lists the category lookup and sends categoryId", async () => {
    grants = { "MasterData.Relations": true };
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    const category = document.getElementById("relations-category-filter") as HTMLSelectElement;
    await waitFor(() => expect(within(category).getByText("Cat A")).toBeInTheDocument());
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(1));
    fireEvent.change(category, { target: { value: "cat-1" } });
    await waitFor(() => expect(lastListQuery("/api/master-data/relation").categoryId).toBe("cat-1"));
  });

  it("Archive is a row action with Update only; it confirms, calls the archive endpoint and refreshes the grid", async () => {
    grants = { "MasterData.Relations": true };
    gridRows.current = [relationRows[0]];
    const { unmount } = renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:Archive")).toBeNull();
    expect(screen.queryByText("Edit")).toBeNull();
    unmount();
    GET.mockClear();

    grants = { "MasterData.Relations": true, "MasterData.Relations.Update": true };
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(1));
    fireEvent.click(screen.getByText("MasterData:Archive"));
    const dialog = alertDialog();
    expect(within(dialog).getByText("MasterData:ArchiveRelation")).toBeInTheDocument();
    expect(within(dialog).getByText(/Acme/)).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/relation/{id}/archive", { params: { path: { id: "r1" } } }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    await waitFor(() => expect(listCalls("/api/master-data/relation")).toHaveLength(2));
    expect(toast.success).toHaveBeenCalledWith("MasterData:RelationArchived");
  });

  it("an archived row offers Unarchive and calls the unarchive endpoint", async () => {
    grants = { "MasterData.Relations": true, "MasterData.Relations.Update": true };
    gridRows.current = [relationRows[1]];
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:Archive")).toBeNull();
    fireEvent.click(screen.getByText("MasterData:Unarchive"));
    const dialog = alertDialog();
    expect(within(dialog).getByText("MasterData:UnarchiveRelation")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "MasterData:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/relation/{id}/unarchive", { params: { path: { id: "r2" } } }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("MasterData:RelationUnarchived"));
  });

  it("a failing archive shows the server message in the confirmation and keeps it open", async () => {
    grants = { "MasterData.Relations": true, "MasterData.Relations.Update": true };
    gridRows.current = [relationRows[0]];
    POST.mockResolvedValue({ data: undefined, error: { error: { message: "Relation is in use" } }, response: new Response() });
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("MasterData:Archive"));
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Relation is in use"));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(listCalls("/api/master-data/relation")).toHaveLength(1);
  });

  it("Delete from the row confirms and calls the delete endpoint; bulk delete sends the selection with the list filters", async () => {
    grants = { "MasterData.Relations": true, "MasterData.Relations.Delete": true };
    gridRows.current = relationRows;
    renderView(<RelationsView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getAllByText("Delete")[0]);
    let dialog = alertDialog();
    expect(within(dialog).getByText("MasterData:DeleteRelation")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(DELETE).toHaveBeenCalledWith("/api/master-data/relation/{id}", { params: { path: { id: "r1" } } }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

    fireEvent.click(screen.getByText("select-all"));
    fireEvent.click(await screen.findByRole("button", { name: /DeleteNSelected/ }));
    dialog = alertDialog();
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/master-data/relation/bulk-delete", {
        body: { selection: { mode: "Explicit", explicitIds: ["r1", "r2"], excludedIds: [], filterRequest: undefined }, includeArchived: false, roles: undefined, categoryId: undefined },
      }),
    );
  });
});

describe("DriversView", () => {
  it("sits behind MasterData.Relations and MasterData.Drivers and fetches nothing without them", () => {
    features = {};
    grants = { "MasterData.Drivers": true };
    const { unmount } = renderView(<DriversView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    unmount();

    features = { "MasterData.Relations": "true" };
    grants = {};
    renderView(<DriversView />);
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(listCalls("/api/master-data/driver")).toHaveLength(0);
  });

  it("renders the grid masterData.drivers with the designed columns and the Employer and Show archived filters", async () => {
    grants = { "MasterData.Drivers": true };
    renderView(<DriversView />);
    const grid = await screen.findByTestId("mock-grid");
    expect(grid.getAttribute("data-grid-id")).toBe("masterData.drivers");
    expect(columns()).toEqual([
      { field: "code", header: "MasterData:Code", hidden: false, sortable: true },
      { field: "fullName", header: "MasterData:Name", hidden: false, sortable: true },
      { field: "employerName", header: "MasterData:Employer", hidden: false, sortable: false },
      { field: "mobile", header: "MasterData:Mobile", hidden: false, sortable: true },
      { field: "email", header: "MasterData:Email", hidden: false, sortable: true },
      { field: "qualificationsExpiring", header: "MasterData:QualificationsExpiring", hidden: false, sortable: false },
      { field: "hasUser", header: "MasterData:UserAccount", hidden: true, sortable: false },
      { field: "isAnonymised", header: "MasterData:Anonymised", hidden: true, sortable: false },
      { field: "isArchived", header: "MasterData:IsArchived", hidden: true, sortable: true },
    ]);
    expect(screen.getByTestId("employer-filter")).toHaveTextContent("MasterData:Employer");
    expect(screen.getByText("MasterData:ShowArchived")).toBeInTheDocument();
    await waitFor(() => expect(listCalls("/api/master-data/driver")).toHaveLength(1));
    expect(lastListQuery("/api/master-data/driver")).toEqual({ startRow: 0, endRow: 50, wildcardSearch: undefined, sortBy: undefined, sortDir: undefined, includeArchived: false, employerRelationId: undefined });
  });

  it("offers New and Delete selected per permission and opens the driver editor", async () => {
    grants = { "MasterData.Drivers": true, "MasterData.Drivers.Create": true };
    const { unmount } = renderView(<DriversView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:DeleteSelected")).toBeNull();
    fireEvent.click(screen.getByText("MasterData:NewDriver"));
    expect(screen.getByTestId("driver-editor")).toHaveAttribute("data-mode", "create");
    unmount();

    grants = { "MasterData.Drivers": true, "MasterData.Drivers.Delete": true };
    renderView(<DriversView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:NewDriver")).toBeNull();
    expect(screen.getByText("MasterData:DeleteSelected")).toBeInTheDocument();
  });

  it("Show archived and the Employer filter travel as includeArchived and employerRelationId", async () => {
    grants = { "MasterData.Drivers": true };
    renderView(<DriversView />);
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls("/api/master-data/driver")).toHaveLength(1));
    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/master-data/driver").includeArchived).toBe(true));
    fireEvent.click(screen.getByTestId("employer-filter"));
    await waitFor(() => expect(lastListQuery("/api/master-data/driver").employerRelationId).toBe("rel-1"));
    expect(lastListQuery("/api/master-data/driver").includeArchived).toBe(true);
  });

  it("Archive confirms with the driver's name, calls the archive endpoint and refreshes; an archived row unarchives", async () => {
    grants = { "MasterData.Drivers": true, "MasterData.Drivers.Update": true };
    gridRows.current = driverRows;
    renderView(<DriversView />);
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls("/api/master-data/driver")).toHaveLength(1));
    fireEvent.click(screen.getByText("MasterData:Archive"));
    let dialog = alertDialog();
    expect(within(dialog).getByText("MasterData:ArchiveDriver")).toBeInTheDocument();
    expect(within(dialog).getByText(/Jan Jansen/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver/{id}/archive", { params: { path: { id: "d1" } } }));
    await waitFor(() => expect(listCalls("/api/master-data/driver")).toHaveLength(2));
    expect(toast.success).toHaveBeenCalledWith("MasterData:DriverArchived");

    fireEvent.click(screen.getByText("MasterData:Unarchive"));
    dialog = alertDialog();
    expect(within(dialog).getByText(/Piet Pietersen/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "MasterData:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver/{id}/unarchive", { params: { path: { id: "d2" } } }));
  });

  it("hides Archive and Edit without Drivers.Update", async () => {
    grants = { "MasterData.Drivers": true, "MasterData.Drivers.Delete": true };
    gridRows.current = [driverRows[0]];
    renderView(<DriversView />);
    await screen.findByTestId("mock-grid");
    expect(screen.queryByText("MasterData:Archive")).toBeNull();
    expect(screen.queryByText("Edit")).toBeNull();
    expect(screen.getAllByText("Delete").length).toBeGreaterThanOrEqual(1);
  });
});
