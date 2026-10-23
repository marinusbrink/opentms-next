import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The report definition editor's ribbon group Template (design 50, UI design): Edit template (the provider's editor
 * in a new tab), Copy template (confirmed; opens the new definition) and Test document (downloads the test PDF).
 * Edit mode only; each command hidden without its permission (UI-PERM-002: EditTemplate; EditTemplate + Create; the
 * view permission), shown in read-only editors too, and disabled with the reason from the rendering status as its
 * tooltip (UI-PERM-005), a provider failure's message appended; unsaved changes disable all three ("save first").
 * Under Provider template id the template's name at the provider, or "not found". The domain hooks, the browser
 * helpers and the dialogs are real; the generated client, window.open and the object-URL API are the mocks.
 * Risk class: Medium (UI flow); the server enforces permissions, the feature, archived and ownership again. */

const TAKES_VALUE = new Set(["Platform:TemplateName", "Platform:TemplateCopied"]);
vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") || TAKES_VALUE.has(k) ? `${k} {0}` : k), culture: "en", isLoading: false }),
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
import { toast } from "sonner";

import { apiClient } from "@/lib/api/client";
import { ReportDefinitionEditor } from "@/apps/master-data/editors/ReportDefinitionEditor";
import { editorDialog, fieldIn, ok, panel, submitEditor, typeIn } from "./platform-test-helpers";

type ClientInit = { params?: { path?: Record<string, unknown> }; body?: unknown; parseAs?: string };
type ClientCall = (path: string, init?: ClientInit) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;

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

const CLEAR = {
  featureEnabled: true,
  providerAvailable: true,
  providerConfigured: true,
  supportsTemplateEditor: true,
  supportsTemplateCopy: true,
  templateFound: true,
  templateName: "Factuur NL (standaard)",
  templateOwnedByWorkspace: true,
  message: null,
};

const ALL_GRANTS = {
  "Platform.ReportDefinitions": true,
  "Platform.ReportDefinitions.Create": true,
  "Platform.ReportDefinitions.Update": true,
  "Platform.ReportDefinitions.EditTemplate": true,
};

const DETAIL = "/api/platform/report-definition/{id}";
const STATUS = "/api/platform/report-definition/{id}/rendering-status";
const EDITOR_URL = "/api/platform/report-definition/{id}/template-editor";
const COPY = "/api/platform/report-definition/{id}/copy-template";
const TEST_RENDER = "/api/platform/report-definition/{id}/test-render";

const EDIT = "Platform:EditTemplate";
const COPY_LABEL = "Platform:CopyTemplate";
const TEST = "Platform:TestDocument";
const GENERAL = "Platform:Tab.General";

const failure = (message: string) => ({ data: undefined, error: { error: { message } }, response: new Response(null, { status: 400 }) });

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

let report: Record<string, unknown>;
let status: () => Promise<unknown>;
let postFor: Record<string, () => Promise<unknown>>;
let client: QueryClient;

