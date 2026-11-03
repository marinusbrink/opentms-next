import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import type { PermissionGroup } from "@/domains/platform/administration-permissions";

/* The permissions dialog of a role or a user (docs/designs/73-role-permissions.md): ABP's groups hidden, the module
 * groups as trees, parent/child cascade, only the changed permissions sent, role grants read-only in user mode.
 * Risk class: Critical (authorization). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: vi.fn() } }));

let groups: PermissionGroup[] = [];
const updateMutateAsync = vi.fn();
vi.mock("@/domains/platform/administration-permissions", async (importActual) => ({
  ...(await importActual<typeof import("@/domains/platform/administration-permissions")>()),
  usePermissionGrants: () => ({ data: groups, isLoading: false, isError: false, error: null }),
  useUpdatePermissionGrants: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

import { toPermissionGroups } from "@/domains/platform/administration-permissions";
import { PermissionsDialog } from "@/apps/admin/components/PermissionsDialog";

/** ABP's response for a role: an OpenTMS group with a two-level tree, and ABP's own Identity group. */
const abpRoleResponse = [
  {
    name: "Orders",
    displayName: "Orders",
    permissions: [
      { name: "Orders.TransportOrders", displayName: "Transport orders", parentName: null, isGranted: true, isEditable: true, grantedProviders: [{ providerName: "R", providerKey: "Planner" }] },
      { name: "Orders.TransportOrders.Create", displayName: "Create transport orders", parentName: "Orders.TransportOrders", isGranted: false, isEditable: true, grantedProviders: [] },
      { name: "Orders.TransportOrders.Approve", displayName: "Approve transport orders", parentName: "Orders.TransportOrders", isGranted: false, isEditable: true, grantedProviders: [] },
    ],
  },
  {
    name: "AbpIdentity",
    displayName: "Identity management",
    permissions: [{ name: "AbpIdentity.Roles", displayName: "Role management", parentName: null, isGranted: true, isEditable: true, grantedProviders: [] }],
  },
];

beforeEach(() => {
  updateMutateAsync.mockReset();
  toastSuccess.mockReset();
  groups = toPermissionGroups(abpRoleResponse, "R");
});

/** A permission's checkbox by its localized name (the label may add "Through role: …"). */
const checkbox = (displayName: string) => screen.getByRole("checkbox", { name: new RegExp(`^${displayName}`) });
const isChecked = (displayName: string) => checkbox(displayName).getAttribute("aria-checked") === "true";
const allCheckbox = () => screen.getByRole("checkbox", { name: "Administration:PermissionsAll" });
const save = () =>
  act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
  });

describe("toPermissionGroups", () => {
  it("keeps only the OpenTMS module groups and gives each permission its depth", () => {
    expect(groups.map((g) => g.name)).toEqual(["Orders"]);
    expect(groups[0].permissions.map((p) => [p.name, p.depth])).toEqual([
      ["Orders.TransportOrders", 0],
      ["Orders.TransportOrders.Create", 1],
      ["Orders.TransportOrders.Approve", 1],
    ]);
  });

  it("in user mode reads the direct grant and lists the roles a permission comes through", () => {
    const user = toPermissionGroups(
      [
        {
          name: "Orders",
          displayName: "Orders",
          permissions: [
            { name: "Orders.TransportOrders", isGranted: true, grantedProviders: [{ providerName: "R", providerKey: "Planner" }] },
            { name: "Orders.TransportOrders.Create", parentName: "Orders.TransportOrders", isGranted: true, grantedProviders: [{ providerName: "U", providerKey: "u1" }] },
          ],
        },
      ],
      "U",
    );
    expect(user[0].permissions.map((p) => [p.name, p.isGranted, p.grantedThroughRoles])).toEqual([
      ["Orders.TransportOrders", false, ["Planner"]],
      ["Orders.TransportOrders.Create", true, []],
    ]);
  });
});

