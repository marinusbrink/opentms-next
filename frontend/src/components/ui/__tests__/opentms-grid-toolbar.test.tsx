import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { OpenTmsGridToolbar } from "@/components/ui/opentms-grid-toolbar";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (key: string) => key,
    culture: "en",
    isLoading: false,
  }),
}));

// Base UI Menu / Tooltip can fail in jsdom due to portal and positioning
// logic. Mock the compound components with simple HTML equivalents so toolbar
// behaviour can be tested without the real widget internals.
vi.mock("@/components/ui/dropdown-menu", () => ({
  DropdownMenu: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="dropdown-menu">{children}</div>
  ),
  DropdownMenuTrigger: ({
    children,
    render: renderProp,
  }: {
    children?: React.ReactNode;
    render?: React.ReactElement;
  }) => (
    <div data-testid="dropdown-trigger">
      {renderProp ?? children}
    </div>
  ),
  DropdownMenuContent: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="dropdown-content">{children}</div>
  ),
  DropdownMenuCheckboxItem: ({
    children,
    checked,
    onCheckedChange,
  }: {
    children: React.ReactNode;
    checked?: boolean;
    onCheckedChange?: (v: boolean) => void;
  }) => (
    <button
      data-testid="dropdown-checkbox-item"
      data-checked={checked}
      onClick={() => onCheckedChange?.(!checked)}
    >
      {children}
    </button>
  ),
}));

vi.mock("@/components/ui/tooltip", () => ({
  TooltipProvider: ({ children }: { children: React.ReactNode }) => (
    <>{children}</>
  ),
  Tooltip: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  TooltipTrigger: ({
    children,
    render: renderProp,
  }: {
    children?: React.ReactNode;
    render?: React.ReactElement;
  }) => renderProp ?? <>{children}</>,
  TooltipContent: ({ children }: { children: React.ReactNode }) => (
    <div>{children}</div>
  ),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

const defaultProps = {
  searchValue: "",
  onSearchChange: vi.fn(),
  columnDefs: [
    { colId: "reference", field: "reference", headerName: "Reference" },
    { colId: "status", field: "status", headerName: "Status" },
  ],
  columnVisibility: { reference: true, status: true },
  onToggleColumn: vi.fn(),
  onReset: vi.fn(),
};

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("OpenTmsGridToolbar", () => {
  it("renders the search input with the Grid:Search placeholder", () => {
    render(<OpenTmsGridToolbar {...defaultProps} />);
    const input = screen.getByPlaceholderText("Grid:Search");
    expect(input).toBeInTheDocument();
  });

  it("calls onSearchChange with the typed value when the search input changes", () => {
    const onSearchChange = vi.fn();
    render(<OpenTmsGridToolbar {...defaultProps} onSearchChange={onSearchChange} />);
    const input = screen.getByPlaceholderText("Grid:Search");
    fireEvent.change(input, { target: { value: "rotterdam" } });
    expect(onSearchChange).toHaveBeenCalledWith("rotterdam");
  });

  it("reflects the current searchValue in the input", () => {
    render(<OpenTmsGridToolbar {...defaultProps} searchValue="initial" />);
    const input = screen.getByPlaceholderText("Grid:Search") as HTMLInputElement;
    expect(input.value).toBe("initial");
  });

  it("renders a column entry for each toggleable column definition", () => {
    render(<OpenTmsGridToolbar {...defaultProps} />);
    // Both column names appear in the dropdown menu area
    expect(screen.getByText("Reference")).toBeInTheDocument();
    expect(screen.getByText("Status")).toBeInTheDocument();
  });

  it("calls onToggleColumn with colId and new visibility when a column item is clicked", () => {
    const onToggleColumn = vi.fn();
    render(
      <OpenTmsGridToolbar
        {...defaultProps}
        columnVisibility={{ reference: true, status: true }}
        onToggleColumn={onToggleColumn}
      />,
    );
    // Click the "Status" checkbox item — currently visible (true), click toggles to false
    const items = screen.getAllByTestId("dropdown-checkbox-item");
    const statusItem = items.find((el) => el.textContent === "Status")!;
    fireEvent.click(statusItem);
    expect(onToggleColumn).toHaveBeenCalledWith("status", false);
  });

  it("calls onToggleColumn with visible=true when a hidden column is toggled on", () => {
    const onToggleColumn = vi.fn();
    render(
      <OpenTmsGridToolbar
        {...defaultProps}
        columnVisibility={{ reference: true, status: false }}
        onToggleColumn={onToggleColumn}
      />,
    );
    const items = screen.getAllByTestId("dropdown-checkbox-item");
    const statusItem = items.find((el) => el.textContent === "Status")!;
    fireEvent.click(statusItem);
    expect(onToggleColumn).toHaveBeenCalledWith("status", true);
  });

  it("calls onReset when the reset button is clicked", () => {
    const onReset = vi.fn();
    render(<OpenTmsGridToolbar {...defaultProps} onReset={onReset} />);
    const resetButton = screen.getByRole("button", { name: "Grid:Reset" });
    fireEvent.click(resetButton);
    expect(onReset).toHaveBeenCalledTimes(1);
  });

  it("does not include columns that have no colId or field", () => {
    const propsWithUnidentified = {
      ...defaultProps,
      columnDefs: [
        { colId: "reference", field: "reference", headerName: "Reference" },
        { headerName: "Unnamed" }, // no colId or field — not toggleable
      ],
    };
    render(<OpenTmsGridToolbar {...propsWithUnidentified} />);
    expect(screen.getByText("Reference")).toBeInTheDocument();
    expect(screen.queryByText("Unnamed")).not.toBeInTheDocument();
  });
});
