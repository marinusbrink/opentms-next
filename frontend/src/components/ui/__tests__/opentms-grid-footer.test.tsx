import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { OpenTmsGridFooter } from "@/components/ui/opentms-grid-footer";

// Mock useL so the footer renders without a real localization provider.
// The mock returns each key verbatim so assertions can target the key strings.
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (key: string) => key,
    culture: "en",
    isLoading: false,
  }),
}));

describe("OpenTmsGridFooter", () => {
  it("renders a skeleton placeholder when totalCount is null", () => {
    const { container } = render(
      <OpenTmsGridFooter totalCount={null} filteredCount={null} />,
    );
    expect(container.querySelector("[data-slot='skeleton']")).toBeInTheDocument();
  });

  it("shows total count using the Grid:Total localization key", () => {
    render(<OpenTmsGridFooter totalCount={42} filteredCount={42} />);
    // t() returns the key itself; the component formats it with the count
    // fmt("{0} records", "42") → "Grid:Total" is the key, rendered as "Grid:Total"
    // The text node contains the key (since mock returns key verbatim)
    expect(screen.getByText(/Grid:Total/)).toBeInTheDocument();
  });

  it("does not show filtered row count when filteredCount equals totalCount", () => {
    render(<OpenTmsGridFooter totalCount={100} filteredCount={100} />);
    expect(screen.queryByText(/Grid:Filtered/)).not.toBeInTheDocument();
  });

  it("shows filtered count using Grid:Filtered key when filteredCount is less than totalCount", () => {
    render(<OpenTmsGridFooter totalCount={100} filteredCount={30} />);
    expect(screen.getByText(/Grid:Filtered/)).toBeInTheDocument();
  });

  it("does not show filtered count when filteredCount is null but totalCount is set", () => {
    render(<OpenTmsGridFooter totalCount={100} filteredCount={null} />);
    expect(screen.queryByText(/Grid:Filtered/)).not.toBeInTheDocument();
  });

  it("does not render skeleton once totalCount is provided", () => {
    const { container } = render(
      <OpenTmsGridFooter totalCount={0} filteredCount={0} />,
    );
    expect(container.querySelector("[data-slot='skeleton']")).not.toBeInTheDocument();
  });
});
