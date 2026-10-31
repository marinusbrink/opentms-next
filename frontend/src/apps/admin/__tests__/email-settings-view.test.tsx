import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { useApplicationConfiguration } from "@/lib/abp/queries";

/* The host's e-mail settings page (docs/designs/64-email-delivery-postmark.md, "Host e-mail settings"). Risk class:
 * Critical — the page configures every tenant's mail: it renders on the host side only and with its permission, never
 * shows a stored secret and never sends an empty secret as a change; High — the save payload, the test mail and its
 * disabled states; Medium — the hints. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...args: unknown[]) => toastSuccess(...args), error: vi.fn() } }));

const updateMutateAsync = vi.fn();
const testMutateAsync = vi.fn();
let settingsData: Record<string, unknown>;
vi.mock("@/domains/platform/administration-email", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/domains/platform/administration-email")>();
  return {
    ...original,
    useEmailSettings: () => ({ data: settingsData, isError: false, error: undefined }),
    useUpdateEmailSettings: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useSendTestEmail: () => ({ mutateAsync: testMutateAsync, isPending: false }),
  };
});

const { EmailSettingsView } = await import("@/apps/admin/email/EmailSettingsView");

const ALL = ["Platform.Administration.EmailSettings", "Platform.Administration.EmailSettings.Update", "Platform.Administration.EmailSettings.SendTest"];

function configure({ host = true, granted = ALL }: { host?: boolean; granted?: string[] } = {}) {
  vi.mocked(useApplicationConfiguration).mockReturnValue({
    data: {
      currentTenant: { id: host ? null : "11111111-1111-1111-1111-111111111111" },
      auth: { grantedPolicies: Object.fromEntries(granted.map((p) => [p, true])) },
    },
  } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

function stored(overrides: Record<string, unknown> = {}) {
  return {
    hasServerToken: true,
    fromAddress: null,
    fromName: "Ryd",
    trackOpens: null,
    trackLinks: null,
    webhookUsername: "postmark",
    hasWebhookPassword: true,
    webhookUrl: "https://api.example.test/api/integrations/postmark/webhook",
    effective: { isConfigured: true, serverTokenSource: 1, fromAddress: "noreply@ryd-software.com", fromName: "Ryd", trackOpens: true, trackLinks: "HtmlAndText", isWebhookConfigured: true },
    ...overrides,
  };
}

const command = (label: string) => screen.queryByRole("button", { name: label });
/* The command bar keeps a disabled command focusable for its reason tooltip: aria-disabled, not disabled. */
const isDisabled = (label: string) => command(label)?.getAttribute("aria-disabled") === "true";

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", class { observe() {} disconnect() {} unobserve() {} });
});

beforeEach(() => {
  vi.clearAllMocks();
  settingsData = stored();
  configure();
  updateMutateAsync.mockResolvedValue(undefined);
});

