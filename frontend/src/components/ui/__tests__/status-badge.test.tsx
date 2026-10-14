import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { StatusBadge, StatusCell } from "@/components/ui/status-badge";

/* StatusBadge (UI-TOK-005, UI-GRID-004): the tenant provisioning state in the Tenants list and dialog.
 * Risk class: Medium — a wrong tone or a hidden unknown state misleads the operator about a tenant's database. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => `t(${key})`, culture: "en", isLoading: false }),
}));

describe("StatusBadge", () => {
  it.each([
    ["Queued", "neutral"],
    ["Running", "info"],
    ["Succeeded", "success"],
    ["Failed", "danger"],
  ])("shows the localized label and the %s tone for a known state", (state, tone) => {
    render(<StatusBadge entity="TenantProvisioning" state={state} />);

    const badge = screen.getByText(`t(Tenants:State.${state})`);
    expect(badge).toHaveAttribute("data-tone", tone);
    expect(badge.className).toContain(`bg-tone-${tone}-bg`);
  });

  it("shows an unknown state with its raw name in the neutral tone, never hidden", () => {
    render(<StatusBadge entity="TenantProvisioning" state="Paused" />);

    const badge = screen.getByText("Paused");
    expect(badge).toHaveAttribute("data-tone", "neutral");
  });

  it("shows an unknown entity's state raw and neutral", () => {
    render(<StatusBadge entity="NoSuchEntity" state="Queued" />);

    expect(screen.getByText("Queued")).toHaveAttribute("data-tone", "neutral");
  });

  it.each([null, undefined, ""])("renders nothing for %s", (state) => {
    const { container } = render(<StatusBadge entity="TenantProvisioning" state={state} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("works as a grid cell", () => {
    render(<StatusCell entity="TenantProvisioning" value="Failed" />);

    expect(screen.getByText("t(Tenants:State.Failed)")).toHaveAttribute("data-tone", "danger");
  });
});
