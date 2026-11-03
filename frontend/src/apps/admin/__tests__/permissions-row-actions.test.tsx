import { act, render } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The Permissions row action of the Roles and Users lists (docs/designs/73-role-permissions.md): shown only with the
 * provider's ManagePermissions permission (UI-PERM-002), disabled with its reason for the admin role (UI-PERM-005),
 * and it opens the permissions dialog for the row's role name or user id. Risk class: Critical (authorization). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants } } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

type Action = { id: string; disabled?: boolean; disabledReasonKey?: string; onClick: () => void };
let gridProps: { rowActions?: (rows: unknown[]) => Action[] } = {};
vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: (props: typeof gridProps) => {
    gridProps = props;
    return <div data-testid="mock-grid" />;
  },
}));

vi.mock("@/domains/platform/administration-roles", async (importActual) => ({
  ...(await importActual<typeof import("@/domains/platform/administration-roles")>()),
  useRolesGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteRole: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/domains/platform/administration-users", () => ({
  useUsersGridFetcher: () => vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUnlockUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useResendInvitation: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/apps/admin/components/RoleFormDialog", () => ({ RoleFormDialog: () => null }));
vi.mock("@/apps/admin/components/BulkDeleteRolesDialog", () => ({ BulkDeleteRolesDialog: () => null }));
vi.mock("@/apps/admin/components/BulkDeleteUsersDialog", () => ({ BulkDeleteUsersDialog: () => null }));
vi.mock("@/apps/admin/components/ResetPasswordDialog", () => ({ ResetPasswordDialog: () => null }));
vi.mock("@/domains/platform/components/UserFormDialog", () => ({ UserFormDialog: () => null }));

const dialogProps = vi.fn();
vi.mock("@/apps/admin/components/PermissionsDialog", () => ({
  PermissionsDialog: (props: Record<string, unknown>) => {
    dialogProps(props);
    return <div data-testid="permissions-dialog" />;
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
  gridProps = {};
  dialogProps.mockReset();
});

import { RolesView } from "@/apps/admin/roles/RolesView";
import { UsersView } from "@/apps/admin/users/UsersView";

const permissionsAction = (rows: unknown[]) => gridProps.rowActions!(rows).find((a) => a.id === "permissions");

describe("RolesView Permissions action", () => {
  const renderView = () =>
    act(async () => {
      render(<RolesView />);
    });

  it("is hidden without Roles.ManagePermissions", async () => {
    grants = { "Platform.Administration.Roles": true, "Platform.Administration.Roles.Update": true };
    await renderView();
    expect(permissionsAction([{ id: "r1", name: "Planner" }])).toBeUndefined();
  });

  it("opens the dialog for the role name, a predefined role with its note", async () => {
    grants = { "Platform.Administration.Roles": true, "Platform.Administration.Roles.ManagePermissions": true };
    await renderView();

    const action = permissionsAction([{ id: "r1", name: "Planner", isStatic: true }])!;
    expect(action.disabled).toBe(false);
    await act(async () => action.onClick());

    expect(dialogProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ providerName: "R", providerKey: "Planner", subject: "Planner", isPredefinedRole: true }),
    );
  });

  it("is disabled with its reason for the admin role", async () => {
    grants = { "Platform.Administration.Roles": true, "Platform.Administration.Roles.ManagePermissions": true };
    await renderView();
    const action = permissionsAction([{ id: "a1", name: "admin", isStatic: true }])!;
    expect(action.disabled).toBe(true);
    expect(action.disabledReasonKey).toBe("Administration:AdminRolePermissionsReadOnly");
  });

  it("is not offered for several rows", async () => {
    grants = { "Platform.Administration.Roles": true, "Platform.Administration.Roles.ManagePermissions": true };
    await renderView();
    expect(permissionsAction([{ id: "r1", name: "Planner" }, { id: "r2", name: "Finance" }])).toBeUndefined();
  });
});

describe("UsersView Permissions action", () => {
  const renderView = () =>
    act(async () => {
      render(<UsersView />);
    });

  it("is hidden without Users.ManagePermissions, also with Roles.ManagePermissions", async () => {
    grants = { "Platform.Administration.Users": true, "Platform.Administration.Roles.ManagePermissions": true };
    await renderView();
    expect(permissionsAction([{ id: "u1", userName: "j.devries" }])).toBeUndefined();
  });

  it("opens the dialog for the user's direct grants", async () => {
    grants = { "Platform.Administration.Users": true, "Platform.Administration.Users.ManagePermissions": true };
    await renderView();

    await act(async () => permissionsAction([{ id: "u1", userName: "j.devries" }])!.onClick());

    expect(dialogProps).toHaveBeenLastCalledWith(
      expect.objectContaining({ providerName: "U", providerKey: "u1", subject: "j.devries" }),
    );
  });
});
