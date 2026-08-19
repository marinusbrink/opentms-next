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
    <div data-testid="tooltip-content" data-label={typeof children === "string" ? children : ""}></div>
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

    it("active leaf entry in expanded mode has bg-brand fill class", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const activeLink = screen.getByRole("link", { name: /Administration:Users/ });
      expect(activeLink.className).toContain("bg-brand");
    });

    it("active leaf entry in collapsed mode has bg-brand fill class on the icon container link", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/users" });
      render(<AppNavPane app={ADMIN_APP} collapsed={true} onToggleCollapsed={vi.fn()} />);
      const nav = screen.getByRole("navigation");
      const activeLink = nav.querySelector('ul li a[aria-current="page"]');
      expect(activeLink).not.toBeNull();
      expect((activeLink as HTMLElement).className).toContain("bg-brand");
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
      const tooltipLabels = tooltips.map((el) => el.getAttribute("data-label"));
      expect(tooltipLabels).toContain("Administration:Users");
      expect(tooltipLabels).toContain("Administration:Roles");
    });

    it("active entry with children: button does not carry aria-current (not a page link)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/parent" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // Category buttons are not page links — aria-current="page" would be incorrect
      const parentButton = screen.getByRole("button", { name: "ParentView" });
      expect(parentButton).not.toHaveAttribute("aria-current");
    });

    it("no span[aria-hidden] is present anywhere in the pane (accent bar removed entirely)", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/parent" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // The accent bar <span aria-hidden> was removed entirely in this PBI; no entry should render one.
      const nav = screen.getByRole("navigation");
      const anyAriaHiddenSpan = nav.querySelector("span[aria-hidden]");
      expect(anyAriaHiddenSpan).not.toBeInTheDocument();
    });

    it("dark-mode: nav does not carry bg-[#F8F9FA] as an inline style (dark surface token applies via CSS class)", () => {
      const { container } = render(
        <div className="dark">
          <AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />
        </div>,
      );
      const nav = container.querySelector("nav");
      expect(nav).not.toBeNull();
      // The dark variant must be a CSS class (dark:bg-background), not an inline style.
      const styleAttr = (nav as HTMLElement).getAttribute("style") ?? "";
      expect(styleAttr).not.toContain("F8F9FA");
      expect((nav as HTMLElement).className).toContain("dark:bg-background");
    });
  });

  // ── Medium: expanded mode tooltip and label styling ───────────────────────

  describe("expanded mode tooltips and label styling (medium risk)", () => {
    it("expanded mode: leaf entry label span carries the truncate class", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const usersLabel = screen.getByText("Administration:Users");
      expect(usersLabel.className).toContain("truncate");
    });

    it("expanded mode: parent entry label span carries the truncate class", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const parentLabel = screen.getByText("ParentView");
      expect(parentLabel.className).toContain("truncate");
    });

    it("expanded mode: tooltip content is present for leaf entries", () => {
      render(<AppNavPane app={ADMIN_APP} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const tooltips = screen.getAllByTestId("tooltip-content");
      const tooltipLabels = tooltips.map((el) => el.getAttribute("data-label"));
      expect(tooltipLabels).toContain("Administration:Users");
      expect(tooltipLabels).toContain("Administration:Roles");
    });

    it("expanded mode: tooltip content is present for parent (children) entries", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/other" });
      render(<AppNavPane app={APP_WITH_CHILDREN} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const tooltips = screen.getAllByTestId("tooltip-content");
      const tooltipLabels = tooltips.map((el) => el.getAttribute("data-label"));
      expect(tooltipLabels).toContain("ParentView");
    });
  });

  // ── Critical: isActive path-segment boundary ───────────────────────────────
  //
  // BUG FINDING: NavEntry uses pathname.startsWith(view.path) without a trailing-
  // slash / exact-match boundary guard.  findAppByPath already uses the correct
  // pattern (pathname === app.path || pathname.startsWith(`${app.path}/`)) to
  // avoid matching /administrators for /admin.  NavEntry has the same responsibility
  // but lacks the guard, so a view with path "/admin/users" incorrectly marks itself
  // active when the current pathname is "/admin/usersettings".
  //
  // Risk class: critical — AppNavPane is a horizontal shell component; the bug
  // silently produces wrong active state for any app that later adds views whose
  // paths share a string prefix (e.g. Transport: /transport/order, /transport/orders).

  describe("isActive path-segment boundary (critical risk — bug finding)", () => {
    const APP_AMBIGUOUS: AppDefinition = {
      ...ADMIN_APP,
      views: [
        {
          nameKey: "UsersView",
          path: "/admin/users",
          icon: MockIcon as unknown as AppView["icon"],
        },
        {
          nameKey: "UserSettingsView",
          path: "/admin/usersettings",
          icon: MockIcon as unknown as AppView["icon"],
        },
      ],
    };

    it("isActive does not fire for a view whose path is a string-prefix but not a path-segment prefix of the current pathname", () => {
      // pathname "/admin/usersettings" must NOT activate the "/admin/users" view.
      // Current implementation: pathname.startsWith("/admin/users") === true → WRONG.
      // Correct:  pathname === "/admin/users" || pathname.startsWith("/admin/users/") → false.
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/usersettings" });
      render(<AppNavPane app={APP_AMBIGUOUS} collapsed={false} onToggleCollapsed={vi.fn()} />);
      const usersLink = screen.getByRole("link", { name: /UsersView/ });
      expect(usersLink).not.toHaveAttribute("aria-current", "page");
    });

    it("isActive does not show accent bar for a view falsely matched by string-prefix", () => {
      (useLocation as ReturnType<typeof vi.fn>).mockReturnValue({ pathname: "/admin/usersettings" });
      render(<AppNavPane app={APP_AMBIGUOUS} collapsed={false} onToggleCollapsed={vi.fn()} />);
      // Only the UserSettingsView entry (exact match) should be active; no accent bar
      // for UsersView since it is not the active route.
      const nav = screen.getByRole("navigation");
      // Count links with aria-current="page": must be exactly one (UserSettingsView),
      // not two (which would happen with the current startsWith bug).
      const activeLinks = Array.from(nav.querySelectorAll('a[aria-current="page"]'));
      expect(activeLinks).toHaveLength(1);
      const activeLink = activeLinks[0];
      expect(activeLink).toHaveAttribute("href", "/admin/usersettings");
    });
  });
});
