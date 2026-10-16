import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The e-mail template editor and the report definition editor (design 49, UI design). E-mail template: Name and
 * Purpose required; the English content (subject and body) is required, a Dutch tab is optional but a started one
 * needs both parts, and an empty Dutch tab sends no content row; the POST/PUT body carries purpose, default, the
 * addresses and the contents; the HTML body shows in the sandboxed preview; read-only mode has no commands and no
 * editable control. Report definition: the fields, the language a new definition starts in, the body shape and the
 * Archived switch. The domain hooks are real; the generated client is the mock. Risk class: Medium (UI flow); the
 * server tests prove the same rules where they are enforced (DefaultLanguageContentRequired, Scriban parsing). */

let culture = "en";
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture, isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: { auth: { grantedPolicies: grants }, features: { values: {} }, localization: { languages: [] } },
  }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { EmailTemplateEditor } from "@/apps/master-data/editors/EmailTemplateEditor";
import { ReportDefinitionEditor } from "@/apps/master-data/editors/ReportDefinitionEditor";
import {
  checkbox,
  editorDialog,
  enabledControls,
  expectNoSave,
  fieldIn,
  ok,
  panel,
  ribbonButton,
  submitEditor,
  tab,
  typeIn,
} from "./platform-test-helpers";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;
const DELETE = apiClient.DELETE as unknown as Mock<ClientCall>;

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

const TEMPLATE = {
  id: "et1",
  name: "Order confirmation",
  purpose: 1,
  isDefault: true,
  description: null,
  senderAddress: "planning@example.com",
  senderName: "{{ company.name }}",
  replyTo: null,
  to: null,
  cc: null,
  bcc: "archive@example.com",
  sendCopyToSender: false,
  contents: [
    { culture: "en", subject: "Your order {{ order.number }}", htmlBody: "<p>Dear customer</p>" },
    { culture: "nl", subject: "Uw order {{ order.number }}", htmlBody: "<p>Beste klant</p>" },
  ],
  concurrencyStamp: "stamp-et1",
  creationTime: "2026-10-16T10:00:00Z",
  lastModificationTime: null,
};

const REPORT = {
  id: "rd1",
  name: "Invoice NL",
  purpose: 3,
  culture: "nl",
  provider: 1,
  providerTemplateId: "tpl-123",
  fileNamePattern: "factuur-{{ number }}.pdf",
  isDefault: false,
  isArchived: false,
  availableInCustomerPortal: true,
  description: "The Dutch invoice",
  concurrencyStamp: "stamp-rd1",
  creationTime: "2026-10-16T10:00:00Z",
  lastModificationTime: null,
};

let template: Record<string, unknown> = TEMPLATE;
let report: Record<string, unknown> = REPORT;

beforeEach(() => {
  culture = "en";
  grants = {};
  template = TEMPLATE;
  report = REPORT;
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/platform/email-template/{id}":
        return ok(template);
      case "/api/platform/report-definition/{id}":
        return ok(report);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => ok(path.includes("email") ? { ...TEMPLATE, id: "et-new" } : { ...REPORT, id: "rd-new" }));
  PUT.mockReset().mockImplementation(async (path) => ok(path.includes("email") ? TEMPLATE : REPORT));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const GENERAL = "Platform:Tab.General";
const ADDRESSES = "Platform:Tab.Addresses";
const EN = "Platform:Language.en";
const NL = "Platform:Language.nl";

const postedBody = (path: string) => POST.mock.calls.find(([p]) => p === path)![1]!.body as Record<string, unknown>;
const putCall = () => PUT.mock.calls[0] as [string, { params: { path: { id: string } }; body: Record<string, unknown> }];

/* ── E-mail template ───────────────────────────────────────────────────────────────────────────── */

const renderTemplate = (props: Partial<React.ComponentProps<typeof EmailTemplateEditor>> = {}) =>
  renderWith(<EmailTemplateEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} {...props} />);

