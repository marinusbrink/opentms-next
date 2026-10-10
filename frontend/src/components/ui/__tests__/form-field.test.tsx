import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";

describe("FormField (UI-FORM-001/002/006)", () => {
  it("renders the label above the control and links them", () => {
    render(
      <FormField label="Licence plate" htmlFor="plate">
        <Input id="plate" />
      </FormField>,
    );
    const input = screen.getByLabelText("Licence plate");
    expect(input).toHaveAttribute("id", "plate");
    expect(input).not.toHaveAttribute("aria-required");
    expect(input).not.toHaveAttribute("aria-invalid");
  });

  it("marks required fields with an asterisk after the label and aria-required on the control", () => {
    render(
      <FormField label="Licence plate" htmlFor="plate" required>
        <Input id="plate" />
      </FormField>,
    );
    expect(screen.getByText("*", { exact: false })).toBeInTheDocument();
    expect(screen.getByLabelText(/Licence plate/)).toHaveAttribute("aria-required", "true");
  });

  it("shows the error under the control and sets aria-invalid", () => {
    render(
      <FormField label="Licence plate" htmlFor="plate" error="This field is required." hint="Hidden while erroring">
        <Input id="plate" />
      </FormField>,
    );
    const input = screen.getByLabelText("Licence plate");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAttribute("aria-describedby", "plate-error");
    expect(screen.getByRole("alert")).toHaveTextContent("This field is required.");
    expect(screen.queryByText("Hidden while erroring")).not.toBeInTheDocument();
  });

  it("shows the hint when there is no error", () => {
    render(
      <FormField label="Weight (kg)" htmlFor="w" hint="Gross weight">
        <Input id="w" />
      </FormField>,
    );
    expect(screen.getByText("Gross weight")).toBeInTheDocument();
    expect(screen.getByLabelText("Weight (kg)")).toHaveAttribute("aria-describedby", "w-hint");
  });
});
