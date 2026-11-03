import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* Design 72 "UI design", Integrations app → Received and the trace page /integrations/received/{messageId}: the list
 * (gates, the default six-month window, the status and channel filters, opening a row opens the trace), the trace (one
 * tab per document; the step timeline with localized step texts and their parameters, tokens and cost on the AI step,
 * the user on a manual step; the field table with the source location of each value; links to the intake and the
 * order only when the user may open them; Reprocess only with Integrations.InboundMessages.Reprocess, on the active
 * document) and the document redirect /integrations/received/document/{documentId} (resolves the message and replaces
 * the history entry; not found). The domain hooks are real; the generated client is a path dispatcher. Risk class:
 * Medium (UI flows); tenant isolation of the endpoints is a backend test (InboundMessageAppService). */

const navigateMock = vi.fn();
let pathname = "/integrations/received";
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname, search: {} }),
  Link: ({ to, children, className }: { to: string; children?: ReactNode; className?: string }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

/* Identity translator, except the texts that prove step templates and enum parameters are localized. */
const TEXTS: Record<string, string> = {
  "Integrations:Step.Fingerprinted": "Fingerprint {fingerprint} of a {format}",
  "Integrations:DocumentFormat.Pdf": "PDF document",
  "Integrations:Step.Received": "Received from {from}",
  "Integrations:AiUsage": "Model {model}: {input} in, {output} out, cost {cost}",
  "Integrations:StepBy": "by {0}",
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
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { toast } from "sonner";

import { IntegrationsApp } from "@/apps/integrations";
import { DocumentTraceRedirect } from "@/apps/integrations/pages/DocumentTraceRedirect";
import { TracePage } from "@/apps/integrations/pages/TracePage";
import { ReceivedView } from "@/apps/integrations/views/ReceivedView";
import { GET, POST, calls, lastCall, resetClient, routes } from "@/apps/transport/__tests__/test-setup-mocks";
import { fail, panel, ribbonButton, ribbonLabels, stubGlobals } from "@/apps/transport/__tests__/transport-test-mocks";

const FEATURES = {
  "Integrations.OrderIntake": "true",
  "Apps.Integrations": "true",
  "Apps.Integrations.Received": "true",
  "Apps.Transport": "true",
  "Apps.Transport.Intake": "true",
  "Apps.Transport.Orders": "true",
  "Apps.MasterData": "true",
  "Apps.MasterData.Integrations": "true",
  "Apps.MasterData.Integrations.Intake": "true",
  "Apps.MasterData.Integrations.Intake.ImportProfiles": "true",
};
const VIEW = { "Integrations.InboundMessages": true };

const MESSAGE = {
  id: "msg-1",
  channel: 1,
  sourceId: "mb-1",
  sourceName: "Orders inbox",
  externalId: "gmail-18c2",
  from: "planning@acme.test",
  subject: "Order PO-4711",
  receivedAt: "2026-11-03T08:00:00Z",
  status: 3,
  hasRawContent: true,
  documents: [
    {
      id: "doc-1",
      kind: 2,
      fileName: "order-4711.pdf",
      format: 3,
      status: 4,
      mappingMethod: 3,
      fingerprint: "fp-abc123",
      senderKey: "planning@acme.test",
      importProfileId: "p-1",
      importProfileName: "Acme pdf",
      importProfileVersion: 2,
      sizeBytes: 2048,
      steps: [
        { id: "s1", at: "2026-11-03T08:00:01Z", step: 1, outcome: 1, messageKey: "Integrations:Step.Received", parameters: { from: "planning@acme.test" } },
        { id: "s2", at: "2026-11-03T08:00:02Z", step: 4, outcome: 1, messageKey: "Integrations:Step.Fingerprinted", parameters: { fingerprint: "fp-abc123", format: "Pdf" } },
        { id: "s3", at: "2026-11-03T08:00:03Z", step: 6, outcome: 3, messageKey: "Integrations:Step.ProfileNotFound", parameters: {} },
        { id: "s4", at: "2026-11-03T08:00:20Z", step: 9, outcome: 2, messageKey: "Integrations:Step.AiCompleted", modelId: "claude-opus-5-5", inputTokens: 12345, outputTokens: 4321, costMicroUsd: 250000, parameters: {} },
        { id: "s5", at: "2026-11-03T08:00:21Z", step: 17, outcome: 2, messageKey: "Integrations:Step.OrderCreated", intakeIndex: 0, intakeId: "i-1", transportOrderId: "o-1", orderNumber: "TO-100", parameters: {} },
        { id: "s6", at: "2026-11-03T09:00:00Z", step: 21, outcome: 2, messageKey: "Integrations:Step.OrderConfirmed", intakeIndex: 0, userName: "Clerk Jan", parameters: {} },
      ],
      intakes: [
        {
          index: 0,
          confidence: 0.9,
          intakeId: "i-1",
          transportOrderId: "o-1",
          orderNumber: "TO-100",
          payload: { customerReference: "PO-4711", lines: [{ quantity: 2, weightKg: 800, stops: [{ role: 1, address: { postalCode: "1012 AB", city: "Amsterdam" } }] }] },
          provenance: {
            "order.customerReference": { kind: 2, ref: "line 3", snippet: "Ordernummer: PO-4711" },
            "lines[0].weightKg": { kind: 2, ref: "line 14", snippet: "Gewicht 800 kg" },
            "order.orderDate": { kind: 2, ref: "line 2", snippet: "Datum:" },
          },
        },
      ],
    },
    { id: "doc-2", kind: 1, fileName: null, format: 1, status: 7, mappingMethod: 0, steps: [], intakes: [] },
  ],
};

beforeAll(stubGlobals);
beforeEach(() => {
  navigateMock.mockReset();
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  pathname = "/integrations/received";
  grants = { ...VIEW };
  features = { ...FEATURES };
  resetClient();
  routes.GET["/api/integrations/inbound-message/{id}"] = () => MESSAGE;
  routes.GET["/api/integrations/inbound-message"] = () => ({ rows: [{ id: "msg-1", channel: 1, sourceName: "Orders inbox", from: "planning@acme.test", subject: "Order PO-4711", documentCount: 2, status: 3, receivedAt: "2026-11-03T08:00:00Z" }], totalCount: 1, filteredCount: 1 });
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

describe("ReceivedView (design 72)", () => {
  it("is not rendered with the tenant feature off or the Received node off; needs Integrations.InboundMessages", () => {
    features = { ...FEATURES, "Integrations.OrderIntake": "false" };
    const first = renderWith(<ReceivedView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    first.unmount();
    features = { ...FEATURES, "Apps.Integrations.Received": "false" };
    const second = renderWith(<ReceivedView />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    second.unmount();
    features = { ...FEATURES };
    grants = {};
    renderWith(<ReceivedView />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(calls(GET, "/api/integrations/inbound-message")).toHaveLength(0);
  });

  it("starts on the last six months, filters by status and channel, and opens a row's trace", async () => {
    renderWith(<ReceivedView />);
    await waitFor(() => expect(calls(GET, "/api/integrations/inbound-message")).toHaveLength(1));
    const first = lastCall(GET, "/api/integrations/inbound-message")!.params!.query!;
    const from = new Date(String(first.ReceivedFrom));
    const expected = new Date();
    expected.setMonth(expected.getMonth() - 6);
    expect(Math.abs(from.getTime() - new Date(expected.toISOString().slice(0, 10)).getTime())).toBeLessThan(2 * 86_400_000);
    expect(first.Status).toBeUndefined();
    expect(first.Channel).toBeUndefined();
    expect(document.querySelector("[data-grid-id='integrations.inboundMessages']")).not.toBeNull();

    fireEvent.click(screen.getByRole("checkbox", { name: "Integrations:InboundMessageStatus.NeedsAttention" }));
    await waitFor(() => expect(lastCall(GET, "/api/integrations/inbound-message")!.params!.query!.Status).toEqual([4]));
    fireEvent.click(screen.getByRole("checkbox", { name: "Integrations:InboundChannel.Api" }));
    await waitFor(() => expect(lastCall(GET, "/api/integrations/inbound-message")!.params!.query!.Channel).toEqual([3]));

    await click(await screen.findByRole("button", { name: "open-row" }));
    expect(navigateMock).toHaveBeenCalledWith(expect.objectContaining({ to: "/integrations/received/msg-1" }));
  });
});

describe("TracePage (design 72)", () => {
  it("is not rendered with the feature off, needs the permission, and shows not found for an unknown message", async () => {
    features = { ...FEATURES, "Integrations.OrderIntake": "false" };
    const first = renderWith(<TracePage id="msg-1" />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    first.unmount();
    features = { ...FEATURES };
    grants = {};
    const second = renderWith(<TracePage id="msg-1" />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    expect(calls(GET, "/api/integrations/inbound-message/{id}")).toHaveLength(0);
    second.unmount();
    grants = { ...VIEW };
    routes.GET["/api/integrations/inbound-message/{id}"] = () => fail("Not found", 404);
    renderWith(<TracePage id="other-tenants-message" />);
    expect(await screen.findByText("RecordNotFound")).toBeInTheDocument();
  });

  it("one tab per document; the header and the document's facts with a link to the profile used", async () => {
    grants = { ...VIEW, "Integrations.ImportProfiles": true };
    renderWith(<TracePage id="msg-1" />);
    const doc = await waitFor(() => panel("order-4711.pdf"));
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual(["order-4711.pdf", "Integrations:InboundDocumentKind.MailBody"]);
    expect(doc.textContent).toContain("Orders inbox");
    expect(doc.textContent).toContain("planning@acme.test");
    expect(doc.textContent).toContain("Order PO-4711");
    expect(doc.textContent).toContain("fp-abc123");
    expect(doc.textContent).toContain("PDF document");
    expect(doc.textContent).toContain("Integrations:MappingMethod.AiExtraction");
    const profile = within(doc).getByRole("link", { name: "Acme pdf (Integrations:VersionN)" });
    expect(profile.getAttribute("href")).toBe("/master-data/integrations/import-profiles");
    expect(ribbonLabels()).toContain("Integrations:DownloadMessage");
  });

  it("the timeline: localized step texts with their parameters (enum names localized), tokens and cost on the AI step, the user on a manual step", async () => {
    renderWith(<TracePage id="msg-1" />);
    const doc = await waitFor(() => panel("order-4711.pdf"));
    const steps = Array.from(doc.querySelectorAll<HTMLElement>("[data-slot=step-timeline] > li"));
    expect(steps.map((s) => s.getAttribute("data-step"))).toEqual(["Received", "Fingerprinted", "ProfileNotFound", "AiCompleted", "OrderCreated", "OrderConfirmed"]);
    expect(steps.map((s) => s.getAttribute("data-outcome"))).toEqual(["Info", "Info", "Warning", "Success", "Success", "Success"]);
    expect(steps[0].textContent).toContain("Integrations:IntakeStep.Received");
    expect(steps[0].textContent).toContain("Received from planning@acme.test");
    expect(steps[1].textContent).toContain("Fingerprint fp-abc123 of a PDF document");
    /* Only the AI step carries usage. */
    const usage = steps[3].textContent!;
    expect(usage).toContain("Model claude-opus-5-5:");
    expect(usage).toMatch(/12[.,\s]?345 in/);
    expect(usage).toMatch(/4[.,\s]?321 out/);
    expect(usage).toMatch(/cost .*0[.,]25/);
    for (const index of [0, 1, 2, 4, 5]) expect(steps[index].textContent).not.toContain("Model ");
    expect(steps[5].textContent).toContain("by Clerk Jan");
  });

  it("the field table: every value with its source location and snippet; a read but empty source still shows", async () => {
    renderWith(<TracePage id="msg-1" />);
    const doc = await waitFor(() => panel("order-4711.pdf"));
    const table = doc.querySelector<HTMLElement>("[data-slot=intake-field-table]")!;
    const row = (path: string) => table.querySelector<HTMLElement>(`[data-path="${path}"]`)!;
    expect(row("order.customerReference").textContent).toContain("PO-4711");
    expect(row("order.customerReference").textContent).toContain("Integrations:SourceLocationKind.Line");
    expect(row("order.customerReference").textContent).toContain("line 3");
    expect(row("order.customerReference").textContent).toContain("Ordernummer: PO-4711");
    expect(row("lines[0].weightKg").textContent).toContain("800");
    expect(row("lines[0].weightKg").textContent).toContain("line 14");
    /* Read from the document but empty: the row is there with its location. */
    expect(row("order.orderDate").textContent).toContain("line 2");
    /* A value without provenance says so. */
    expect(row("lines[0].stops[0].address.city").textContent).toContain("Integrations:NoSourceLocation");
  });

  it("links to the intake and the order only with their permission and app feature", async () => {
    const first = renderWith(<TracePage id="msg-1" />);
    let doc = await waitFor(() => panel("order-4711.pdf"));
    expect(within(doc).queryByRole("link", { name: "Integrations:OpenIntake" })).toBeNull();
    expect(within(doc).queryByRole("link", { name: "Integrations:OpenOrder" })).toBeNull();
    first.unmount();

    grants = { ...VIEW, "Orders.OrderIntakes": true, "Orders.TransportOrders": true };
    const second = renderWith(<TracePage id="msg-1" />);
    doc = await waitFor(() => panel("order-4711.pdf"));
    const intakeLinks = within(doc).getAllByRole("link", { name: "Integrations:OpenIntake" });
    expect(intakeLinks.length).toBeGreaterThanOrEqual(2);
    intakeLinks.forEach((link) => expect(link).toHaveAttribute("href", "/transport/intake/i-1"));
    within(doc)
      .getAllByRole("link", { name: "Integrations:OpenOrder" })
      .forEach((link) => expect(link).toHaveAttribute("href", "/transport/orders/o-1"));
    second.unmount();

    features = { ...FEATURES, "Apps.Transport.Intake": "false" };
    renderWith(<TracePage id="msg-1" />);
    doc = await waitFor(() => panel("order-4711.pdf"));
    expect(within(doc).queryByRole("link", { name: "Integrations:OpenIntake" })).toBeNull();
    expect(within(doc).getAllByRole("link", { name: "Integrations:OpenOrder" }).length).toBeGreaterThan(0);
  });

  it("Reprocess only with its permission; it reprocesses the active document", async () => {
    const first = renderWith(<TracePage id="msg-1" />);
    await waitFor(() => panel("order-4711.pdf"));
    expect(ribbonButton("Integrations:Reprocess")).toBeNull();
    first.unmount();

    grants = { ...VIEW, "Integrations.InboundMessages.Reprocess": true };
    routes.POST["/api/integrations/inbound-documents/{id}/reprocess"] = () => MESSAGE;
    renderWith(<TracePage id="msg-1" />);
    await waitFor(() => expect(ribbonButton("Integrations:Reprocess")).not.toBeNull());
    await click(ribbonButton("Integrations:Reprocess")!);
    await waitFor(() => expect(lastCall(POST, "/api/integrations/inbound-documents/{id}/reprocess")?.params?.path).toEqual({ id: "doc-1" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Integrations:ReprocessRequested"));

    await click(screen.getByRole("tab", { name: "Integrations:InboundDocumentKind.MailBody" }));
    await click(ribbonButton("Integrations:Reprocess")!);
    await waitFor(() => expect(lastCall(POST, "/api/integrations/inbound-documents/{id}/reprocess")?.params?.path).toEqual({ id: "doc-2" }));
  });
});

describe("DocumentTraceRedirect (design 72)", () => {
  it("resolves the document's message and replaces the history entry with its trace", async () => {
    routes.GET["/api/integrations/inbound-message/by-document/{documentId}"] = () => ({ id: "msg-1" });
    renderWith(<DocumentTraceRedirect documentId="doc-1" />);
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/received/msg-1", replace: true }));
    expect(lastCall(GET, "/api/integrations/inbound-message/by-document/{documentId}")?.params?.path).toEqual({ documentId: "doc-1" });
  });

  it("shows not found for an unknown document (or another tenant's) and navigates nowhere", async () => {
    routes.GET["/api/integrations/inbound-message/by-document/{documentId}"] = () => fail("Not found", 404);
    renderWith(<DocumentTraceRedirect documentId="unknown" />);
    expect(await screen.findByText("RecordNotFound")).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("asks nothing without the permission or with the feature off", () => {
    grants = {};
    const first = renderWith(<DocumentTraceRedirect documentId="doc-1" />);
    expect(screen.getByRole("status")).toHaveTextContent("PermissionDenied");
    first.unmount();
    grants = { ...VIEW };
    features = { ...FEATURES, "Integrations.OrderIntake": "false" };
    renderWith(<DocumentTraceRedirect documentId="doc-1" />);
    expect(screen.getByRole("status")).toHaveTextContent("FeatureUnavailable");
    expect(calls(GET, "/api/integrations/inbound-message/by-document/{documentId}")).toHaveLength(0);
  });
});

describe("IntegrationsApp routing (design 72)", () => {
  it("the bare path goes to Received; a message path renders its trace; a document path resolves it", async () => {
    pathname = "/integrations";
    const first = renderWith(<IntegrationsApp />);
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/received", replace: true }));
    first.unmount();

    pathname = "/integrations/received/msg-1";
    const second = renderWith(<IntegrationsApp />);
    await waitFor(() => expect(document.querySelector("[data-slot=trace-page]")).toHaveAttribute("data-record-id", "msg-1"));
    second.unmount();

    navigateMock.mockReset();
    pathname = "/integrations/received/document/doc-9";
    routes.GET["/api/integrations/inbound-message/by-document/{documentId}"] = () => ({ id: "msg-1" });
    renderWith(<IntegrationsApp />);
    await waitFor(() => expect(lastCall(GET, "/api/integrations/inbound-message/by-document/{documentId}")?.params?.path).toEqual({ documentId: "doc-9" }));
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith({ to: "/integrations/received/msg-1", replace: true }));
  });
});
