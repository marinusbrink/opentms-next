import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The user editor's Roles and Organization units tabs (PO decision 2026-10-10).
 * Risk class: High — the roles and memberships sent on Save are the whole set; a wrong
 * toggle silently grants or removes access. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants } } }),
}));

const createMutateAsync = vi.fn();
const updateMutateAsync = vi.fn();
const setPictureMutateAsync = vi.fn();
const deletePictureMutateAsync = vi.fn();
const invalidateQueries = vi.fn();
vi.mock("@tanstack/react-query", () => ({
  useQueryClient: () => ({ invalidateQueries }),
}));
vi.mock("@/domains/platform/administration-users", () => ({
  useCreateUser: () => ({ mutateAsync: createMutateAsync, isPending: false }),
  useUpdateUser: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
  useSetProfilePicture: () => ({ mutateAsync: setPictureMutateAsync, isPending: false }),
  useDeleteProfilePicture: () => ({ mutateAsync: deletePictureMutateAsync, isPending: false }),
  useProfilePictureUrl: (userId: string | undefined, hasPicture: boolean) => ({ url: userId && hasPicture ? "blob:stored" : null, isLoading: false }),
  profilePictureQueryKey: (id: string) => ["picture", id],
}));
vi.mock("@/domains/platform/administration-roles", () => ({
  useAllRoles: () => ({ data: [{ id: "r1", name: "admin" }, { id: "r2", name: "planner" }], isLoading: false, isError: false }),
}));
vi.mock("@/domains/platform/administration-organization-units", () => ({
  useAllOrganizationUnits: () => ({
    data: [
      { id: "root", displayName: "Head office", parentId: null },
      { id: "plan", displayName: "Planning", parentId: "root" },
    ],
    isLoading: false,
    isError: false,
  }),
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
  vi.stubGlobal("URL", { ...URL, createObjectURL: vi.fn(() => "blob:preview"), revokeObjectURL: vi.fn() });
});

beforeEach(() => {
  createMutateAsync.mockReset();
  updateMutateAsync.mockReset();
  setPictureMutateAsync.mockReset();
  deletePictureMutateAsync.mockReset();
  invalidateQueries.mockReset();
  grants = { "Platform.Administration.OrganizationUnits": true };
});

import { UserFormDialog } from "@/apps/admin/components/UserFormDialog";

const user = {
  id: "u1",
  userName: "jansen",
  email: "jansen@t.nl",
  phoneNumber: "0101234567",
  isActive: true,
  shouldChangePasswordOnNextLogin: false,
  lockoutEnabled: true,
  hasProfilePicture: true,
  roleNames: ["planner"],
  organizationUnitIds: ["plan"],
  creationTime: "2026-10-01T10:00:00Z",
};

const submitForm = () =>
  act(async () => {
    fireEvent.submit(document.body.querySelector("form")!);
  });

/* Inactive tab panels are inert (UI-DLG-015), so a tab is opened before its checkboxes are read. */
const openTab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));

