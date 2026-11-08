import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* Design 74 §1: the Planboard shows no left panel; its views stay, because they are the feature tree and the source of the
 * pane registry. Any other app keeps its pane. Uses the real apps.config.ts. Risk class: Medium. */

let pathname = "/planboard";
vi.mock("react-oidc-context", () => ({ useAuth: () => ({ isLoading: false, isAuthenticated: true, activeNavigator: undefined, signinRedirect: vi.fn() }) }));
vi.mock("@tanstack/react-router", () => ({ useLocation: () => ({ pathname }), Outlet: () => <div data-testid="outlet" /> }));
vi.mock("@/app/shell/WaffleLauncher", () => ({ WaffleLauncher: () => null }));
vi.mock("@/app/shell/ImpersonationBanner", () => ({ ImpersonationBanner: () => null }));
vi.mock("@/app/shell/UserMenu", () => ({ UserMenu: () => null }));
vi.mock("@/app/shell/AppNavPane", () => ({ AppNavPane: ({ app }: { app: { id: string } }) => <nav data-testid="app-nav-pane" data-app={app.id} /> }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));
const FEATURES = Object.fromEntries(
  ["Apps.Planboard", "Apps.Planboard.Trips", "Apps.Planboard.Scheduler", "Apps.Planboard.Orders", "Apps.Planboard.Stops", "Apps.Planboard.Legs", "Apps.Planboard.LegsToSchedule", "Apps.Planboard.Map", "Apps.Planboard.Resources", "Apps.Transport", "Apps.Transport.Orders"].map((name) => [name, "true"]),
);
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, features: { values: FEATURES }, auth: { grantedPolicies: {} } } }) }));

import { APPS, findAppByPath } from "@/app/apps.config";
import { AppShell } from "@/app/shell/AppShell";
import { isPlanboardPaneType, PLANBOARD_PANE_TYPES, PLANBOARD_PANES } from "@/apps/planboard/panes/registry";

beforeEach(() => {
  pathname = "/planboard";
});

describe("AppShell: hideNavPane", () => {
  it("shows no nav pane in the Planboard, and still renders its view full width", () => {
    render(<AppShell />);
    expect(screen.queryByTestId("app-nav-pane")).toBeNull();
    expect(screen.getByRole("main")).toContainElement(screen.getByTestId("outlet"));
  });

  it("the Planboard is the only app that hides its pane", () => {
    expect(APPS.filter((app) => app.hideNavPane).map((app) => app.id)).toEqual(["planboard"]);
  });

  it("another app with views keeps its pane", () => {
    pathname = "/transport/orders";
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane")).toHaveAttribute("data-app", "transport");
  });
});

describe("the Planboard's views stay: they are the feature tree and the pane registry's source", () => {
  const planboard = () => findAppByPath("/planboard")!;

  it("every pane type still has its nav node, with a feature", () => {
    const views = planboard().views ?? [];
    expect(views.length).toBe(PLANBOARD_PANE_TYPES.length);
    for (const type of PLANBOARD_PANE_TYPES) {
      const view = views.find((v) => v.search?.pane === type);
      expect(view, type).toBeDefined();
      expect(view!.feature, type).toMatch(/^Apps\.Planboard\./);
    }
  });

  it("the registry resolves every pane from those nodes", () => {
    for (const type of PLANBOARD_PANE_TYPES) {
      const pane = PLANBOARD_PANES[type];
      const view = planboard().views!.find((v) => v.search?.pane === type)!;
      expect(pane).toMatchObject({ type, feature: view.feature, titleKey: view.nameKey, icon: view.icon, multiple: type === "scheduler" });
      expect(pane.component).toBeDefined();
      expect(isPlanboardPaneType(type)).toBe(true);
    }
    expect(isPlanboardPaneType("nav-pane")).toBe(false);
  });
});
