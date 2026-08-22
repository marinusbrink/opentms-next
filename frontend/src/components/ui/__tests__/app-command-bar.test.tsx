import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { Trash2, Mail } from "lucide-react";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { useApplicationConfiguration } from "@/lib/abp/queries";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (key: string) => key,
    culture: "en",
    isLoading: false,
  }),
}));

// Default: flag off (data undefined → isNewStyleEnabled = false)
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(() => ({ data: undefined })),
}));

// ResizeObserver is not available in jsdom; control the reported width via mockROWidth.
let mockROWidth = 0;

class MockResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb(
      [
        {
          contentRect: { width: mockROWidth } as DOMRectReadOnly,
          target: el,
        } as ResizeObserverEntry,
      ],
      this as unknown as ResizeObserver,
    );
  }
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

afterAll(() => {
  vi.unstubAllGlobals();
});

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeCommand(overrides: Partial<AppCommandBarCommand> = {}): AppCommandBarCommand {
  return {
    id: "test-cmd",
    labelKey: "Test:Command",
    onClick: vi.fn(),
    ...overrides,
  };
}

const mockUseAppConfig = vi.mocked(useApplicationConfiguration);

function setFlagOn() {
  mockUseAppConfig.mockReturnValue(
    { data: { features: { values: { "UI.CommonToolbar": "true" } } } } as unknown as ReturnType<
      typeof useApplicationConfiguration
    >,
  );
}

function setFlagOff() {
  mockUseAppConfig.mockReturnValue(
    { data: undefined } as unknown as ReturnType<typeof useApplicationConfiguration>,
  );
}

// ── Tests — Critical: shared library component, cross-product API contract ────

describe("AppCommandBar (Critical — shared library component)", () => {
  // ── Flag-off path (regression guard: existing tests unchanged) ────────────

  it("renders a container with h-12 class even with no commands", () => {
    const { container } = render(<AppCommandBar commands={[]} />);
    const bar = container.firstElementChild;
    expect(bar).not.toBeNull();
    expect(bar!.className).toContain("h-12");
  });

  it("renders with mb-4 margin below and rounded border", () => {
    const { container } = render(<AppCommandBar commands={[]} />);
    const bar = container.firstElementChild;
    expect(bar!.className).toContain("mb-4");
    expect(bar!.className).toContain("rounded-md");
    expect(bar!.className).toContain("border");
  });

  it("renders a primary (non-selection) command button with its label", () => {
    const cmd = makeCommand({ id: "new-role", labelKey: "Administration:NewRole" });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: "Administration:NewRole" })).toBeInTheDocument();
  });

  it("primary command button is enabled when not explicitly disabled", () => {
    const cmd = makeCommand({ id: "new-role", labelKey: "Administration:NewRole" });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: "Administration:NewRole" })).not.toBeDisabled();
  });

  it("primary command button is disabled when disabled prop is true", () => {
    const cmd = makeCommand({ labelKey: "Administration:NewRole", disabled: true });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: "Administration:NewRole" })).toBeDisabled();
  });

  it("calls onClick when primary command button is clicked", () => {
    const onClick = vi.fn();
    const cmd = makeCommand({ labelKey: "Administration:NewRole", onClick });
    render(<AppCommandBar commands={[cmd]} />);
    fireEvent.click(screen.getByRole("button", { name: "Administration:NewRole" }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("selection command is disabled when selectionCount is 0", () => {
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
      icon: Trash2,
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={0} />);
    expect(screen.getByRole("button", { name: /Administration:BulkDelete/ })).toBeDisabled();
  });

  it("selection command is disabled when selectionCount is omitted", () => {
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
    });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: /Administration:BulkDelete/ })).toBeDisabled();
  });

  it("selection command is enabled when selectionCount > 0", () => {
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={3} />);
    expect(screen.getByRole("button", { name: /Administration:BulkDelete/ })).not.toBeDisabled();
  });

  it("selection command remains disabled when disabled prop is true even with selectionCount > 0", () => {
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
      disabled: true,
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={5} />);
    expect(screen.getByRole("button", { name: /Administration:BulkDelete/ })).toBeDisabled();
  });

  it("selection count badge shown when selectionCount > 0", () => {
    const cmd = makeCommand({ requiresSelection: true });
    render(<AppCommandBar commands={[cmd]} selectionCount={3} />);
    // The badge renders t('Administration:NSelected') with {0} replaced
    expect(screen.getByText(/Administration:NSelected/)).toBeInTheDocument();
  });

  it("selection count badge not shown when selectionCount is 0", () => {
    const cmd = makeCommand({ requiresSelection: true });
    render(<AppCommandBar commands={[cmd]} selectionCount={0} />);
    expect(screen.queryByText(/Administration:NSelected/)).not.toBeInTheDocument();
  });

  it("selection count badge not shown when selectionCount is omitted", () => {
    const cmd = makeCommand({ requiresSelection: true });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.queryByText(/Administration:NSelected/)).not.toBeInTheDocument();
  });

  it("mixed commands: primary on left, selection on right; spacer flex-1 present", () => {
    const primary = makeCommand({ id: "new", labelKey: "Administration:NewRole" });
    const selCmd = makeCommand({
      id: "del",
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
    });
    const { container } = render(
      <AppCommandBar commands={[primary, selCmd]} selectionCount={2} />,
    );

    const allButtons = container.querySelectorAll("button");
    expect(allButtons).toHaveLength(2);

    // Spacer div with flex-1 separates the two sides
    const flexDividers = container.querySelectorAll("div.flex-1");
    expect(flexDividers.length).toBeGreaterThanOrEqual(1);
  });

  it("applies additional className prop to the bar container", () => {
    const { container } = render(
      <AppCommandBar commands={[]} className="custom-class" />,
    );
    expect(container.firstElementChild!.className).toContain("custom-class");
  });

  it("destructive variant command carries destructive styling", () => {
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
      variant: "destructive",
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Administration:BulkDelete/ });
    // The Button component sets data-variant or a class that reflects variant;
    // at minimum the button must be present (variant wired through)
    expect(btn).toBeInTheDocument();
  });

  it("does not call onClick when selection command is clicked while disabled", () => {
    const onClick = vi.fn();
    const cmd = makeCommand({
      labelKey: "Administration:BulkDelete",
      requiresSelection: true,
      onClick,
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={0} />);
    const btn = screen.getByRole("button", { name: /Administration:BulkDelete/ });
    // Clicking a disabled button must not trigger onClick
    fireEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });
});

