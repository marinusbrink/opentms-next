import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { useApplicationConfiguration } from "@/lib/abp/queries";

/* Administration → Services (docs/designs/maintenance-view.md, "Services"; PO 2026-11-02): a tab per service, E-mail
 * among them. Risk class: Critical — the page configures every tenant's documents and routing: it renders on the host
 * side only and per permission, never shows a stored secret and never sends an empty secret as a change; High — the
 * save payload, URL validation, the test and its disabled state. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: vi.fn(),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...args: unknown[]) => toastSuccess(...args), error: vi.fn() } }));

vi.mock("@/apps/admin/email/EmailSettingsView", () => ({
  EmailSettingsView: () => <div data-testid="email-settings">email</div>,
}));

const updateMutateAsync = vi.fn();
const testMutateAsync = vi.fn();
let settingsData: Record<string, unknown>;
vi.mock("@/domains/integrations/service-settings", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/domains/integrations/service-settings")>();
  return {
    ...original,
    useServiceSettings: () => ({ data: settingsData, isError: false, error: undefined }),
    useUpdateServiceSettings: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
    useTestService: () => ({ mutateAsync: testMutateAsync, isPending: false }),
  };
});

const { ServicesView } = await import("@/apps/admin/services/ServicesView");

const EMAIL = "Platform.Administration.EmailSettings";
const SERVICES = ["Integrations.ServiceSettings", "Integrations.ServiceSettings.Update", "Integrations.ServiceSettings.Test"];

function configure({ host = true, granted = [EMAIL, ...SERVICES] }: { host?: boolean; granted?: string[] } = {}) {
  vi.mocked(useApplicationConfiguration).mockReturnValue({
    data: {
      currentTenant: { id: host ? null : "11111111-1111-1111-1111-111111111111" },
      auth: { grantedPolicies: Object.fromEntries(granted.map((p) => [p, true])) },
    },
  } as unknown as ReturnType<typeof useApplicationConfiguration>);
}

function stored(overrides: Record<string, unknown> = {}) {
  return {
    pdfGeneratorApiBaseUrl: null,
    hasPdfGeneratorApiKey: true,
    hasPdfGeneratorApiSecret: false,
    routingProvider: null,
    osrmBaseUrl: "https://osrm.example.test/",
    routeFunctionBaseUrl: null,
    hasRouteFunctionApiKey: false,
    vroomBaseUrl: null,
    defaults: {
      pdfGeneratorApiBaseUrl: "https://us1.pdfgeneratorapi.com/api/v4/",
      hasPdfGeneratorApiKey: false,
      hasPdfGeneratorApiSecret: true,
      routingProvider: "Osrm",
      osrmBaseUrl: null,
      routeFunctionBaseUrl: null,
      hasRouteFunctionApiKey: false,
      vroomBaseUrl: null,
    },
    ...overrides,
  };
}

const command = (label: string) => screen.queryByRole("button", { name: label });
const isDisabled = (label: string) => command(label)?.getAttribute("aria-disabled") === "true";
const openTab = (label: string) => fireEvent.click(screen.getByRole("tab", { name: label }));

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", class { observe() {} disconnect() {} unobserve() {} });
});

beforeEach(() => {
  vi.clearAllMocks();
  settingsData = stored();
  configure();
  updateMutateAsync.mockResolvedValue(undefined);
});

describe("ServicesView", () => {
  it("has a tab per service, E-mail first, and shows only the tabs the user may see", () => {
    const { unmount } = render(<ServicesView />);
    /* Design 72 "Host settings" adds the fourth tab, Integrations. */
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Email", "Services:Tab.Documents", "Services:Tab.Routing", "Services:Tab.Integrations"]);
    expect(screen.getByTestId("email-settings")).toBeTruthy();
    unmount();

    configure({ granted: [EMAIL] });
    const emailOnly = render(<ServicesView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Email"]);
    emailOnly.unmount();

    configure({ granted: SERVICES });
    render(<ServicesView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Documents", "Services:Tab.Routing", "Services:Tab.Integrations"]);
  });

  it("is refused on the tenant side and without any service permission", () => {
    configure({ host: false });
    const { unmount } = render(<ServicesView />);
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    unmount();

    configure({ granted: [] });
    render(<ServicesView />);
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
  });
});

