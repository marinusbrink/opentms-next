import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The nav pane shows only the nodes whose feature chain is on (PO 2026-10-14, "App features") and, in the Tenants
 * app, only the views the user may see (PO 2026-10-14: Editions merged into Tenants). Real apps.config and real
 * feature checker. Risk class: High (feature gating of every screen). */

let pathname = "/master-data/countries";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  Link: ({ to, children, "aria-label": ariaLabel }: { to: string; children?: ReactNode; "aria-label"?: string }) => (
    <a href={to} aria-label={ariaLabel}>
      {children}
    </a>
  ),
}));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/components/ui/tooltip", () => ({
  TooltipProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  TooltipTrigger: ({ children, render: renderProp }: { children?: ReactNode; render?: React.ReactElement }) =>
    renderProp != null ? renderProp : <>{children}</>,
  TooltipContent: () => null,
}));

let config: unknown;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: config }),
}));

import { APPS } from "@/app/apps.config";
import { AppNavPane } from "@/app/shell/AppNavPane";

const masterData = APPS.find((a) => a.id === "master-data")!;
const admin = APPS.find((a) => a.id === "admin")!;
const tenants = APPS.find((a) => a.id === "tenants")!;

const tenantConfig = (values: Record<string, string>) => ({ currentTenant: { id: "t-1" }, features: { values }, auth: { grantedPolicies: {} } });
const hostConfig = (grants: Record<string, boolean>) => ({ currentTenant: { id: null }, features: { values: {} }, auth: { grantedPolicies: grants } });

const renderPane = (app = masterData) =>
  render(<AppNavPane app={app} collapsed={false} onToggleCollapsed={() => {}} />);

const topLevelLabels = () =>
  within(screen.getByRole("navigation"))
    .getAllByRole("listitem")
    .filter((li) => li.parentElement?.parentElement?.tagName === "NAV")
    .map((li) => li.querySelector("span")?.textContent);

beforeEach(() => {
  pathname = "/master-data/countries";
});

describe("AppNavPane – app features", () => {
  it("shows only the groups and lists that are on (an open group shows only its enabled children)", () => {
    config = tenantConfig({
      "Apps.MasterData": "true",
      "Apps.MasterData.Reference": "true",
      "Apps.MasterData.Reference.Countries": "true",
      "Apps.MasterData.Reference.Currencies": "false",
      "Apps.MasterData.Fleet": "true",
      "Apps.MasterData.Fleet.Drivers": "false",
    });
    renderPane();

    expect(topLevelLabels()).toEqual(["MasterData:Nav.Reference"]);
    /* The group's path is not the list's, so the group starts closed; open it. */
    fireEvent.click(screen.getByRole("button", { name: /MasterData:Nav.Reference/ }));
    expect(screen.getByRole("link", { name: /MasterData:Countries/ })).toHaveAttribute("href", "/master-data/countries");
    expect(screen.queryByText("MasterData:Currencies")).toBeNull();
    expect(screen.queryByText("MasterData:GeographicalRegions")).toBeNull();
    expect(screen.queryByText("MasterData:Nav.Fleet")).toBeNull();
  });

  it("hides a list whose group is off even when the list itself is on", () => {
    config = tenantConfig({
      "Apps.MasterData": "true",
      "Apps.MasterData.Reference": "false",
      "Apps.MasterData.Reference.Countries": "true",
      "Apps.MasterData.Transport": "true",
      "Apps.MasterData.Transport.Products": "true",
    });
    renderPane();

    expect(topLevelLabels()).toEqual(["MasterData:Nav.Transport"]);
  });

  it("hides the Administration nodes that are off on a tenant", () => {
    pathname = "/admin/users";
    config = tenantConfig({
      "Apps.Administration": "true",
      "Apps.Administration.Users": "true",
      "Apps.Administration.Roles": "false",
      "Apps.Administration.OrganizationUnits": "true",
      "Apps.Administration.AuditLogs": "false",
      "Apps.Administration.AuditLogs.FunctionalLogbook": "true",
      "Apps.Administration.Maintenance": "false",
    });
    renderPane(admin);

    expect(topLevelLabels()).toEqual(["Administration:Users", "Administration:OrganizationUnits"]);
  });

  it("shows every Administration node on the host, whatever the values", () => {
    pathname = "/admin/users";
    config = hostConfig({});
    renderPane(admin);

    expect(topLevelLabels()).toEqual([
      "Administration:Users",
      "Administration:Roles",
      "Administration:OrganizationUnits",
      "Administration:AuditLogs",
      "Administration:Maintenance",
    ]);
  });
});

describe("AppNavPane – Tenants app views by permission", () => {
  beforeEach(() => {
    pathname = "/tenants/list";
  });

  it("shows Tenants and Editions to a host user with both permissions", () => {
    config = hostConfig({ "Platform.Tenants": true, "Platform.Editions": true });
    renderPane(tenants);
    expect(topLevelLabels()).toEqual(["Tenants:Nav.Tenants", "Tenants:Nav.Editions"]);
    expect(screen.getByRole("link", { name: /Tenants:Nav.Editions/ })).toHaveAttribute("href", "/tenants/editions");
  });

  it("hides the Editions entry without Platform.Editions", () => {
    config = hostConfig({ "Platform.Tenants": true });
    renderPane(tenants);
    expect(topLevelLabels()).toEqual(["Tenants:Nav.Tenants"]);
    expect(screen.queryByText("Tenants:Nav.Editions")).toBeNull();
  });

  it("hides the Tenants entry without Platform.Tenants", () => {
    config = hostConfig({ "Platform.Editions": true });
    renderPane(tenants);
    expect(topLevelLabels()).toEqual(["Tenants:Nav.Editions"]);
  });
});