beforeEach(() => {
  grants = { ...ALL_GRANTS };
  report = { ...REPORT };
  status = async () => ok({ ...CLEAR });
  postFor = {};
  GET.mockReset().mockImplementation(async (path) => {
    if (path === DETAIL) return ok(report);
    if (path === STATUS) return status();
    return ok([]);
  });
  POST.mockReset().mockImplementation(async (path) => (postFor[path] ? postFor[path]() : ok(undefined)));
  PUT.mockReset().mockImplementation(async (_path, init) => {
    report = { ...report, ...(init!.body as Record<string, unknown>), concurrencyStamp: "stamp-2" };
    return ok(report);
  });
  vi.mocked(toast.success).mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

function renderEditor(props: Partial<React.ComponentProps<typeof ReportDefinitionEditor>> = {}) {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const all = { mode: "edit" as const, id: "rd1", readOnly: false, open: true, onClose: vi.fn(), onSaved: vi.fn(), onOpenRecord: vi.fn(), ...props };
  render(
    <QueryClientProvider client={client}>
      <ReportDefinitionEditor {...all} />
    </QueryClientProvider>,
  );
  return all;
}

/** A ribbon button by its exact label (the label span), or null. */
const command = (label: string) =>
  Array.from(editorDialog().querySelectorAll<HTMLElement>("[data-slot=ribbon-button]")).find((b) => b.querySelector("span")?.textContent === label) ?? null;
const ribbonLabels = () => Array.from(editorDialog().querySelectorAll<HTMLElement>("[data-slot=ribbon-button] span")).map((s) => s.textContent);
const isDisabled = (label: string) => command(label)?.getAttribute("aria-disabled") === "true";
const templateGroup = () => within(editorDialog()).queryByText("Platform:TemplateGroup");

const loaded = () => waitFor(() => expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Invoice NL"));
/** The record and its rendering status are in: with a clear status every command is enabled. */
const ready = async () => {
  await loaded();
  await waitFor(() => expect(GET.mock.calls.some(([p]) => p === STATUS)).toBe(true));
  await waitFor(() => expect(isDisabled(TEST)).toBe(false));
};
const generalAlerts = () => within(panel(GENERAL)).queryAllByRole("alert").map((a) => a.textContent);

/* ── Presence ─────────────────────────────────────────────────────────────────────────────────── */

describe("Template commands — where they show", () => {
  it("edit mode: the group Template with Edit, Copy and Test document, in that order after Save and before Delete", async () => {
    grants = { ...ALL_GRANTS, "Platform.ReportDefinitions.Delete": true };
    renderEditor();
    await ready();
    expect(templateGroup()).toBeInTheDocument();
    const labels = ribbonLabels();
    const at = (label: string) => labels.indexOf(label);
    expect(at("Save")).toBeLessThan(at(EDIT));
    expect(at(EDIT)).toBeLessThan(at(COPY_LABEL));
    expect(at(COPY_LABEL)).toBeLessThan(at(TEST));
    expect(at(TEST)).toBeLessThan(at("Delete"));
    for (const label of [EDIT, COPY_LABEL, TEST]) {
      expect(isDisabled(label)).toBe(false);
      expect(command(label)).toHaveAttribute("title", label);
    }
  });

  it("create mode: no Template group and no rendering-status request", async () => {
    renderEditor({ mode: "create", id: undefined });
    await waitFor(() => expect(command("Save")).not.toBeNull());
    expect(templateGroup()).toBeNull();
    for (const label of [EDIT, COPY_LABEL, TEST]) expect(command(label)).toBeNull();
    expect(GET.mock.calls.some(([p]) => p === STATUS)).toBe(false);
  });

  it("read-only editor: the three commands show (they do not change this record), Save does not", async () => {
    renderEditor({ readOnly: true });
    await ready();
    expect(command("Save")).toBeNull();
    expect(templateGroup()).toBeInTheDocument();
    for (const label of [EDIT, COPY_LABEL, TEST]) expect(isDisabled(label)).toBe(false);
  });

  it.each([
    ["no permission", {}, []],
    ["view only", { "Platform.ReportDefinitions": true }, [TEST]],
    ["EditTemplate only", { "Platform.ReportDefinitions.EditTemplate": true }, [EDIT]],
    ["Create only", { "Platform.ReportDefinitions.Create": true }, []],
    ["EditTemplate + Create", { "Platform.ReportDefinitions.EditTemplate": true, "Platform.ReportDefinitions.Create": true }, [EDIT, COPY_LABEL]],
    ["view + Create", { "Platform.ReportDefinitions": true, "Platform.ReportDefinitions.Create": true }, [TEST]],
    ["all", ALL_GRANTS, [EDIT, COPY_LABEL, TEST]],
  ])("permissions %s → %j (hidden, never disabled, without a permission)", async (_name, granted, expected) => {
    grants = granted as Record<string, boolean>;
    renderEditor({ readOnly: true });
    await loaded();
    expect([EDIT, COPY_LABEL, TEST].filter((label) => command(label) != null)).toEqual(expected);
    if (expected.length === 0) expect(templateGroup()).toBeNull();
  });
});

/* ── Disabled reasons ─────────────────────────────────────────────────────────────────────────── */

describe("Template commands — disabled with the reason as tooltip", () => {
  it.each([
    ["feature off", { featureEnabled: false }, false, ["Platform:RenderingFeatureOff", "Platform:RenderingFeatureOff", "Platform:RenderingFeatureOff"]],
    ["provider not available", { providerAvailable: false }, false, Array(3).fill("Platform:RenderingUnavailableHint")],
    ["not configured", { providerConfigured: false }, false, Array(3).fill("OpenTms.Platform:RenderingNotConfigured")],
    ["archived", {}, true, Array(3).fill("OpenTms.Platform:ReportDefinitionArchived")],
    ["editor not supported", { supportsTemplateEditor: false }, false, ["Platform:TemplateEditorNotSupported", null, null]],
    ["copy not supported", { supportsTemplateCopy: false }, false, [null, "Platform:TemplateCopyNotSupported", null]],
    ["template not found", { templateFound: false, templateName: null, templateOwnedByWorkspace: null }, false, Array(3).fill("Platform:TemplateNotFoundHint")],
    ["FiLogic standard template (not owned)", { templateOwnedByWorkspace: false }, false, ["OpenTms.Platform:TemplateNotOwned", null, null]],
  ])("%s", async (_name, patch, archived, reasons) => {
    report = { ...REPORT, isArchived: archived };
    status = async () => ok({ ...CLEAR, ...patch });
    renderEditor();
    await loaded();
    const labels = [EDIT, COPY_LABEL, TEST];
    await waitFor(() => expect(command(labels[reasons.findIndex((r) => r != null)])).toHaveAttribute("aria-disabled", "true"));
    labels.forEach((label, i) => {
      if (reasons[i] == null) {
        expect(isDisabled(label)).toBe(false);
        expect(command(label)).toHaveAttribute("title", label);
      } else {
        expect(isDisabled(label)).toBe(true);
        expect(command(label)).toHaveAttribute("title", reasons[i]);
      }
    });
  });

  it("a provider that failed to answer: unavailable, with the server's message appended", async () => {
    status = async () => ok({ ...CLEAR, templateFound: null, templateName: null, templateOwnedByWorkspace: null, message: "PDF Generator API timed out." });
    renderEditor();
    await loaded();
    await waitFor(() => expect(isDisabled(EDIT)).toBe(true));
    for (const label of [EDIT, COPY_LABEL, TEST]) {
      expect(isDisabled(label)).toBe(true);
      expect(command(label)).toHaveAttribute("title", "Platform:RenderingUnavailableHint PDF Generator API timed out.");
    }
  });

  it("while the status loads: disabled, without a reason (the label is the tooltip)", async () => {
    status = () => new Promise(() => {});
    renderEditor();
    await loaded();
    await waitFor(() => expect(GET.mock.calls.some(([p]) => p === STATUS)).toBe(true));
    for (const label of [EDIT, COPY_LABEL, TEST]) {
      expect(isDisabled(label)).toBe(true);
      expect(command(label)).toHaveAttribute("title", label);
    }
  });

  it("when the status failed to load: enabled (the server decides; its answer to the command shows)", async () => {
    status = async () => failure("Status unavailable");
    renderEditor();
    await loaded();
    await waitFor(() => expect(isDisabled(EDIT)).toBe(false));
    for (const label of [EDIT, COPY_LABEL, TEST]) expect(isDisabled(label)).toBe(false);
  });
});

/* ── Hint under Provider template id ──────────────────────────────────────────────────────────── */

describe("Provider template id — the template's name at the provider", () => {
  const hintTexts = () => ["Platform:TemplateName", "Platform:TemplateNotFoundHint"].flatMap((p) => within(panel(GENERAL)).queryAllByText(new RegExp(`^${p}`)).map((e) => e.textContent));

  it("shows Template: <name> for a found template", async () => {
    renderEditor();
    await ready();
    expect(hintTexts()).toEqual(["Platform:TemplateName Factuur NL (standaard)"]);
  });

  it("falls back to the template id when the provider has no name", async () => {
    status = async () => ok({ ...CLEAR, templateName: null });
    renderEditor();
    await ready();
    expect(hintTexts()).toEqual(["Platform:TemplateName tpl-123"]);
  });

  it("shows not found for an unknown template", async () => {
    status = async () => ok({ ...CLEAR, templateFound: false, templateName: null, templateOwnedByWorkspace: null });
    renderEditor();
    await loaded();
    await waitFor(() => expect(hintTexts()).toEqual(["Platform:TemplateNotFoundHint"]));
  });

  it("shows nothing while the template is unknown (not looked up, or the status still loading)", async () => {
    status = async () => ok({ ...CLEAR, featureEnabled: false, templateFound: null, templateName: null, templateOwnedByWorkspace: null });
    renderEditor();
    await loaded();
    await waitFor(() => expect(isDisabled(EDIT)).toBe(true));
    expect(hintTexts()).toEqual([]);
  });

  it("shows nothing while the status loads", async () => {
    status = () => new Promise(() => {});
    renderEditor();
    await loaded();
    expect(hintTexts()).toEqual([]);
  });

  it("hides the name while the typed id differs from the saved one, and shows it again for the saved id", async () => {
    renderEditor();
    await ready();
    typeIn(GENERAL, "Platform:ProviderTemplateId", "tpl-999");
    expect(hintTexts()).toEqual([]);
    typeIn(GENERAL, "Platform:ProviderTemplateId", " tpl-123 ");
    expect(hintTexts()).toEqual(["Platform:TemplateName Factuur NL (standaard)"]);
  });
});

/* ── Unsaved changes ──────────────────────────────────────────────────────────────────────────── */

describe("Template commands — unsaved changes", () => {
  it("typing disables all three with SaveChangesFirst; undoing the change enables them again", async () => {
    renderEditor();
    await ready();
    typeIn(GENERAL, "Platform:Name", "Invoice NL v2");
    for (const label of [EDIT, COPY_LABEL, TEST]) {
      expect(isDisabled(label)).toBe(true);
      expect(command(label)).toHaveAttribute("title", "Platform:SaveChangesFirst");
    }
    typeIn(GENERAL, "Platform:Name", "Invoice NL");
    for (const label of [EDIT, COPY_LABEL, TEST]) expect(isDisabled(label)).toBe(false);
  });

  it("a more fundamental reason wins over unsaved changes", async () => {
    status = async () => ok({ ...CLEAR, templateOwnedByWorkspace: false });
    renderEditor();
    await loaded();
    await waitFor(() => expect(isDisabled(EDIT)).toBe(true));
    typeIn(GENERAL, "Platform:Description", "changed");
    expect(command(EDIT)).toHaveAttribute("title", "OpenTms.Platform:TemplateNotOwned");
    expect(command(TEST)).toHaveAttribute("title", "Platform:SaveChangesFirst");
  });

  it("saving re-enables them, and the rendering status is fetched again for the saved definition", async () => {
    const props = renderEditor();
    await ready();
    const statusCalls = () => GET.mock.calls.filter(([p]) => p === STATUS).length;
    const before = statusCalls();
    typeIn(GENERAL, "Platform:Name", "Invoice NL v2");
    expect(isDisabled(TEST)).toBe(true);
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(isDisabled(TEST)).toBe(false));
    for (const label of [EDIT, COPY_LABEL, TEST]) expect(isDisabled(label)).toBe(false);
    expect(statusCalls()).toBeGreaterThan(before);
    expect(props.onClose).toHaveBeenCalled();
  });
});

/* ── Edit template ────────────────────────────────────────────────────────────────────────────── */

function fakeTab() {
  return { opener: {} as unknown, closed: false, location: { replace: vi.fn() }, close: vi.fn() };
}

describe("Edit template — the provider's editor in a new tab", () => {
  it("opens a blank tab inside the click, cuts its opener, and sends it to the URL after the POST", async () => {
    const tab = fakeTab();
    const open = vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    const answer = deferred<unknown>();
    postFor[EDITOR_URL] = () => answer.promise;
    renderEditor();
    await ready();

    fireEvent.click(command(EDIT)!);
    /* Synchronously, before the server answered: popup blockers allow a tab only inside the click. */
    expect(open).toHaveBeenCalledTimes(1);
    expect(open).toHaveBeenCalledWith("about:blank", "_blank");
    expect(tab.opener).toBeNull();
    /* TanStack Query starts the mutation on a microtask: the POST follows the tab, never precedes it. */
    await waitFor(() => expect(POST).toHaveBeenCalledWith(EDITOR_URL, { params: { path: { id: "rd1" } } }));
    expect(tab.location.replace).not.toHaveBeenCalled();
    await waitFor(() => expect(command("Platform:OpeningTemplate")).not.toBeNull());
    expect(isDisabled("Platform:OpeningTemplate")).toBe(true);
    expect(isDisabled(TEST)).toBe(true);

    await act(async () => answer.resolve(ok({ url: "https://us1.pdfgeneratorapi.com/v4/templates/123/editor?token=abc" })));
    await waitFor(() => expect(tab.location.replace).toHaveBeenCalledWith("https://us1.pdfgeneratorapi.com/v4/templates/123/editor?token=abc"));
    expect(tab.close).not.toHaveBeenCalled();
    expect(open).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(command(EDIT)).not.toBeNull());
    expect(generalAlerts()).toEqual([]);
  });

  it("on a POST failure closes the blank tab and shows the server's message on the tab the user is on", async () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    postFor[EDITOR_URL] = async () => failure("This is a FiLogic standard template: copy it first.");
    renderEditor();
    await ready();
    fireEvent.click(command(EDIT)!);
    await waitFor(() => expect(tab.close).toHaveBeenCalledTimes(1));
    expect(tab.location.replace).not.toHaveBeenCalled();
    await waitFor(() => expect(generalAlerts()).toEqual(["This is a FiLogic standard template: copy it first."]));
  });

  it("a response without a URL closes the tab and shows an error", async () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    postFor[EDITOR_URL] = async () => ok({ url: null });
    renderEditor();
    await ready();
    fireEvent.click(command(EDIT)!);
    await waitFor(() => expect(tab.close).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(generalAlerts()).toEqual(["UnexpectedError"]));
  });

  it("when the browser refused the blank tab, opens the URL with noopener after the POST", async () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    postFor[EDITOR_URL] = async () => ok({ url: "https://provider.example/editor" });
    renderEditor();
    await ready();
    fireEvent.click(command(EDIT)!);
    await waitFor(() => expect(open).toHaveBeenCalledTimes(2));
    expect(open).toHaveBeenNthCalledWith(1, "about:blank", "_blank");
    expect(open).toHaveBeenNthCalledWith(2, "https://provider.example/editor", "_blank", "noopener,noreferrer");
  });

  it("the next command clears an earlier command's error", async () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    postFor[EDITOR_URL] = async () => failure("Provider down");
    renderEditor();
    await ready();
    fireEvent.click(command(EDIT)!);
    await waitFor(() => expect(generalAlerts()).toEqual(["Provider down"]));
    postFor[EDITOR_URL] = async () => ok({ url: "https://provider.example/editor" });
    fireEvent.click(command(EDIT)!);
    await waitFor(() => expect(generalAlerts()).toEqual([]));
  });
});