describe("Documents and Routing tabs", () => {
  it("never shows a stored secret, says where each value comes from and shows only its own service", () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);

    expect((document.querySelector("#services-pdf-key") as HTMLInputElement).value).toBe("");
    expect(screen.getByText("Services:Secret.Stored")).toBeTruthy();
    expect(screen.getByText("Services:Secret.FromConfiguration")).toBeTruthy();
    expect(screen.getByText("Services:Default")).toBeTruthy();
    expect(document.querySelector("#services-osrm-url")).toBeNull();
    expect(command("Services:TestDocuments")).toBeTruthy();
    expect(command("Services:TestRouting")).toBeNull();

    openTab("Services:Tab.Routing");
    expect((document.querySelector("#services-osrm-url") as HTMLInputElement).value).toBe("https://osrm.example.test/");
    expect(document.querySelector("#services-pdf-key")).toBeNull();
  });

  it("saves the form and leaves untouched secrets out of the change", async () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Routing");
    fireEvent.change(document.querySelector("#services-vroom-url")!, { target: { value: " https://vroom.example.test/ " } });

    fireEvent.click(command("Services:Save")!);

    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateMutateAsync).toHaveBeenCalledWith({
      pdfGeneratorApiBaseUrl: null,
      pdfGeneratorApiKey: null,
      clearPdfGeneratorApiKey: false,
      pdfGeneratorApiSecret: null,
      clearPdfGeneratorApiSecret: false,
      routingProvider: null,
      osrmBaseUrl: "https://osrm.example.test/",
      routeFunctionBaseUrl: null,
      routeFunctionApiKey: null,
      clearRouteFunctionApiKey: false,
      vroomBaseUrl: "https://vroom.example.test/",
      /* Design 72: the Integrations tab's fields travel in the same change, untouched secrets empty and not cleared. */
      aiApiKey: null,
      clearAiApiKey: false,
      aiLearningModel: null,
      aiExtractionModel: null,
      googleClientId: null,
      googleClientSecret: null,
      clearGoogleClientSecret: false,
      microsoftClientId: null,
      microsoftClientSecret: null,
      clearMicrosoftClientSecret: false,
    });
    await waitFor(() => expect(toastSuccess).toHaveBeenCalledWith("Services:Saved"));
  });

  it("sends a new key and can remove the stored one", async () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    fireEvent.change(document.querySelector("#services-pdf-key")!, { target: { value: "new-key" } });
    fireEvent.click(command("Services:Save")!);
    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ pdfGeneratorApiKey: "new-key", clearPdfGeneratorApiKey: false })));

    fireEvent.click(screen.getByText("Services:Clear"));
    expect((document.querySelector("#services-pdf-key") as HTMLInputElement).disabled).toBe(true);
  });

  it("refuses an address that is not a full URL under the field without saving", async () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Routing");
    fireEvent.change(document.querySelector("#services-osrm-url")!, { target: { value: "osrm.example.test" } });

    fireEvent.click(command("Services:Save")!);

    expect(await screen.findByText("Services:Url.Invalid")).toBeTruthy();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("tests only its own service, after a save, and shows the components' findings", async () => {
    testMutateAsync.mockResolvedValue([
      { name: "routing", status: "Unhealthy", reason: "Unreachable", target: "osrm.example.test", data: { provider: "Osrm" }, description: "raw" },
    ]);
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Routing");

    fireEvent.change(document.querySelector("#services-vroom-url")!, { target: { value: "https://vroom.example.test/" } });
    expect(isDisabled("Services:TestRouting")).toBe(true);
    fireEvent.change(document.querySelector("#services-vroom-url")!, { target: { value: "" } });
    expect(isDisabled("Services:TestRouting")).toBe(false);

    fireEvent.click(command("Services:TestRouting")!);

    await waitFor(() => expect(testMutateAsync).toHaveBeenCalledWith("routing"));
    /* The identity translator has no texts: the component falls back to its name and the check's description. */
    expect(await screen.findByText("routing: Maintenance:StatusUnhealthy")).toBeTruthy();
    expect(screen.getByText("raw")).toBeTruthy();
  });

  it("is read-only without the update permission and has no test without the test permission", () => {
    configure({ granted: ["Integrations.ServiceSettings"] });
    render(<ServicesView />);

    expect((document.querySelector("#services-pdf-url") as HTMLInputElement).disabled).toBe(true);
    expect(command("Services:Save")).toBeNull();
    expect(command("Services:TestDocuments")).toBeNull();
  });
});

