import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

// ── Mocks ─────────────────────────────────────────────────────────────────────

const navigateMock = vi.fn();

vi.mock("@tanstack/react-router", () => ({
  useLocation: vi.fn(() => ({ pathname: "/admin/users" })),
  useNavigate: () => navigateMock,
}));

vi.mock("@/apps/admin/users/UsersView", () => ({
  UsersView: () => <div data-testid="users-view">UsersView</div>,
}));

vi.mock("@/apps/admin/roles/RolesView", () => ({
  RolesView: () => <div data-testid="roles-view">RolesView</div>,
}));

vi.mock("@/apps/admin/organization-units/OrganizationUnitsView", () => ({
  OrganizationUnitsView: () => <div data-testid="organization-units-view">OrganizationUnitsView</div>,
}));

vi.mock("@/apps/admin/logbook/FunctionalLogbookView", () => ({
  FunctionalLogbookView: () => <div data-testid="functional-logbook-view">FunctionalLogbookView</div>,
}));

vi.mock("@/apps/admin/logbook/TechnicalLogbookView", () => ({
  TechnicalLogbookView: () => <div data-testid="technical-logbook-view">TechnicalLogbookView</div>,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

import { useLocation } from "@tanstack/react-router";
import { AdministrationApp } from "@/apps/admin/index";

function setPathname(pathname: string) {
  (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname });
  navigateMock.mockReset();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AdministrationApp – tab removal (medium risk)", () => {
  it("renders UsersView when pathname is /admin/users", () => {
    setPathname("/admin/users");
    render(<AdministrationApp />);
    expect(screen.getByTestId("users-view")).toBeInTheDocument();
    expect(screen.queryByTestId("roles-view")).not.toBeInTheDocument();
  });

  it("renders RolesView when pathname is /admin/roles", () => {
    setPathname("/admin/roles");
    render(<AdministrationApp />);
    expect(screen.getByTestId("roles-view")).toBeInTheDocument();
    expect(screen.queryByTestId("users-view")).not.toBeInTheDocument();
  });

  it("renders OrganizationUnitsView when pathname is /admin/organization-units", () => {
    setPathname("/admin/organization-units");
    render(<AdministrationApp />);
    expect(screen.getByTestId("organization-units-view")).toBeInTheDocument();
    expect(screen.queryByTestId("users-view")).not.toBeInTheDocument();
    expect(screen.queryByTestId("roles-view")).not.toBeInTheDocument();
  });

  it("renders FunctionalLogbookView when pathname is /admin/logbook/functional", () => {
    setPathname("/admin/logbook/functional");
    render(<AdministrationApp />);
    expect(screen.getByTestId("functional-logbook-view")).toBeInTheDocument();
    expect(screen.queryByTestId("technical-logbook-view")).not.toBeInTheDocument();
    expect(screen.queryByTestId("users-view")).not.toBeInTheDocument();
  });

  it("renders TechnicalLogbookView when pathname is /admin/logbook/technical", () => {
    setPathname("/admin/logbook/technical");
    render(<AdministrationApp />);
    expect(screen.getByTestId("technical-logbook-view")).toBeInTheDocument();
    expect(screen.queryByTestId("functional-logbook-view")).not.toBeInTheDocument();
  });

  it("navigates bare /admin/logbook to the functional logbook", () => {
    setPathname("/admin/logbook");
    render(<AdministrationApp />);
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/logbook/functional", replace: true });
  });

  it("renders UsersView for any non-roles pathname under /admin", () => {
    setPathname("/admin/something-else");
    render(<AdministrationApp />);
    expect(screen.getByTestId("users-view")).toBeInTheDocument();
    expect(screen.queryByTestId("roles-view")).not.toBeInTheDocument();
  });

  it("navigates to /admin/users when pathname is exactly /admin", () => {
    setPathname("/admin");
    render(<AdministrationApp />);
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/users", replace: true });
  });

  it("navigates to /admin/users when pathname is /admin/ (trailing slash)", () => {
    setPathname("/admin/");
    render(<AdministrationApp />);
    expect(navigateMock).toHaveBeenCalledWith({ to: "/admin/users", replace: true });
  });

  it("does not navigate when pathname is already /admin/users", () => {
    setPathname("/admin/users");
    render(<AdministrationApp />);
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("does not render any tab navigation bar", () => {
    setPathname("/admin/users");
    const { container } = render(<AdministrationApp />);
    // The previous tab bar had border-b-2 styled links. None should remain.
    const borderLinks = container.querySelectorAll("a.border-b-2");
    expect(borderLinks).toHaveLength(0);
  });

  it("does not render a nav element inside the admin component itself", () => {
    setPathname("/admin/users");
    const { container } = render(<AdministrationApp />);
    // Navigation is now the shell's responsibility (AppNavPane), not the admin component's
    const navElements = container.querySelectorAll("nav");
    expect(navElements).toHaveLength(0);
  });
});