describe("EmailSettingsView", () => {
  it("is refused on the tenant side and without the view permission", () => {
    configure({ host: false });
    const { unmount } = render(<EmailSettingsView />);
    expect(document.querySelector("#email-server-token")).toBeNull();
    unmount();

    configure({ granted: [] });
    render(<EmailSettingsView />);
    expect(document.querySelector("#email-server-token")).toBeNull();
  });

  it("never shows a stored secret and says one is stored", () => {
    render(<EmailSettingsView />);

    expect((document.querySelector("#email-server-token") as HTMLInputElement).value).toBe("");
    expect((document.querySelector("#email-webhook-password") as HTMLInputElement).value).toBe("");
    expect(screen.getByText("Email:ServerToken.Stored")).toBeTruthy();
    expect(screen.getByText("Email:WebhookPassword.Stored")).toBeTruthy();
    expect(screen.getByText("Email:Status.Configured")).toBeTruthy();
    expect((document.querySelector("#email-webhook-url") as HTMLInputElement).value).toBe("https://api.example.test/api/integrations/postmark/webhook");
  });

  it("saves the form and leaves untouched secrets out of the change", async () => {
    render(<EmailSettingsView />);
    fireEvent.change(document.querySelector("#email-from-address")!, { target: { value: " mail@ryd-software.com " } });

    fireEvent.click(command("Email:Save")!);

    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateMutateAsync).toHaveBeenCalledWith({
      serverToken: null,
      clearServerToken: false,
      fromAddress: "mail@ryd-software.com",
      fromName: "Ryd",
      trackOpens: null,
      trackLinks: null,
      webhookUsername: "postmark",
      webhookPassword: null,
      clearWebhookPassword: false,
    });
    await waitFor(() => expect(toastSuccess).toHaveBeenCalledWith("Email:Saved"));
  });

  it("sends a new token and can remove the stored one", async () => {
    render(<EmailSettingsView />);
    fireEvent.change(document.querySelector("#email-server-token")!, { target: { value: "new-token" } });
    fireEvent.click(command("Email:Save")!);
    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ serverToken: "new-token", clearServerToken: false })));

    fireEvent.click(screen.getByText("Email:ClearServerToken"));
    expect((document.querySelector("#email-server-token") as HTMLInputElement).disabled).toBe(true);
  });

  it("refuses an invalid sender address under the field without saving", async () => {
    render(<EmailSettingsView />);
    fireEvent.change(document.querySelector("#email-from-address")!, { target: { value: "not an address" } });

    fireEvent.click(command("Email:Save")!);

    expect(await screen.findByText("Email:FromAddress.Invalid")).toBeTruthy();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("shows a failed save on the page", async () => {
    updateMutateAsync.mockRejectedValue({ error: { message: "Choose a valid link tracking mode." } });
    render(<EmailSettingsView />);

    fireEvent.click(command("Email:Save")!);

    expect(await screen.findByText("Choose a valid link tracking mode.")).toBeTruthy();
    expect(toastSuccess).not.toHaveBeenCalled();
  });

  it("disables the test mail while there are unsaved changes or nothing is configured", () => {
    render(<EmailSettingsView />);
    expect(isDisabled("Email:SendTest")).toBe(false);

    fireEvent.change(document.querySelector("#email-from-name")!, { target: { value: "Other" } });
    expect(isDisabled("Email:SendTest")).toBe(true);
  });

  it("disables the test mail when e-mail is not configured, and says so", () => {
    settingsData = stored({ hasServerToken: false, effective: { isConfigured: false, serverTokenSource: 0, trackOpens: true } });
    render(<EmailSettingsView />);

    expect(isDisabled("Email:SendTest")).toBe(true);
    expect(screen.getByText("Email:Status.NotConfigured")).toBeTruthy();
    expect(screen.getByText("Email:ServerToken.None")).toBeTruthy();
  });

  it("sends a test mail and shows Postmark's answer in the dialog", async () => {
    testMutateAsync.mockResolvedValueOnce({ succeeded: true, status: 2, providerMessageId: "pm-123" });
    testMutateAsync.mockResolvedValueOnce({ succeeded: false, status: 6, error: "406: inactive recipient" });
    render(<EmailSettingsView />);
    fireEvent.click(command("Email:SendTest")!);
    const dialog = await screen.findByRole("dialog");
    const to = dialog.querySelector("#send-test-email-to")!;

    fireEvent.change(to, { target: { value: "beheer@ryd-software.com" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Email:TestSend" }));
    expect(await within(dialog).findByText("Email:TestSucceeded")).toBeTruthy();
    expect(testMutateAsync).toHaveBeenCalledWith("beheer@ryd-software.com");

    fireEvent.click(within(dialog).getByRole("button", { name: "Email:TestSend" }));
    expect(await within(dialog).findByText("Email:TestFailed")).toBeTruthy();
  });

  it("does not send a test mail to an invalid address", async () => {
    render(<EmailSettingsView />);
    fireEvent.click(command("Email:SendTest")!);
    const dialog = await screen.findByRole("dialog");

    fireEvent.click(within(dialog).getByRole("button", { name: "Email:TestSend" }));

    expect(await within(dialog).findByText("Email:FromAddress.Invalid")).toBeTruthy();
    expect(testMutateAsync).not.toHaveBeenCalled();
  });

  it("without the update permission there is no Save and every field is read-only", () => {
    configure({ granted: ["Platform.Administration.EmailSettings"] });
    render(<EmailSettingsView />);

    expect(command("Email:Save")).toBeNull();
    expect(command("Email:SendTest")).toBeNull();
    expect((document.querySelector("#email-from-address") as HTMLInputElement).disabled).toBe(true);
    expect((document.querySelector("#email-server-token") as HTMLInputElement).disabled).toBe(true);
  });
});
