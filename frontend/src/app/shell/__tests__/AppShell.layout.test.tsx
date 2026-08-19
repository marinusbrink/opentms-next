import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("react-oidc-context", () => ({
  useAuth: vi.fn(() => ({
    isLoading: false,
    isAuthenticated: true,
    activeNavigator: undefined,
    signinRedirect: vi.fn(),
  })),
}));

vi.mock("@tanstack/react-router", () => ({
  useLocation: vi.fn(() => ({ pathname: "/admin/users" })),
  Outlet: () => <div data-testid="outlet" />,
}));

vi.mock("@/app/apps.config", async (importOriginal) => {
  const orig = await importOriginal<typeof import("@/app/apps.config")>();
  return { ...orig, findAppByPath: vi.fn() };
});

vi.mock("@/app/shell/WaffleLauncher", () => ({
  WaffleLauncher: () => <div data-testid="waffle-launcher" />,
}));

vi.mock("@/app/shell/UserTenantMenu", () => ({
  UserTenantMenu: () => <div data-testid="user-tenant-menu" />,
}));

vi.mock("@/app/shell/AppNavPane", () => ({
  AppNavPane: ({
    app,
    collapsed,
    onToggleCollapsed,
  }: {
    app: { nameKey: string };
    collapsed: boolean;
    onToggleCollapsed: () => void;
  }) => (
    <div data-testid="app-nav-pane" data-app={app.nameKey} data-collapsed={String(collapsed)}>
      <button data-testid="nav-toggle" onClick={onToggleCollapsed} />
    </div>
  ),
}));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

// ── Imports after mocks ────────────────────────────────────────────────────────

import { useLocation } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { findAppByPath } from "@/app/apps.config";
import type { AppDefinition } from "@/app/apps.config";
import { AppShell } from "@/app/shell/AppShell";

const APP_WITH_VIEWS: AppDefinition = {
  id: "admin",
  nameKey: "App:Administration",
  path: "/admin",
  domains: ["Platform"],
  icon: (() => null) as unknown as AppDefinition["icon"],
  tileClass: "bg-gray-700",
  views: [
    {
      nameKey: "Administration:Users",
      path: "/admin/users",
      icon: (() => null) as unknown as AppDefinition["icon"],
    },
  ],
};

const APP_WITHOUT_VIEWS: AppDefinition = {
  id: "dashboard",
  nameKey: "App:Dashboard",
  path: "/dashboard",
  domains: ["Reporting"],
  icon: (() => null) as unknown as AppDefinition["icon"],
  tileClass: "bg-sky-600",
};

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AppShell – layout change (critical risk)", () => {
  it("renders AppNavPane inside a flex wrapper when activeApp has views", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    render(<AppShell />);

    const navPane = screen.getByTestId("app-nav-pane");
    expect(navPane).toBeInTheDocument();

    // Nav pane and main should share a flex container
    const main = screen.getByRole("main");
    expect(navPane.parentElement).toBe(main.parentElement);
    expect(navPane.parentElement?.className).toContain("flex");
  });

  it("does not render AppNavPane when activeApp has no views", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITHOUT_VIEWS);
    render(<AppShell />);

    expect(screen.queryByTestId("app-nav-pane")).not.toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("does not render AppNavPane when no app matches the current path", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(undefined);
    render(<AppShell />);

    expect(screen.queryByTestId("app-nav-pane")).not.toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("renders the outlet inside main in both layouts", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    render(<AppShell />);
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
    expect(screen.getByRole("main")).toContainElement(screen.getByTestId("outlet"));
  });

  it("renders FullScreenSpinner when not authenticated", () => {
    (useAuth as ReturnType<typeof vi.fn>).mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      activeNavigator: undefined,
      signinRedirect: vi.fn(),
    });
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    render(<AppShell />);

    expect(screen.queryByTestId("app-nav-pane")).not.toBeInTheDocument();
    expect(screen.queryByRole("main")).not.toBeInTheDocument();

    // Restore for subsequent tests
    (useAuth as ReturnType<typeof vi.fn>).mockReturnValue({
      isLoading: false,
      isAuthenticated: true,
      activeNavigator: undefined,
      signinRedirect: vi.fn(),
    });
  });

  it("passes the active app to AppNavPane", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    render(<AppShell />);

    const navPane = screen.getByTestId("app-nav-pane");
    expect(navPane.dataset.app).toBe(APP_WITH_VIEWS.nameKey);
  });

  it("switches from two-zone layout to full-width when navigating to app without views", () => {
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    const { rerender } = render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane")).toBeInTheDocument();

    (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/dashboard" });
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITHOUT_VIEWS);
    rerender(<AppShell />);

    expect(screen.queryByTestId("app-nav-pane")).not.toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });
});

// ── Viewport-driven initialization and toggle (critical risk) ──────────────────

describe("AppShell – navCollapsed initialization and toggle (critical risk)", () => {
  function setInnerWidth(width: number) {
    Object.defineProperty(window, "innerWidth", { writable: true, configurable: true, value: width });
  }

  beforeEach(() => {
    setInnerWidth(1280);
    (findAppByPath as ReturnType<typeof vi.fn>).mockReturnValue(APP_WITH_VIEWS);
    (useAuth as ReturnType<typeof vi.fn>).mockReturnValue({
      isLoading: false,
      isAuthenticated: true,
      activeNavigator: undefined,
      signinRedirect: vi.fn(),
    });
  });

  it("passes collapsed=false to AppNavPane when innerWidth >= 768 (wide viewport)", () => {
    setInnerWidth(1280);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("false");
  });

  it("passes collapsed=true to AppNavPane when innerWidth < 768 (narrow viewport)", () => {
    setInnerWidth(375);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("true");
  });

  it("passes collapsed=true at the md breakpoint boundary (innerWidth === 767)", () => {
    setInnerWidth(767);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("true");
  });

  it("passes collapsed=false at the md breakpoint boundary (innerWidth === 768)", () => {
    setInnerWidth(768);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("false");
  });

  it("onToggleCollapsed flips collapsed from false to true on wide viewport", () => {
    setInnerWidth(1280);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("false");
    fireEvent.click(screen.getByTestId("nav-toggle"));
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("true");
  });

  it("onToggleCollapsed flips collapsed from true to false (narrow viewport, user opts into expanded)", () => {
    setInnerWidth(375);
    render(<AppShell />);
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("true");
    fireEvent.click(screen.getByTestId("nav-toggle"));
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("false");
  });

  it("toggle is idempotent: two clicks return to the original state", () => {
    setInnerWidth(1280);
    render(<AppShell />);
    fireEvent.click(screen.getByTestId("nav-toggle"));
    fireEvent.click(screen.getByTestId("nav-toggle"));
    expect(screen.getByTestId("app-nav-pane").dataset.collapsed).toBe("false");
  });
});
