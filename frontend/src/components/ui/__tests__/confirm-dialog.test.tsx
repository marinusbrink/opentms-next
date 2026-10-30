import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

function renderDialog(overrides: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
  const props = {
    open: true,
    onOpenChange: vi.fn(),
    title: "Delete transport order",
    description: "Delete transport order 2026-01234? This cannot be undone.",
    confirmLabel: "Delete",
    busyLabel: "Deleting",
    destructive: true,
    onConfirm: vi.fn(),
    ...overrides,
  };
  render(<ConfirmDialog {...props} />);
  return props;
}

describe("ConfirmDialog (UI-DLG-011, UI-ACT-003/005, UI-KEY-003)", () => {
  it("names the object and the verb; Cancel sits directly left of the primary", () => {
    renderDialog();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(screen.getByText("Delete transport order")).toBeInTheDocument();
    const buttons = screen.getAllByRole("button");
    expect(buttons.at(-2)).toHaveTextContent("Cancel");
    expect(buttons.at(-1)).toHaveTextContent("Delete");
  });

  it("starts with focus on Cancel for a destructive confirmation", async () => {
    renderDialog();
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Cancel"));
  });

  it("starts with focus on the confirm button for a non-destructive confirmation", async () => {
    renderDialog({ destructive: false, confirmLabel: "Archive" });
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Archive"));
  });

  it("runs the action and shows the busy label while it runs", async () => {
    let finish: () => void = () => undefined;
    const onConfirm = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)));
    renderDialog({ onConfirm });

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(onConfirm).toHaveBeenCalledOnce();
    await waitFor(() => expect(screen.getByRole("button", { name: "Deleting" })).toBeDisabled());
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();

    finish();
    await waitFor(() => expect(screen.getByRole("button", { name: "Delete" })).not.toBeDisabled());
  });

  it("shows a failure inside the dialog and keeps it open", async () => {
    const onOpenChange = vi.fn();
    renderDialog({
      onOpenChange,
      onConfirm: vi.fn().mockRejectedValue({ error: { message: "Own account cannot be deleted" } }),
    });

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(screen.getByText("Own account cannot be deleted")).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });

  it("Cancel closes through onOpenChange(false)", () => {
    const props = renderDialog();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(props.onOpenChange).toHaveBeenCalledWith(false);
  });

  it("names the closing button with cancelLabel when given (e.g. Keep editing)", () => {
    const props = renderDialog({ cancelLabel: "Keep editing" });
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Keep editing" }));
    expect(props.onOpenChange).toHaveBeenCalledWith(false);
  });
});
