import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The app launcher hides apps whose requiredPermission is not granted (tenants-and-editions: the Tenants app, which
 * holds Tenants and Editions since PO 2026-10-14, is host-only; UI-PERM-002: hide, never disable) and apps whose
 * app feature is off or whose navigation nodes are all off ("App features", PO 2026-10-14). Risk class: High
 * (feature gating of every screen) — the backend refuses regardless; this keeps the tiles out of sight. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ to, children, onClick }: { to: string; children: ReactNode; onClick?: () => void }) => (
    <a href={to} onClick={onClick}>
      {children}
    </a>
  ),
}));

let grants: Record<string, boolean> | undefined = {};
let features: Record<string, string> = {};
let tenantId: string | null = null;
let loaded = true;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: loaded
      ? { auth: { grantedPolicies: grants }, features: { values: features }, currentTenant: { id: tenantId } }
      : undefined,
  }),
}));

import { APPS, type AppView } from "@/app/apps.config";
import { WaffleLauncher } from "@/app/shell/WaffleLauncher";

/** Every app and node feature named in apps.config, switched on. */
function allFeaturesOn(): Record<string, string> {
  const values: Record<string, string> = {};
  const add = (feature?: string) => {
    if (feature) values[feature] = "true";
  };
  for (const app of APPS) {
    add(app.feature);
    /* Every depth: Master data is app → domain group → group → list (PO 2026-10-16). */
    const walk = (views: readonly AppView[] | undefined) =>
      views?.forEach((view) => {
        add(view.feature);
        walk(view.children);
      });
    walk(app.views);
  }
  return values;
}

const openLauncher = () => fireEvent.click(screen.getByRole("button", { name: "Shell:AppLauncher" }));
const tileNames = () => screen.queryAllByRole("link").map((link) => link.textContent);

beforeEach(() => {
  grants = {};
  features = allFeaturesOn();
  tenantId = null;
  loaded = true;
});

describe("WaffleLauncher – permissions", () => {
  it("the Tenants app requires either host-side permission and has no Editions app beside it", () => {
    expect(APPS.find((a) => a.id === "tenants")?.requiredPermission).toEqual(["Platform.Tenants", "Platform.Editions"]);
    expect(APPS.find((a) => a.id === "editions")).toBeUndefined();
  });

  it("hides apps whose permission is not granted and shows every app without one", () => {
    render(<WaffleLauncher />);
    openLauncher();

    const names = tileNames();
    expect(names).not.toContain("App:Tenants");
    for (const app of APPS.filter((a) => !a.requiredPermission)) {
      expect(names).toContain(app.nameKey);
    }
  });

  it("shows the Tenants app with only Platform.Tenants", () => {
    grants = { "Platform.Tenants": true };
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).toContain("App:Tenants");
    expect(screen.getByRole("link", { name: /App:Tenants/ })).toHaveAttribute("href", "/tenants");
  });

  it("shows the Tenants app with only Platform.Editions (any of the listed permissions)", () => {
    grants = { "Platform.Editions": true };
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).toContain("App:Tenants");
  });

  it("treats a false grant and missing configuration as not granted", () => {
    grants = { "Platform.Tenants": false, "Platform.Editions": false };
    const { unmount } = render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).not.toContain("App:Tenants");
    unmount();

    grants = undefined;
    render(<WaffleLauncher />);
    openLauncher();
    expect(tileNames()).not.toContain("App:Tenants");
  });
});

describe("WaffleLauncher – app features", () => {
  it("on a tenant with no feature values shows no app at all: every app feature is off", () => {
    tenantId = "t-1";
    features = {};
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).toEqual([]);
  });

  it("on a tenant hides an app whose feature is off and keeps the others", () => {
    tenantId = "t-1";
    features = { ...allFeaturesOn(), "Apps.Dashboard": "false", "Apps.Finance": "false" };
    render(<WaffleLauncher />);
    openLauncher();

    const names = tileNames();
    expect(names).not.toContain("App:Dashboard");
    expect(names).not.toContain("App:Finance");
    expect(names).toContain("App:Planboard");
    expect(names).toContain("App:MasterData");
    expect(names).toContain("App:Administration");
  });

  it("on a tenant hides an app that is on but whose navigation nodes are all off", () => {
    tenantId = "t-1";
    /* General's fifteen lists (Apps.MasterData.General.<Group>.<List>) and Platform's four (Apps.MasterData.Platform.<List>). */
    const masterDataLists = Object.keys(allFeaturesOn()).filter(
      (f) => (f.split(".").length === 5 && f.startsWith("Apps.MasterData.")) || (f.split(".").length === 4 && f.startsWith("Apps.MasterData.Platform.")),
    );
    expect(masterDataLists).toHaveLength(19);
    features = { ...allFeaturesOn(), ...Object.fromEntries(masterDataLists.map((f) => [f, "false"])) };
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).not.toContain("App:MasterData");
    expect(tileNames()).toContain("App:Administration");
  });

  it("on a tenant keeps an app with a single node on", () => {
    tenantId = "t-1";
    features = { "Apps.MasterData": "true", "Apps.MasterData.General": "true", "Apps.MasterData.General.Reference": "true", "Apps.MasterData.General.Reference.Countries": "true" };
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).toEqual(["App:MasterData"]);
  });

  it("on the host shows Administration whatever its feature values (hostAlwaysEnabled) and still hides feature-off apps", () => {
    features = {};
    grants = { "Platform.Tenants": true };
    render(<WaffleLauncher />);
    openLauncher();

    /* The Tenants app has no feature: a host has no tenant or edition to carry one. */
    expect(tileNames()).toEqual(["App:Tenants", "App:Administration"]);
  });

  it("on the host shows Tenants before Administration (PO 2026-10-14)", () => {
    grants = { "Platform.Tenants": true, "Platform.Editions": true };
    render(<WaffleLauncher />);
    openLauncher();

    const names = tileNames();
    expect(names.indexOf("App:Tenants")).toBeGreaterThanOrEqual(0);
    expect(names.indexOf("App:Tenants")).toBeLessThan(names.indexOf("App:Administration"));
    expect(APPS.findIndex((a) => a.id === "tenants")).toBeLessThan(APPS.findIndex((a) => a.id === "admin"));
  });

  it("on a tenant Administration follows its feature like any other app", () => {
    tenantId = "t-1";
    features = { ...allFeaturesOn(), "Apps.Administration": "false" };
    render(<WaffleLauncher />);
    openLauncher();

    expect(tileNames()).not.toContain("App:Administration");
  });
});
