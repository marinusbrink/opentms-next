import { render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { AmountCell, BoolCell, DateCell, DateTimeCell, NumberCell, TimestampCell } from "@/components/ui/grid-cells";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "nl", isLoading: false }),
}));

const stamp = new Date(2026, 9, 9, 14, 30, 15).toISOString();

describe("grid cells (UI-GRID-004, UI-TOK-009)", () => {
  it("renders numbers and amounts with tabular figures in the user's culture", () => {
    const { container: n } = render(<NumberCell value={1234.5} />);
    expect(n.textContent).toBe("1.234,5");
    expect(n.firstElementChild!.className).toContain("tabular-nums");

    const { container: a } = render(<AmountCell value={1234.5} />);
    expect(a.textContent).toBe("1.234,50");
  });

  it("renders dates numerically and date-times with a 24-hour clock", () => {
    expect(render(<DateCell value={stamp} />).container.textContent).toBe("09-10-2026");
    expect(render(<DateTimeCell value={stamp} />).container.textContent).toBe("09-10-2026 14:30");
  });

  it("renders technical timestamps culture-independently with seconds (UI-FMT-003)", () => {
    const { container } = render(<TimestampCell value={stamp} />);
    expect(container.textContent).toBe("2026-10-09 14:30:15");
    expect(container.firstElementChild!.className).toContain("tabular-nums");
  });

  it("renders a check for true and nothing for false, never the words", () => {
    const { container: yes } = render(<BoolCell value />);
    expect(yes.querySelector("svg")).toBeInTheDocument();
    expect(yes.textContent).toBe("");

    const { container: no } = render(<BoolCell value={false} />);
    expect(no.innerHTML).toBe("");
  });
});
