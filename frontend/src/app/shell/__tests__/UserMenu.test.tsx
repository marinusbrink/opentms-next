import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The user menu (docs/designs/63-user-menu.md): picture or initials, name, e-mail and the tenant ABP resolved; the
 * other accounts of the user's e-mail address (lock icon when a password is needed); the direct switch and its
 * PasswordRequired fallback; "Log in to a tenant…" only for a host user with the Impersonation permission who is not
 * impersonating. The child dialogs are stand-ins exposing their props.
 * Risk class: Critical — authentication (account switch, impersonation); this UI part: Medium (component tests). */

const setUserLanguage = vi.fn();
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage }),
}));

const signoutRedirect = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ user: { profile: { preferred_username: "jdoe" } }, signoutRedirect }),
}));

let config: Record<string, unknown> | undefined;
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: config }),
}));

let profile: Record<string, unknown> | undefined;
let pictureUrl: string | null = null;
const pictureUrlCalls: boolean[] = [];
const updateMutate = vi.fn();
vi.mock("@/domains/platform/my-profile", () => ({
  useMyProfile: () => ({ data: profile }),
  useUpdateMyProfile: () => ({ mutateAsync: updateMutate, isPending: false }),
  useMyProfilePictureUrl: (hasPicture: boolean) => {
    pictureUrlCalls.push(hasPicture);
    return pictureUrl;
  },
}));

let accounts: Array<Record<string, unknown>> = [];
const switchMutate = vi.fn();
vi.mock("@/domains/platform/my-accounts", () => ({
  ACCOUNT_PASSWORD_REQUIRED: "OpenTms.Platform:AccountPasswordRequired",
  useMyAccounts: () => ({ data: accounts }),
  useSwitchAccount: () => ({ mutateAsync: switchMutate, isPending: false }),
}));

const switchSession = vi.fn();
vi.mock("@/auth/session-switch", () => ({
  switchSession: (...args: unknown[]) => switchSession(...args),
}));

const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { error: (...args: unknown[]) => toastError(...args), success: vi.fn() } }));

/* Base UI's AvatarImage renders only after the image loaded, which never happens in jsdom: a plain stand-in. */
vi.mock("@/components/ui/avatar", () => ({
  Avatar: ({ children }: { children: React.ReactNode }) => <span data-testid="avatar">{children}</span>,
  AvatarImage: ({ src }: { src: string }) => <img data-testid="avatar-image" src={src} alt="" />,
  AvatarFallback: ({ children }: { children: React.ReactNode }) => <span data-testid="avatar-fallback">{children}</span>,
}));

vi.mock("@/app/shell/user-menu/SwitchAccountDialog", () => ({
  SwitchAccountDialog: (p: { account: { tenantId?: string | null }; name: string }) => (
    <div data-testid="switch-dialog" data-tenant={p.account.tenantId ?? "host"} data-name={p.name} />
  ),
}));
vi.mock("@/app/shell/user-menu/TenantPickerDialog", () => ({
  TenantPickerDialog: (p: { onPick: (row: { id: string; name: string }) => void }) => (
    <div data-testid="tenant-picker">
      <button onClick={() => p.onPick({ id: "t-acme", name: "acme" })}>pick-acme</button>
    </div>
  ),
}));
vi.mock("@/domains/platform/components/ImpersonateTenantDialog", () => ({
  ImpersonateTenantDialog: (p: { tenant: { id: string; name: string } }) => (
    <div data-testid="impersonate-dialog" data-tenant={p.tenant.id} data-name={p.tenant.name} />
  ),
}));
vi.mock("@/app/shell/user-menu/MySettingsDialog", () => ({ MySettingsDialog: () => <div data-testid="settings-dialog" /> }));
vi.mock("@/app/shell/user-menu/ChangeProfilePictureDialog", () => ({
  ChangeProfilePictureDialog: () => <div data-testid="picture-dialog" />,
}));
vi.mock("@/app/shell/user-menu/ChangePasswordDialog", () => ({ ChangePasswordDialog: () => <div data-testid="password-dialog" /> }));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => vi.stubGlobal("ResizeObserver", MockResizeObserver));

import { UserMenu } from "@/app/shell/UserMenu";

const HOST_CONFIG = (extra: Record<string, unknown> = {}) => ({
  currentTenant: { id: null, name: null },
  currentUser: { userName: "jdoe" },
  auth: { grantedPolicies: { "Platform.Tenants.Impersonation": true } },
  ...extra,
});
const TENANT_CONFIG = (extra: Record<string, unknown> = {}) => ({
  currentTenant: { id: "t-home", name: "home" },
  currentUser: { userName: "jdoe" },
  auth: { grantedPolicies: { "Platform.Tenants.Impersonation": true } },
  ...extra,
});

