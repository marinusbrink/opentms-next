import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The organization-unit editor and the Add-members picker.
 * Risk class: High — a wrong dirty state loses or blocks edits (UI-FORM-009), staged
 * membership must become exactly one add and one remove call on Save, a picker that submits
 * an empty selection or hides its count misleads the administrator (UI-GRID-016). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(() => ({
    data: { auth: { grantedPolicies: { "Platform.Administration.OrganizationUnits.ManageMembers": true } } },
  })),
}));

const invalidateQueries = vi.fn();
vi.mock("@tanstack/react-query", () => ({
  useQueryClient: () => ({ invalidateQueries }),
}));

const createMutateAsync = vi.fn();
const updateMutateAsync = vi.fn();
const addMutateAsync = vi.fn();
const removeMutateAsync = vi.fn();
const fetchUnaddedUsers = vi.fn();
let membersData: unknown[] | undefined = [];
let selectionListener: ((selection: unknown) => void) | undefined;
let gridFetch: ((req: unknown) => Promise<unknown>) | undefined;
let panelProps: { members?: { id?: string }[]; onChange?: (m: unknown[]) => void; canManage?: boolean } = {};

vi.mock("@/domains/platform/administration-organization-units", () => ({
  useCreateOrganizationUnit: () => ({ mutateAsync: createMutateAsync, isPending: false }),
  useUpdateOrganizationUnit: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
  useAddOrganizationUnitMembers: () => ({ mutateAsync: addMutateAsync, isPending: false }),
  useRemoveOrganizationUnitMembers: () => ({ mutateAsync: removeMutateAsync, isPending: false }),
  useAllOrganizationUnits: () => ({
    data: [
      { id: "root", displayName: "Head office", parentId: null },
      { id: "u1", displayName: "Planning", parentId: "root" },
      { id: "u1-child", displayName: "Rotterdam", parentId: "u1" },
      { id: "other", displayName: "Warehouse", parentId: null },
    ],
    isLoading: false,
  }),
  useOrganizationUnitMembers: () => ({ data: membersData, isLoading: membersData === undefined }),
  membersQueryKey: (id: string) => ["members", id],
  fetchUnaddedUsers: (...args: unknown[]) => fetchUnaddedUsers(...args),
  fetchAllPages: async (fetchPage: (s: number, e: number) => Promise<{ rows: unknown[] }>) => (await fetchPage(0, 200)).rows,
}));

vi.mock("@/apps/admin/components/OrganizationUnitMembersPanel", () => ({
  OrganizationUnitMembersPanel: (props: typeof panelProps) => {
    panelProps = props;
    return <div data-testid="members-panel" />;
  },
}));

vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: (props: { gridId: string; onSelectionChange?: (s: unknown) => void; fetchRows: (req: unknown) => Promise<unknown> }) => {
    selectionListener = props.onSelectionChange;
    gridFetch = props.fetchRows;
    return <div data-testid={`grid-${props.gridId}`} />;
  },
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
  createMutateAsync.mockReset();
  updateMutateAsync.mockReset();
  addMutateAsync.mockReset();
  removeMutateAsync.mockReset();
  fetchUnaddedUsers.mockReset();
  invalidateQueries.mockReset();
  membersData = [];
  selectionListener = undefined;
  gridFetch = undefined;
  panelProps = {};
});

import { OrganizationUnitFormDialog } from "@/apps/admin/components/OrganizationUnitFormDialog";
import { AddOrganizationUnitMembersDialog } from "@/apps/admin/components/AddOrganizationUnitMembersDialog";

const unit = {
  id: "u1",
  displayName: "Planning",
  parentId: "root",
  parentDisplayName: "Head office",
  hasChildren: true,
  memberCount: 2,
  creationTime: "2026-10-01T10:00:00Z",
};

const submitForm = () =>
  act(async () => {
    fireEvent.submit(document.body.querySelector("form")!);
  });

const closeCommand = () =>
  Array.from(document.body.querySelectorAll("button")).find((b) => b.textContent?.trim() === "Close" && !b.getAttribute("aria-label"))!;