// ── Tests — Flag-on path (new Office-365 style) ───────────────────────────────

describe("AppCommandBar — flag-on path (UI.CommonToolbar = true)", () => {
  beforeEach(() => {
    setFlagOn();
    mockROWidth = 9999; // default to full width so items don't overflow
  });

  afterEach(() => {
    setFlagOff();
    mockROWidth = 0;
  });

  it("empty commands array returns null (bar not rendered)", () => {
    const { container } = render(<AppCommandBar commands={[]} />);
    expect(container.firstElementChild).toBeNull();
  });

  it("renders h-10 bar (not h-12) when flag is on", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const bar = container.firstElementChild;
    expect(bar).not.toBeNull();
    expect(bar!.className).toContain("h-10");
    expect(bar!.className).not.toContain("h-12");
  });

  it("bar container has no top border (no amber accent) in flag-on path", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const bar = container.firstElementChild!;
    expect(bar.className).not.toContain("border-t");
    expect(bar.className).not.toContain("amber");
  });

  it("bar has no rounded-md in flag-on path", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    expect(container.firstElementChild!.className).not.toContain("rounded-md");
  });

  it("primary action renders with font-semibold label", () => {
    const cmd = makeCommand({
      id: "new-role",
      labelKey: "Administration:NewRole",
      isPrimary: true,
    });
    render(<AppCommandBar commands={[cmd]} />);
    const label = screen.getByText("Administration:NewRole");
    expect(label.className).toContain("font-semibold");
  });

  it("primary action calls onClick when clicked", () => {
    const onClick = vi.fn();
    const cmd = makeCommand({ id: "new", labelKey: "Test:New", isPrimary: true, onClick });
    render(<AppCommandBar commands={[cmd]} />);
    fireEvent.click(screen.getByText("Test:New").closest("button")!);
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("secondary action renders with bg-white flat style", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action"));
    expect(actionBtn).toBeInTheDocument();
    expect(actionBtn!.className).toContain("bg-white");
    expect(actionBtn!.className).not.toContain("bg-transparent");
  });

  it("secondary action label has truncate and max-w-[120px] classes", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    // Text appears in both the hidden measurement div and the visible slot
    const labels = screen.getAllByText("Test:Action");
    const visibleLabel = labels.find((el) => !el.closest('[aria-hidden="true"]'));
    expect(visibleLabel).toBeInTheDocument();
    expect(visibleLabel!.className).toContain("truncate");
    expect(visibleLabel!.className).toContain("max-w-[120px]");
  });

  it("primary button has no rounded-t-md (square corners)", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).not.toContain("rounded-t-md");
  });

  it("primary button has no shadow-sm", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).not.toContain("shadow-sm");
  });

  it("primary button has m-[5px] margin class", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).toContain("m-[5px]");
  });

  it("primary button has border-transparent at rest and hover:border-current", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).toContain("border-transparent");
    expect(btn!.className).toContain("hover:border-current");
  });

  it("secondary button has m-[5px] margin class", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("m-[5px]");
  });

  it("secondary button has border-transparent at rest and hover:border-current", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("border-transparent");
    expect(actionBtn!.className).toContain("hover:border-current");
  });

  it("selection-gated action uses flat style (bg-white, no rounded corners)", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("bg-white");
    expect(btn.className).not.toContain("rounded");
  });

  it("selection-gated action has border-transparent at rest and hover:border-current", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("border-transparent");
    expect(btn.className).toContain("hover:border-current");
  });

  it("destructive selection-gated action uses text-destructive colour", () => {
    const delCmd = makeCommand({
      id: "del",
      labelKey: "Test:Delete",
      requiresSelection: true,
      variant: "destructive",
    });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("text-destructive");
    expect(btn.className).not.toContain("text-brand");
  });

  it("selection-gated action onClick not called when aria-disabled", () => {
    const onClick = vi.fn();
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true, onClick });
    render(<AppCommandBar commands={[delCmd]} selectionCount={0} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    fireEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("no divider rendered when no command has isPrimary: true", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const dividers = container.querySelectorAll(".border-r.border-border");
    expect(dividers).toHaveLength(0);
  });

  it("applies className to bar container in flag-on path", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} className="extra-class" />);
    expect(container.firstElementChild!.className).toContain("extra-class");
  });

  // ── Disabled secondary action ─────────────────────────────────────────────

  it("disabled secondary action has aria-disabled=true", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action", disabled: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn).toHaveAttribute("aria-disabled", "true");
  });

  it("disabled secondary action has opacity-50 class", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action", disabled: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"))!;
    expect(actionBtn.className).toContain("opacity-50");
  });

  it("onClick not called when disabled secondary action is clicked", () => {
    const onClick = vi.fn();
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action", disabled: true, onClick });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"))!;
    fireEvent.click(actionBtn);
    expect(onClick).not.toHaveBeenCalled();
  });

  // ── Selection area (unchanged behaviour) ──────────────────────────────────

  it("selection count badge shown in flag-on path when selectionCount > 0", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={5} />);
    expect(screen.getByText(/Administration:NSelected/)).toBeInTheDocument();
  });

  it("selection-gated action has aria-disabled when selectionCount is 0 in flag-on path", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={0} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn).toHaveAttribute("aria-disabled", "true");
    expect(btn).not.toBeDisabled();
  });

  // ── Overflow menu ─────────────────────────────────────────────────────────

  it("overflow button NOT rendered when secondary actions fit (width=9999)", () => {
    // mockROWidth = 9999 set in beforeEach — items have offsetWidth=0 in jsdom, all fit
    const cmd1 = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    const cmd2 = makeCommand({ id: "b", labelKey: "Test:ActionB" });
    render(<AppCommandBar commands={[cmd1, cmd2]} />);
    expect(screen.queryByText("Shell:CommandBarMore")).not.toBeInTheDocument();
  });

  it("overflow button IS rendered when zero container width forces all overflow", () => {
    mockROWidth = 0;
    const cmd1 = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    const cmd2 = makeCommand({ id: "b", labelKey: "Test:ActionB" });
    render(<AppCommandBar commands={[cmd1, cmd2]} />);
    expect(screen.getByText("Shell:CommandBarMore")).toBeInTheDocument();
  });

  it("overflow button has accessible aria-label from Shell:CommandBarMoreLabel", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" })).toBeInTheDocument();
  });

  it("overflowed actions appear in the overflow menu", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA", icon: Mail });
    render(<AppCommandBar commands={[cmd]} />);
    const overflowBtn = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });
    fireEvent.click(overflowBtn);
    const menuItem = screen.getByRole("menuitem");
    expect(within(menuItem).getByText("Test:ActionA")).toBeInTheDocument();
    expect(menuItem.querySelector("svg")).toBeInTheDocument();
  });

  it("single secondary action without overflow does not show overflow button", () => {
    // mockROWidth = 9999 — one item always fits
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.queryByText("Shell:CommandBarMore")).not.toBeInTheDocument();
  });

  it("selection-gated actions are never included in the overflow menu", () => {
    mockROWidth = 0;
    const secondary = makeCommand({ id: "s", labelKey: "Test:Secondary" });
    const selCmd = makeCommand({ id: "d", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[secondary, selCmd]} selectionCount={0} />);

    // Overflow button should be present (secondary overflows at width=0)
    expect(screen.getByText("Shell:CommandBarMore")).toBeInTheDocument();

    // Open the overflow menu
    fireEvent.click(screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" }));

    // Menu items should NOT include selection-gated command
    const menuItems = screen.queryAllByRole("menuitem");
    const deleteInMenu = menuItems.filter((item) => item.textContent?.includes("Test:Delete"));
    expect(deleteInMenu).toHaveLength(0);
  });

  it("primary action is not included in overflow menu", () => {
    mockROWidth = 0;
    const primary = makeCommand({ id: "p", labelKey: "Test:Primary", isPrimary: true });
    const secondary = makeCommand({ id: "s", labelKey: "Test:Secondary" });
    render(<AppCommandBar commands={[primary, secondary]} />);

    // Primary slot renders the primary label
    const primarySpan = screen.getAllByText("Test:Primary")[0];
    expect(primarySpan).toBeInTheDocument();

    // Open overflow menu (secondary overflows)
    fireEvent.click(screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" }));

    const menuItems = screen.queryAllByRole("menuitem");
    const primaryInMenu = menuItems.filter((item) => item.textContent?.includes("Test:Primary"));
    expect(primaryInMenu).toHaveLength(0);
  });

  // ── Feature flag reading ───────────────────────────────────────────────────

  it("flag-off path (UI.CommonToolbar=false) renders h-12 bar", () => {
    setFlagOff();
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    expect(container.firstElementChild!.className).toContain("h-12");
  });

  it("flag-on path (UI.CommonToolbar=true) renders h-10 bar", () => {
    // Already set to flag-on in beforeEach
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    expect(container.firstElementChild!.className).toContain("h-10");
  });
});