const trigger = () => screen.getByRole("button", { name: "Shell:SignedInAs" });
const openMenu = async () => {
  await act(async () => fireEvent.click(trigger()));
};
/* A menu row's accessible name is its label followed by its hint, so a string matches the start of it. */
const item = (name: string | RegExp) =>
  screen.getByRole("menuitem", { name: typeof name === "string" ? new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`) : name });

beforeEach(() => {
  setUserLanguage.mockReset();
  updateMutate.mockReset();
  signoutRedirect.mockReset();
  switchMutate.mockReset();
  switchSession.mockReset();
  toastError.mockReset();
  pictureUrlCalls.length = 0;
  config = TENANT_CONFIG();
  profile = { userName: "jdoe", name: "Jan", surname: "de Vries", email: "jan@example.test", hasProfilePicture: false, language: "nl" };
  pictureUrl = null;
  accounts = [];
});

describe("UserMenu trigger", () => {
  it("shows the initials of name and surname without a picture", () => {
    render(<UserMenu />);
    expect(within(trigger()).queryByTestId("avatar-image")).not.toBeInTheDocument();
    expect(within(trigger()).getByTestId("avatar-fallback")).toHaveTextContent("JD");
    expect(pictureUrlCalls.at(-1)).toBe(false);
  });

  it("shows the profile picture when the user has one", () => {
    profile = { ...profile, hasProfilePicture: true };
    pictureUrl = "blob:me";
    render(<UserMenu />);
    expect(pictureUrlCalls.at(-1)).toBe(true);
    expect(within(trigger()).getByTestId("avatar-image")).toHaveAttribute("src", "blob:me");
  });

  it("falls back to the user name's first letters without a name", () => {
    profile = { userName: "jdoe", hasProfilePicture: false };
    render(<UserMenu />);
    expect(within(trigger()).getByTestId("avatar-fallback")).toHaveTextContent("JD");
  });

  it("shows the tenant ABP resolved, or Host on the host side", () => {
    const { unmount } = render(<UserMenu />);
    expect(trigger()).toHaveTextContent("home");
    unmount();
    config = HOST_CONFIG();
    render(<UserMenu />);
    expect(trigger()).toHaveTextContent("Shell:TenantHost");
  });

  it("applies the profile's language once the profile loaded", () => {
    render(<UserMenu />);
    expect(setUserLanguage).toHaveBeenCalledWith("nl");
  });

  it("does not touch the language before the profile loaded", () => {
    profile = undefined;
    render(<UserMenu />);
    expect(setUserLanguage).not.toHaveBeenCalled();
  });
});

describe("UserMenu content", () => {
  it("shows name, e-mail and resolved tenant", async () => {
    render(<UserMenu />);
    await openMenu();
    expect(screen.getByText("Jan de Vries")).toBeInTheDocument();
    expect(screen.getByText("jan@example.test")).toBeInTheDocument();
    /* The trigger and the header's organisation chip both name it. */
    expect(screen.getAllByText("home")).toHaveLength(2);
  });

  it("lists the other accounts under Switch to, with a lock icon only where a password is needed", async () => {
    accounts = [
      { tenantId: null, tenantName: null, needsPassword: false },
      { tenantId: "t-acme", tenantName: "acme", needsPassword: true },
    ];
    render(<UserMenu />);
    await openMenu();
    expect(screen.getByText("UserMenu:SwitchTo")).toBeInTheDocument();
    const host = item(/Shell:TenantHost/);
    const acme = item(/acme/);
    expect(within(host).queryByLabelText("UserMenu:NeedsPassword")).not.toBeInTheDocument();
    expect(within(acme).getByLabelText("UserMenu:NeedsPassword")).toBeInTheDocument();
  });

  it("shows the tenant id in a tenant", async () => {
    render(<UserMenu />);
    await openMenu();
    expect(screen.getByText(/UserMenu:TenantId/)).toBeInTheDocument();
    expect(screen.getByText("t-home")).toBeInTheDocument();
  });

  it("copies the tenant id", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "UserMenu:CopyTenantId" })));
    expect(writeText).toHaveBeenCalledWith("t-home");
  });

  it("the language switch saves the chosen language with the profile and applies it", async () => {
    config = TENANT_CONFIG({ localization: { languages: [{ cultureName: "nl", displayName: "Nederlands" }, { cultureName: "en", displayName: "English" }] } });
    updateMutate.mockResolvedValue({ ...profile, language: "nl" });
    render(<UserMenu />);
    await openMenu();
    const group = screen.getByRole("group", { name: "UserMenu:Language" });
    expect(within(group).getByRole("button", { name: "en" })).toHaveAttribute("aria-pressed", "true");
    setUserLanguage.mockReset();
    await act(async () => fireEvent.click(within(group).getByRole("button", { name: "nl" })));
    expect(updateMutate).toHaveBeenCalledWith({ name: "Jan", surname: "de Vries", phoneNumber: null, language: "nl" });
    expect(setUserLanguage).toHaveBeenCalledWith("nl");
  });

  it("the language switch does nothing for the language already shown", async () => {
    config = TENANT_CONFIG({ localization: { languages: [{ cultureName: "nl" }, { cultureName: "en" }] } });
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(within(screen.getByRole("group", { name: "UserMenu:Language" })).getByRole("button", { name: "en" })));
    expect(updateMutate).not.toHaveBeenCalled();
  });

  it("the picture in the header opens Change profile picture", async () => {
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(screen.getByRole("button", { name: "UserMenu:ChangeProfilePicture" })));
    expect(screen.getByTestId("picture-dialog")).toBeInTheDocument();
  });

  it("shows no tenant id on the host", async () => {
    config = HOST_CONFIG();
    render(<UserMenu />);
    await openMenu();
    expect(screen.queryByText(/UserMenu:TenantId/)).not.toBeInTheDocument();
  });

  it("has no Switch to group without other accounts", async () => {
    render(<UserMenu />);
    await openMenu();
    expect(screen.queryByText("UserMenu:SwitchTo")).not.toBeInTheDocument();
  });

  it("Sign out signs out", async () => {
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item("Shell:SignOut")));
    expect(signoutRedirect).toHaveBeenCalledOnce();
  });

  it.each([
    ["UserMenu:MySettings", "settings-dialog"],
    ["UserMenu:ChangeProfilePicture", "picture-dialog"],
    ["UserMenu:ChangePassword", "password-dialog"],
  ])("%s opens its dialog", async (label, testId) => {
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(label)));
    expect(screen.getByTestId(testId)).toBeInTheDocument();
  });
});

describe("UserMenu account switch", () => {
  it("switches directly to a tenant account and hands the tenant to the session switch", async () => {
    accounts = [{ tenantId: "t-acme", tenantName: "acme", needsPassword: false }];
    switchMutate.mockResolvedValue("https://auth.example.test/switch?t=1");
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(/acme/)));

    expect(switchMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: "t-acme" });
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/switch?t=1", { id: "t-acme", name: "acme" });
    expect(screen.queryByTestId("switch-dialog")).not.toBeInTheDocument();
  });

  it("switches to the host account with no tenant", async () => {
    accounts = [{ tenantId: null, tenantName: null, needsPassword: false }];
    switchMutate.mockResolvedValue("https://auth.example.test/switch?h=1");
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(/Shell:TenantHost/)));

    expect(switchMutate).toHaveBeenCalledExactlyOnceWith({ tenantId: null });
    expect(switchSession).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/switch?h=1", undefined);
  });

  it("opens the password dialog for an account that needs a password, without calling the switch", async () => {
    accounts = [{ tenantId: "t-acme", tenantName: "acme", needsPassword: true }];
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(/acme/)));

    expect(switchMutate).not.toHaveBeenCalled();
    const dialog = screen.getByTestId("switch-dialog");
    expect(dialog.dataset.tenant).toBe("t-acme");
    expect(dialog.dataset.name).toBe("acme");
  });

  it("opens the password dialog after all when the server answers AccountPasswordRequired", async () => {
    accounts = [{ tenantId: "t-acme", tenantName: "acme", needsPassword: false }];
    switchMutate.mockRejectedValue({ error: { code: "OpenTms.Platform:AccountPasswordRequired", message: "Password required." } });
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(/acme/)));

    expect(switchSession).not.toHaveBeenCalled();
    expect(toastError).not.toHaveBeenCalled();
    expect(screen.getByTestId("switch-dialog").dataset.tenant).toBe("t-acme");
  });

  it("shows any other switch error as a toast and stays", async () => {
    accounts = [{ tenantId: "t-acme", tenantName: "acme", needsPassword: false }];
    switchMutate.mockRejectedValue({ error: { code: "Something.Else", message: "This account is locked." } });
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item(/acme/)));

    expect(toastError).toHaveBeenCalledExactlyOnceWith("This account is locked.");
    expect(switchSession).not.toHaveBeenCalled();
    expect(screen.queryByTestId("switch-dialog")).not.toBeInTheDocument();
  });
});

describe("UserMenu Log in to a tenant", () => {
  it("is offered on the host side with the Impersonation permission when not impersonating", async () => {
    config = HOST_CONFIG();
    render(<UserMenu />);
    await openMenu();
    expect(item("UserMenu:AllTenants")).toBeInTheDocument();
  });

  it.each([
    ["on the tenant side", () => TENANT_CONFIG()],
    ["without the permission", () => HOST_CONFIG({ auth: { grantedPolicies: {} } })],
    ["while impersonating", () => HOST_CONFIG({ currentUser: { userName: "jdoe", impersonatorUserId: "u-admin" } })],
    ["before the configuration loaded", () => undefined],
  ])("is hidden %s", async (_label, make) => {
    config = make();
    render(<UserMenu />);
    await openMenu();
    expect(screen.queryByRole("menuitem", { name: "UserMenu:AllTenants" })).not.toBeInTheDocument();
  });

  it("opens the tenant picker, and the chosen tenant opens the Log in as dialog", async () => {
    config = HOST_CONFIG();
    render(<UserMenu />);
    await openMenu();
    await act(async () => fireEvent.click(item("UserMenu:AllTenants")));
    expect(screen.getByTestId("tenant-picker")).toBeInTheDocument();

    await act(async () => fireEvent.click(screen.getByRole("button", { name: "pick-acme" })));
    expect(screen.queryByTestId("tenant-picker")).not.toBeInTheDocument();
    const dialog = screen.getByTestId("impersonate-dialog");
    expect(dialog.dataset.tenant).toBe("t-acme");
    expect(dialog.dataset.name).toBe("acme");
  });
});
