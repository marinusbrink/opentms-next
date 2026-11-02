import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Reset password on a user: sending a reset link by e-mail is the default (the user chooses the password);
 * setting one directly stays as the alternative and then requires the password.
 * Risk class: High — account access. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: vi.fn() } }));

const resetMutateAsync = vi.fn();
const sendLinkMutateAsync = vi.fn();
vi.mock("@/domains/platform/administration-users", () => ({
  useResetPassword: () => ({ mutateAsync: resetMutateAsync, isPending: false }),
  useSendPasswordResetLink: () => ({ mutateAsync: sendLinkMutateAsync, isPending: false }),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

beforeEach(() => {
  resetMutateAsync.mockReset();
  sendLinkMutateAsync.mockReset();
  toastSuccess.mockReset();
});

import { ResetPasswordDialog } from "@/apps/admin/components/ResetPasswordDialog";

const submit = () =>
  act(async () => {
    fireEvent.submit(document.getElementById("reset-password-form")!);
  });

describe("ResetPasswordDialog", () => {
  it("sends a reset link by default, without asking for a password", async () => {
    sendLinkMutateAsync.mockResolvedValue(undefined);
    const onClose = vi.fn();
    render(<ResetPasswordDialog userId="u1" userName="j.devries" open onClose={onClose} />);

    expect(screen.getByRole("radio", { name: "Administration:SendResetLink" })).toHaveAttribute("aria-checked", "true");
    expect(document.getElementById("rp-password")).toBeNull();
    expect(screen.getByRole("button", { name: "Administration:SendLink" })).toBeInTheDocument();

    await submit();

    expect(sendLinkMutateAsync).toHaveBeenCalledWith("u1");
    expect(resetMutateAsync).not.toHaveBeenCalled();
    expect(toastSuccess).toHaveBeenCalledWith("Administration:PasswordResetLinkSent");
    expect(onClose).toHaveBeenCalled();
  });

  it("shows a failed send inside the dialog and stays open", async () => {
    sendLinkMutateAsync.mockRejectedValue({ error: { message: "E-mail delivery is off" } });
    const onClose = vi.fn();
    render(<ResetPasswordDialog userId="u1" userName="j.devries" open onClose={onClose} />);

    await submit();

    expect(screen.getByText("E-mail delivery is off")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("with 'set a password myself' requires the password and sets it", async () => {
    resetMutateAsync.mockResolvedValue(undefined);
    render(<ResetPasswordDialog userId="u1" userName="j.devries" open onClose={vi.fn()} />);

    fireEvent.click(screen.getByRole("radio", { name: "Administration:SetPasswordMyself" }));
    await submit();
    expect(screen.getByText("FieldRequired")).toBeInTheDocument();
    expect(resetMutateAsync).not.toHaveBeenCalled();

    fireEvent.change(document.getElementById("rp-password")!, { target: { value: "N3w-P@ss" } });
    await submit();

    expect(resetMutateAsync).toHaveBeenCalledWith({ id: "u1", newPassword: "N3w-P@ss" });
    expect(sendLinkMutateAsync).not.toHaveBeenCalled();
    expect(toastSuccess).toHaveBeenCalledWith("Administration:PasswordReset");
  });
});
