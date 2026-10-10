import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { Mail } from "lucide-react";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (key: string) => key,
    culture: "en",
    isLoading: false,
  }),
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

// ── Tests — Critical: shared library component, cross-product API contract ────

describe("AppCommandBar", () => {
  beforeEach(() => {
    mockROWidth = 9999; // default to full width so items don't overflow
  });

  afterEach(() => {
    mockROWidth = 0;
  });

  it("empty commands array returns null (bar not rendered)", () => {
    const { container } = render(<AppCommandBar commands={[]} />);
    expect(container.firstElementChild).toBeNull();
  });

  it("renders h-10 bar", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const bar = container.firstElementChild;
    expect(bar).not.toBeNull();
    expect(bar!.className).toContain("h-10");
    expect(bar!.className).not.toContain("h-12");
  });

  it("bar container has no top border (no amber accent)", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const bar = container.firstElementChild!;
    expect(bar.className).not.toContain("border-t");
    expect(bar.className).not.toContain("amber");
  });

  it("bar has no rounded-md", () => {
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

  it("secondary action renders with bg-transparent flat style (no white background)", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action"));
    expect(actionBtn).toBeInTheDocument();
    expect(actionBtn!.className).toContain("bg-transparent");
    expect(actionBtn!.className).not.toContain("bg-white");
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

  it("primary button has no border classes (no hover border, no transparent border placeholder)", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).not.toContain("border-transparent");
    expect(btn!.className).not.toContain("hover:border-current");
  });

  it("secondary button has m-[5px] margin class", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("m-[5px]");
  });

  it("secondary button has no border classes (no hover border, no transparent border placeholder)", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).not.toContain("border-transparent");
    expect(actionBtn!.className).not.toContain("hover:border-current");
  });

  it("selection-gated action uses flat style (bg-transparent, no rounded corners, no border)", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("bg-transparent");
    expect(btn.className).not.toContain("bg-white");
    expect(btn.className).not.toContain("rounded");
    expect(btn.className).not.toContain("border-transparent");
    expect(btn.className).not.toContain("hover:border-current");
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

  it("applies className to bar container", () => {
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

  // ── Selection area ────────────────────────────────────────────────────────

  it("selection count badge shown when selectionCount > 0", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={5} />);
    expect(screen.getByText(/Administration:NSelected/)).toBeInTheDocument();
  });

  it("selection-gated action has aria-disabled when selectionCount is 0", () => {
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

  // ── Round 4 visual rules ───────────────────────────────────────────────────

  it("bar container has bottom border border-b-2 and border-b-[#E5E5E5]", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    const { container } = render(<AppCommandBar commands={[cmd]} />);
    const bar = container.firstElementChild!;
    expect(bar.className).toContain("border-b-2");
    expect(bar.className).toContain("border-b-[#E5E5E5]");
  });

  it("primary button has text-brand class", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).toContain("text-brand");
  });

  it("primary button has hover:bg-brand/20 class", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).toContain("hover:bg-brand/20");
  });

  it("primary button is bg-transparent (not bg-white)", () => {
    const cmd = makeCommand({ id: "new", labelKey: "Test:Primary", isPrimary: true });
    render(<AppCommandBar commands={[cmd]} />);
    const btn = screen.getAllByRole("button").find((b) => b.textContent?.includes("Test:Primary"));
    expect(btn!.className).toContain("bg-transparent");
    expect(btn!.className).not.toContain("bg-white");
  });

  it("secondary button has text-brand class", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("text-brand");
  });

  it("secondary button has hover:bg-brand/20 class", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("hover:bg-brand/20");
  });

  it("secondary button has py-1 vertical padding", () => {
    const cmd = makeCommand({ id: "act", labelKey: "Test:Action" });
    render(<AppCommandBar commands={[cmd]} />);
    const btns = screen.getAllByRole("button");
    const actionBtn = btns.find((b) => b.textContent?.includes("Test:Action") && !b.getAttribute("aria-label"));
    expect(actionBtn!.className).toContain("py-1");
  });

  it("overflow trigger button has py-1 vertical padding", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    const trigger = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });
    expect(trigger.className).toContain("py-1");
  });

  it("overflow trigger button has text-brand and hover:bg-brand/20", () => {
    mockROWidth = 0;
    const cmd = makeCommand({ id: "a", labelKey: "Test:ActionA" });
    render(<AppCommandBar commands={[cmd]} />);
    const trigger = screen.getByRole("button", { name: "Shell:CommandBarMoreLabel" });
    expect(trigger.className).toContain("text-brand");
    expect(trigger.className).toContain("hover:bg-brand/20");
  });

  it("non-destructive selection-gated button has text-brand class", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("text-brand");
  });

  it("non-destructive selection-gated button has hover:bg-brand/20 class", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("hover:bg-brand/20");
  });

  it("selection-gated button has py-1 vertical padding", () => {
    const delCmd = makeCommand({ id: "del", labelKey: "Test:Delete", requiresSelection: true });
    render(<AppCommandBar commands={[delCmd]} selectionCount={1} />);
    const btn = screen.getByRole("button", { name: /Test:Delete/ });
    expect(btn.className).toContain("py-1");
  });

  // ── selectionLabelKeys: dynamic label based on count ──────────────────────

  it("selection-gated renders zero-state label when selectionCount=0 (disabled)", () => {
    const cmd = makeCommand({
      id: "del",
      labelKey: "Test:Delete",
      requiresSelection: true,
      selectionLabelKeys: { zero: "Test:DeleteZero", one: "Test:DeleteOne", many: "Test:DeleteMany" },
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={0} />);
    expect(screen.getByText("Test:DeleteZero")).toBeInTheDocument();
    const btn = screen.getByRole("button", { name: /Test:DeleteZero/ });
    expect(btn).toHaveAttribute("aria-disabled", "true");
  });

  it("selection-gated renders one-state label when selectionCount=1", () => {
    const cmd = makeCommand({
      id: "del",
      labelKey: "Test:Delete",
      requiresSelection: true,
      selectionLabelKeys: { zero: "Test:DeleteZero", one: "Test:DeleteOne", many: "Test:DeleteMany" },
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={1} />);
    expect(screen.getByText("Test:DeleteOne")).toBeInTheDocument();
    const btn = screen.getByRole("button", { name: /Test:DeleteOne/ });
    expect(btn).not.toHaveAttribute("aria-disabled");
  });

  it("selection-gated renders many-state label with count substituted when selectionCount>1", () => {
    const cmd = makeCommand({
      id: "del",
      labelKey: "Test:Delete",
      requiresSelection: true,
      selectionLabelKeys: { zero: "Test:DeleteZero", one: "Test:DeleteOne", many: "Delete {0} items" },
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={5} />);
    expect(screen.getByText("Delete 5 items")).toBeInTheDocument();
  });

  it("selection-gated without selectionLabelKeys falls back to labelKey", () => {
    const cmd = makeCommand({
      id: "del",
      labelKey: "Test:FallbackLabel",
      requiresSelection: true,
    });
    render(<AppCommandBar commands={[cmd]} selectionCount={3} />);
    expect(screen.getByText("Test:FallbackLabel")).toBeInTheDocument();
  });
});

// ── Accessibility: aria attributes ────────────────────────────────────────────

describe("AppCommandBar — accessibility attributes", () => {
  beforeEach(() => {
    mockROWidth = 9999;
  });

  afterEach(() => {
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

describe("AppCommandBar — keyboard: overflow menu", () => {
  beforeEach(() => {
    mockROWidth = 0;
  });

  afterEach(() => {
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
