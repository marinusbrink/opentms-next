import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import type { AppDefinition, AppView } from "@/app/apps.config";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@tanstack/react-router", () => ({
  useLocation: vi.fn(() => ({ pathname: "/admin/users" })),
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  Link: ({ to, children, "aria-current": ariaCurrent, "aria-label": ariaLabel, className }: any) => (
    <a href={to} aria-current={ariaCurrent} aria-label={ariaLabel} className={className}>
      {children}
    </a>
  ),
}));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/components/ui/tooltip", () => ({
  TooltipProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  Tooltip: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  TooltipTrigger: ({
    children,
    render: renderProp,
  }: {
    children?: React.ReactNode;
    render?: React.ReactElement;
  }) => (renderProp != null ? renderProp : <>{children}</>),
  TooltipContent: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="tooltip-content">{children}</div>
  ),
}));

// ── Fixtures ──────────────────────────────────────────────────────────────────

const MockIcon = () => <svg data-testid="mock-icon" />;

const ADMIN_APP: AppDefinition = {
  id: "admin",
  nameKey: "App:Administration",
  path: "/admin",
  domains: ["Platform"],
  icon: MockIcon as unknown as AppDefinition["icon"],
  tileClass: "bg-gray-700",
  views: [
    { nameKey: "Administration:Users", path: "/admin/users", icon: MockIcon as unknown as AppView["icon"] },
    { nameKey: "Administration:Roles", path: "/admin/roles", icon: MockIcon as unknown as AppView["icon"] },
  ],
};

const APP_WITH_CHILDREN: AppDefinition = {
  ...ADMIN_APP,
  views: [
    {
      nameKey: "ParentView",
      path: "/admin/parent",
      icon: MockIcon as unknown as AppView["icon"],
      children: [
        {
          nameKey: "ChildView",
          path: "/admin/parent/child",
          icon: MockIcon as unknown as AppView["icon"],
        },
      ],
    },
  ],
};

// ── Viewport helpers ───────────────────────────────────────────────────────────

