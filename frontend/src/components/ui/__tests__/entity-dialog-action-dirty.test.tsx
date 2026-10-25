import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";

/* An action dialog with input (UI-WIZ-001: a wizard past its first step) asks before Esc, the X or the backdrop
 * discard it (UI-DLG-008); without `isDirty` it still closes at once. Risk class: Medium (shared dialog behaviour). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { features: { values: {} } } }),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
vi.stubGlobal("ResizeObserver", MockResizeObserver);

function renderAction(isDirty: boolean) {
  const onOpenChange = vi.fn();
  render(
    <EntityDialog open onOpenChange={onOpenChange} variant="action" size="full" title="Generate invoices" isDirty={isDirty} footer={<Button variant="outline">Cancel</Button>}>
      <p>Body</p>
    </EntityDialog>,
  );
  return onOpenChange;
}

const headerClose = () => within(document.body.querySelector('[data-slot="entity-dialog-header"]') as HTMLElement).getByRole("button", { name: "Close" });

describe("EntityDialog action – isDirty", () => {
  it("asks before discarding when dirty, and closes only after Discard", async () => {
    const onOpenChange = renderAction(true);
    fireEvent.click(headerClose());
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(await screen.findByText("UnsavedChanges")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("Cancel in the discard question keeps the dialog open", async () => {
    const onOpenChange = renderAction(true);
    fireEvent.click(headerClose());
    const question = await screen.findByText("UnsavedChanges");
    fireEvent.click(within(question.closest('[role="alertdialog"]') as HTMLElement).getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it("closes at once when clean", () => {
    const onOpenChange = renderAction(false);
    fireEvent.click(headerClose());
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(screen.queryByText("UnsavedChanges")).toBeNull();
  });
});
