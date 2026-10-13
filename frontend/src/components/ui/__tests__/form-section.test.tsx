import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { FORM_SECTION_CAPTION, FormSection } from "@/components/ui/form-section";

/* Risk class: Low — a presentational wrapper; the tests pin the UI-FORM-005 markup. After the PO's
 * 2026-10-13 change the title is optional: a section whose first row is its own caption (a switch, a
 * tab strip) names itself with `aria-label` and renders no heading. FORM_SECTION_CAPTION is the caption
 * style such a control reuses, so the heading and the control must carry the very same classes. */

const captionClasses = FORM_SECTION_CAPTION.split(/\s+/);

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

  it("renders the caption heading as the section's first child", () => {
    render(
      <FormSection title="Customer">
        <input aria-label="Code" />
      </FormSection>,
    );
    const section = screen.getByRole("region", { name: "Customer" });
    expect(section.tagName).toBe("SECTION");
    expect(section).toHaveAttribute("data-slot", "form-section");
    const heading = within(section).getByRole("heading", { level: 3, name: "Customer" });
    expect(section.firstElementChild).toBe(heading);
    expect(within(section).getByLabelText("Code")).toBeInTheDocument();
  });

  it("the heading carries every caption class, so a control styled with FORM_SECTION_CAPTION reads the same", () => {
    render(<FormSection title="Customer">x</FormSection>);
    const heading = screen.getByRole("heading", { level: 3 });
    expect(captionClasses).toContain("text-label");
    expect(captionClasses).toContain("text-brand");
    for (const cls of captionClasses) expect(heading, cls).toHaveClass(cls);
  });

  it("without a title names itself with aria-label and renders no heading at all", () => {
    render(
      <FormSection aria-label="Addresses">
        <label className={FORM_SECTION_CAPTION}>
          <input type="checkbox" /> All addresses equal
        </label>
      </FormSection>,
    );
    const section = screen.getByRole("region", { name: "Addresses" });
    expect(within(section).queryByRole("heading")).toBeNull();
    expect(section.querySelector("h3")).toBeNull();
    /* The first child is the caller's own caption row. */
    expect(section.firstElementChild!.tagName).toBe("LABEL");
    expect(within(section).getByRole("checkbox")).toBeInTheDocument();
  });

  it("an explicit aria-label wins over the title for the accessible name; the title is still the heading", () => {
    render(
      <FormSection title="Name" aria-label="Name section">
        x
      </FormSection>,
    );
    expect(screen.getByRole("region", { name: "Name section" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Name" })).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "Name" })).toBeInTheDocument();
  });

  it("keeps the card classes and appends the caller's className", () => {
    render(
      <FormSection title="Customer" className="flex flex-col">
        x
      </FormSection>,
    );
    const section = screen.getByRole("region", { name: "Customer" });
    expect(section).toHaveClass("rounded-lg", "border", "flex", "flex-col");
  });
});
