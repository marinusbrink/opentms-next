import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The Tenants app (host only; PO 2026-10-14: Editions merged into it): two views by pathname, and bare /tenants goes to
 * the first view the user may see — Tenants, else Editions. Risk class: Medium (UI flow) on a Critical boundary
 * (host-only administration; the backend refuses regardless). */

const navigateMock = vi.fn();
let pathname = "/tenants";
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname }),
  useNavigate: () => navigateMock,
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: null }, auth: { grantedPolicies: grants }, features: { values: {} } } }),
}));

vi.mock("@/apps/tenants/TenantsView", () => ({ TenantsView: () => <div data-testid="tenants-view" /> }));
vi.mock("@/apps/tenants/EditionsView", () => ({ EditionsView: () => <div data-testid="editions-view" /> }));

import { TenantsApp } from "@/apps/tenants/index";

beforeEach(() => {
  navigateMock.mockReset();
  grants = {};
});

const go = (path: string) => {
  pathname = path;
  render(<TenantsApp />);
};

describe("TenantsApp", () => {
  it("redirects bare /tenants to /tenants/list with Platform.Tenants", () => {
    grants = { "Platform.Tenants": true, "Platform.Editions": true };
    go("/tenants");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/tenants/list", replace: true });
  });

  it("redirects bare /tenants/ to /tenants/editions with only Platform.Editions", () => {
    grants = { "Platform.Editions": true };
    go("/tenants/");
    expect(navigateMock).toHaveBeenCalledWith({ to: "/tenants/editions", replace: true });
  });

  it("does not redirect without either permission", () => {
    go("/tenants");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("does not redirect from a view path", () => {
    grants = { "Platform.Tenants": true };
    go("/tenants/list");
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("renders the Editions view on /tenants/editions and the Tenants view on /tenants/list", () => {
    go("/tenants/editions");
    expect(screen.getByTestId("editions-view")).toBeInTheDocument();
    expect(screen.queryByTestId("tenants-view")).toBeNull();
  });

  it("renders the Tenants view on /tenants/list", () => {
    go("/tenants/list");
    expect(screen.getByTestId("tenants-view")).toBeInTheDocument();
  });
});
