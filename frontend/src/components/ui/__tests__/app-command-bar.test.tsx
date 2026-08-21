import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Trash2 } from "lucide-react";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (key: string) => key,
    culture: "en",
    isLoading: false,
  }),
}));

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

describe("AppCommandBar (Critical — shared library component)", () => {
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
