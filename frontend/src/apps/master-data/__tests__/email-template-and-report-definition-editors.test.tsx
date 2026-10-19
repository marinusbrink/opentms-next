import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The e-mail template editor and the report definition editor (design 49, UI design). E-mail template (PO 2026-10-18:
 * as the OpenTMS template): one tab General in the OpenTMS order (Name | Default; Purpose; Language; a divider;
 * From | Reply-to; To; Cc; Bcc | Send me a copy; Subject; HTML body); Name, Purpose, Subject and HTML body required;
 * Language "All languages" is the null culture; the POST/PUT body carries every mail field (empty addresses as null),
 * the description sent back unchanged and the concurrency stamp; the body is the real RichTextEditor (lazy, Tiptap in
 * jsdom), so a body the visual view cannot keep is saved unchanged; a server refusal shows on the tab the user is on;
 * read-only mode has no commands and no editable control. Report definition: the fields, the language a new definition
 * starts in, the body shape and the Archived switch. The domain hooks are real; the generated client is the mock.
 * Risk class: Medium (UI flow); the server tests prove the same rules where they are enforced (required fields,
 * configured cultures, Scriban parsing). */

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
import { HtmlBodyField } from "@/apps/master-data/components/HtmlBodyField";
import { EmailTemplateEditor } from "@/apps/master-data/editors/EmailTemplateEditor";
import { ReportDefinitionEditor } from "@/apps/master-data/editors/ReportDefinitionEditor";
import { installProseMirrorDomStubs, typeAtEnd } from "@/components/ui/__tests__/rich-text-test-helpers";
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