function setupMatchMedia(matches: boolean) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: vi.fn().mockReturnValue({
      matches,
      media: "",
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
}

function setupWideViewport() {
  Object.defineProperty(window, "innerWidth", { writable: true, configurable: true, value: 1280 });
  setupMatchMedia(false);
}

function setupNarrowViewport() {
  Object.defineProperty(window, "innerWidth", { writable: true, configurable: true, value: 375 });
  setupMatchMedia(true);
}

// ── Imports ────────────────────────────────────────────────────────────────────

import { useLocation } from "@tanstack/react-router";
import { AppNavPane } from "@/app/shell/AppNavPane";

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AppNavPane (critical + high risk)", () => {
  beforeEach(() => {
    setupWideViewport();
    (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
  });

  // ── Critical: nav element ──────────────────────────────────────────────────

  describe("nav element", () => {
    it("renders a <nav> element", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      expect(screen.getByRole("navigation")).toBeInTheDocument();
    });

    it("nav aria-label contains the app name key", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      expect(nav.getAttribute("aria-label")).toContain("App:Administration");
    });

    it("renders one list item per view in app.views", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // Two views → two list items in the main view list
      const listItems = screen.getByRole("navigation").querySelectorAll("ul > li");
      expect(listItems).toHaveLength(2);
    });
  });

  // ── Critical: active state ─────────────────────────────────────────────────

  describe("active state", () => {
    it("active entry has aria-current=page when pathname starts with view.path", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const activeLink = screen.getByRole("link", { name: /Administration:Users/ });
      expect(activeLink).toHaveAttribute("aria-current", "page");
    });

    it("inactive entry does not have aria-current", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const inactiveLink = screen.getByRole("link", { name: /Administration:Roles/ });
      expect(inactiveLink).not.toHaveAttribute("aria-current");
    });

    it("active entry in expanded mode has the accent bar (aria-hidden span)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // The active accent bar is a span with aria-hidden and bg-primary class
      const accentBar = screen
        .getByRole("navigation")
        .querySelector('span[aria-hidden][class*="bg-primary"]');
      expect(accentBar).toBeInTheDocument();
    });
  });

  // ── Critical: expanded state labels ───────────────────────────────────────

  describe("expanded state", () => {
    it("shows label text for each view entry in expanded mode", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      expect(screen.getByText("Administration:Users")).toBeInTheDocument();
      expect(screen.getByText("Administration:Roles")).toBeInTheDocument();
    });

    it("labels are rendered in <span> elements in expanded mode", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const usersLabel = screen.getByText("Administration:Users");
      expect(usersLabel.tagName).toBe("SPAN");
    });

    it("entry without children renders as a link (<a>)", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const usersLink = screen.getByRole("link", { name: /Administration:Users/ });
      expect(usersLink.tagName).toBe("A");
      expect(usersLink).toHaveAttribute("href", "/admin/users");
    });
  });

  // ── Critical: toggle button ────────────────────────────────────────────────

  describe("collapse/expand toggle", () => {
    it("expanded: toggle button has aria-label Shell:NavCollapse", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      expect(screen.getByRole("button", { name: "Shell:NavCollapse" })).toBeInTheDocument();
    });

    it("collapsed: toggle button has aria-label Shell:NavExpand", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      expect(screen.getByRole("button", { name: "Shell:NavExpand" })).toBeInTheDocument();
    });

    it("clicking toggle button calls onToggleCollapsed", () => {
      const onToggle = vi.fn();
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={onToggle} />);
      fireEvent.click(screen.getByRole("button", { name: "Shell:NavCollapse" }));
      expect(onToggle).toHaveBeenCalledTimes(1);
    });
  });

  // ── Critical: entries with children ───────────────────────────────────────

  describe("entries with children", () => {
    it("expanded: entry with children renders as a button (not a link)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const parentButton = screen.getByRole("button", { name: "ParentView" });
      expect(parentButton).toBeInTheDocument();
    });

    it("expanded: clicking entry with children opens sub-list", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);

      expect(screen.queryByText("ChildView")).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "ParentView" }));
      expect(screen.getByText("ChildView")).toBeInTheDocument();
    });

    it("expanded: clicking entry with children twice collapses sub-list again", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "ParentView" }));
      expect(screen.getByText("ChildView")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "ParentView" }));
      expect(screen.queryByText("ChildView")).not.toBeInTheDocument();
    });

    it("collapsed: clicking entry with children calls onToggleCollapsed (expands the pane)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      const onToggle = vi.fn();
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={true} onToggleCollapsed={onToggle} />);

      // In collapsed (icon-rail) mode, the button is rendered via TooltipTrigger render prop
      // Clicking it calls onToggleCollapsed to expand the pane (per design §UI design)
      // We can find the button by its aria-label
      const parentButton = screen.getByRole("button", { name: "ParentView" });
      fireEvent.click(parentButton);

      expect(onToggle).toHaveBeenCalledTimes(1);
    });

    it("collapsed: clicking entry with children does not toggle sub-list", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={true} onToggleCollapsed={vi.fn()} />);

      const parentButton = screen.getByRole("button", { name: "ParentView" });
      fireEvent.click(parentButton);

      // Children should NOT appear in collapsed mode (pane should expand first)
      expect(screen.queryByText("ChildView")).not.toBeInTheDocument();
    });
  });

  // ── High: layout width classes ─────────────────────────────────────────────

  describe("layout width (high risk)", () => {
    it("collapsed=true: nav has icon-rail width class w-14", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      expect(nav.className).toContain("w-14");
    });

    it("collapsed=false on wide viewport: nav has expanded width class w-[250px]", () => {
      setupWideViewport();
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      expect(nav.className).toContain("w-[250px]");
    });

    it("collapsed=true: pane has icon-rail width w-14 regardless of viewport", () => {
      setupNarrowViewport();
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      expect(nav.className).toContain("w-14");
    });

    it("collapsed=true: toggle button label is Shell:NavExpand", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      expect(screen.getByRole("button", { name: "Shell:NavExpand" })).toBeInTheDocument();
    });

    it("narrow viewport: pane expands when collapsed=false (user opted into expanded view)", () => {
      setupNarrowViewport();
      // AppShell initialises navCollapsed=true on narrow; after user clicks expand,
      // navCollapsed=false is passed down. The pane must honour that and show expanded.
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      expect(nav.className).toContain("w-[250px]");
    });
  });

  // ── Medium: keyboard / a11y ────────────────────────────────────────────────

  describe("keyboard navigation / a11y (medium risk)", () => {
    it("toggle button is reachable via keyboard (is a real button)", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const toggle = screen.getByRole("button", { name: "Shell:NavCollapse" });
      expect(toggle.tagName).toBe("BUTTON");
    });

    it("active link has aria-current=page for screen readers", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/roles" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const activeLink = screen.getByRole("link", { name: /Administration:Roles/ });
      expect(activeLink).toHaveAttribute("aria-current", "page");
    });

    it("inactive link does not have aria-current", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/roles" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const inactiveLink = screen.getByRole("link", { name: /Administration:Users/ });
      expect(inactiveLink).not.toHaveAttribute("aria-current");
    });

    it("collapsed mode: icon-only links have aria-label for screen readers", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      const collapsedLinks = Array.from(nav.querySelectorAll("ul li a"));
      expect(collapsedLinks.length).toBeGreaterThan(0);
      collapsedLinks.forEach((link) => {
        expect(link).toHaveAttribute("aria-label");
      });
    });

    it("collapsed mode: active link still carries aria-current=page for screen readers", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      // Even in icon-rail mode, the active link must carry aria-current for AT users
      const nav = screen.getByRole("navigation");
      const activeLink = nav.querySelector('ul li a[aria-current="page"]');
      expect(activeLink).toBeInTheDocument();
    });

    it("collapsed mode: tooltip content shows the view label for each entry", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      const tooltips = screen.getAllByTestId("tooltip-content");
      const tooltipTexts = tooltips.map((el) => el.textContent);
      expect(tooltipTexts).toContain("Administration:Users");
      expect(tooltipTexts).toContain("Administration:Roles");
    });

    it("active entry with children: button does not carry aria-current (not a page link)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/parent" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // Category buttons are not page links — aria-current="page" would be incorrect
      const parentButton = screen.getByRole("button", { name: "ParentView" });
      expect(parentButton).not.toHaveAttribute("aria-current");
    });

    it("active entry with children: no accent bar rendered (parent category stays visually neutral)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/parent" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // Design: accent bar is for leaf entries only (isActive && !hasChildren);
      // a parent-category button active state is conveyed via font-medium class, not the bar
      const nav = screen.getByRole("navigation");
      const accentBar = nav.querySelector('span[aria-hidden][class*="bg-primary"]');
      expect(accentBar).not.toBeInTheDocument();
    });
  });
});
