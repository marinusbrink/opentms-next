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
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Email", "Services:Tab.Documents", "Services:Tab.Routing"]);
    expect(screen.getByTestId("email-settings")).toBeTruthy();
    unmount();

    configure({ granted: [EMAIL] });
    const emailOnly = render(<ServicesView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Email"]);
    emailOnly.unmount();

    configure({ granted: SERVICES });
    render(<ServicesView />);
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Services:Tab.Documents", "Services:Tab.Routing"]);
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