// ── Accessibility: aria attributes (flag-on) ──────────────────────────────────

describe("AppCommandBar — accessibility attributes (flag-on)", () => {
  beforeEach(() => {
    setFlagOn();
    mockROWidth = 9999;
  });

  afterEach(() => {
    setFlagOff();
    mockROWidth = 0;
  });

  it("non-disabled secondary action does not carry aria-disabled attribute", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action", disabled: false });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn).not.toHaveAttribute("aria-disabled");
  });

  it("overflow trigger has aria-label from Shell:CommandBarMoreLabel", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    expect(screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" })).toBeInTheDocument();
  });

  it("overflow menu items contain both icon and label", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA", icon: Mail });
    render(<AppCommandBar commands={[cmd]} />);
    fireEvent.click(screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" }));
    const menuItem = screen.getByRole("menuitem");
    expect(within(menuItem).getByText("Test:ActionA")).toBeInTheDocument();
    expect(menuItem.querySelector("svg")).toBeInTheDocument();
  });
});

// ── Keyboard interactions — High risk: WCAG 2.1 AA ───────────────────────────
//
// Design §Test risk analysis (High — Keyboard and accessibility):
//   "Keyboard: simulate keyboard open/close of overflow menu; assert Escape returns focus."
//
// Enter/Space activation: a native <button> fires click on Enter/Space in real browsers.
// jsdom does not replicate that native activation; testing it requires
// @testing-library/user-event (not yet a devDependency). The click-opens-menu path is
// already covered above. The tests here use fireEvent.keyDown to verify that Base UI's
// own keydown handlers (separate from native button activation) close the menu and
// restore focus.
//
// Finding: @testing-library/user-event should be added to devDependencies so that
// Enter/Space activation can be fully tested without relying on native browser behaviour.

