import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { StatusBadge } from "@/components/ui/status-badge";
import { AI_CONNECTION_STATUSES, canSwitchOffAiConnection } from "@/domains/integrations/ai-connector";

/* StatusBadge: the states design 74 adds. A connection's status (PO decision 6) and the acceptance of the data sharing, each with
 * its label key in the Integrations resource and its tone (UI-TOK-005). Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => `t(${key})`, culture: "en", isLoading: false }),
}));

describe("StatusBadge: the AI connector's states (design 74)", () => {
  it.each([
    ["AiConnection", "Live", "AiConnector:State.Live", "success"],
    ["AiConnection", "NotWorking", "AiConnector:State.NotWorking", "warning"],
    ["AiConnection", "Expired", "AiConnector:State.Expired", "neutral"],
    ["AiConnection", "SwitchedOff", "AiConnector:State.SwitchedOff", "neutral"],
    ["AiConnectorSharing", "Accepted", "AiConnector:Status.Accepted", "success"],
    ["AiConnectorSharing", "NotAccepted", "AiConnector:Status.NotAccepted", "warning"],
  ])("%s %s shows %s in the %s tone", (entity, state, labelKey, tone) => {
    render(<StatusBadge entity={entity} state={state} />);

    const badge = screen.getByText(`t(${labelKey})`);
    expect(badge).toHaveAttribute("data-tone", tone);
    expect(badge.className).toContain(`bg-tone-${tone}-bg`);
    expect(badge.className).toContain(`text-tone-${tone}`);
  });

  it("only a Live connection reads as healthy", () => {
    for (const state of ["NotWorking", "Expired", "SwitchedOff"]) {
      const { unmount } = render(<StatusBadge entity="AiConnection" state={state} />);
      expect(screen.getByText(`t(AiConnector:State.${state})`)).not.toHaveAttribute("data-tone", "success");
      unmount();
    }
  });

  it("every value of the contract's enum has a state of the badge", () => {
    expect(AI_CONNECTION_STATUSES).toEqual({ 1: "Live", 2: "NotWorking", 3: "Expired", 4: "SwitchedOff" });
    for (const state of Object.values(AI_CONNECTION_STATUSES)) {
      const { unmount } = render(<StatusBadge entity="AiConnection" state={state} />);
      expect(screen.getByText(`t(AiConnector:State.${state})`)).toBeInTheDocument();
      unmount();
    }
  });

  it("every row but a switched-off one can be switched off", () => {
    expect([1, 2, 3, 4].map((status) => canSwitchOffAiConnection({ status: status as 1 | 2 | 3 | 4 }))).toEqual([true, true, true, false]);
  });
});
