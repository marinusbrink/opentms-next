import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Administration dialogs as targets and hosts of UI-LOOK-004 (PO decision 2026-10-16). As targets (a master-data
 * picker opens them on top): create mode takes the typed text (`initialName`) and a save passes the created record to
 * `onSuccess(saved)`. As host: the organisation unit's parent TreeSelect offers "Add new organization unit…" with the
 * Create permission and, on the tenant side, the Administration.OrganizationUnits feature chain (UI-PERM-006; not
 * without them, not read-only); a new unit's editor opens on top and, once saved, is the parent.
 * The real dialogs, TreeSelect and query client run; the domain hooks are mocked over a mutable server list.
 * Risk class: Medium (UI flow) on authorization-relevant records (users, organisation units). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
/* Host side by default (no current tenant): Administration is hostAlwaysEnabled there, so no feature is needed. */
let tenantId: string | null = null;
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: tenantId }, auth: { grantedPolicies: grants }, features: { values: features } } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const server = vi.hoisted(() => ({ units: [] as { id: string; displayName: string; parentId: string | null }[] }));
const createUnit = vi.fn();
const createUser = vi.fn();

vi.mock("@/domains/platform/administration-organization-units", async () => {
  const { useQuery } = await import("@tanstack/react-query");
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: false });
  return {
    useCreateOrganizationUnit: () => ({ mutateAsync: createUnit, isPending: false }),
    useUpdateOrganizationUnit: mutation,
    useAddOrganizationUnitMembers: mutation,
    useRemoveOrganizationUnitMembers: mutation,
    useAllOrganizationUnits: () => useQuery({ queryKey: ["administration", "organization-units", "all"], queryFn: async () => [...server.units] }),
    useOrganizationUnitMembers: () => ({ data: [], isLoading: false }),
    membersQueryKey: (id: string) => ["members", id],
    fetchUnaddedUsers: vi.fn(),
    fetchAllPages: vi.fn(),
  };
});
vi.mock("@/domains/platform/components/OrganizationUnitMembersPanel", () => ({ OrganizationUnitMembersPanel: () => <div data-testid="members-panel" /> }));

vi.mock("@/domains/platform/administration-users", () => ({
  useCreateUser: () => ({ mutateAsync: createUser, isPending: false }),
  useUpdateUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useSetProfilePicture: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteProfilePicture: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useProfilePictureUrl: () => ({ url: null, isLoading: false }),
  profilePictureQueryKey: (id: string) => ["picture", id],
}));
vi.mock("@/domains/platform/administration-roles", () => ({
  useAllRoles: () => ({ data: [{ id: "r1", name: "admin" }], isLoading: false, isError: false }),
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

import { OrganizationUnitFormDialog } from "@/domains/platform/components/OrganizationUnitFormDialog";
import { UserFormDialog } from "@/domains/platform/components/UserFormDialog";

beforeEach(() => {
  tenantId = null;
  features = {};
  grants = { "Platform.Administration.OrganizationUnits.Create": true };
  server.units = [{ id: "root", displayName: "Head office", parentId: null }];
  let n = 0;
  createUnit.mockReset().mockImplementation(async ({ displayName, parentId }: { displayName: string; parentId: string | null }) => {
    const created = { id: `ou-new-${++n}`, displayName, parentId, code: "0000" + n };
    server.units.push(created);
    return created;
  });
  createUser.mockReset().mockImplementation(async (dto: { userName: string; email: string }) => ({ id: "u-new", ...dto, name: "Jane", surname: "Doe" }));
});

function renderIn(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
  return client;
}

const dialogs = () => Array.from(document.querySelectorAll<HTMLElement>("[role=dialog]"));
const submit = (dialog: HTMLElement) =>
  act(async () => {
    fireEvent.submit(dialog.querySelector("form")!);
  });

/* Fields by their label within a dialog: the ids are per instance (useId), since the dialog can open on top of itself. */
const fieldIn = (dialog: HTMLElement, label: RegExp) =>
  document.getElementById((within(dialog).getByText(label, { selector: "label" }) as HTMLLabelElement).htmlFor) as HTMLInputElement;
const nameIn = (dialog: HTMLElement) => fieldIn(dialog, /^Administration:DisplayName/);
const parentIn = (dialog: HTMLElement) => fieldIn(dialog, /^Administration:ParentUnit/);

describe("OrganizationUnitFormDialog as the target of Add new", () => {
  it("takes the typed name in create mode and passes the created unit to onSuccess", async () => {
    const onSuccess = vi.fn();
    const onClose = vi.fn();
    renderIn(<OrganizationUnitFormDialog mode="create" open initialName="Depot Venlo" onClose={onClose} onSuccess={onSuccess} />);
    expect(nameIn(dialogs()[0])).toHaveValue("Depot Venlo");
    await submit(dialogs()[0]);
    expect(createUnit).toHaveBeenCalledWith({ displayName: "Depot Venlo", parentId: null });
    expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ id: "ou-new-1", displayName: "Depot Venlo" }));
    expect(onClose).toHaveBeenCalled();
  });
});

