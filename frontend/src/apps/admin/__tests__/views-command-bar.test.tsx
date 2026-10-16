import { render, screen, act } from "@testing-library/react";
import { describe, it, expect, vi, beforeAll, afterAll, beforeEach } from "vitest";
import { useApplicationConfiguration } from "@/lib/abp/queries";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(() => ({ data: undefined })),
}));

vi.mock("@/domains/platform/administration-users", () => ({
  useUsersGridFetcher: () =>
    vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUnlockUser: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock("@/domains/platform/administration-roles", () => ({
  useRolesGridFetcher: () =>
    vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteRole: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock("@/domains/platform/administration-organization-units", () => ({
  useOrganizationUnitsGridFetcher: () =>
    vi.fn().mockResolvedValue({ rows: [], totalCount: 0, filteredCount: 0 }),
  useDeleteOrganizationUnit: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock("@/domains/platform/components/OrganizationUnitFormDialog", () => ({
  OrganizationUnitFormDialog: () => null,
}));

vi.mock("@/components/ui/opentms-grid-lazy", () => ({
  LazyOpenTmsGrid: () => <div data-testid="mock-grid" />,
}));

vi.mock("@/domains/platform/components/UserFormDialog", () => ({
  UserFormDialog: () => null,
}));

vi.mock("@/apps/admin/components/ResetPasswordDialog", () => ({
  ResetPasswordDialog: () => null,
}));

vi.mock("@/apps/admin/components/BulkDeleteUsersDialog", () => ({
  BulkDeleteUsersDialog: () => null,
}));

vi.mock("@/apps/admin/components/RoleFormDialog", () => ({
  RoleFormDialog: () => null,
}));

vi.mock("@/apps/admin/components/DeleteRoleConfirmDialog", () => ({
  DeleteRoleConfirmDialog: () => null,
}));

vi.mock("@/apps/admin/components/BulkDeleteRolesDialog", () => ({
  BulkDeleteRolesDialog: () => null,
}));

vi.mock("@/components/ui/opentms-grid", () => ({
  OpenTmsGrid: () => <div data-testid="mock-grid" />,
}));

// ── ResizeObserver stub (required by AppCommandBar overflow detection) ─────────

class MockResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb(
      [{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry],
      this as unknown as ResizeObserver,
    );
  }
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

afterAll(() => {
  vi.unstubAllGlobals();
});

// ── Permission helpers ────────────────────────────────────────────────────────

const mockAppConfig = vi.mocked(useApplicationConfiguration);

function setGrants(...policies: string[]) {
  const grantedPolicies = Object.fromEntries(policies.map((p) => [p, true]));
  mockAppConfig.mockReturnValue({
    data: { auth: { grantedPolicies } },
  } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

// ── Imports (after mock declarations, hoisted by vitest) ──────────────────────

import { UsersView } from "@/apps/admin/users/UsersView";
import { RolesView } from "@/apps/admin/roles/RolesView";
import { OrganizationUnitsView } from "@/apps/admin/organization-units/OrganizationUnitsView";

// ── UsersView tests (Critical — AppCommandBar caller) ─────────────────────────

describe("UsersView — AppCommandBar integration (Critical)", () => {
  beforeEach(() => {
    setGrants(
      "Platform.Administration.Users",
      "Platform.Administration.Users.Create",
      "Platform.Administration.Users.Update",
      "Platform.Administration.Users.Delete",
      "Platform.Administration.Users.BulkDelete",
      "Platform.Administration.Users.ResetPassword",
    );
  });

  it("renders the new-style h-10 command bar when user has create permission", async () => {
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<UsersView />));
    });
    const bar = container.querySelector(".h-10");
    expect(bar).not.toBeNull();
  });

  it("does not render the old-style h-12 bar (flag-off path removed)", async () => {
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<UsersView />));
    });
    expect(container.querySelector(".h-12")).toBeNull();
  });

  it("shows NewUser primary action in command bar when canCreate is true", async () => {
    await act(async () => {
      render(<UsersView />);
    });
    expect(screen.getByText("Administration:NewUser")).toBeInTheDocument();
  });

  it("command bar is absent when user has view permission but no create or bulk-delete permission", async () => {
    setGrants("Platform.Administration.Users");
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<UsersView />));
    });
    // commands = [] → AppCommandBar returns null
    expect(container.querySelector(".h-10")).toBeNull();
    expect(container.querySelector(".h-12")).toBeNull();
  });

  it("permission-denied screen shown when user lacks view permission", async () => {
    setGrants();
    await act(async () => {
      render(<UsersView />);
    });
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});

// ── RolesView tests (Critical — AppCommandBar caller) ─────────────────────────

describe("RolesView — AppCommandBar integration (Critical)", () => {
  beforeEach(() => {
    setGrants(
      "Platform.Administration.Roles",
      "Platform.Administration.Roles.Create",
      "Platform.Administration.Roles.Update",
      "Platform.Administration.Roles.Delete",
      "Platform.Administration.Roles.BulkDelete",
    );
  });

  it("renders the new-style h-10 command bar when user has create permission", async () => {
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<RolesView />));
    });
    const bar = container.querySelector(".h-10");
    expect(bar).not.toBeNull();
  });

  it("does not render the old-style h-12 bar (flag-off path removed)", async () => {
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<RolesView />));
    });
    expect(container.querySelector(".h-12")).toBeNull();
  });

  it("shows NewRole primary action in command bar when canCreate is true", async () => {
    await act(async () => {
      render(<RolesView />);
    });
    expect(screen.getByText("Administration:NewRole")).toBeInTheDocument();
  });

  it("command bar is absent when user has view permission but no create or bulk-delete permission", async () => {
    setGrants("Platform.Administration.Roles");
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<RolesView />));
    });
    expect(container.querySelector(".h-10")).toBeNull();
    expect(container.querySelector(".h-12")).toBeNull();
  });

  it("permission-denied screen shown when user lacks view permission", async () => {
    setGrants();
    await act(async () => {
      render(<RolesView />);
    });
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
  });
});

// ── OrganizationUnitsView tests (Critical — permission-gated AppCommandBar caller) ─

describe("OrganizationUnitsView — AppCommandBar integration (Critical)", () => {
  beforeEach(() => {
    setGrants(
      "Platform.Administration.OrganizationUnits",
      "Platform.Administration.OrganizationUnits.Create",
      "Platform.Administration.OrganizationUnits.Update",
      "Platform.Administration.OrganizationUnits.Delete",
      "Platform.Administration.OrganizationUnits.ManageMembers",
    );
  });

  it("shows the New organization unit primary command when the user may create", async () => {
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<OrganizationUnitsView />));
    });
    expect(container.querySelector(".h-10")).not.toBeNull();
    expect(screen.getByText("Administration:NewOrganizationUnit")).toBeInTheDocument();
    expect(screen.getByTestId("mock-grid")).toBeInTheDocument();
  });

  it("hides the command bar without the create permission (UI-PERM-002: hidden, never disabled)", async () => {
    setGrants("Platform.Administration.OrganizationUnits");
    let container!: HTMLElement;
    await act(async () => {
      ({ container } = render(<OrganizationUnitsView />));
    });
    expect(container.querySelector(".h-10")).toBeNull();
    expect(screen.queryByText("Administration:NewOrganizationUnit")).not.toBeInTheDocument();
    expect(screen.getByTestId("mock-grid")).toBeInTheDocument();
  });

  it("shows the permission-denied panel without the view permission", async () => {
    setGrants();
    await act(async () => {
      render(<OrganizationUnitsView />);
    });
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(screen.queryByTestId("mock-grid")).not.toBeInTheDocument();
  });
});
