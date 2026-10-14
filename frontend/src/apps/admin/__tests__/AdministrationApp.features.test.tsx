import { render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Administration index redirects on a tenant (PO 2026-10-14, "App features"): bare /admin and /admin/logbook go to
 * the first view that is on. Risk class: High (feature gating of every screen). */

const navigateMock = vi.fn();
let pathname = "/admin";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  useNavigate: () => navigateMock,
}));

let values: Record<string, string> = {};
let tenantId: string | null = "t-1";
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: tenantId }, features: { values }, auth: { grantedPolicies: {} } } }),
}));

vi.mock("@/apps/admin/users/UsersView", () => ({ UsersView: () => null }));
vi.mock("@/apps/admin/roles/RolesView", () => ({ RolesView: () => null }));
vi.mock("@/apps/admin/organization-units/OrganizationUnitsView", () => ({ OrganizationUnitsView: () => null }));
vi.mock("@/apps/admin/logbook/FunctionalLogbookView", () => ({ FunctionalLogbookView: () => null }));
vi.mock("@/apps/admin/logbook/TechnicalLogbookView", () => ({ TechnicalLogbookView: () => null }));
vi.mock("@/apps/admin/maintenance/MaintenanceView", () => ({ MaintenanceView: () => null }));

import { AdministrationApp } from "@/apps/admin/index";

const ON = {
  "Apps.Administration": "true",
  "Apps.Administration.Users": "true",
  "Apps.Administration.Roles": "true",
  "Apps.Administration.OrganizationUnits": "true",
  "Apps.Administration.AuditLogs": "true",
  "Apps.Administration.AuditLogs.FunctionalLogbook": "true",
  "Apps.Administration.AuditLogs.TechnicalLogbook": "true",
};

beforeEach(() => {
  navigateMock.mockReset();
  values = { ...ON };
  tenantId = "t-1";
});

const go = (path: string) => {
  pathname = path;
  render(<AdministrationApp />);
};

describe("AdministrationApp – redirects follow the features on a tenant", () => {
  it("bare /admin goes to Roles when Users is off", () => {
    values = { ...ON, "Apps.Administration.Users": "false" };
    go("/admin");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/roles", replace: true });
  });

  it("bare /admin/logbook goes to the technical logbook when the functional one is off", () => {
    values = { ...ON, "Apps.Administration.AuditLogs.FunctionalLogbook": "false" };
    go("/admin/logbook");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/logbook/technical", replace: true });
  });

  it("does not navigate when every view is off", () => {
    values = { "Apps.Administration": "true" };
    go("/admin");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("on the host goes to Users whatever the values", () => {
    tenantId = null;
    values = {};
    go("/admin");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/users", replace: true });
  });
});
