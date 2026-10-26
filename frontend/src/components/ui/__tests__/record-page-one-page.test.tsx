import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { RecordPage } from "@/components/ui/record-page";

/* A record laid out as one page without content tabs (design 61 "Order page, revised" 1): RecordPage renders the
 * children in one scrolling panel under the ribbon and no tab strip. Risk class: Low. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

describe("RecordPage without tabs", () => {
  it("shows the children in one scrolling panel and no tab strip", () => {
    render(
      <RecordPage title="Transport order TO-1" commands={[]} onClose={() => {}}>
        <p>The whole order</p>
      </RecordPage>,
    );
    expect(screen.getByRole("heading", { level: 1, name: "Transport order TO-1" })).toBeInTheDocument();
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    expect(document.querySelector("[data-editor-tab-strip]")).toBeNull();
    const panels = document.querySelectorAll("[data-slot=record-page-panel]");
    expect(panels).toHaveLength(1);
    expect(panels[0]).toHaveClass("overflow-y-auto");
    expect(panels[0]).toHaveTextContent("The whole order");
  });
});