/* Design 72 "Host settings": the Integrations tab (AI vendor Anthropic, the Google and Microsoft 365 OAuth clients, the
 * redirect URI to register). Risk class: Critical (host-wide secrets: write-only, never sent unless typed or cleared;
 * host only); Medium — the per-section test result and the redirect URI copy. */
describe("Integrations tab (design 72)", () => {
  const integrationsStored = (overrides: Record<string, unknown> = {}) =>
    stored({
      hasAiApiKey: true,
      aiLearningModel: null,
      aiExtractionModel: "claude-custom-model",
      googleClientId: "google-id.apps.googleusercontent.com",
      hasGoogleClientSecret: true,
      microsoftClientId: null,
      hasMicrosoftClientSecret: false,
      mailboxRedirectUri: "https://api.example.test/api/integrations/mailbox-oauth/callback",
      defaults: { ...stored().defaults, hasAiApiKey: false, aiLearningModel: null, aiExtractionModel: null, googleClientId: null, hasGoogleClientSecret: false, microsoftClientId: "ms-from-config", hasMicrosoftClientSecret: true },
      ...overrides,
    });
  const input = (id: string) => document.querySelector(`#${id}`) as HTMLInputElement;

  beforeEach(() => {
    settingsData = integrationsStored();
  });

  it("never shows a stored secret: every secret field starts empty, plain values are shown", () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");

    for (const id of ["services-ai-key", "services-google-secret", "services-microsoft-secret"]) {
      expect(input(id).value, id).toBe("");
      expect(input(id).type, id).toBe("password");
    }
    expect(input("services-google-client-id").value).toBe("google-id.apps.googleusercontent.com");
    expect(input("services-ai-extraction-model").value).toBe("claude-custom-model");
    expect(input("services-ai-learning-model").value).toBe("");
    /* Stored AI key and Google secret; the Microsoft secret comes from the deployment. */
    expect(screen.getAllByText("Services:Secret.Stored")).toHaveLength(2);
    expect(screen.getByText("Services:Secret.FromConfiguration")).toBeTruthy();
    /* Only stored secrets can be cleared. */
    expect(screen.getAllByText("Services:Clear")).toHaveLength(2);
    /* The other services' fields are not on this tab. */
    expect(document.querySelector("#services-pdf-key")).toBeNull();
    expect(command("Services:TestIntegrations")).toBeTruthy();
  });

  it("saves only typed secrets and the clear flags; untouched secrets stay null", async () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");

    fireEvent.change(input("services-ai-key"), { target: { value: "  sk-ant-new  " } });
    fireEvent.change(input("services-ai-learning-model"), { target: { value: " claude-opus-5-5 " } });
    fireEvent.change(input("services-microsoft-client-id"), { target: { value: "ms-app-id" } });
    fireEvent.change(input("services-microsoft-secret"), { target: { value: "ms-secret" } });
    /* The Google secret's clear box is the second "Clear" (AI key first). */
    fireEvent.click(screen.getAllByText("Services:Clear")[1]);
    expect(input("services-google-secret").disabled).toBe(true);

    fireEvent.click(command("Services:Save")!);

    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        aiApiKey: "sk-ant-new",
        clearAiApiKey: false,
        aiLearningModel: "claude-opus-5-5",
        aiExtractionModel: "claude-custom-model",
        googleClientId: "google-id.apps.googleusercontent.com",
        googleClientSecret: null,
        clearGoogleClientSecret: true,
        microsoftClientId: "ms-app-id",
        microsoftClientSecret: "ms-secret",
        clearMicrosoftClientSecret: false,
        /* The other services' secrets are not touched by a save on this tab. */
        pdfGeneratorApiKey: null,
        clearPdfGeneratorApiKey: false,
        routeFunctionApiKey: null,
        clearRouteFunctionApiKey: false,
      }),
    );
  });

  it("shows the model default in the hint when no model is stored", () => {
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");
    /* The identity translator returns "Services:Default" without a {0} placeholder; the hint at least names the default text. */
    expect(screen.getAllByText("Services:Default").length).toBeGreaterThanOrEqual(2);
  });

  it("shows the redirect URI read-only with a copy button that copies it", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");

    const uri = input("services-redirect-uri");
    expect(uri.value).toBe("https://api.example.test/api/integrations/mailbox-oauth/callback");
    expect(uri.readOnly).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Services:CopyRedirectUri" }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("https://api.example.test/api/integrations/mailbox-oauth/callback"));
  });

  it("has no copy button when the deployment has no redirect URI", () => {
    settingsData = integrationsStored({ mailboxRedirectUri: null });
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");
    expect(screen.queryByRole("button", { name: "Services:CopyRedirectUri" })).toBeNull();
  });

  it("tests the integrations service and shows each check under its own section", async () => {
    testMutateAsync.mockResolvedValue([
      { name: "anthropic", status: "Healthy", description: "ai-ok" },
      { name: "google-oauth", status: "Unhealthy", description: "google-bad" },
      { name: "microsoft-oauth", status: "Degraded", description: "ms-degraded" },
    ]);
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");

    fireEvent.click(command("Services:TestIntegrations")!);

    await waitFor(() => expect(testMutateAsync).toHaveBeenCalledWith("integrations"));
    const results = await screen.findAllByLabelText("Services:TestResult");
    expect(results).toHaveLength(3);
    expect(results[0].textContent).toContain("ai-ok");
    expect(results[0].textContent).not.toContain("google-bad");
    expect(results[1].textContent).toContain("google-bad");
    expect(results[2].textContent).toContain("ms-degraded");
  });

  it("a section the test did not answer shows no result", async () => {
    testMutateAsync.mockResolvedValue([{ name: "anthropic", status: "Healthy", description: "ai-ok" }]);
    configure({ granted: SERVICES });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");
    fireEvent.click(command("Services:TestIntegrations")!);
    await waitFor(() => expect(screen.getAllByLabelText("Services:TestResult")).toHaveLength(1));
  });

  it("is read-only without the update permission and has no test without the test permission", () => {
    configure({ granted: ["Integrations.ServiceSettings"] });
    render(<ServicesView />);
    openTab("Services:Tab.Integrations");
    for (const id of ["services-ai-key", "services-ai-learning-model", "services-google-client-id", "services-google-secret", "services-microsoft-secret"]) {
      expect(input(id).disabled, id).toBe(true);
    }
    expect(command("Services:Save")).toBeNull();
    expect(command("Services:TestIntegrations")).toBeNull();
  });

  it("the tab is not rendered without the services permission or on the tenant side", () => {
    configure({ granted: [EMAIL] });
    const { unmount } = render(<ServicesView />);
    expect(screen.queryByRole("tab", { name: "Services:Tab.Integrations" })).toBeNull();
    unmount();

    configure({ host: false });
    render(<ServicesView />);
    expect(screen.queryByRole("tab", { name: "Services:Tab.Integrations" })).toBeNull();
    expect(document.querySelector("#services-ai-key")).toBeNull();
  });
});
