import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { NestedTabs } from "@/components/ui/nested-tabs";

/* Nested tabs (UI-FORM-008: start-aligned, text only, one level; UI-FORM-007: a tab with an error is marked), the
 * General tab's group of the order page (design 61 "Order page, legacy tab structure" 2). Risk class: Low. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const TABS = [
  { value: "items", label: "Freight items", content: <input aria-label="Quantity" /> },
  { value: "services", label: "Additional services", content: <p>Services</p> },
  { value: "references", label: "Other references", content: <p>References</p>, hasError: true },
];

describe("NestedTabs", () => {
  it("is a start-aligned strip of text tabs, the first one active", () => {
    render(<NestedTabs tabs={TABS} />);
    const list = screen.getByRole("tablist");
    expect(list).toHaveClass("justify-start");
    expect(list).not.toHaveClass("justify-end");
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Freight items", "Additional services", "Other references"]);
    expect(screen.getByRole("tab", { name: "Freight items" })).toHaveAttribute("aria-selected", "true");
    expect(document.querySelectorAll("[data-slot=nested-tabs] [data-slot=nested-tabs]")).toHaveLength(0);
  });

  it("marks a tab whose content holds an error", () => {
    render(<NestedTabs tabs={TABS} />);
    const marked = screen.getByRole("tab", { name: "Other references" });
    expect(marked).toHaveClass("text-destructive");
    expect(marked.querySelector("svg")).not.toBeNull();
    expect(screen.getByRole("tab", { name: "Freight items" })).not.toHaveClass("text-destructive");
  });

  it("keeps every panel mounted: what was typed in a hidden tab is still there", () => {
    render(<NestedTabs tabs={TABS} />);
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("tab", { name: "Additional services" }));
    expect(screen.getByText("Services")).toBeVisible();
    fireEvent.click(screen.getByRole("tab", { name: "Freight items" }));
    expect(screen.getByLabelText("Quantity")).toHaveValue("4");
  });

  it("follows a controlled value and reports the change", () => {
    const changes: string[] = [];
    const { rerender } = render(<NestedTabs tabs={TABS} value="references" onValueChange={(v) => changes.push(v)} />);
    expect(screen.getByRole("tab", { name: "Other references" })).toHaveAttribute("aria-selected", "true");
    fireEvent.click(screen.getByRole("tab", { name: "Additional services" }));
    expect(changes).toEqual(["services"]);
    rerender(<NestedTabs tabs={TABS} value="services" onValueChange={(v) => changes.push(v)} />);
    expect(screen.getByRole("tab", { name: "Additional services" })).toHaveAttribute("aria-selected", "true");
  });
});