const loadedTemplate = () => waitFor(() => expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Order confirmation"));

function fillMinimalTemplate() {
  typeIn(GENERAL, "Platform:Name", "Order confirmation");
  typeIn(EN, "Platform:Subject", "Your order");
  typeIn(EN, "Platform:HtmlBody", "<p>Hello</p>");
}

describe("EmailTemplateEditor — tabs and fields", () => {
  it("has General, Addresses and one tab per configured language, English first; the purposes are the nine of the design", () => {
    renderTemplate();
    const tabs = within(editorDialog()).getAllByRole("tab").map((t) => t.textContent);
    expect(tabs.slice(0, 4)).toEqual([GENERAL, ADDRESSES, EN, NL]);
    const purpose = fieldIn(GENERAL, "Platform:Purpose");
    expect(purpose).toHaveValue("1");
    expect(Array.from(purpose.querySelectorAll("option")).map((o) => o.textContent).filter((t) => t !== "—")).toEqual([
      "Platform:EmailTemplatePurpose.TransportOrder",
      "Platform:EmailTemplatePurpose.TransportFile",
      "Platform:EmailTemplatePurpose.Invoice",
      "Platform:EmailTemplatePurpose.InvoiceReminder",
      "Platform:EmailTemplatePurpose.PurchaseOrder",
      "Platform:EmailTemplatePurpose.EtaNotification",
      "Platform:EmailTemplatePurpose.DriverInstructions",
      "Platform:EmailTemplatePurpose.DigitalArchive",
      "Platform:EmailTemplatePurpose.ProformaOverview",
    ]);
    for (const label of ["Platform:SenderAddress", "Platform:SenderName", "Platform:ReplyTo", "Platform:To", "Platform:Cc", "Platform:Bcc"]) {
      expect(fieldIn(ADDRESSES, label)).toBeInTheDocument();
    }
    expect(fieldIn(EN, "Platform:HtmlBody").tagName).toBe("TEXTAREA");
    expect(fieldIn(NL, "Platform:HtmlBody").tagName).toBe("TEXTAREA");
  });
});

/** Whether the field's label carries the required marker (FormField renders " *"). */
const markedRequired = (tabLabel: string, label: string) => {
  const control = fieldIn(tabLabel, label);
  return panel(tabLabel).querySelector(`label[for="${control.id}"]`)?.textContent?.includes("*") ?? false;
};

describe("EmailTemplateEditor — required fields", () => {
  it("marks Name, Purpose and the English subject and body required; the Dutch ones and the addresses optional", () => {
    renderTemplate();
    expect(markedRequired(GENERAL, "Platform:Name")).toBe(true);
    expect(markedRequired(GENERAL, "Platform:Purpose")).toBe(true);
    expect(markedRequired(EN, "Platform:Subject")).toBe(true);
    expect(markedRequired(EN, "Platform:HtmlBody")).toBe(true);
    expect(markedRequired(NL, "Platform:Subject")).toBe(false);
    expect(markedRequired(NL, "Platform:HtmlBody")).toBe(false);
    expect(markedRequired(ADDRESSES, "Platform:SenderAddress")).toBe(false);
    expect(markedRequired(ADDRESSES, "Platform:To")).toBe(false);
  });

  it("a purpose is always chosen: clearing the select keeps the previous purpose", async () => {
    renderTemplate();
    fillMinimalTemplate();
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "6" } });
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "" } });
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("6");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").purpose).toBe(6);
  });
});

describe("EmailTemplateEditor — validation", () => {
  it("refuses an empty name and missing English content, and sends nothing", async () => {
    renderTemplate();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getByText("Platform:Required")).toBeInTheDocument();
    expect(within(panel(EN)).getAllByText("Platform:DefaultLanguageContentRequired")).toHaveLength(2);
    /* The Dutch tab is optional: no error there while it is empty. */
    expect(within(panel(NL)).queryByText("Platform:Required")).toBeNull();
  });

  it("refuses an English subject without a body (and a body without a subject)", async () => {
    renderTemplate();
    typeIn(GENERAL, "Platform:Name", "T");
    typeIn(EN, "Platform:Subject", "Only a subject");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(EN)).getAllByText("Platform:DefaultLanguageContentRequired")).toHaveLength(1);

    typeIn(EN, "Platform:Subject", "   ");
    typeIn(EN, "Platform:HtmlBody", "<p>Only a body</p>");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(EN)).getAllByText("Platform:DefaultLanguageContentRequired")).toHaveLength(1);
  });

  it("refuses a half-filled Dutch tab: a subject without a body, then a body without a subject", async () => {
    renderTemplate();
    fillMinimalTemplate();
    typeIn(NL, "Platform:Subject", "Uw order");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(NL)).getAllByText("Platform:Required")).toHaveLength(1);
    /* The editor moves to the tab with the first error. */
    expect(screen.getByRole("tab", { name: NL })).toHaveAttribute("aria-selected", "true");

    typeIn(NL, "Platform:Subject", "");
    typeIn(NL, "Platform:HtmlBody", "<p>Beste klant</p>");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(NL)).getAllByText("Platform:Required")).toHaveLength(1);

    typeIn(NL, "Platform:Subject", "Uw order");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").contents).toEqual([
      { culture: "en", subject: "Your order", htmlBody: "<p>Hello</p>" },
      { culture: "nl", subject: "Uw order", htmlBody: "<p>Beste klant</p>" },
    ]);
  });
});

