import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "My settings" (docs/designs/63-user-menu.md): prefilled from the profile; the e-mail is read-only; "browser" is sent as
 * language null; the saved language applies at once through setUserLanguage; the remembered accounts can be
 * forgotten one by one. Risk class: Medium (UI flow on the user's own data). */

const setUserLanguage = vi.fn();
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage }),
}));

let profile: Record<string, unknown> | undefined;
const updateMutate = vi.fn();
vi.mock("@/domains/platform/my-profile", () => ({
  useMyProfile: () => ({ data: profile, isLoading: false }),
  useUpdateMyProfile: () => ({ mutateAsync: updateMutate, isPending: false }),
}));

let accounts: Array<Record<string, unknown>> = [];
const forgetMutate = vi.fn();
vi.mock("@/domains/platform/my-accounts", () => ({
  useMyAccounts: () => ({ data: accounts }),
  useForgetAccount: () => ({ mutateAsync: forgetMutate, isPending: false }),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: vi.fn() } }));

class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { MySettingsDialog } from "@/app/shell/user-menu/MySettingsDialog";

const field = (id: string) => document.getElementById(id) as HTMLInputElement;
const save = () =>
  act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
  });
const chooseLanguage = async (label: string) => {
  const trigger = field("ms-language");
  fireEvent.pointerDown(trigger);
  fireEvent.mouseDown(trigger);
  fireEvent.click(trigger);
  /* Base UI commits a mouse selection only for a click that started on the item. */
  const option = await screen.findByRole("option", { name: label });
  fireEvent.pointerDown(option, { pointerType: "mouse" });
  fireEvent.click(option);
};

beforeEach(() => {
  setUserLanguage.mockReset();
  updateMutate.mockReset();
  forgetMutate.mockReset();
  toastSuccess.mockReset();
  profile = { userName: "jdoe", name: "Jan", surname: "de Vries", phoneNumber: "0612345678", email: "jan@example.test", language: "nl" };
  accounts = [];
});

describe("MySettingsDialog", () => {
  it("prefills the fields from the profile and shows the e-mail read-only", () => {
    render(<MySettingsDialog open onClose={() => {}} />);
    expect(field("ms-name").value).toBe("Jan");
    expect(field("ms-surname").value).toBe("de Vries");
    expect(field("ms-phone").value).toBe("0612345678");
    expect(field("ms-email").value).toBe("jan@example.test");
    expect(field("ms-email")).toHaveAttribute("readonly");
    expect(field("ms-email")).toBeDisabled();
    expect(field("ms-language")).toHaveTextContent("Nederlands");
  });

  it("shows the browser option when the profile has no language", () => {
    profile = { ...profile, language: null };
    render(<MySettingsDialog open onClose={() => {}} />);
    expect(field("ms-language")).toHaveTextContent("UserMenu:LanguageBrowser");
  });

  it("sends the edited fields, trims them and sends empty ones as null", async () => {
    updateMutate.mockResolvedValue({ ...profile });
    render(<MySettingsDialog open onClose={() => {}} />);
    fireEvent.change(field("ms-name"), { target: { value: "  Johan " } });
    fireEvent.change(field("ms-phone"), { target: { value: "   " } });
    await save();

    expect(updateMutate).toHaveBeenCalledExactlyOnceWith({ name: "Johan", surname: "de Vries", phoneNumber: null, language: "nl" });
  });

  it("sends the browser choice as language null and applies the saved language", async () => {
    updateMutate.mockResolvedValue({ ...profile, language: null });
    const onClose = vi.fn();
    render(<MySettingsDialog open onClose={onClose} />);
    await chooseLanguage("UserMenu:LanguageBrowser");
    await save();

    expect(updateMutate).toHaveBeenCalledWith(expect.objectContaining({ language: null }));
    expect(setUserLanguage).toHaveBeenCalledExactlyOnceWith(null);
    expect(toastSuccess).toHaveBeenCalledWith("UserMenu:SettingsSaved");
    expect(onClose).toHaveBeenCalled();
  });

  it("applies a chosen language through setUserLanguage with what the server saved", async () => {
    updateMutate.mockResolvedValue({ ...profile, language: "en" });
    render(<MySettingsDialog open onClose={() => {}} />);
    await chooseLanguage("English");
    await save();

    expect(updateMutate).toHaveBeenCalledWith(expect.objectContaining({ language: "en" }));
    expect(setUserLanguage).toHaveBeenCalledExactlyOnceWith("en");
  });

  it("shows a save error inline, keeps the language and stays open", async () => {
    updateMutate.mockRejectedValue({ error: { message: "Phone number is invalid." } });
    const onClose = vi.fn();
    render(<MySettingsDialog open onClose={onClose} />);
    await save();

    expect(screen.getByRole("alert")).toHaveTextContent("Phone number is invalid.");
    expect(setUserLanguage).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("lists only the remembered accounts and forgets one (host as null)", async () => {
    accounts = [
      { tenantId: "t-acme", tenantName: "acme", isRemembered: true, needsPassword: false },
      { tenantId: "t-beta", tenantName: "beta", isRemembered: false, needsPassword: true },
      { tenantId: null, tenantName: null, isRemembered: true, needsPassword: false },
    ];
    forgetMutate.mockResolvedValue(undefined);
    render(<MySettingsDialog open onClose={() => {}} />);

    const rows = screen.getAllByRole("listitem");
    expect(rows.map((r) => r.querySelector("span")?.textContent)).toEqual(["acme", "Shell:TenantHost"]);

    await act(async () => fireEvent.click(within(rows[0]).getByRole("button", { name: "UserMenu:Forget" })));
    expect(forgetMutate).toHaveBeenLastCalledWith("t-acme");
    await act(async () => fireEvent.click(within(rows[1]).getByRole("button", { name: "UserMenu:Forget" })));
    expect(forgetMutate).toHaveBeenLastCalledWith(null);
    expect(updateMutate).not.toHaveBeenCalled();
  });

  it("has no Remembered section when nothing is remembered", () => {
    accounts = [{ tenantId: "t-beta", tenantName: "beta", isRemembered: false, needsPassword: true }];
    render(<MySettingsDialog open onClose={() => {}} />);
    expect(screen.queryByText("UserMenu:Remembered")).not.toBeInTheDocument();
  });

  it("shows a failed Stop remembering inline", async () => {
    accounts = [{ tenantId: "t-acme", tenantName: "acme", isRemembered: true, needsPassword: false }];
    forgetMutate.mockRejectedValue({ error: { message: "Not found." } });
    render(<MySettingsDialog open onClose={() => {}} />);
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "UserMenu:Forget" })));
    expect(screen.getByRole("alert")).toHaveTextContent("Not found.");
  });
});
