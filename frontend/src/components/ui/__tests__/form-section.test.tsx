import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { FormSection } from "@/components/ui/form-section";

/* Risk class: Low — a presentational wrapper; the test pins the UI-FORM-005 markup. */
describe("FormSection", () => {
  it("is a labelled section card with a brand caption and the fields inside", () => {
    render(
      <FormSection title="Customer">
        <input aria-label="Name" />
      </FormSection>,
    );
    const section = screen.getByRole("region", { name: "Customer" });
    expect(section.className).toContain("bg-surface-section");
    expect(section.className).toContain("border-border-section");
    expect(screen.getByRole("heading", { level: 3, name: "Customer" }).className).toContain("text-brand");
    expect(screen.getByRole("textbox", { name: "Name" })).toBeInTheDocument();
  });
});