/* ── Copy template ────────────────────────────────────────────────────────────────────────────── */

const COPY_RESULT = { ...REPORT, id: "rd-copy", name: "Invoice NL (kopie)", providerTemplateId: "tpl-456" };
const confirmation = () => screen.queryByRole("alertdialog");

describe("Copy template — confirmed, then the new definition opens", () => {
  it("asks first, naming the template; Cancel copies nothing", async () => {
    renderEditor();
    await ready();
    fireEvent.click(command(COPY_LABEL)!);
    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).getByText(COPY_LABEL)).toBeInTheDocument();
    expect(within(dialog).getByText("Platform:CopyTemplateQuestion Factuur NL (standaard)")).toBeInTheDocument();
    expect(POST.mock.calls.some(([p]) => p === COPY)).toBe(false);
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(confirmation()).toBeNull());
    expect(POST.mock.calls.some(([p]) => p === COPY)).toBe(false);
  });

  it("POSTs, toasts, refreshes the list, reports the save and opens the copy", async () => {
    postFor[COPY] = async () => ok(COPY_RESULT);
    const props = renderEditor();
    await ready();
    const invalidate = vi.spyOn(client, "invalidateQueries");
    fireEvent.click(command(COPY_LABEL)!);
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Platform:Copy" }));
    await waitFor(() => expect(props.onOpenRecord).toHaveBeenCalledWith("rd-copy"));
    expect(POST).toHaveBeenCalledWith(COPY, { params: { path: { id: "rd1" } } });
    expect(toast.success).toHaveBeenCalledWith("Platform:TemplateCopied Invoice NL (kopie)");
    expect(props.onSaved).toHaveBeenCalledWith(COPY_RESULT);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["masterData", "reportDefinitions"] });
    expect(props.onClose).not.toHaveBeenCalled();
    await waitFor(() => expect(confirmation()).toBeNull());
  });

  it("without onOpenRecord the editor closes after the copy", async () => {
    postFor[COPY] = async () => ok(COPY_RESULT);
    const props = renderEditor({ onOpenRecord: undefined });
    await ready();
    fireEvent.click(command(COPY_LABEL)!);
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Platform:Copy" }));
    await waitFor(() => expect(props.onClose).toHaveBeenCalledTimes(1));
    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it("a failure shows inside the confirmation, which stays open; nothing opens", async () => {
    postFor[COPY] = async () => failure("The provider refused the copy.");
    const props = renderEditor();
    await ready();
    fireEvent.click(command(COPY_LABEL)!);
    const dialog = await screen.findByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Platform:Copy" }));
    await waitFor(() => expect(within(dialog).getByText("The provider refused the copy.")).toBeInTheDocument());
    expect(confirmation()).not.toBeNull();
    expect(props.onOpenRecord).not.toHaveBeenCalled();
    expect(props.onSaved).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
    expect(generalAlerts()).toEqual([]);
  });

  it("shows the busy label in the confirmation while copying", async () => {
    const answer = deferred<unknown>();
    postFor[COPY] = () => answer.promise;
    renderEditor();
    await ready();
    fireEvent.click(command(COPY_LABEL)!);
    const dialog = await screen.findByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Platform:Copy" }));
    await waitFor(() => expect(within(dialog).getByRole("button", { name: "Platform:CopyingTemplate" })).toBeDisabled());
    await act(async () => answer.resolve(ok(COPY_RESULT)));
  });
});

