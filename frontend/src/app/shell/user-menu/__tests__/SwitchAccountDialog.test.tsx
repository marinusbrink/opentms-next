import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The password prompt for switching to another account of the same e-mail address (docs/designs/63-user-menu.md):
 * posts { tenantId, password, rememberPassword }, then switches the session into that tenant (host → no tenant); a
 * refusal shows inline and does not leave. Risk class: Critical — authentication; this UI part: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage: vi.fn() }),
}));

const switchMutate = vi.fn();
vi.mock("@/domains/platform/my-accounts", () => ({
  useSwitchAccount: () => ({ mutateAsync: switchMutate, isPending: false }),
}));

const switchSession = vi.fn();
vi.mock("@/auth/session-switch", () => ({
  switchSession: (...args: unknown[]) => switchSession(...args),
}));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { SwitchAccountDialog } from "@/app/shell/user-menu/SwitchAccountDialog";

const ACME = { tenantId: "t-acme", tenantName: "acme", needsPassword: true, isRemembered: false } as never;
const HOST = { tenantId: null, tenantName: null, needsPassword: true, isRemembered: false } as never;

const password = () => document.getElementById("switch-password") as HTMLInputElement;
const switchButton = () => screen.getByRole("button", { name: "UserMenu:Switch" });

beforeEach(() => {
  switchMutate.mockReset();
  switchSession.mockReset();
});

describe("SwitchAccountDialog", () => {
  it("needs a password before Switch is enabled", () => {
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={() => {}} />);
    expect(switchButton()).toBeDisabled();
    fireEvent.change(password(), { target: { value: "secret" } });
    expect(switchButton()).toBeEnabled();
  });

  it("posts the password without remembering by default and switches into the tenant", async () => {
    switchMutate.mockResolvedValue("https://auth.example.test/s");
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={() => {}} />);
    fireEvent.change(password(), { target: { value: "secret" } });
    await act(async () => fireEvent.click(switchButton()));

    expect(switchMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: "t-acme", password: "secret", rememberPassword: false });
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/s", { id: "t-acme", name: "acme" }, window.location.pathname);
  });

  it("sends rememberPassword when Remember password is ticked", async () => {
    switchMutate.mockResolvedValue("https://auth.example.test/s");
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={() => {}} />);
    fireEvent.change(password(), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("checkbox"));
    await act(async () => fireEvent.click(switchButton()));

    expect(switchMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: "t-acme", password: "secret", rememberPassword: true });
  });

  it("switches to the host account with no tenant", async () => {
    switchMutate.mockResolvedValue("https://auth.example.test/h");
    render(<SwitchAccountDialog account={HOST} name="Host" open onClose={() => {}} />);
    fireEvent.change(password(), { target: { value: "secret" } });
    await act(async () => fireEvent.click(switchButton()));

    expect(switchMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: null, password: "secret", rememberPassword: false });
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/h", undefined, window.location.pathname);
  });

  it("Enter in the password field submits", async () => {
    switchMutate.mockResolvedValue("https://auth.example.test/s");
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={() => {}} />);
    fireEvent.change(password(), { target: { value: "secret" } });
    await act(async () => {
      fireEvent.submit(password().closest("form")!);
    });
    expect(switchMutate).toHaveBeenCalledOnce();
  });

  it("shows the server's refusal inline and does not leave", async () => {
    switchMutate.mockRejectedValue({ error: { message: "Invalid password." } });
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={() => {}} />);
    fireEvent.change(password(), { target: { value: "wrong" } });
    await act(async () => fireEvent.click(switchButton()));

    expect(screen.getByRole("alert")).toHaveTextContent("Invalid password.");
    expect(switchSession).not.toHaveBeenCalled();
  });

  it("Cancel closes without switching", () => {
    const onClose = vi.fn();
    render(<SwitchAccountDialog account={ACME} name="acme" open onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
    expect(switchMutate).not.toHaveBeenCalled();
  });
});