describe("EmailTemplateEditor — the request body", () => {
  it("POSTs purpose, default, every address (empty as null) and only the English content when the Dutch tab is empty", async () => {
    renderTemplate();
    typeIn(GENERAL, "Platform:Name", "  Invoice mail  ");
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "3" } });
    fireEvent.click(checkbox("Platform:IsDefault"));
    typeIn(GENERAL, "Platform:Description", "  Sent with every invoice ");
    tab(ADDRESSES);
    typeIn(ADDRESSES, "Platform:SenderAddress", "{{ company.email }}");
    typeIn(ADDRESSES, "Platform:SenderName", " Finance ");
    typeIn(ADDRESSES, "Platform:Cc", "cc@example.com");
    typeIn(ADDRESSES, "Platform:Bcc", "   ");
    fireEvent.click(checkbox("Platform:SendCopyToSender"));
    typeIn(EN, "Platform:Subject", "  Invoice {{ invoice.number }}  ");
    typeIn(EN, "Platform:HtmlBody", "<p>Dear {{ customer.name }}</p>\n");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/platform/email-template");
    expect(postedBody("/api/platform/email-template")).toEqual({
      name: "Invoice mail",
      purpose: 3,
      isDefault: true,
      description: "Sent with every invoice",
      senderAddress: "{{ company.email }}",
      senderName: "Finance",
      replyTo: null,
      to: null,
      cc: "cc@example.com",
      bcc: null,
      sendCopyToSender: true,
      /* The HTML source goes as typed; the subject is trimmed. No "nl" row: the Dutch tab is empty. */
      contents: [{ culture: "en", subject: "Invoice {{ invoice.number }}", htmlBody: "<p>Dear {{ customer.name }}</p>\n" }],
    });
  });

  it("loads both languages and PUTs them with the concurrency stamp; clearing the Dutch tab drops its row", async () => {
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(fieldIn(EN, "Platform:Subject")).toHaveValue("Your order {{ order.number }}");
    expect(fieldIn(NL, "Platform:Subject")).toHaveValue("Uw order {{ order.number }}");
    expect(fieldIn(NL, "Platform:HtmlBody")).toHaveValue("<p>Beste klant</p>");
    expect(fieldIn(ADDRESSES, "Platform:Bcc")).toHaveValue("archive@example.com");

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[0]).toBe("/api/platform/email-template/{id}");
    expect(putCall()[1].params).toEqual({ path: { id: "et1" } });
    expect(putCall()[1].body).toEqual(
      expect.objectContaining({
        name: "Order confirmation",
        purpose: 1,
        isDefault: true,
        senderAddress: "planning@example.com",
        bcc: "archive@example.com",
        concurrencyStamp: "stamp-et1",
        contents: [
          { culture: "en", subject: "Your order {{ order.number }}", htmlBody: "<p>Dear customer</p>" },
          { culture: "nl", subject: "Uw order {{ order.number }}", htmlBody: "<p>Beste klant</p>" },
        ],
      }),
    );

    PUT.mockClear();
    typeIn(NL, "Platform:Subject", "");
    typeIn(NL, "Platform:HtmlBody", "");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.contents).toEqual([{ culture: "en", subject: "Your order {{ order.number }}", htmlBody: "<p>Dear customer</p>" }]);
  });

  it("reads content rows whatever the case of their culture", async () => {
    template = { ...TEMPLATE, contents: [{ culture: "EN", subject: "S", htmlBody: "<b>B</b>" }] };
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(fieldIn(EN, "Platform:Subject")).toHaveValue("S");
    expect(fieldIn(NL, "Platform:Subject")).toHaveValue("");
  });
});