describe("OrganizationUnitFormDialog parent picker — Add new", () => {
  const openParent = () => fireEvent.click(parentIn(dialogs()[0]));
  const addNew = () => screen.queryByRole("button", { name: "Administration:AddNewOrganizationUnit" });

  it("has no row without OrganizationUnits.Create", async () => {
    grants = {};
    renderIn(<OrganizationUnitFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);
    await waitFor(() => expect(parentIn(dialogs()[0])).not.toBeDisabled());
    openParent();
    expect(await screen.findByRole("option", { name: "Head office" })).toBeInTheDocument();
    expect(addNew()).toBeNull();
  });

  it.each(["Apps.Administration.OrganizationUnits", "Apps.Administration"])("tenant side: no row with the permission when %s is off", async (off) => {
    tenantId = "t-1";
    features = { "Apps.Administration": "true", "Apps.Administration.OrganizationUnits": "true", [off]: "false" };
    renderIn(<OrganizationUnitFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);
    await waitFor(() => expect(parentIn(dialogs()[0])).not.toBeDisabled());
    openParent();
    expect(await screen.findByRole("option", { name: "Head office" })).toBeInTheDocument();
    expect(addNew()).toBeNull();
  });

  it("tenant side: the row with the permission and the chain on", async () => {
    tenantId = "t-1";
    features = { "Apps.Administration": "true", "Apps.Administration.OrganizationUnits": "true" };
    renderIn(<OrganizationUnitFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);
    await waitFor(() => expect(parentIn(dialogs()[0])).not.toBeDisabled());
    openParent();
    expect(await screen.findByRole("button", { name: "Administration:AddNewOrganizationUnit" })).toBeInTheDocument();
  });

  it("has no row in a read-only dialog", () => {
    renderIn(<OrganizationUnitFormDialog mode="create" open readOnly onClose={vi.fn()} onSuccess={vi.fn()} />);
    openParent();
    expect(addNew()).toBeNull();
  });

  it("a unit created on top becomes the parent and is sent on Save", async () => {
    const onSuccess = vi.fn();
    renderIn(<OrganizationUnitFormDialog mode="create" open initialName="Depot Venlo" onClose={vi.fn()} onSuccess={onSuccess} />);
    await waitFor(() => expect(parentIn(dialogs()[0])).not.toBeDisabled());
    openParent();
    fireEvent.click(await screen.findByRole("button", { name: "Administration:AddNewOrganizationUnit" }));

    await waitFor(() => expect(dialogs()).toHaveLength(2));
    const nested = dialogs()[1];
    const nestedName = nameIn(nested);
    expect(nestedName).toHaveValue("");
    fireEvent.change(nestedName, { target: { value: "Region South" } });
    await submit(nested);
    expect(createUnit).toHaveBeenNthCalledWith(1, { displayName: "Region South", parentId: null });
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    await waitFor(() => expect(parentIn(dialogs()[0])).toHaveTextContent("Region South"));
    /* The host keeps what the user typed. */
    expect(nameIn(dialogs()[0])).toHaveValue("Depot Venlo");

    await submit(dialogs()[0]);
    expect(createUnit).toHaveBeenNthCalledWith(2, { displayName: "Depot Venlo", parentId: "ou-new-1" });
    expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ id: "ou-new-2", parentId: "ou-new-1" }));
  });

  it("the nested dialog's fields have ids of their own (no duplicate ids with the dialog below)", async () => {
    renderIn(<OrganizationUnitFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);
    await waitFor(() => expect(parentIn(dialogs()[0])).not.toBeDisabled());
    openParent();
    fireEvent.click(await screen.findByRole("button", { name: "Administration:AddNewOrganizationUnit" }));
    await waitFor(() => expect(dialogs()).toHaveLength(2));
    /* A label's `for` must reach the input in its own dialog; a duplicate id sends it to the dialog below. */
    const nested = dialogs()[1];
    const nameLabel = within(nested).getByText(/^Administration:DisplayName/, { selector: "label" }) as HTMLLabelElement;
    expect(document.getElementById(nameLabel.htmlFor)?.closest("[role=dialog]"), "the nested Name label points into the dialog below").toBe(nested);
    expect(nameIn(nested).id).not.toBe(nameIn(dialogs()[0]).id);
    expect(parentIn(nested).id).not.toBe(parentIn(dialogs()[0]).id);
  });
});

describe("UserFormDialog as the target of Add new", () => {
  it("takes the typed text as the user name and passes the created user to onSuccess", async () => {
    const onSuccess = vi.fn();
    renderIn(<UserFormDialog mode="create" open initialName="jdoe" onClose={vi.fn()} onSuccess={onSuccess} />);
    expect(document.getElementById("uf-userName")).toHaveValue("jdoe");
    fireEvent.change(document.getElementById("uf-email")!, { target: { value: "jane@example.test" } });
    const password = document.getElementById("uf-password");
    if (password) fireEvent.change(password, { target: { value: "1q2w3E*Secret" } });
    await submit(dialogs()[0]);
    await waitFor(() => expect(createUser).toHaveBeenCalledTimes(1));
    expect(createUser.mock.calls[0][0]).toEqual(expect.objectContaining({ userName: "jdoe", email: "jane@example.test" }));
    expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ id: "u-new", userName: "jdoe" }));
  });

  it("an edit ignores initialName", () => {
    renderIn(
      <UserFormDialog
        mode="edit"
        user={{ id: "u1", userName: "jansen", email: "j@t.nl", isActive: true, lockoutEnabled: true, roleNames: [], organizationUnitIds: [] } as never}
        initialName="ignored"
        open
        onClose={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );
    expect(document.getElementById("uf-userName")).toHaveValue("jansen");
  });
});