describe("PermissionsDialog", () => {
  it("shows the module groups with localized names and never ABP's own groups", () => {
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={vi.fn()} />);

    expect(screen.getByText("Administration:Permissions Planner")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Orders" })).toBeInTheDocument();
    expect(screen.getByText("Create transport orders")).toBeInTheDocument();
    expect(screen.queryByText("Identity management")).toBeNull();
    expect(screen.queryByText("Role management")).toBeNull();
    /* A child sits one step in under its parent. */
    expect(checkbox("Create transport orders").closest("label")!.className).toContain("ps-6");
    expect(checkbox("Transport orders").closest("label")!.className).not.toContain("ps-6");
  });

  it("checking a child checks its parent, unchecking the parent unchecks its children, and only the changes are sent", async () => {
    groups = toPermissionGroups(
      [{ ...abpRoleResponse[0], permissions: abpRoleResponse[0].permissions.map((p) => ({ ...p, isGranted: false })) }],
      "R",
    );
    updateMutateAsync.mockResolvedValue(undefined);
    const onClose = vi.fn();
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={onClose} />);

    fireEvent.click(checkbox("Create transport orders"));
    expect(isChecked("Transport orders")).toBe(true);
    expect(isChecked("Create transport orders")).toBe(true);

    fireEvent.click(checkbox("Approve transport orders"));
    fireEvent.click(checkbox("Transport orders"));
    expect(isChecked("Create transport orders")).toBe(false);
    expect(isChecked("Approve transport orders")).toBe(false);

    fireEvent.click(checkbox("Approve transport orders"));
    await save();

    expect(updateMutateAsync).toHaveBeenCalledWith({
      providerName: "R",
      providerKey: "Planner",
      permissions: [
        { name: "Orders.TransportOrders", isGranted: true },
        { name: "Orders.TransportOrders.Approve", isGranted: true },
      ],
    });
    expect(toastSuccess).toHaveBeenCalledWith("Administration:PermissionsSaved");
    expect(onClose).toHaveBeenCalled();
  });

  it("removing a grant sends it as not granted", async () => {
    updateMutateAsync.mockResolvedValue(undefined);
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={vi.fn()} />);

    fireEvent.click(checkbox("Transport orders"));
    await save();

    expect(updateMutateAsync).toHaveBeenCalledWith({
      providerName: "R",
      providerKey: "Planner",
      permissions: [{ name: "Orders.TransportOrders", isGranted: false }],
    });
  });

  it("the group's All checkbox grants every changeable permission of the group", async () => {
    updateMutateAsync.mockResolvedValue(undefined);
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={vi.fn()} />);

    expect(allCheckbox()).toBePartiallyChecked();
    fireEvent.click(allCheckbox());
    await save();

    expect(updateMutateAsync.mock.calls[0][0].permissions).toEqual([
      { name: "Orders.TransportOrders.Create", isGranted: true },
      { name: "Orders.TransportOrders.Approve", isGranted: true },
    ]);
  });

  it("closes without a request when nothing changed", async () => {
    const onClose = vi.fn();
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={onClose} />);
    await save();
    expect(updateMutateAsync).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it("a permission the caller may not change is disabled", () => {
    groups = toPermissionGroups(
      [{ ...abpRoleResponse[0], permissions: abpRoleResponse[0].permissions.map((p, i) => ({ ...p, isEditable: i !== 2 })) }],
      "R",
    );
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={vi.fn()} />);
    expect(checkbox("Approve transport orders")).toHaveAttribute("aria-disabled", "true");
    expect(checkbox("Create transport orders")).not.toHaveAttribute("aria-disabled", "true");
  });

  it("a predefined role shows what changing it means", () => {
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" isPredefinedRole open onClose={vi.fn()} />);
    expect(screen.getByText("Administration:PredefinedRolePermissionsNote")).toBeInTheDocument();
  });

  it("in user mode a permission held through a role is checked, disabled and names the role", () => {
    groups = toPermissionGroups(
      [
        {
          name: "Orders",
          displayName: "Orders",
          permissions: [
            { name: "Orders.TransportOrders", displayName: "Transport orders", isGranted: true, isEditable: true, grantedProviders: [{ providerName: "R", providerKey: "Planner" }] },
            { name: "Orders.TransportOrders.Create", displayName: "Create transport orders", parentName: "Orders.TransportOrders", isGranted: false, isEditable: true, grantedProviders: [] },
          ],
        },
      ],
      "U",
    );
    render(<PermissionsDialog providerName="U" providerKey="u1" subject="j.devries" open onClose={vi.fn()} />);

    expect(screen.getByText("Administration:UserPermissionsNote")).toBeInTheDocument();
    expect(isChecked("Transport orders")).toBe(true);
    expect(checkbox("Transport orders")).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText("Administration:GrantedThroughRole")).toBeInTheDocument();
    expect(checkbox("Create transport orders")).not.toHaveAttribute("aria-disabled", "true");
  });

  it("shows a refused save inside the dialog and stays open", async () => {
    updateMutateAsync.mockRejectedValue({ error: { message: "The administrator role always has every permission" } });
    const onClose = vi.fn();
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={onClose} />);

    fireEvent.click(checkbox("Transport orders"));
    await save();

    expect(screen.getByText("The administrator role always has every permission")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("filters the permissions by name", () => {
    render(<PermissionsDialog providerName="R" providerKey="Planner" subject="Planner" open onClose={vi.fn()} />);
    fireEvent.change(screen.getByRole("searchbox", { name: "Administration:PermissionsSearch" }), { target: { value: "approve" } });
    expect(screen.getByText("Approve transport orders")).toBeInTheDocument();
    expect(screen.queryByText("Create transport orders")).toBeNull();

    fireEvent.change(screen.getByRole("searchbox", { name: "Administration:PermissionsSearch" }), { target: { value: "zzz" } });
    expect(screen.getByText("Administration:PermissionsNone")).toBeInTheDocument();
  });
});
