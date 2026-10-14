import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";

/* ConfirmDialog's confirmDisabled (tenants-and-editions: the irreversible tenant delete keeps Delete disabled
 * until the name is typed). Risk class: Medium (UI) guarding a Critical action. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "en", isLoading: false }),
}));

function renderDialog(confirmDisabled: boolean | undefined, onConfirm = vi.fn()) {
  const props = {
    open: true,
    onOpenChange: vi.fn(),
    title: "Delete tenant",
    description: "Delete tenant Acme? This cannot be undone.",
    confirmLabel: "Delete",
    destructive: true,
    onConfirm,
    ...(confirmDisabled === undefined ? {} : { confirmDisabled }),
  };
  return { ...render(<ConfirmDialog {...props} />), props };
}

describe("ConfirmDialog confirmDisabled", () => {
  it("disables the confirm button and a click runs nothing", () => {
    const onConfirm = vi.fn();
    renderDialog(true, onConfirm);

    const confirm = screen.getByRole("button", { name: "Delete" });
    expect(confirm).toBeDisabled();
    fireEvent.click(confirm);
    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled();
  });

  it("is enabled by default and when false", () => {
    const { unmount } = renderDialog(undefined);
    expect(screen.getByRole("button", { name: "Delete" })).toBeEnabled();
    unmount();

    renderDialog(false);
    expect(screen.getByRole("button", { name: "Delete" })).toBeEnabled();
  });

  it("enables once the condition clears", () => {
    const onConfirm = vi.fn();
    const { rerender, props } = renderDialog(true, onConfirm);
    expect(screen.getByRole("button", { name: "Delete" })).toBeDisabled();

    rerender(<ConfirmDialog {...props} confirmDisabled={false} />);
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(onConfirm).toHaveBeenCalledOnce();
  });
});
