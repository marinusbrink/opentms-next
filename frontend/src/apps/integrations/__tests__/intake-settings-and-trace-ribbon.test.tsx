import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 72 build notes 25–28 (PO decisions 2026-11-03), Integrations app:
 * - Order intake → Settings (IntakeSettingsView, /integrations/intake-settings): loads the tenant's name similarity
 *   threshold, offers exactly 0.50–0.95 in steps of 0.05 (the default marked), shows a stored value as the nearest
 *   offered one, saves the chosen value with PUT, shows a refusal; hidden without Integrations.OrderIntakeSettings, the
 *   tenant feature or its nav node (and then asks nothing);
 * - the trace page's ribbon group Order intake: Review opens the review page of the message's first intake whose last
 *   review step is NeedsReview, over all documents; disabled with its reason when none waits; Open order shows only when
 *   the message produced exactly one distinct order; both only with the permission and the app node of their page;
 * - the routing of /integrations/intake-settings and the group path /integrations/intake.
 * The domain hooks are real; the generated client is a path dispatcher. Risk class: Medium (UI flows); the server enforces
 * the permission, the feature, the range and the tenant (backend OrderIntakeSettingsTests). */

const navigateMock = vi.fn();
let pathname = "/integrations/intake-settings";
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname, search: {} }),
  Link: ({ to, children, className }: { to: string; children?: ReactNode; className?: string }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

const TEXTS: Record<string, string> = {
  "Integrations:IntakeSettings.DefaultValue": "{0} (default)",
  "Integrations:IntakeSettings.NameSimilarity.Hint": "From {0} to {1}",
};
vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => TEXTS[k] ?? k, culture: "en", isLoading: false }) }));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: "t-1" }, auth: { grantedPolicies: grants }, features: { values: features }, localization: { languages: [] } } }),
}));
vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));
vi.mock("@/components/ui/opentms-grid-lazy", async () => ({ LazyOpenTmsGrid: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockOpenTmsGrid }));
vi.mock("@/components/ui/multi-select", async () => ({ MultiSelect: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockMultiSelect }));
vi.mock("@/components/ui/date-picker", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/components/ui/date-picker")>()),
  DatePicker: (await import("@/apps/transport/__tests__/transport-test-mocks")).MockDatePicker,
}));
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("@/apps/transport/__tests__/transport-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";

import { IntegrationsApp } from "@/apps/integrations";
import { TracePage } from "@/apps/integrations/pages/TracePage";
import { IntakeSettingsView } from "@/apps/integrations/views/IntakeSettingsView";
import { GET, PUT, calls, lastCall, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { barButton, fail, ribbonButton, ribbonLabels, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";

const SETTINGS_PATH = "/api/integrations/order-intake-settings";

const FEATURES = {
  "Integrations.OrderIntake": "true",
  "Apps.Integrations": "true",
  "Apps.Integrations.Intake": "true",
  "Apps.Integrations.Intake.Received": "true",
  "Apps.Integrations.Intake.Mailboxes": "true",
  "Apps.Integrations.Intake.ApiClients": "true",
  "Apps.Integrations.Intake.ImportProfiles": "true",
  "Apps.Integrations.Intake.Settings": "true",
  "Apps.Transport": "true",
  "Apps.Transport.Intake": "true",
  "Apps.Transport.Orders": "true",
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  pathname = "/integrations/intake-settings";
  grants = { "Integrations.OrderIntakeSettings": true };
  features = { ...FEATURES };
  resetClient();
  routes.GET[SETTINGS_PATH] = () => ({ nameSimilarityThreshold: 0.8 });
  routes.PUT[SETTINGS_PATH] = (init) => init?.body;
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}
const click = async (element: HTMLElement) =>
  act(async () => {
    fireEvent.click(element);
    await Promise.resolve();
  });

const thresholdSelect = () => document.getElementById("intake-settings-name-similarity") as HTMLSelectElement | null;
const offered = () => Array.from(thresholdSelect()!.options).filter((o) => o.value !== "" && o.textContent !== "—");

/* ── IntakeSettingsView ─────────────────────────────────────────────────────────────────────────────────────── */

describe("IntakeSettingsView (design 72 build note 25)", () => {
  it("loads the tenant's threshold and offers exactly 0.50 to 0.95 in steps of 0.05, the default marked", async () => {
    renderWith(<IntakeSettingsView />);

    await waitFor(() => expect(thresholdSelect()).not.toBeNull());
    expect(calls(GET, SETTINGS_PATH)).toHaveLength(1);
    expect(thresholdSelect()!.value).toBe("0.80");
    expect(offered().map((o) => o.value)).toEqual(["0.50", "0.55", "0.60", "0.65", "0.70", "0.75", "0.80", "0.85", "0.90", "0.95"]);
    expect(offered().find((o) => o.value === "0.75")!.textContent).toBe("0.75 (default)");
    expect(offered().find((o) => o.value === "0.80")!.textContent).toBe("0.80");
    expect(document.body.textContent).toContain("From 0.50 to 0.95");
  });

  it("saves the chosen value and says so", async () => {
    renderWith(<IntakeSettingsView />);
    await waitFor(() => expect(thresholdSelect()).not.toBeNull());

    fireEvent.change(thresholdSelect()!, { target: { value: "0.90" } });
    await click(barButton("Save"));

    await waitFor(() => expect(calls(PUT, SETTINGS_PATH)).toHaveLength(1));
    expect(lastCall(PUT, SETTINGS_PATH)!.body).toEqual({ nameSimilarityThreshold: 0.9 });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:IntakeSettings.Saved"));
    expect(thresholdSelect()!.value).toBe("0.90");
  });

  it.each([
    [0.83, "0.85"],
    [0.2, "0.50"],
    [0.99, "0.95"],
    [0.75, "0.75"],
  ])("shows a stored %s as the nearest offered value %s, so only an allowed value can be saved", async (stored, shown) => {
    routes.GET[SETTINGS_PATH] = () => ({ nameSimilarityThreshold: stored });
    renderWith(<IntakeSettingsView />);
    await waitFor(() => expect(thresholdSelect()).not.toBeNull());

    expect(thresholdSelect()!.value).toBe(shown);
    await click(barButton("Save"));
    await waitFor(() => expect(calls(PUT, SETTINGS_PATH)).toHaveLength(1));
    const sent = (lastCall(PUT, SETTINGS_PATH)!.body as { nameSimilarityThreshold: number }).nameSimilarityThreshold;
    expect(sent).toBe(Number(shown));
    expect(sent).toBeGreaterThanOrEqual(0.5);
    expect(sent).toBeLessThanOrEqual(0.95);
  });

  it("shows the server's refusal and keeps the chosen value", async () => {
    routes.PUT[SETTINGS_PATH] = () => fail("The value is out of range");
    renderWith(<IntakeSettingsView />);
    await waitFor(() => expect(thresholdSelect()).not.toBeNull());

    fireEvent.change(thresholdSelect()!, { target: { value: "0.60" } });
    await click(barButton("Save"));

    expect(await screen.findByText("The value is out of range")).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
    expect(thresholdSelect()!.value).toBe("0.60");
  });

  it("shows a load failure instead of the form", async () => {
    routes.GET[SETTINGS_PATH] = () => fail("Server unavailable", 500);
    renderWith(<IntakeSettingsView />);

    expect(await screen.findByText("Server unavailable")).toBeInTheDocument();
    expect(thresholdSelect()).toBeNull();
  });

  it("is not rendered and asks nothing without the permission, with the tenant feature off or with its nav node off", () => {
    grants = {};
    const first = renderWith(<IntakeSettingsView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    first.unmount();

    grants = { "Integrations.OrderIntakeSettings": true };
    for (const off of ["Integrations.OrderIntake", "Apps.Integrations.Intake.Settings"]) {
      features = { ...FEATURES, [off]: "false" };
      const view = renderWith(<IntakeSettingsView />);
      expect(screen.getByRole("status"), off).toHaveTextContent("FeatureUnavailable");
      view.unmount();
    }

    expect(calls(GET, SETTINGS_PATH)).toHaveLength(0);
  });
});

/* ── The trace page's ribbon: Review and Open order (build note 27) ─────────────────────────────────────────── */

const step = (id: string, kind: number, intakeIndex: number, extra: Record<string, unknown> = {}) => ({
  id,
  at: `2026-11-03T08:00:${id.padStart(2, "0").slice(-2)}Z`,
  step: kind,
  outcome: kind === 18 ? 3 : 2,
  messageKey: "Integrations:Step.X",
  intakeIndex,
  parameters: {},
  ...extra,
});

type Intake = { index: number; intakeId?: string; transportOrderId?: string; orderNumber?: string };

function message(documents: { id: string; fileName: string; intakes: Intake[]; steps: ReturnType<typeof step>[] }[]) {
  return {
    id: "msg-1",
    channel: 1,
    sourceId: "mb-1",
    sourceName: "Orders inbox",
    externalId: "gmail-1",
    from: "planning@acme.test",
    subject: "Orders",
    receivedAt: "2026-11-03T08:00:00Z",
    status: 3,
    hasRawContent: false,
    documents: documents.map((d) => ({
      id: d.id,
      kind: 2,
      fileName: d.fileName,
      format: 3,
      status: 4,
      mappingMethod: 1,
      sizeBytes: 10,
      steps: d.steps,
      intakes: d.intakes.map((i) => ({ confidence: 1, payload: { lines: [] }, provenance: {}, ...i })),
    })),
  };
}

/* doc-1: intake 0 became order o-1, intake 1 waits for review; doc-2: intake 0 waited, then was resolved into o-1's sibling. */
const WAITING = message([
  {
    id: "doc-1",
    fileName: "a.pdf",
    intakes: [{ index: 0, intakeId: "i-1", transportOrderId: "o-1", orderNumber: "TO-1" }, { index: 1, intakeId: "i-2" }],
    steps: [step("1", 16, 0), step("2", 17, 0, { intakeId: "i-1", transportOrderId: "o-1" }), step("3", 16, 1), step("4", 18, 1, { intakeId: "i-2" })],
  },
  {
    id: "doc-2",
    fileName: "b.pdf",
    intakes: [{ index: 0, intakeId: "i-3", transportOrderId: "o-1", orderNumber: "TO-1" }],
    steps: [step("5", 18, 0, { intakeId: "i-3" }), step("6", 17, 0, { intakeId: "i-3", transportOrderId: "o-1" })],
  },
]);

const BOTH_DONE = message([
  {
    id: "doc-1",
    fileName: "a.pdf",
    intakes: [{ index: 0, intakeId: "i-1", transportOrderId: "o-1", orderNumber: "TO-1" }, { index: 1, intakeId: "i-2", transportOrderId: "o-2", orderNumber: "TO-2" }],
    steps: [step("1", 17, 0), step("2", 18, 1), step("3", 17, 1)],
  },
]);

const TRACE_GRANTS = { "Integrations.InboundMessages": true, "Orders.OrderIntakes": true, "Orders.TransportOrders": true };

describe("TracePage ribbon: Review and Open order (design 72 build note 27)", () => {
  beforeEach(() => {
    pathname = "/integrations/received/msg-1";
    grants = { ...TRACE_GRANTS };
  });

  it("Review opens the first intake of the message whose last review step is NeedsReview, over all its documents", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => WAITING;
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:ReviewIntake")).not.toBeNull());

    const review = ribbonButton("Integrations:ReviewIntake")!;
    expect(review).not.toHaveAttribute("aria-disabled");
    await click(review);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/intake/i-2" }));
    expect(navigateMock).not.toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/intake/i-3" }));
  });

  it("Review is disabled with its reason when no intake of the message waits, and does nothing", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => BOTH_DONE;
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:ReviewIntake")).not.toBeNull());

    const review = ribbonButton("Integrations:ReviewIntake")!;
    expect(review).toHaveAttribute("aria-disabled", "true");
    expect(review).toHaveAttribute("title", "Integrations:NoIntakeToReview");
    await click(review);
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("Open order shows when the message produced exactly one distinct order and opens it", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => WAITING;
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:OpenOrderCommand")).not.toBeNull());

    await click(ribbonButton("Integrations:OpenOrderCommand")!);
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/transport/orders/o-1" }));
  });

  it("Open order is not offered for two orders or for none", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => BOTH_DONE;
    const two = renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:ReviewIntake")).not.toBeNull());
    expect(ribbonButton("Integrations:OpenOrderCommand")).toBeNull();
    two.unmount();

    routes.GET["/api/integrations/inbound-message/{id}"] = () =>
      message([{ id: "doc-1", fileName: "a.pdf", intakes: [{ index: 0, intakeId: "i-9" }], steps: [step("1", 18, 0, { intakeId: "i-9" })] }]);
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:ReviewIntake")).not.toBeNull());
    expect(ribbonButton("Integrations:OpenOrderCommand")).toBeNull();
  });

  it("the ribbon does not depend on the Details card: the same commands collapsed and expanded", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => WAITING;
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:ReviewIntake")).not.toBeNull());
    const collapsed = ribbonLabels();
    const toggle = screen.getByRole("button", { name: "Expand" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    await click(toggle);
    expect(screen.getByRole("button", { name: "Collapse" })).toHaveAttribute("aria-expanded", "true");
    expect(ribbonLabels()).toEqual(collapsed);
    expect(collapsed).toEqual(expect.arrayContaining(["Integrations:ReviewIntake", "Integrations:OpenOrderCommand"]));
  });

  it("both show only with the permission and the app node of their page", async () => {
    routes.GET["/api/integrations/inbound-message/{id}"] = () => WAITING;
    const cases: [Record<string, boolean>, Record<string, string>, boolean, boolean][] = [
      [{ "Integrations.InboundMessages": true }, FEATURES, false, false],
      [{ ...TRACE_GRANTS, "Orders.OrderIntakes": false }, FEATURES, false, true],
      [{ ...TRACE_GRANTS, "Orders.TransportOrders": false }, FEATURES, true, false],
      [TRACE_GRANTS, { ...FEATURES, "Apps.Transport.Intake": "false" }, false, true],
      [TRACE_GRANTS, { ...FEATURES, "Apps.Transport.Orders": "false" }, true, false],
      [TRACE_GRANTS, FEATURES, true, true],
    ];
    for (const [g, f, review, open] of cases) {
      grants = g;
      features = f;
      const view = renderWith(<TracePage id="msg-1" />);
      await waitFor(() => expect(ribbonLabels().length).toBeGreaterThan(0));
      const label = JSON.stringify({ g, f: Object.entries(f).filter(([, v]) => v === "false") });
      expect(ribbonLabels().includes("Integrations:ReviewIntake"), label).toBe(review);
      expect(ribbonLabels().includes("Integrations:OpenOrderCommand"), label).toBe(open);
      view.unmount();
    }
  });
});

/* ── Routing ────────────────────────────────────────────────────────────────────────────────────────────────── */

describe("IntegrationsApp routing of the order-intake setup (design 72 build note 25)", () => {
  it("/integrations/intake-settings renders the settings; /integrations/intake goes to the first list that is on", async () => {
    pathname = "/integrations/intake-settings";
    const first = renderWith(<IntegrationsApp />);
    await waitFor(() => expect(document.querySelector("[data-slot=intake-settings-view]")).not.toBeNull());
    expect(navigateMock).not.toHaveBeenCalled();
    first.unmount();

    pathname = "/integrations/intake";
    const second = renderWith(<IntegrationsApp />);
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/received", replace: true }));
    second.unmount();

    navigateMock.mockReset();
    features = { ...FEATURES, "Apps.Integrations.Intake.Received": "false" };
    renderWith(<IntegrationsApp />);
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/mailboxes", replace: true }));
  });
});
