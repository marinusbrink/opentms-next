import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { MultiSelect } from "@/components/ui/multi-select";

/* The library MultiSelect (UI-LOOK-007): comma list, "+N" truncation, no chips. Risk class: Low. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const options = [
  { id: "a", label: "Alpha" },
  { id: "b", label: "Beta" },
  { id: "c", label: "Gamma" },
  { id: "d", label: "Delta" },
];

describe("MultiSelect", () => {
  it("shows the select prompt when nothing is selected", () => {
    render(<MultiSelect id="ms" value={[]} onChange={() => {}} options={options} />);
    expect(screen.getByText("MasterData:Select")).toBeInTheDocument();
  });

  it("shows the selected names as a comma list", () => {
    render(<MultiSelect id="ms" value={["a", "c"]} onChange={() => {}} options={options} />);
    expect(screen.getByText("Alpha, Gamma")).toBeInTheDocument();
  });

  it("renders the Add new row only when a creator is given", () => {
    const { unmount } = render(<MultiSelect id="ms" value={[]} onChange={() => {}} options={options} />);
    expect(screen.queryByText("Add new characteristic…")).toBeNull();
    unmount();
    render(<MultiSelect id="ms" value={[]} onChange={() => {}} options={options} onCreateNew={() => {}} createLabel="Add new characteristic…" />);
    expect(document.querySelectorAll("[data-slot=combobox-trigger] svg").length).toBe(1);
  });

  it("truncates with +N beyond the visible count and never renders chips", () => {
    render(<MultiSelect id="ms" value={["a", "b", "c", "d"]} onChange={() => {}} options={options} maxVisible={2} />);
    expect(screen.getByText("Alpha, Beta, MasterData:MoreSelected")).toBeInTheDocument();
    expect(document.querySelector("[data-slot=combobox-chip]")).toBeNull();
  });
});
