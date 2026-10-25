import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

import { CalculatingIndicator, calculatingValueClass, isCalculating } from "@/components/ui/calculating-indicator";

/* Design 49, UI design: the visible "calculating" and "failed" states; nothing for an up-to-date record. Risk: Medium. */
describe("CalculatingIndicator", () => {
  it("shows the calculating status with its accessible name while pending", () => {
    render(<CalculatingIndicator state={{ status: 1 }} />);
    expect(screen.getByRole("status", { name: "Platform:DerivedValues.Calculating" })).toBeInTheDocument();
    expect(isCalculating({ status: 1 })).toBe(true);
    expect(calculatingValueClass({ status: 1 })).toBe("text-muted-foreground");
  });

  it("shows the failed status", () => {
    render(<CalculatingIndicator state={{ status: 3, lastErrorCode: "Financial:NoRate" }} />);
    expect(screen.getByRole("status", { name: "Platform:DerivedValues.Failed" })).toBeInTheDocument();
  });

  it("renders nothing when up to date or never calculated", () => {
    const { container, rerender } = render(<CalculatingIndicator state={{ status: 2 }} />);
    expect(container).toBeEmptyDOMElement();
    rerender(<CalculatingIndicator state={undefined} />);
    expect(container).toBeEmptyDOMElement();
    expect(calculatingValueClass(undefined)).toBeUndefined();
  });
});
