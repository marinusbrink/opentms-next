import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { CheckboxGroup } from "@/components/ui/checkbox-group";
import { FormField } from "@/components/ui/form-field";

/* The checkbox group (UI-FORM-010; design 43: the relation roles). Risk class: Low. */

const options = [
  { id: "Customer", label: "Customer" },
  { id: "Carrier", label: "Carrier" },
  { id: "OwnCompany", label: "Own company" },
];

describe("CheckboxGroup", () => {
  it("renders one checkbox per option with the current value checked", () => {
    render(<CheckboxGroup id="roles" value={["Carrier"]} onChange={() => {}} options={options} />);
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(3);
    expect(screen.getByRole("checkbox", { name: "Carrier" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Customer" })).not.toBeChecked();
  });

  it("adds and removes ids through onChange, also when the label text is clicked", () => {
    const onChange = vi.fn();
    render(<CheckboxGroup id="roles" value={["Carrier"]} onChange={onChange} options={options} />);
    fireEvent.click(screen.getByRole("checkbox", { name: "Customer" }));
    expect(onChange).toHaveBeenLastCalledWith(["Carrier", "Customer"]);
    fireEvent.click(screen.getByText("Carrier"));
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it("does nothing when disabled", () => {
    const onChange = vi.fn();
    render(<CheckboxGroup id="roles" value={[]} onChange={onChange} options={options} disabled />);
    for (const box of screen.getAllByRole("checkbox")) expect(box).toHaveAttribute("data-disabled");
    fireEvent.click(screen.getByRole("checkbox", { name: "Customer" }));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("is named by the FormField label and carries its error description and invalid state", () => {
    render(
      <FormField label="Roles" htmlFor="roles" floating="always" required error="Pick at least one role">
        <CheckboxGroup id="roles" value={[]} onChange={() => {}} options={options} columns={2} />
      </FormField>,
    );
    const group = screen.getByRole("group", { name: "Roles" });
    expect(group).toHaveAttribute("aria-describedby", "roles-error");
    expect(group).toHaveAttribute("aria-invalid", "true");
    expect(group).toHaveAttribute("aria-required", "true");
    expect(group.className).toContain("grid-cols-2");
    expect(screen.getByRole("checkbox", { name: "Customer" })).toHaveAttribute("aria-invalid", "true");
  });
});