describe("UserFormDialog roles and organization units", () => {
  it("shows every role as a checkbox and every unit as a checkbox tree with the user's state, and saves the whole sets", async () => {
    updateMutateAsync.mockResolvedValue({});
    render(<UserFormDialog mode="edit" user={user} open onClose={vi.fn()} onSuccess={vi.fn()} />);

    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual([
      "General",
      "Administration:Roles",
      "Administration:OrganizationUnits",
      "",
    ]);
    openTab("Administration:Roles");
    expect(screen.getByRole("checkbox", { name: "planner" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "admin" })).not.toBeChecked();
    fireEvent.click(screen.getByRole("checkbox", { name: "admin" }));

    openTab("Administration:OrganizationUnits");
    expect(screen.getByRole("checkbox", { name: "Planning" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Head office" })).not.toBeChecked();
    fireEvent.click(screen.getByRole("checkbox", { name: "Planning" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Head office" }));
    await submitForm();

    expect(updateMutateAsync).toHaveBeenCalledWith({
      id: "u1",
      dto: expect.objectContaining({ roleNames: ["planner", "admin"], organizationUnitIds: ["root"] }),
    });
  });

  it("is clean until a checkbox changes (UI-FORM-009)", () => {
    const onClose = vi.fn();
    render(<UserFormDialog mode="edit" user={user} open onClose={onClose} onSuccess={vi.fn()} />);
    const closeCommand = () =>
      Array.from(document.body.querySelectorAll("button")).find((b) => b.textContent?.trim() === "Close" && !b.getAttribute("aria-label"))!;

    fireEvent.click(closeCommand());
    expect(onClose).toHaveBeenCalledTimes(1);

    openTab("Administration:Roles");
    fireEvent.click(screen.getByRole("checkbox", { name: "admin" }));
    fireEvent.click(closeCommand());
    expect(screen.getByText("DiscardChangesQuestion")).toBeInTheDocument();
  });

  it("hides the units tab without the units permission and still sends the memberships unchanged", async () => {
    grants = {};
    updateMutateAsync.mockResolvedValue({});
    render(<UserFormDialog mode="edit" user={user} open onClose={vi.fn()} onSuccess={vi.fn()} />);

    expect(screen.queryByRole("tab", { name: "Administration:OrganizationUnits" })).not.toBeInTheDocument();
    await submitForm();
    expect(updateMutateAsync).toHaveBeenCalledWith({ id: "u1", dto: expect.objectContaining({ organizationUnitIds: ["plan"] }) });
  });

  it("has an Account section with the picture and identity fields, and an Access section with the sign-in switches", async () => {
    updateMutateAsync.mockResolvedValue({});
    setPictureMutateAsync.mockResolvedValue(undefined);
    render(<UserFormDialog mode="edit" user={user} open onClose={vi.fn()} onSuccess={vi.fn()} />);

    const account = screen.getByRole("region", { name: "Administration:Account" });
    expect(account).toContainElement(screen.getByRole("button", { name: "Administration:ProfilePicture" }));
    expect(account).toContainElement(screen.getByLabelText(/Administration:PhoneNumber/));
    expect(account).toContainElement(screen.getByLabelText(/Administration:Username/));
    const access = screen.getByRole("region", { name: "Administration:Access" });
    expect(access).toContainElement(screen.getByRole("checkbox", { name: "Administration:LockoutEnabled" }));
    expect(screen.getByRole("checkbox", { name: "Administration:LockoutEnabled" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Administration:ShouldChangePasswordOnNextLogin" })).not.toBeChecked();
    expect(screen.queryByRole("checkbox", { name: "Administration:SetRandomPassword" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: "Administration:ShouldChangePasswordOnNextLogin" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Administration:LockoutEnabled" }));
    const file = new File(["x"], "me.png", { type: "image/png" });
    fireEvent.change(screen.getByLabelText("Administration:ChangePicture"), { target: { files: [file] } });
    await submitForm();

    expect(updateMutateAsync).toHaveBeenCalledWith({
      id: "u1",
      dto: expect.objectContaining({ phoneNumber: "0101234567", shouldChangePasswordOnNextLogin: true, lockoutEnabled: false }),
    });
    expect(setPictureMutateAsync).toHaveBeenCalledWith({ id: "u1", file });
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ["picture", "u1"] });
  });

  it("removes the stored picture on Save when Remove picture was chosen", async () => {
    updateMutateAsync.mockResolvedValue({});
    deletePictureMutateAsync.mockResolvedValue(undefined);
    render(<UserFormDialog mode="edit" user={user} open onClose={vi.fn()} onSuccess={vi.fn()} />);

    fireEvent.contextMenu(screen.getByRole("button", { name: "Administration:ProfilePicture" }));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Administration:RemovePicture" }));
    await submitForm();
    expect(deletePictureMutateAsync).toHaveBeenCalledWith("u1");
    expect(setPictureMutateAsync).not.toHaveBeenCalled();
  });

  it("creates with a random password: the password field disappears and no password is sent", async () => {
    createMutateAsync.mockResolvedValue({ id: "new" });
    render(<UserFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/Administration:Username/), { target: { value: "nieuw" } });
    fireEvent.change(screen.getByLabelText(/Administration:Email/), { target: { value: "nieuw@t.nl" } });
    await submitForm();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText("FieldRequired")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: "Administration:SetRandomPassword" }));
    expect(screen.queryByLabelText(/Administration:Password/)).not.toBeInTheDocument();
    await submitForm();
    expect(createMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ setRandomPassword: true, password: null, lockoutEnabled: true }));
  });

  it("creates a user with the checked roles and units, and disables every checkbox when read-only", async () => {
    createMutateAsync.mockResolvedValue({});
    const { unmount } = render(<UserFormDialog mode="create" open onClose={vi.fn()} onSuccess={vi.fn()} />);

    fireEvent.change(screen.getByLabelText(/Administration:Username/), { target: { value: "nieuw" } });
    fireEvent.change(screen.getByLabelText(/Administration:Email/), { target: { value: "nieuw@t.nl" } });
    fireEvent.change(screen.getByLabelText(/Administration:Password/), { target: { value: "Secret1!" } });
    createMutateAsync.mockResolvedValue({ id: "new" });
    openTab("Administration:Roles");
    fireEvent.click(screen.getByRole("checkbox", { name: "planner" }));
    openTab("Administration:OrganizationUnits");
    fireEvent.click(screen.getByRole("checkbox", { name: "Head office" }));
    await submitForm();
    expect(createMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ roleNames: ["planner"], organizationUnitIds: ["root"] }));
    unmount();

    render(<UserFormDialog mode="edit" user={user} readOnly open onClose={vi.fn()} onSuccess={vi.fn()} />);
    openTab("Administration:Roles");
    expect(screen.getByRole("checkbox", { name: "planner" })).toHaveAttribute("data-disabled");
    openTab("Administration:OrganizationUnits");
    expect(screen.getByRole("checkbox", { name: "Planning" })).toHaveAttribute("data-disabled");
  });
});