/* ── Test document ────────────────────────────────────────────────────────────────────────────── */

describe("Test document — downloads the test PDF", () => {
  let downloads: string[];
  let createObjectURL: Mock<(blob: Blob) => string>;
  let revokeObjectURL: Mock<(url: string) => void>;

  beforeEach(() => {
    downloads = [];
    createObjectURL = vi.fn(() => "blob:http://localhost/test-doc");
    revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(this.download);
    });
  });

  const pdf = () => new Blob(["%PDF-1.7"], { type: "application/pdf" });
  const blobAnswer = (blob: Blob, headers: Record<string, string>) => ({ data: blob, error: undefined, response: new Response(null, { headers }) });

  it("POSTs for a blob and saves it under the filename* of Content-Disposition; the object URL is revoked", async () => {
    const blob = pdf();
    postFor[TEST_RENDER] = async () =>
      blobAnswer(blob, { "Content-Type": "application/pdf", "Content-Disposition": "attachment; filename=Factuur_12.pdf; filename*=UTF-8''Factuur%20%E2%82%AC%2012.pdf" });
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(downloads).toEqual(["Factuur € 12.pdf"]));
    expect(POST).toHaveBeenCalledWith(TEST_RENDER, { params: { path: { id: "rd1" } }, parseAs: "blob" });
    expect(createObjectURL).toHaveBeenCalledWith(blob);
    await waitFor(() => expect(revokeObjectURL).toHaveBeenCalledWith("blob:http://localhost/test-doc"));
    expect(generalAlerts()).toEqual([]);
  });

  it("uses the plain filename when there is no filename*", async () => {
    postFor[TEST_RENDER] = async () => blobAnswer(pdf(), { "Content-Type": "application/pdf", "Content-Disposition": 'attachment; filename="factuur-TEST.pdf"' });
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(downloads).toEqual(["factuur-TEST.pdf"]));
  });

  it.each([
    ["the response's content type", { "Content-Type": "text/html" }, new Blob(["<p/>"]), "Invoice NL.html"],
    ["the blob's type when the response has none", {}, new Blob(["%PDF"], { type: "application/pdf" }), "Invoice NL.pdf"],
    ["pdf when nothing says otherwise", {}, new Blob(["?"]), "Invoice NL.pdf"],
  ])("without a readable Content-Disposition: the definition's name plus the extension of %s", async (_name, headers, blob, expected) => {
    postFor[TEST_RENDER] = async () => blobAnswer(blob, headers as Record<string, string>);
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(downloads).toEqual([expected]));
  });

  it("shows Generating… and keeps the dialog busy while the document renders", async () => {
    const answer = deferred<unknown>();
    postFor[TEST_RENDER] = () => answer.promise;
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(command("Platform:GeneratingDocument")).not.toBeNull());
    expect(command(TEST)).toBeNull();
    for (const label of ["Platform:GeneratingDocument", EDIT, COPY_LABEL, "Save"]) expect(isDisabled(label)).toBe(true);
    /* A second click while busy starts nothing. */
    fireEvent.click(command("Platform:GeneratingDocument")!);
    expect(POST.mock.calls.filter(([p]) => p === TEST_RENDER)).toHaveLength(1);
    await act(async () => answer.resolve(blobAnswer(pdf(), { "Content-Type": "application/pdf" })));
    await waitFor(() => expect(command(TEST)).not.toBeNull());
    expect(isDisabled(TEST)).toBe(false);
    expect(downloads).toEqual(["Invoice NL.pdf"]);
  });

  it("a failure shows the server's message on the tab the user is on; nothing downloads", async () => {
    postFor[TEST_RENDER] = async () => failure("The template has no sample data.");
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(generalAlerts()).toEqual(["The template has no sample data."]));
    expect(downloads).toEqual([]);
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it("an empty answer is an error, not an empty download", async () => {
    postFor[TEST_RENDER] = async () => ({ data: undefined, error: undefined, response: new Response(null) });
    renderEditor();
    await ready();
    fireEvent.click(command(TEST)!);
    await waitFor(() => expect(generalAlerts()).toEqual(["UnexpectedError"]));
    expect(downloads).toEqual([]);
  });
});