describe("OrganizationUnitFormDialog", () => {
  it("creates a sub-unit under the prefilled parent", async () => {
    createMutateAsync.mockResolvedValue({});
    const onSuccess = vi.fn();
    const onClose = vi.fn();
    render(
      <OrganizationUnitFormDialog mode="create" parent={{ id: "root", displayName: "Head office" }} open onClose={onClose} onSuccess={onSuccess} />,
    );

    expect(screen.getByText("Administration:NewSubUnit")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Administration:ParentUnit" })).toHaveTextContent("Head office");
    expect(screen.queryByText("Administration:Members")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Administration:DisplayName/), { target: { value: "  Rotterdam " } });
    await submitForm();

    expect(createMutateAsync).toHaveBeenCalledWith({ displayName: "Rotterdam", parentId: "root" });
    expect(onSuccess).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it("creates a root unit when no parent is chosen and refuses an empty name", async () => {
    render(<OrganizationUnitFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);

    expect(screen.getByText("Administration:NewOrganizationUnit")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Administration:ParentUnit" })).toHaveTextContent("");

    await submitForm();

    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("FieldRequired")).toBeInTheDocument();
  });

  it("moves the unit by choosing another parent; the unit and its subtree cannot be chosen", async () => {
    updateMutateAsync.mockResolvedValue({});
    render(<OrganizationUnitFormDialog mode="edit" unit={unit} open onClose={vi.fn()} onSuccess={vi.fn()} />);

    const picker = screen.getByRole("combobox", { name: "Administration:ParentUnit" });
    expect(picker).toHaveTextContent("Head office");
    fireEvent.click(picker);
    await screen.findAllByRole("option");
    /* The unit's own branch sits under Head office: expand it to see the greyed-out subtree. */
    fireEvent.click(screen.getByRole("button", { name: "Expand" }));

    const options = screen.getAllByRole("option");
    const byLabel = (label: string) => options.find((o) => o.textContent === label)!;
    expect(byLabel("Planning")).toBeDisabled();
    expect(byLabel("Warehouse")).toBeEnabled();
    fireEvent.click(byLabel("Warehouse"));
    expect(picker).toHaveTextContent("Warehouse");

    await submitForm();
    expect(updateMutateAsync).toHaveBeenCalledWith({ id: "u1", dto: { displayName: "Planning", parentId: "other" } });
  });

  it("is clean until something changes, then applies the member difference on Save (UI-FORM-009)", async () => {
    membersData = [{ id: "a" }, { id: "b" }];
    updateMutateAsync.mockResolvedValue({});
    addMutateAsync.mockResolvedValue({ affectedCount: 1 });
    removeMutateAsync.mockResolvedValue({ affectedCount: 1 });
    const onClose = vi.fn();
    render(<OrganizationUnitFormDialog mode="edit" unit={unit} open onClose={onClose} onSuccess={vi.fn()} />);

    expect(screen.getByRole("tab", { name: "Administration:Members" })).toBeInTheDocument();
    expect(panelProps.members).toEqual([{ id: "a" }, { id: "b" }]);
    expect(panelProps.canManage).toBe(true);

    fireEvent.click(closeCommand());
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("DiscardChangesQuestion")).not.toBeInTheDocument();

    act(() => panelProps.onChange!([{ id: "b" }, { id: "c" }]));
    fireEvent.click(closeCommand());
    expect(screen.getByText("DiscardChangesQuestion")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    await submitForm();
    expect(updateMutateAsync).not.toHaveBeenCalled();
    expect(addMutateAsync).toHaveBeenCalledWith({ id: "u1", selection: { mode: "Explicit", explicitIds: ["c"], filterRequest: null, excludedIds: [] } });
    expect(removeMutateAsync).toHaveBeenCalledWith({ id: "u1", selection: { mode: "Explicit", explicitIds: ["a"], filterRequest: null, excludedIds: [] } });
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["members", "u1"] });
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("shows a failed save inside the dialog and stays open (UI-FB-004, UI-FB-005)", async () => {
    updateMutateAsync.mockRejectedValue({ error: { message: "Name already taken" } });
    const onClose = vi.fn();
    render(<OrganizationUnitFormDialog mode="edit" unit={unit} open onClose={onClose} onSuccess={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/Administration:DisplayName/), { target: { value: "Taken" } });
    await submitForm();

    expect(screen.getByText("Name already taken")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("is read-only without the update permission (UI-PERM-004)", () => {
    membersData = [{ id: "a" }];
    render(<OrganizationUnitFormDialog mode="edit" unit={unit} readOnly open onClose={vi.fn()} onSuccess={vi.fn()} />);

    expect(screen.getByLabelText(/Administration:DisplayName/)).toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Administration:ParentUnit" })).toBeDisabled();
    expect(panelProps.canManage).toBe(false);
    expect(Array.from(document.body.querySelectorAll("button")).some((b) => b.textContent?.trim() === "Save")).toBe(false);
  });
});

describe("AddOrganizationUnitMembersDialog", () => {
  it("hides staged members, counts the selection on the primary and hands the picked rows back", async () => {
    fetchUnaddedUsers.mockResolvedValue({
      rows: [{ id: "a", userName: "a" }, { id: "x", userName: "x" }, { id: "y", userName: "y" }],
      totalCount: 3,
      filteredCount: 3,
    });
    const onPicked = vi.fn();
    const onClose = vi.fn();
    render(
      <AddOrganizationUnitMembersDialog unitId="u1" unitName="Planning" excludeIds={new Set(["a"])} open onClose={onClose} onPicked={onPicked} />,
    );

    expect(screen.getByText("Administration:AddMembersTo".replace("{0}", "Planning"))).toBeInTheDocument();
    const page = (await gridFetch!({ startRow: 0, endRow: 100, sortModels: [], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] })) as { rows: { id: string }[] };
    expect(page.rows.map((r) => r.id)).toEqual(["x", "y"]);

    const primary = screen.getByRole("button", { name: "Administration:AddSelected (0)" });
    expect(primary).toBeDisabled();

    act(() => {
      selectionListener?.({ mode: "Explicit", explicitIds: ["x", "y"], filterRequest: null, excludedIds: [] });
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Administration:AddSelected (2)" }));
    });

    expect(onPicked).toHaveBeenCalledWith([{ id: "x", userName: "x" }, { id: "y", userName: "y" }]);
    expect(onClose).toHaveBeenCalled();
  });

  it("resolves a select-all over a filter on the server and shows a failure inside the dialog", async () => {
    const page = { rows: [{ id: "x" }, { id: "y" }, { id: "z" }], totalCount: 3, filteredCount: 3 };
    fetchUnaddedUsers.mockResolvedValue(page);
    const onPicked = vi.fn();
    const onClose = vi.fn();
    render(<AddOrganizationUnitMembersDialog unitId="u1" unitName="Planning" excludeIds={new Set()} open onClose={onClose} onPicked={onPicked} />);

    /* The grid loaded a page, so a select-all over the filter counts 3 minus the excluded one. */
    await gridFetch!({ startRow: 0, endRow: 100, sortModels: [], columnFilters: {}, wildcardSearch: "jan", rowGroupCols: [], groupKeys: [] });
    act(() => {
      selectionListener?.({
        mode: "FilterBased",
        explicitIds: [],
        filterRequest: { startRow: 0, endRow: 100, sortModels: [], columnFilters: {}, wildcardSearch: "jan", rowGroupCols: [], groupKeys: [] },
        excludedIds: ["z"],
      });
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /Administration:AddSelected/ }));
    });
    expect(fetchUnaddedUsers).toHaveBeenLastCalledWith("u1", expect.objectContaining({ wildcardSearch: "jan" }));
    expect(onPicked).toHaveBeenCalledWith([{ id: "x" }, { id: "y" }]);

    fetchUnaddedUsers.mockRejectedValueOnce({ error: { message: "Users unavailable" } });
    act(() => {
      selectionListener?.({ mode: "FilterBased", explicitIds: [], filterRequest: null, excludedIds: [] });
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /Administration:AddSelected/ }));
    });
    expect(screen.getByText("Users unavailable")).toBeInTheDocument();
  });
});