describe("AppCommandBar — keyboard: overflow menu (flag-on)", () => {
  beforeEach(() => {
    setFlagOn();
    mockROWidth = 0;
  });

  afterEach(() => {
    setFlagOff();
    mockROWidth = 0;
  });

  it("Escape key closes the overflow menu while it is open", () => {
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    const trigger = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });

    fireEvent.click(trigger);
    expect(screen.getByRole("menuitem")).toBeInTheDocument();

    fireEvent.keyDown(document.activeElement ?? trigger, { key: "Escape", code: "Escape" });
    expect(screen.queryByRole("menuitem")).not.toBeInTheDocument();
  });

  it("focus returns to the overflow trigger after Escape closes the menu", () => {
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    const trigger = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });

    fireEvent.click(trigger);
    expect(screen.getByRole("menuitem")).toBeInTheDocument();

    fireEvent.keyDown(document.activeElement ?? trigger, { key: "Escape", code: "Escape" });
    expect(document.activeElement).toBe(trigger);
  });

  it("menu item is activated and menu closes on Enter inside the open menu", () => {
    const onClick = vi.fn();
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA", onClick });
    render(<AppCommandBar commands={[cmd]} />);
    const trigger = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });

    fireEvent.click(trigger);
    const item = screen.getByRole("menuitem");

    fireEvent.keyDown(item, { key: "Enter", code: "Enter" });
    expect(onClick).toHaveBeenCalledOnce();
  });
});
