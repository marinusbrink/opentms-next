import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "Change password" (docs/designs/63-user-menu.md): all three fields required, the repeat must match, the server checks
 * the rules; success toasts and closes, a refusal shows inline. Risk class: Critical — authentication (server side);
 * this UI part: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage: vi.fn() }),
}));

const changeMutate = vi.fn();
let changePending = false;
vi.mock("@/domains/platform/my-profile", () => ({
  useChangeMyPassword: () => ({ mutateAsync: changeMutate, isPending: changePending }),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: vi.fn() } }));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { ChangePasswordDialog } from "@/app/shell/user-menu/ChangePasswordDialog";

const type = (id: string, value: string) => fireEvent.change(document.getElementById(id)!, { target: { value } });
const submit = () =>
  act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "UserMenu:ChangePassword" }));
  });

beforeEach(() => {
  changePending = false;
  changeMutate.mockReset();
  toastSuccess.mockReset();
});

describe("ChangePasswordDialog", () => {
  it("labels the primary UserMenu:ChangingPassword and disables it while busy", () => {
    changePending = true;
    render(<ChangePasswordDialog open onClose={() => {}} />);
    expect(screen.getByRole("button", { name: "UserMenu:ChangingPassword" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "UserMenu:ChangePassword" })).not.toBeInTheDocument();
  });

  it("marks every empty field required and does not call the server", async () => {
    render(<ChangePasswordDialog open onClose={() => {}} />);
    expect(screen.queryByText("FieldRequired")).not.toBeInTheDocument();
    await submit();
    expect(screen.getAllByText("FieldRequired")).toHaveLength(3);
    expect(changeMutate).not.toHaveBeenCalled();
  });

  it("shows a mismatch of the repeated password and blocks the submit", async () => {
    render(<ChangePasswordDialog open onClose={() => {}} />);
    type("cp-current", "old");
    type("cp-new", "New-1234");
    type("cp-confirm", "New-12345");
    expect(screen.getByText("UserMenu:PasswordsDoNotMatch")).toBeInTheDocument();
    await submit();
    expect(changeMutate).not.toHaveBeenCalled();

    type("cp-confirm", "New-1234");
    expect(screen.queryByText("UserMenu:PasswordsDoNotMatch")).not.toBeInTheDocument();
  });

  it("changes the password, toasts and closes", async () => {
    changeMutate.mockResolvedValue(undefined);
    const onClose = vi.fn();
    render(<ChangePasswordDialog open onClose={onClose} />);
    type("cp-current", "old");
    type("cp-new", "New-1234");
    type("cp-confirm", "New-1234");
    await submit();

    expect(changeMutate).toHaveBeenCalledExactlyOnceWith({ currentPassword: "old", newPassword: "New-1234" });
    expect(toastSuccess).toHaveBeenCalledExactlyOnceWith("UserMenu:PasswordChanged");
    expect(onClose).toHaveBeenCalled();
  });

  it("shows the server's refusal inline and stays open", async () => {
    changeMutate.mockRejectedValue({ error: { message: "Incorrect password." } });
    const onClose = vi.fn();
    render(<ChangePasswordDialog open onClose={onClose} />);
    type("cp-current", "wrong");
    type("cp-new", "New-1234");
    type("cp-confirm", "New-1234");
    await submit();

    expect(screen.getByRole("alert")).toHaveTextContent("Incorrect password.");
    expect(toastSuccess).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });
});