beforeAll(async () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
  installProseMirrorDomStubs();
  /* The body editor is React.lazy: it suspends once per module, on its first render. Resolve that here (render the
   * fallback, let the chunk load, unmount before the content commits), so every test below meets the editor itself
   * and none depends on running first. The first, suspended mount is covered on its own in html-body-field.test.tsx. */
  const warmUp = render(<HtmlBodyField id="warm-up" label="" value="" readOnly onChange={() => {}} />);
  await act(async () => {
    await import("@/components/ui/rich-text-editor");
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  warmUp.unmount();
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
  name: "Orderbevestiging",
  purpose: 1,
  culture: "nl",
  isDefault: true,
  description: "Kept although the editor has no input for it",
  senderAddress: "planning@example.com",
  replyTo: null,
  to: null,
  cc: null,
  bcc: "archive@example.com",
  sendCopyToSender: true,
  subject: "Uw order {{ order.number }}",
  htmlBody: "<p>Beste klant</p>",
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
const EN = "Platform:Language.en";
const NL = "Platform:Language.nl";

const postedBody = (path: string) => POST.mock.calls.find(([p]) => p === path)![1]!.body as Record<string, unknown>;
const putCall = () => PUT.mock.calls[0] as [string, { params: { path: { id: string } }; body: Record<string, unknown> }];

/* ── E-mail template ───────────────────────────────────────────────────────────────────────────── */

const renderTemplate = (props: Partial<React.ComponentProps<typeof EmailTemplateEditor>> = {}) =>
  renderWith(<EmailTemplateEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} {...props} />);

const loadedTemplate = () => waitFor(() => expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Orderbevestiging"));

/** The HTML body's editable (the contenteditable of the visual view), once the lazy editor is there. */
const bodyEditor = () => within(panel(GENERAL)).findByRole("textbox", { name: /^Platform:HtmlBody/ });
const sourceToggle = () => within(panel(GENERAL)).getByRole("button", { name: "RichText:SourceView" });

/** Types raw HTML into the body's source view (the textarea is labelled by the field's label there). */
async function typeBodySource(html: string) {
  await bodyEditor();
  if (sourceToggle().getAttribute("aria-pressed") !== "true") fireEvent.click(sourceToggle());
  typeIn(GENERAL, "Platform:HtmlBody", html);
}

async function fillMinimalTemplate() {
  typeIn(GENERAL, "Platform:Name", "Order confirmation");
  typeIn(GENERAL, "Platform:Subject", "Your order");
  await typeBodySource("<p>Hello</p>");
}

/** The FormField / checkbox labels of the General tab in document order, without the required marker. */
const labelsInOrder = () =>
  Array.from(panel(GENERAL).querySelectorAll("label"))
    .map((label) => (label.textContent ?? "").replace(/\s*\*$/, "").trim())
    .filter(Boolean);

/** Whether the field's label carries the required marker (FormField renders " *"). */
const markedRequired = (id: string) => panel(GENERAL).querySelector(`label[for="${id}"]`)?.textContent?.includes("*") ?? false;
const idOf = (label: string) => fieldIn(GENERAL, label).id;
/* The mock select adds an empty "—" option when no item has a null value; it is not one of the editor's items. */
const optionsOf = (label: string) => Array.from(fieldIn(GENERAL, label).querySelectorAll("option")).map((o) => o.textContent).filter((t) => t !== "—");

describe("EmailTemplateEditor — one tab General, laid out like an e-mail", () => {
  it("has the fields in the OpenTMS order with their labels, the divider after Language, and no Description input", async () => {
    renderTemplate();
    await bodyEditor();
    /* One form tab; the record-identification tab is the icon tab every editor has. */
    expect(within(editorDialog()).getAllByRole("tab").map((t) => t.textContent || t.getAttribute("aria-label"))).toEqual([GENERAL, "RecordIdentification"]);
    expect(labelsInOrder()).toEqual([
      "Platform:Name",
      "Platform:IsDefault",
      "Platform:Purpose",
      "Platform:Culture",
      "Platform:From",
      "Platform:ReplyTo",
      "Platform:To",
      "Platform:Cc",
      "Platform:Bcc",
      "Platform:SendMeACopy",
      "Platform:Subject",
      "Platform:HtmlBody",
    ]);
    const divider = panel(GENERAL).querySelector("[data-slot=separator]")!;
    expect(divider).not.toBeNull();
    const follows = (a: Node, b: Node) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
    expect(follows(fieldIn(GENERAL, "Platform:Culture"), divider)).toBe(true);
    expect(follows(divider, fieldIn(GENERAL, "Platform:From"))).toBe(true);
    expect(within(panel(GENERAL)).queryByLabelText(/^Platform:Description/)).toBeNull();
    /* "To" empty means the consumer's recipient: the hint says so. */
    expect(within(panel(GENERAL)).getByText("Platform:ToEmptyHint")).toBeInTheDocument();
  });

  it("offers the nine purposes and the languages All languages, English, Nederlands; a new template is the first purpose for all languages", async () => {
    renderTemplate();
    await bodyEditor();
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("1");
    expect(optionsOf("Platform:Purpose")).toEqual([
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
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("all");
    expect(optionsOf("Platform:Culture")).toEqual(["Platform:AllLanguages", EN, NL]);
    expect(checkbox("Platform:IsDefault")).toHaveAttribute("aria-checked", "false");
    expect(checkbox("Platform:SendMeACopy")).toHaveAttribute("aria-checked", "false");
    /* The body starts empty in the visual view. */
    expect((await bodyEditor()).textContent).toBe("");
  });
});

describe("EmailTemplateEditor — required fields", () => {
  it("marks Name, Purpose, Subject and HTML body required; Language and the addresses optional", async () => {
    renderTemplate();
    const body = await bodyEditor();
    expect(markedRequired(idOf("Platform:Name"))).toBe(true);
    expect(markedRequired(idOf("Platform:Purpose"))).toBe(true);
    expect(markedRequired(idOf("Platform:Subject"))).toBe(true);
    expect(markedRequired(body.id)).toBe(true);
    expect(body).toHaveAttribute("aria-required", "true");
    for (const label of ["Platform:Culture", "Platform:From", "Platform:ReplyTo", "Platform:To", "Platform:Cc", "Platform:Bcc"]) {
      expect(markedRequired(idOf(label)), label).toBe(false);
    }
  });

  it("refuses an empty name, subject and body (whitespace counts as empty) and sends nothing", async () => {
    renderTemplate();
    const body = await bodyEditor();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("Platform:Required")).toHaveLength(3);
    expect(body).toHaveAttribute("aria-invalid", "true");
    expect(fieldIn(GENERAL, "Platform:Subject")).toHaveAttribute("aria-invalid", "true");

    typeIn(GENERAL, "Platform:Name", "T");
    typeIn(GENERAL, "Platform:Subject", "   ");
    await typeBodySource("  \n ");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("Platform:Required")).toHaveLength(2);
  });

  it("a purpose is always chosen: clearing the select keeps the previous purpose", async () => {
    renderTemplate();
    await fillMinimalTemplate();
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "6" } });
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "" } });
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("6");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").purpose).toBe(6);
  });
});

