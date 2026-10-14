import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The shell's feature gate (PO 2026-10-14, docs/designs/tenants-and-editions.md, "App features"): a route whose app
 * or node feature is off renders FeatureUnavailable instead of the screen (direct links, old bookmarks), but only
 * once the configuration has loaded. The list views no longer check features themselves, so this is the one place
 * a disabled list is kept off screen. Real apps.config and real feature checker; only the configuration is mocked.
 * Risk class: High (feature gating of every screen). */

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isLoading: false, isAuthenticated: true, activeNavigator: undefined, signinRedirect: vi.fn() }),
}));

let pathname = "/admin/users";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  Outlet: () => <div data-testid="outlet" />,
}));

vi.mock("@/app/shell/WaffleLauncher", () => ({ WaffleLauncher: () => null }));
/* The "Log in as" banner has its own tests (ImpersonationBanner.test.tsx, AppShell.impersonation.test.tsx). */
vi.mock("@/app/shell/ImpersonationBanner", () => ({ ImpersonationBanner: () => null }));

vi.mock("@/app/shell/UserTenantMenu", () => ({ UserTenantMenu: () => null }));
vi.mock("@/app/shell/AppNavPane", () => ({ AppNavPane: () => <nav data-testid="app-nav-pane" /> }));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let config: unknown;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: config }),
}));

import { AppShell } from "@/app/shell/AppShell";

const tenantConfig = (values: Record<string, string>) => ({ currentTenant: { id: "t-1" }, features: { values }, auth: { grantedPolicies: {} } });
const hostConfig = (values: Record<string, string>) => ({ currentTenant: { id: null }, features: { values }, auth: { grantedPolicies: {} } });

const ADMIN_ON = {
  "Apps.Administration": "true",
  "Apps.Administration.Users": "true",
  "Apps.Administration.Roles": "true",
  "Apps.Administration.OrganizationUnits": "true",
  "Apps.Administration.AuditLogs": "true",
  "Apps.Administration.AuditLogs.FunctionalLogbook": "true",
  "Apps.Administration.AuditLogs.TechnicalLogbook": "true",
};

const COUNTRIES_ON = { "Apps.MasterData": "true", "Apps.MasterData.Reference": "true", "Apps.MasterData.Reference.Countries": "true" };

beforeEach(() => {
  pathname = "/admin/users";
  config = tenantConfig(ADMIN_ON);
});

const unavailable = () => screen.queryByText("FeatureUnavailable");

describe("AppShell – app features", () => {
  it("renders the screen for a route whose chain is on", () => {
    render(<AppShell />);
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
    expect(unavailable()).toBeNull();
  });

  it("renders FeatureUnavailable instead of the screen for a node that is off", () => {
    pathname = "/admin/maintenance";
    render(<AppShell />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    expect(screen.queryByTestId("outlet")).toBeNull();
  });

  it("renders FeatureUnavailable for every screen of an app that is off, and shows no nav pane for it", () => {
    pathname = "/admin/roles";
    config = tenantConfig({ ...ADMIN_ON, "Apps.Administration": "false" });
    render(<AppShell />);
    expect(unavailable()).toBeInTheDocument();
    expect(screen.queryByTestId("outlet")).toBeNull();
    expect(screen.queryByTestId("app-nav-pane")).toBeNull();
  });

  it("renders FeatureUnavailable for a placeholder app whose feature is off (the default)", () => {
    pathname = "/dashboard";
    render(<AppShell />);
    expect(unavailable()).toBeInTheDocument();
  });

  it("does not show FeatureUnavailable while the configuration is still loading", () => {
    pathname = "/admin/maintenance";
    config = undefined;
    render(<AppShell />);
    expect(unavailable()).toBeNull();
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("never gates the host's Administration screens, whatever the values", () => {
    pathname = "/admin/maintenance";
    config = hostConfig({});
    render(<AppShell />);
    expect(unavailable()).toBeNull();
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("does not gate a route outside every app, nor the host-only Tenants app", () => {
    pathname = "/profile";
    config = tenantConfig({});
    const { unmount } = render(<AppShell />);
    expect(unavailable()).toBeNull();
    unmount();

    pathname = "/tenants/editions";
    config = hostConfig({});
    render(<AppShell />);
    expect(unavailable()).toBeNull();
  });

  it("renders a Master data list whose node is on", () => {
    pathname = "/master-data/countries";
    config = tenantConfig(COUNTRIES_ON);
    render(<AppShell />);
    expect(unavailable()).toBeNull();
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("renders FeatureUnavailable for a Master data list whose node is off (moved here from the list views)", () => {
    /* Before PO 2026-10-14 the list view itself showed PermissionDenied while MasterData.FirstRun was off. */
    pathname = "/master-data/currencies";
    config = tenantConfig(COUNTRIES_ON);
    render(<AppShell />);
    expect(unavailable()).toBeInTheDocument();
    expect(screen.queryByTestId("outlet")).toBeNull();
  });

  it("renders FeatureUnavailable for the Relations list while its node is off (formerly MasterData.Relations)", () => {
    pathname = "/master-data/relations";
    config = tenantConfig({ ...COUNTRIES_ON, "Apps.MasterData.Relations": "true", "Apps.MasterData.Relations.Relations": "false" });
    render(<AppShell />);
    expect(unavailable()).toBeInTheDocument();
  });
});