describe("EmailTemplateEditor — preview", () => {
  it("shows each language's body in its own sandboxed preview and follows the edits", async () => {
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    const enPreview = within(panel(EN)).getByTitle(`Platform:Preview ${EN}`);
    const nlPreview = within(panel(NL)).getByTitle(`Platform:Preview ${NL}`);
    expect(enPreview.tagName).toBe("IFRAME");
    expect(enPreview.getAttribute("sandbox")).toBe("");
    expect(nlPreview.getAttribute("sandbox")).toBe("");
    await waitFor(() => expect(enPreview.getAttribute("srcdoc")).toBe("<p>Dear customer</p>"));
    expect(nlPreview.getAttribute("srcdoc")).toBe("<p>Beste klant</p>");

    typeIn(EN, "Platform:HtmlBody", "<h1>Changed</h1><script>alert(1)</script>");
    await waitFor(() => expect(within(panel(EN)).getByTitle(`Platform:Preview ${EN}`).getAttribute("srcdoc")).toBe("<h1>Changed</h1><script>alert(1)</script>"));
    expect(document.querySelectorAll("script")).toHaveLength(0);
  });
});

describe("EmailTemplateEditor — read-only, delete", () => {
  it("read-only: every control disabled, no Save and no Delete, nothing sent on submit", async () => {
    grants = { "Platform.EmailTemplates.Delete": true };
    renderTemplate({ mode: "edit", id: "et1", readOnly: true });
    await loadedTemplate();
    expect(enabledControls()).toEqual([]);
    expect(fieldIn(EN, "Platform:HtmlBody")).toBeDisabled();
    expect(fieldIn(GENERAL, "Platform:Purpose")).toBeDisabled();
    expectNoSave();
    await submitEditor();
    expect(PUT).not.toHaveBeenCalled();
  });

  it("editable: the controls are enabled and Save is there; without Platform.EmailTemplates.Delete there is no Delete", async () => {
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(enabledControls().length).toBeGreaterThan(10);
    expect(ribbonButton("Save")).not.toBeNull();
    expect(ribbonButton("Delete")).toBeNull();
  });

  it("with the permission, Delete confirms and calls the delete endpoint", async () => {
    grants = { "Platform.EmailTemplates.Delete": true };
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    fireEvent.click(ribbonButton("Delete")!);
    const confirm = await screen.findByRole("alertdialog");
    expect(within(confirm).getByText("Platform:DeleteEmailTemplate")).toBeInTheDocument();
    expect(within(confirm).getByText(/Order confirmation/)).toBeInTheDocument();
    fireEvent.click(within(confirm).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(DELETE).toHaveBeenCalledWith("/api/platform/email-template/{id}", { params: { path: { id: "et1" } } }));
  });
});

/* ── Report definition ─────────────────────────────────────────────────────────────────────────── */

const renderReport = (props: Partial<React.ComponentProps<typeof ReportDefinitionEditor>> = {}) =>
  renderWith(<ReportDefinitionEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} {...props} />);