describe("EmailTemplateEditor — the request body", () => {
  it("POSTs every mail field: name and subject trimmed, empty addresses as null, the body as typed in the source view, the chosen language", async () => {
    renderTemplate();
    typeIn(GENERAL, "Platform:Name", "  Factuurmail  ");
    fireEvent.click(checkbox("Platform:IsDefault"));
    fireEvent.change(fieldIn(GENERAL, "Platform:Purpose"), { target: { value: "3" } });
    fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: "nl" } });
    typeIn(GENERAL, "Platform:From", "{{ company.email }}");
    typeIn(GENERAL, "Platform:ReplyTo", "   ");
    typeIn(GENERAL, "Platform:Cc", "cc@example.com");
    typeIn(GENERAL, "Platform:Bcc", " archive@example.com ");
    fireEvent.click(checkbox("Platform:SendMeACopy"));
    typeIn(GENERAL, "Platform:Subject", "  Factuur {{ invoice.number }}  ");
    await typeBodySource("<p>Beste {{ customer.name }}</p>\n");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/platform/email-template");
    expect(postedBody("/api/platform/email-template")).toEqual({
      name: "Factuurmail",
      purpose: 3,
      culture: "nl",
      isDefault: true,
      senderAddress: "{{ company.email }}",
      replyTo: null,
      to: null,
      cc: "cc@example.com",
      bcc: "archive@example.com",
      sendCopyToSender: true,
      subject: "Factuur {{ invoice.number }}",
      htmlBody: "<p>Beste {{ customer.name }}</p>\n",
      description: null,
    });
  });

  it.each([
    ["all", null],
    ["en", "en"],
    ["nl", "nl"],
  ])("Language %s is sent as culture %s", async (choice, expected) => {
    renderTemplate();
    await fillMinimalTemplate();
    fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: choice === "all" ? "nl" : choice } });
    if (choice === "all") fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: "all" } });
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").culture).toBe(expected);
  });

  it("clearing the Language select falls back to All languages (culture null)", async () => {
    renderTemplate();
    await fillMinimalTemplate();
    fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: "en" } });
    fireEvent.change(fieldIn(GENERAL, "Platform:Culture"), { target: { value: "" } });
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("all");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").culture).toBeNull();
  });

  it("a body typed in the visual view is posted as HTML, with Scriban un-escaped inside {{ }} only", async () => {
    renderTemplate();
    typeIn(GENERAL, "Platform:Name", "Reminder");
    typeIn(GENERAL, "Platform:Subject", "Reminder");
    const body = await bodyEditor();
    typeAtEnd(body, "Total {{ if total > 0 }}due{{ end }} & more");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/email-template").htmlBody).toBe("<p>Total {{ if total > 0 }}due{{ end }} &amp; more</p>");
  });

  it("loads a template into the fields and PUTs it unchanged: every field, the description sent back, the concurrency stamp", async () => {
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("nl");
    expect(fieldIn(GENERAL, "Platform:Purpose")).toHaveValue("1");
    expect(fieldIn(GENERAL, "Platform:From")).toHaveValue("planning@example.com");
    expect(fieldIn(GENERAL, "Platform:Bcc")).toHaveValue("archive@example.com");
    expect(fieldIn(GENERAL, "Platform:Subject")).toHaveValue("Uw order {{ order.number }}");
    expect(checkbox("Platform:IsDefault")).toHaveAttribute("aria-checked", "true");
    expect(checkbox("Platform:SendMeACopy")).toHaveAttribute("aria-checked", "true");
    await waitFor(async () => expect(await bodyEditor()).toHaveTextContent("Beste klant"));

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[0]).toBe("/api/platform/email-template/{id}");
    expect(putCall()[1]).toEqual({
      params: { path: { id: "et1" } },
      body: {
        name: "Orderbevestiging",
        purpose: 1,
        culture: "nl",
        isDefault: true,
        senderAddress: "planning@example.com",
        replyTo: null,
        to: null,
        cc: null,
        bcc: "archive@example.com",
        sendCopyToSender: true,
        subject: "Uw order {{ order.number }}",
        htmlBody: "<p>Beste klant</p>",
        description: "Kept although the editor has no input for it",
        concurrencyStamp: "stamp-et1",
      },
    });
  });

  it("a template for all languages (culture null) shows All languages; a culture in another case is read as the configured one", async () => {
    template = { ...TEMPLATE, culture: null };
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("all");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.culture).toBeNull();
  });

  it("reads a culture whatever its case and sends the configured one", async () => {
    template = { ...TEMPLATE, culture: "EN" };
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(fieldIn(GENERAL, "Platform:Culture")).toHaveValue("en");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.culture).toBe("en");
  });
});

