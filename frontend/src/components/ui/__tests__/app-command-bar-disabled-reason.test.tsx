import { render, screen } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";
import { cloneElement } from "react";
import { beforeAll, describe, expect, it, vi } from "vitest";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";

/* AppCommandBarCommand.disabledReasonKey is "tooltip shown on disabled state" (UI-PERM-005). The AI connector's Switch off (design 74)
 * passes one for the moment the grid's select all makes a filter-based selection, which that screen cannot act on: the command stays
 * disabled, and this reason is all the user gets. Base UI's tooltip does not open in jsdom, so the content renders in line here.
 * Risk class: Medium (UI flows). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }) }));
vi.mock("@/components/ui/tooltip", () => ({
  TooltipProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  TooltipTrigger: ({ render: element, children }: { render: ReactElement; children?: ReactNode }) => cloneElement(element, undefined, children),
  TooltipContent: ({ children }: { children: ReactNode }) => <span data-slot="mock-tooltip">{children}</span>,
}));

beforeAll(() => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      private readonly cb: ResizeObserverCallback;
      constructor(cb: ResizeObserverCallback) {
        this.cb = cb;
      }
      observe(el: Element) {
        this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
      }
      disconnect() {}
      unobserve() {}
    },
  );
});

const REASON = "AiConnector:SelectConnections";
const reasonShown = () => {
  const inTooltip = Array.from(document.querySelectorAll("[data-slot=mock-tooltip]")).some((tip) => tip.textContent === REASON);
  const inAttribute = Array.from(document.querySelectorAll("button")).some((button) => button.getAttribute("title") === REASON || button.getAttribute("aria-description") === REASON);
  return inTooltip || inAttribute;
};

describe("AppCommandBar: the reason of a disabled command", () => {
  it("a secondary command shows it", () => {
    const command: AppCommandBarCommand = { id: "other", labelKey: "Other", disabled: true, disabledReasonKey: REASON, onClick: () => undefined };
    render(<AppCommandBar commands={[command]} />);
    expect(screen.getAllByText("Other").length).toBeGreaterThan(0);
    expect(reasonShown()).toBe(true);
  });

  it("a selection command shows it too", () => {
    const command: AppCommandBarCommand = { id: "switch-off-selected", labelKey: "Switch off", requiresSelection: true, disabled: true, disabledReasonKey: REASON, onClick: () => undefined };
    render(<AppCommandBar commands={[command]} selectionCount={0} />);
    expect(screen.getByRole("button", { name: "Switch off" })).toHaveAttribute("aria-disabled", "true");
    expect(reasonShown()).toBe(true);
  });
});