const loadedReport = () => waitFor(() => expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Invoice NL"));

describe("ReportDefinitionEditor — fields and defaults", () => {
  it("has the designed fields; Purpose, Language and Provider are fixed lists", () => {
    renderReport();
    for (const label of ["Platform:Name", "Platform:Purpose", "Platform:Culture", "Platform:Provider", "Platform:ProviderTemplateId", "Platform:FileNamePattern", "Platform:Description"]) {
      expect(fieldIn(GENERAL, label)).toBeInTheDocument();
    }
    for (const name of ["Platform:IsDefault", "Platform:IsArchived", "Platform:AvailableInCustomerPortal"]) {
      expect(checkbox(name)).toBeInTheDocument();
    }
    const options = (label: string) => Array.from(fieldIn(GENERAL, label).querySelectorAll("option")).map((o) => o.textContent).filter((t) => t !== "—");
    expect(options("Platform:Purpose")).toEqual([
      "Platform:ReportPurpose.TransportOrderConfirmation",
      "Platform:ReportPurpose.TransportFile",
      "Platform:ReportPurpose.Invoice",
      "Platform:ReportPurpose.PurchaseOrder",
      "Platform:ReportPurpose.TransportUnitBalance",
    ]);
    expect(options("Platform:Culture")).toEqual([EN, NL]);
    expect(options("Platform:Provider")).toEqual(["Platform:ReportRenderingProvider.PdfGeneratorApi"]);
    expect(within(panel(GENERAL)).getByText("Platform:FileNamePatternHint")).toBeInTheDocument();
  });

  it.each([
    ["en", "en"],
    ["nl", "nl"],
    ["nl-NL", "nl"],
    ["en-GB", "en"],
    ["de", "en"],
  ])("a new definition for a user in %s starts in %s", (userCulture, expected) => {
    culture = userCulture;
    renderReport();
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue(expected);
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("1");
    expect(fieldIn(GENERAL, "Platform:Provider")).toHaveValue("1");
  });
});

describe("ReportDefinitionEditor — validation and body", () => {
  it("refuses an empty name, provider template id and file name pattern", async () => {
    renderReport();
    typeIn(GENERAL, "Platform:ProviderTemplateId", "   ");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("Platform:Required")).toHaveLength(3);
  });

  it("POSTs every field, trimmed, with the chosen purpose and language", async () => {
    culture = "nl";
    renderReport();
    typeIn(GENERAL, "Platform:Name", " Factuur ");
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "3" } });
    fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: "en" } });
    typeIn(GENERAL, "Platform:ProviderTemplateId", " tpl-9 ");
    typeIn(GENERAL, "Platform:FileNamePattern", " invoice-{{ number }} ");
    fireEvent.click(checkbox("Platform:IsDefault"));
    fireEvent.click(checkbox("Platform:AvailableInCustomerPortal"));
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/platform/report-definition");
    expect(postedBody("/api/platform/report-definition")).toEqual({
      name: "Factuur",
      purpose: 3,
      culture: "en",
      provider: 1,
      providerTemplateId: "tpl-9",
      /* ".pdf" is the server's to add. */
      fileNamePattern: "invoice-{{ number }}",
      isDefault: true,
      isArchived: false,
      availableInCustomerPortal: true,
      description: null,
    });
  });

  it("loads a definition and PUTs it with the concurrency stamp; Archived archives it", async () => {
    renderReport({ mode: "edit", id: "rd1" });
    await loadedReport();
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("nl");
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("3");
    expect(checkbox("Platform:AvailableInCustomerPortal")).toHaveAttribute("aria-checked", "true");
    expect(within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-header]")!).queryByText("Platform:IsArchived")).toBeNull();

    fireEvent.click(checkbox("Platform:IsArchived"));
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[0]).toBe("/api/platform/report-definition/{id}");
    expect(putCall()[1]).toEqual({
      params: { path: { id: "rd1" } },
      body: {
        name: "Invoice NL",
        purpose: 3,
        culture: "nl",
        provider: 1,
        providerTemplateId: "tpl-123",
        fileNamePattern: "factuur-{{ number }}.pdf",
        isDefault: false,
        isArchived: true,
        availableInCustomerPortal: true,
        description: "The Dutch invoice",
        concurrencyStamp: "stamp-rd1",
      },
    });
  });

  it("an archived definition shows the Archived badge, and clearing Archived unarchives it", async () => {
    report = { ...REPORT, isArchived: true };
    renderReport({ mode: "edit", id: "rd1" });
    await loadedReport();
    expect(within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-header]")!).getByText("Platform:IsArchived")).toBeInTheDocument();
    fireEvent.click(checkbox("Platform:IsArchived"));
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.isArchived).toBe(false);
  });

  it("read-only: every control disabled, no Save and no Delete", async () => {
    grants = { "Platform.ReportDefinitions.Delete": true };
    renderReport({ mode: "edit", id: "rd1", readOnly: true });
    await loadedReport();
    expect(enabledControls()).toEqual([]);
    expect(checkbox("Platform:IsArchived")).toHaveAttribute("aria-disabled", "true");
    expectNoSave();
  });
});