describe("EmailTemplateEditor — the body is never changed behind the user's back", () => {
  const TABLE_BODY = '<table width="100%"><tbody>{{ for line in order.lines }}<tr><td style="padding: 4px">{{ line.text }}</td></tr>{{ end }}</tbody></table>';

  it("loading a template does not make the form dirty: Close closes without asking", async () => {
    const onClose = vi.fn();
    renderTemplate({ mode: "edit", id: "et1", onClose });
    await loadedTemplate();
    await waitFor(async () => expect(await bodyEditor()).toHaveTextContent("Beste klant"));
    fireEvent.click(within(editorDialog()).getAllByRole("button", { name: "Close" })[0]);
    expect(screen.queryByText("DiscardChangesQuestion")).toBeNull();
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("a table body opens in the source view with the warning and is PUT byte for byte", async () => {
    template = { ...TEMPLATE, htmlBody: TABLE_BODY };
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    expect(await within(panel(GENERAL)).findByRole("status")).toHaveTextContent("RichText:UnsupportedMarkupNotice");
    expect(fieldIn(GENERAL, "Platform:HtmlBody").tagName).toBe("TEXTAREA");
    expect(fieldIn(GENERAL, "Platform:HtmlBody")).toHaveValue(TABLE_BODY);

    typeIn(GENERAL, "Platform:Subject", "Changed subject");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.htmlBody).toBe(TABLE_BODY);
    expect(putCall()[1].body.subject).toBe("Changed subject");
  });
});

describe("EmailTemplateEditor — a server refusal", () => {
  const MESSAGE = "The template in HTML body has a syntax error on line 1.";
  const refuse = () =>
    POST.mockResolvedValue({
      data: undefined,
      error: { error: { code: "OpenTms.Platform:TemplateSyntaxError", message: MESSAGE } },
      response: new Response(null, { status: 400 }),
    });

  it("shows the server's message on the General tab the user saved from, and keeps what was typed", async () => {
    refuse();
    renderTemplate();
    await fillMinimalTemplate();
    typeIn(GENERAL, "Platform:HtmlBody", "{{ if overdue }}<p>Overdue</p>");
    await submitEditor();
    await waitFor(() => expect(within(panel(GENERAL)).getByText(MESSAGE)).toBeInTheDocument());
    expect(fieldIn(GENERAL, "Platform:HtmlBody")).toHaveValue("{{ if overdue }}<p>Overdue</p>");
    expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Order confirmation");
  });

  it("saved from the record-identification tab (no form there), the message shows on General", async () => {
    PUT.mockResolvedValue({
      data: undefined,
      error: { error: { code: "OpenTms.Platform:TemplateSyntaxError", message: MESSAGE } },
      response: new Response(null, { status: 400 }),
    });
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    tab("RecordIdentification");
    await submitEditor();
    await waitFor(() => expect(within(panel(GENERAL)).getByText(MESSAGE)).toBeInTheDocument());
  });
});

describe("EmailTemplateEditor — read-only, delete", () => {
  it("read-only: every control disabled, the body not editable and its formatting off, no Save and no Delete, nothing sent on submit", async () => {
    grants = { "Platform.EmailTemplates.Delete": true };
    renderTemplate({ mode: "edit", id: "et1", readOnly: true });
    await loadedTemplate();
    const body = await bodyEditor();
    expect(enabledControls()).toEqual([]);
    expect(fieldIn(GENERAL, "Platform:Purpose")).toBeDisabled();
    expect(fieldIn(GENERAL, "Platform:Culture")).toBeDisabled();
    expect(body).toHaveAttribute("contenteditable", "false");
    expect(within(panel(GENERAL)).getByRole("button", { name: "RichText:Bold" })).toBeDisabled();
    expectNoSave();
    await submitEditor();
    expect(PUT).not.toHaveBeenCalled();
  });

  it("editable: the controls and the body are editable and Save is there; without Platform.EmailTemplates.Delete there is no Delete", async () => {
    renderTemplate({ mode: "edit", id: "et1" });
    await loadedTemplate();
    const body = await bodyEditor();
    for (const label of ["Platform:Name", "Platform:Purpose", "Platform:Culture", "Platform:From", "Platform:ReplyTo", "Platform:To", "Platform:Cc", "Platform:Bcc", "Platform:Subject"]) {
      expect(fieldIn(GENERAL, label), label).toBeEnabled();
    }
    expect(body).toHaveAttribute("contenteditable", "true");
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
    expect(within(confirm).getByText(/Orderbevestiging/)).toBeInTheDocument();
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
