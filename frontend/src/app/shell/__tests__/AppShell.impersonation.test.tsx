import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* The shell's part of "Log in as" (docs/designs/tenants-and-editions.md, D14): it renders the impersonation banner and,
 * once signed in again after a refused switch, tells the user once. Risk class: Critical — authentication (UI part:
 * Medium). */

let authenticated = true;
vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isLoading: false, isAuthenticated: authenticated, activeNavigator: undefined, signinRedirect: vi.fn() }),
}));
vi.mock("@tanstack/react-router", () => ({
  useLocation: () => ({ pathname: "/admin/users" }),
  Outlet: () => <div data-testid="outlet" />,
}));
vi.mock("@/app/shell/WaffleLauncher", () => ({ WaffleLauncher: () => null }));
vi.mock("@/app/shell/UserMenu", () => ({ UserMenu: () => null }));
vi.mock("@/app/shell/AppNavPane", () => ({ AppNavPane: () => null }));
vi.mock("@/app/shell/ImpersonationBanner", () => ({ ImpersonationBanner: () => <div data-testid="impersonation-banner" /> }));
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: null }, features: { values: {} }, auth: { grantedPolicies: {} } } }),
}));

const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { error: (...args: unknown[]) => toastError(...args), success: vi.fn() }, Toaster: () => null }));
vi.mock("@/components/ui/sonner", () => ({ Toaster: () => null }));

let refused = false;
const takeRefusedSwitch = vi.fn(() => {
  const value = refused;
  refused = false;
  return value;
});
vi.mock("@/auth/session-switch", () => ({ takeRefusedSwitch: () => takeRefusedSwitch(), takeSwitchReturnTo: () => undefined }));

import { AppShell } from "@/app/shell/AppShell";

beforeEach(() => {
  authenticated = true;
  refused = false;
  toastError.mockReset();
  takeRefusedSwitch.mockClear();
});

describe("AppShell – Log in as", () => {
  it("renders the impersonation banner in the signed-in shell", () => {
    render(<AppShell />);
    expect(screen.getByTestId("impersonation-banner")).toBeInTheDocument();
  });

  it("tells the user once after a refused switch", () => {
    refused = true;
    const { rerender } = render(<AppShell />);
    expect(toastError).toHaveBeenCalledExactlyOnceWith("Impersonation:Refused");
    rerender(<AppShell />);
    expect(toastError).toHaveBeenCalledOnce();
  });

  it("does not consume the refusal before the user is signed in again", () => {
    authenticated = false;
    refused = true;
    render(<AppShell />);
    expect(toastError).not.toHaveBeenCalled();
    expect(refused).toBe(true);
  });

  it("shows nothing without a refusal", () => {
    render(<AppShell />);
    expect(toastError).not.toHaveBeenCalled();
  });
});
