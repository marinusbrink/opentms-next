import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { StatusBadge } from "@/components/ui/status-badge";

/* StatusBadge – the states design 72 (order intake) adds, including the review's MailboxConnection.Activating (build
 * note 18: the activation job takes the provider's cursor; pending, so the info tone like Processing). Each state has its
 * module's label key and its tone (UI-TOK-005); the field resolution reads green / amber / red. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => `t(${key})`, culture: "en", isLoading: false }),
}));

describe("StatusBadge – the order intake's states (design 72)", () => {
  it.each([
    ["MailboxConnection", "AwaitingConsent", "Integrations:MailboxStatus.AwaitingConsent", "neutral"],
    ["MailboxConnection", "AwaitingFolder", "Integrations:MailboxStatus.AwaitingFolder", "warning"],
    ["MailboxConnection", "Activating", "Integrations:MailboxStatus.Activating", "info"],
    ["MailboxConnection", "Active", "Integrations:MailboxStatus.Active", "success"],
    ["MailboxConnection", "Paused", "Integrations:MailboxStatus.Paused", "neutral"],
    ["MailboxConnection", "Reconnect", "Integrations:MailboxStatus.Reconnect", "danger"],
    ["ApiClient", "Active", "Integrations:ApiClientStatus.Active", "success"],
    ["ApiClient", "Inactive", "Integrations:ApiClientStatus.Inactive", "neutral"],
    ["ImportProfile", "Proposed", "Integrations:ImportProfileStatus.Proposed", "info"],
    ["ImportProfile", "Active", "Integrations:ImportProfileStatus.Active", "success"],
    ["ImportProfile", "Retired", "Integrations:ImportProfileStatus.Retired", "neutral"],
    ["InboundMessage", "Received", "Integrations:InboundMessageStatus.Received", "neutral"],
    ["InboundMessage", "Processing", "Integrations:InboundMessageStatus.Processing", "info"],
    ["InboundMessage", "Completed", "Integrations:InboundMessageStatus.Completed", "success"],
    ["InboundMessage", "NeedsAttention", "Integrations:InboundMessageStatus.NeedsAttention", "warning"],
    ["InboundMessage", "Failed", "Integrations:InboundMessageStatus.Failed", "danger"],
    ["InboundMessage", "NotAnOrder", "Integrations:InboundMessageStatus.NotAnOrder", "neutral"],
    ["InboundDocument", "Pending", "Integrations:InboundDocumentStatus.Pending", "neutral"],
    ["InboundDocument", "NeedsReview", "Integrations:InboundDocumentStatus.NeedsReview", "warning"],
    ["InboundDocument", "Failed", "Integrations:InboundDocumentStatus.Failed", "danger"],
    ["OrderIntake", "NeedsReview", "Orders:OrderIntakeStatus.NeedsReview", "warning"],
    ["OrderIntake", "OrderCreated", "Orders:OrderIntakeStatus.OrderCreated", "success"],
    ["OrderIntake", "Rejected", "Orders:OrderIntakeStatus.Rejected", "neutral"],
    ["IntakeResolution", "Resolved", "Orders:IntakeResolutionStatus.Resolved", "success"],
    ["IntakeResolution", "Ambiguous", "Orders:IntakeResolutionStatus.Ambiguous", "warning"],
    ["IntakeResolution", "Unresolved", "Orders:IntakeResolutionStatus.Unresolved", "danger"],
  ])("%s %s shows %s in the %s tone", (entity, state, labelKey, tone) => {
    render(<StatusBadge entity={entity} state={state} />);

    const badge = screen.getByText(`t(${labelKey})`);
    expect(badge).toHaveAttribute("data-tone", tone);
    expect(badge.className).toContain(`bg-tone-${tone}-bg`);
    expect(badge.className).toContain(`text-tone-${tone}`);
  });

  it("an Activating connection is not shown as Active or as an error", () => {
    render(<StatusBadge entity="MailboxConnection" state="Activating" />);

    const badge = screen.getByText("t(Integrations:MailboxStatus.Activating)");
    expect(badge).not.toHaveAttribute("data-tone", "success");
    expect(badge).not.toHaveAttribute("data-tone", "danger");
  });

  it("a mailbox state the screen does not know yet stays visible with its raw name", () => {
    render(<StatusBadge entity="MailboxConnection" state="Syncing" />);

    expect(screen.getByText("Syncing")).toHaveAttribute("data-tone", "neutral");
  });
});
