import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The four lists of the Platform domain group (design 49, UI design and API contract): the view permission, the grid
 * id and the designed columns (hidden ones after the visible, the computed ones not sortable), the filters as list
 * request parameters (`purpose` on templates and report definitions, `includeArchived` on report definitions and
 * document types), the row actions per permission (Edit with Update, Archive/Unarchive with Update and confirmed,
 * Delete with Delete and confirmed, Delete disabled with its reason for a number sequence in use), New only with the
 * Create permission, and the bulk delete carrying the list filters. The domain hooks are real; the generated client
 * is the mock. Risk class: Medium (UI flows); the server enforces the permissions, features and the in-use guard. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { auth: { grantedPolicies: grants }, features: { values: {} }, localization: { languages: [] } } }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

/* The mock grid, recording the column definitions so their value formatters can be checked. */
const { gridProps } = vi.hoisted(() => ({ gridProps: { current: null as { columnDefs: { field?: string; valueFormatter?: unknown }[] } | null } }));
vi.mock("@/components/ui/opentms-grid", async () => {
  const mocks = await import("./run2-test-mocks");
  return {
    OpenTmsGrid: (props: Parameters<typeof mocks.MockOpenTmsGrid>[0]) => {
      gridProps.current = props as unknown as typeof gridProps.current;
      return mocks.MockOpenTmsGrid(props);
    },
  };
});
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});

/* The editors are stand-ins that record the props the list opens them with (hoisted: vi.mock factories run first). */
const { editorProps, editorMock } = vi.hoisted(() => {
  const editorProps: { current: Record<string, unknown> | null } = { current: null };
  const editorMock = (testId: string) => (props: Record<string, unknown>) => {
    editorProps.current = props;
    return <div data-testid={testId} />;
  };
  return { editorProps, editorMock };
});
vi.mock("@/apps/master-data/editors/EmailTemplateEditor", () => ({ EmailTemplateEditor: editorMock("email-template-editor") }));
vi.mock("@/apps/master-data/editors/ReportDefinitionEditor", () => ({ ReportDefinitionEditor: editorMock("report-definition-editor") }));
vi.mock("@/apps/master-data/editors/DocumentTypeEditor", () => ({ DocumentTypeEditor: editorMock("document-type-editor") }));
vi.mock("@/apps/master-data/editors/NumberSequenceEditor", () => ({ NumberSequenceEditor: editorMock("number-sequence-editor") }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";

import { apiClient } from "@/lib/api/client";
import { DocumentTypesView } from "@/apps/master-data/views/DocumentTypesView";
import { EmailTemplatesView } from "@/apps/master-data/views/EmailTemplatesView";
import { NumberSequencesView } from "@/apps/master-data/views/NumberSequencesView";
import { ReportDefinitionsView } from "@/apps/master-data/views/ReportDefinitionsView";
import { gridRows } from "./run2-test-mocks";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const DELETE = apiClient.DELETE as unknown as Mock<ClientCall>;

const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
});

beforeEach(() => {
  grants = {};
  gridRows.current = [];
  editorProps.current = null;
  vi.mocked(toast.success).mockClear();
  GET.mockReset().mockResolvedValue(ok({ rows: [], totalCount: 0, filteredCount: 0 }));
  POST.mockReset().mockImplementation(async (path) => (path.endsWith("bulk-delete") ? ok({ deletedCount: 2, skippedRows: [] }) : ok(undefined)));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

function renderView(view: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{view}</QueryClientProvider>);
}

const listCalls = (path: string) => GET.mock.calls.filter(([p]) => p === path);
const lastListQuery = (path: string) => listCalls(path).at(-1)![1]!.params!.query!;
const columnSpans = () => Array.from(document.querySelectorAll("[data-slot=mock-grid-columns] > span"));
const columns = () => columnSpans().map((el) => `${el.getAttribute("data-column")}${el.getAttribute("data-hidden") === "true" ? " (hidden)" : ""}`);
const unsortable = () => columnSpans().filter((el) => el.getAttribute("data-sortable") === "false").map((el) => el.getAttribute("data-column"));
const rowActions = (index = 0) =>
  Array.from(document.querySelectorAll("[data-slot=mock-grid-row]")[index].querySelectorAll("button")).map((b) => b.textContent);
const rowButton = (index: number, label: string) =>
  Array.from(document.querySelectorAll("[data-slot=mock-grid-row]")[index].querySelectorAll("button")).find((b) => b.textContent === label)!;
const alertDialog = () => screen.getByRole("alertdialog");
/** The text a column shows for a value (its valueFormatter, as the grid calls it). */
const formatted = (field: string, value: unknown) => {
  const col = gridProps.current!.columnDefs.find((c) => c.field === field)!;
  return (col.valueFormatter as (p: { value: unknown }) => string)({ value });
};
/* The mock select adds an empty "—" option when no item has a null value; it is not one of the view's items. */
const optionsOf = (select: HTMLElement) => within(select).getAllByRole("option").map((o) => o.textContent).filter((t) => t !== "—");

const PERMISSION_CASES = [
  ["EmailTemplatesView", () => <EmailTemplatesView />, "Platform.EmailTemplates", "/api/platform/email-template", "EmailTemplate"],
  ["ReportDefinitionsView", () => <ReportDefinitionsView />, "Platform.ReportDefinitions", "/api/platform/report-definition", "ReportDefinition"],
  ["DocumentTypesView", () => <DocumentTypesView />, "Platform.DocumentTypes", "/api/platform/document-type", "DocumentType"],
  ["NumberSequencesView", () => <NumberSequencesView />, "Platform.NumberSequences", "/api/platform/number-sequence", "NumberSequence"],
] as const;

describe("the four Platform lists — permissions", () => {
  it.each(PERMISSION_CASES)("%s needs its view permission and fetches nothing without it", (_name, view, _permission, path) => {
    renderView(view());
    expect(screen.getByText("PermissionDenied")).toBeInTheDocument();
    expect(listCalls(path)).toHaveLength(0);
  });

  it.each(PERMISSION_CASES)("%s: view only gives no row action and no New; Update gives Edit, Delete gives Delete", async (_name, view, permission, path, entityKey) => {
    grants = { [permission]: true };
    gridRows.current = [{ id: "r1", name: "Row one", code: "ROW1", isArchived: false, isInUse: false }];
    const { unmount } = renderView(view());
    await screen.findByTestId("mock-grid");
    await waitFor(() => expect(listCalls(path)).toHaveLength(1));
    expect(rowActions()).toEqual([]);
    expect(screen.queryByText(`Platform:New${entityKey}`)).toBeNull();
    expect(screen.queryByText("MasterData:DeleteSelected")).toBeNull();
    unmount();

    grants = { [permission]: true, [`${permission}.Update`]: true, [`${permission}.Delete`]: true };
    renderView(view());
    await screen.findByTestId("mock-grid");
    expect(rowActions()[0]).toBe("Edit");
    expect(rowActions().at(-1)).toBe("Delete");
    expect(screen.queryByText(`Platform:New${entityKey}`)).toBeNull();
  });

  it.each(PERMISSION_CASES)("%s: New with the Create permission opens the create editor", async (_name, view, permission, _path, entityKey) => {
    grants = { [permission]: true, [`${permission}.Create`]: true };
    renderView(view());
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByRole("button", { name: new RegExp(`Platform:New${entityKey}`) }));
    await waitFor(() => expect(editorProps.current).toEqual(expect.objectContaining({ mode: "create", readOnly: false })));
  });

  it.each(PERMISSION_CASES)("%s: Edit opens the editor for the row", async (_name, view, permission) => {
    grants = { [permission]: true, [`${permission}.Update`]: true };
    gridRows.current = [{ id: "r1", name: "Row one", code: "ROW1", isArchived: false, isInUse: false }];
    renderView(view());
    await screen.findByTestId("mock-grid");
    fireEvent.click(rowButton(0, "Edit"));
    await waitFor(() => expect(editorProps.current).toEqual(expect.objectContaining({ mode: "edit", id: "r1", readOnly: false })));
  });
});

describe("EmailTemplatesView", () => {
  it("renders platform.emailTemplates with Name, Purpose, Default and Languages (not sortable) and sends the Purpose filter", async () => {
    grants = { "Platform.EmailTemplates": true };
    renderView(<EmailTemplatesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("platform.emailTemplates");
    expect(columns()).toEqual(["name", "purpose", "isDefault", "cultures"]);
    expect(unsortable()).toEqual(["cultures"]);
    await waitFor(() => expect(listCalls("/api/platform/email-template")).toHaveLength(1));
    expect(lastListQuery("/api/platform/email-template").purpose).toBeUndefined();

    const purpose = screen.getByRole("combobox", { name: "Platform:Purpose" });
    expect(optionsOf(purpose)).toEqual([
      "Platform:AllPurposes",
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
    fireEvent.change(purpose, { target: { value: "4" } });
    await waitFor(() => expect(lastListQuery("/api/platform/email-template").purpose).toBe(4));
    fireEvent.change(purpose, { target: { value: "all" } });
    await waitFor(() => expect(listCalls("/api/platform/email-template")).toHaveLength(3));
    expect(lastListQuery("/api/platform/email-template").purpose).toBeUndefined();
  });

  it("shows the purpose by name and the languages of the joined culture list (the server joins with \", \") by name", async () => {
    grants = { "Platform.EmailTemplates": true };
    renderView(<EmailTemplatesView />);
    await screen.findByTestId("mock-grid");
    expect(formatted("purpose", 3)).toBe("Platform:EmailTemplatePurpose.Invoice");
    expect(formatted("cultures", "en, nl")).toBe("Platform:Language.en, Platform:Language.nl");
    expect(formatted("cultures", "en,nl")).toBe("Platform:Language.en, Platform:Language.nl");
    expect(formatted("cultures", "")).toBe("");
    expect(formatted("cultures", null)).toBe("");
  });

  it("Delete from the row confirms and calls the delete endpoint; bulk delete sends the selection with the Purpose filter", async () => {
    grants = { "Platform.EmailTemplates": true, "Platform.EmailTemplates.Delete": true };
    gridRows.current = [
      { id: "e1", name: "Invoice mail", purpose: 3, isDefault: true, cultures: "en,nl" },
      { id: "e2", name: "Reminder", purpose: 4, isDefault: false, cultures: "en" },
    ];
    renderView(<EmailTemplatesView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(rowButton(0, "Delete"));
    let dialog = alertDialog();
    expect(within(dialog).getByText("Platform:DeleteEmailTemplate")).toBeInTheDocument();
    expect(within(dialog).getByText(/Platform:DeleteEmailTemplateQuestion Invoice mail/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(DELETE).toHaveBeenCalledWith("/api/platform/email-template/{id}", { params: { path: { id: "e1" } } }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Platform:EmailTemplateDeleted"));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

    fireEvent.change(screen.getByRole("combobox", { name: "Platform:Purpose" }), { target: { value: "3" } });
    fireEvent.click(screen.getByText("select-all"));
    fireEvent.click(await screen.findByRole("button", { name: /DeleteNSelected/ }));
    dialog = alertDialog();
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/platform/email-template/bulk-delete", {
        body: { selection: { mode: "Explicit", explicitIds: ["e1", "e2"], excludedIds: [], filterRequest: undefined }, purpose: 3 },
      }),
    );
  });
});

describe("ReportDefinitionsView", () => {
  it("renders platform.reportDefinitions with the designed columns and sends the Purpose and Show archived filters", async () => {
    grants = { "Platform.ReportDefinitions": true };
    renderView(<ReportDefinitionsView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("platform.reportDefinitions");
    expect(columns()).toEqual(["name", "purpose", "culture", "provider", "isDefault", "isArchived (hidden)"]);
    await waitFor(() => expect(listCalls("/api/platform/report-definition")).toHaveLength(1));
    expect(lastListQuery("/api/platform/report-definition").includeArchived).toBe(false);
    expect(lastListQuery("/api/platform/report-definition").purpose).toBeUndefined();

    const purpose = screen.getByRole("combobox", { name: "Platform:Purpose" });
    expect(optionsOf(purpose)).toEqual([
      "Platform:AllPurposes",
      "Platform:ReportPurpose.TransportOrderConfirmation",
      "Platform:ReportPurpose.TransportFile",
      "Platform:ReportPurpose.Invoice",
      "Platform:ReportPurpose.PurchaseOrder",
      "Platform:ReportPurpose.TransportUnitBalance",
    ]);
    fireEvent.change(purpose, { target: { value: "3" } });
    await waitFor(() => expect(lastListQuery("/api/platform/report-definition").purpose).toBe(3));

    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/platform/report-definition").includeArchived).toBe(true));
    expect(lastListQuery("/api/platform/report-definition").purpose).toBe(3);
  });

  it("shows purpose, language and provider by name", async () => {
    grants = { "Platform.ReportDefinitions": true };
    renderView(<ReportDefinitionsView />);
    await screen.findByTestId("mock-grid");
    expect(formatted("purpose", 5)).toBe("Platform:ReportPurpose.TransportUnitBalance");
    expect(formatted("culture", "nl")).toBe("Platform:Language.nl");
    expect(formatted("culture", null)).toBe("");
    expect(formatted("provider", 1)).toBe("Platform:ReportRenderingProvider.PdfGeneratorApi");
  });

  it("Archive is a confirmed row action with Update and posts to the archive endpoint", async () => {
    grants = { "Platform.ReportDefinitions": true, "Platform.ReportDefinitions.Update": true };
    gridRows.current = [{ id: "rd1", name: "Invoice NL", isArchived: false }];
    renderView(<ReportDefinitionsView />);
    await screen.findByTestId("mock-grid");
    expect(rowActions()).toEqual(["Edit", "Platform:Archive"]);
    fireEvent.click(rowButton(0, "Platform:Archive"));
    const confirm = alertDialog();
    expect(within(confirm).getByText("Platform:ArchiveReportDefinition")).toBeInTheDocument();
    expect(within(confirm).getByText(/Invoice NL/)).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole("button", { name: "Platform:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/platform/report-definition/{id}/archive", { params: { path: { id: "rd1" } } }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Platform:ReportDefinitionArchived"));
  });

  it("an archived row offers Unarchive and posts to the unarchive endpoint", async () => {
    grants = { "Platform.ReportDefinitions": true, "Platform.ReportDefinitions.Update": true };
    gridRows.current = [{ id: "rd1", name: "Invoice NL", isArchived: true }];
    renderView(<ReportDefinitionsView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(rowButton(0, "Platform:Unarchive"));
    expect(within(alertDialog()).getByText("Platform:UnarchiveReportDefinition")).toBeInTheDocument();
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "Platform:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/platform/report-definition/{id}/unarchive", { params: { path: { id: "rd1" } } }));
  });

  it("offers no Archive without Update", async () => {
    grants = { "Platform.ReportDefinitions": true, "Platform.ReportDefinitions.Delete": true };
    gridRows.current = [{ id: "rd1", name: "Invoice NL", isArchived: false }];
    renderView(<ReportDefinitionsView />);
    await screen.findByTestId("mock-grid");
    expect(rowActions()).toEqual(["Delete"]);
  });
});

describe("DocumentTypesView", () => {
  it("renders platform.documentTypes with the designed columns and sends the Show archived filter", async () => {
    grants = { "Platform.DocumentTypes": true };
    renderView(<DocumentTypesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("platform.documentTypes");
    expect(columns()).toEqual(["code", "name", "isFreightDocument", "includeWithInvoice", "isArchived (hidden)"]);
    await waitFor(() => expect(listCalls("/api/platform/document-type")).toHaveLength(1));
    expect(lastListQuery("/api/platform/document-type").includeArchived).toBe(false);
    expect(screen.queryByRole("combobox", { name: "Platform:Purpose" })).toBeNull();
    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(lastListQuery("/api/platform/document-type").includeArchived).toBe(true));
  });

  it("Archive and Unarchive are confirmed row actions with Update; the confirmation names the code", async () => {
    grants = { "Platform.DocumentTypes": true, "Platform.DocumentTypes.Update": true };
    gridRows.current = [
      { id: "d1", code: "FREIGHT", name: "Freight document", isArchived: false },
      { id: "d2", code: "OLD", name: "Old", isArchived: true },
    ];
    renderView(<DocumentTypesView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(rowButton(0, "Platform:Archive"));
    expect(within(alertDialog()).getByText(/Platform:ArchiveDocumentTypeQuestion FREIGHT/)).toBeInTheDocument();
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "Platform:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/platform/document-type/{id}/archive", { params: { path: { id: "d1" } } }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

    fireEvent.click(rowButton(1, "Platform:Unarchive"));
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "Platform:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/platform/document-type/{id}/unarchive", { params: { path: { id: "d2" } } }));
  });

  it("the Delete confirmation names the code", async () => {
    grants = { "Platform.DocumentTypes": true, "Platform.DocumentTypes.Delete": true };
    gridRows.current = [{ id: "d1", code: "FREIGHT", name: "Freight document", isArchived: false }];
    renderView(<DocumentTypesView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(rowButton(0, "Delete"));
    expect(within(alertDialog()).getByText(/Platform:DeleteDocumentTypeQuestion FREIGHT/)).toBeInTheDocument();
  });
});

describe("NumberSequencesView", () => {
  it("renders platform.numberSequences with Name, Scheme, Example number and In use, the computed two not sortable; no filters", async () => {
    grants = { "Platform.NumberSequences": true };
    renderView(<NumberSequencesView />);
    expect((await screen.findByTestId("mock-grid")).getAttribute("data-grid-id")).toBe("platform.numberSequences");
    expect(columns()).toEqual(["name", "scheme", "exampleNumber", "isInUse"]);
    expect(unsortable()).toEqual(["exampleNumber", "isInUse"]);
    expect(formatted("scheme", 4)).toBe("Platform:NumberSequenceScheme.TwoDigitYearMonth");
    await waitFor(() => expect(listCalls("/api/platform/number-sequence")).toHaveLength(1));
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
  });

  it("disables Delete with the in-use reason for a sequence in use, and deletes one that is not after confirmation", async () => {
    grants = { "Platform.NumberSequences": true, "Platform.NumberSequences.Delete": true };
    gridRows.current = [
      { id: "n1", name: "Invoices", scheme: 2, exampleNumber: "F26-0001", isInUse: true },
      { id: "n2", name: "Spare", scheme: 5, exampleNumber: "0001", isInUse: false },
    ];
    renderView(<NumberSequencesView />);
    await screen.findByTestId("mock-grid");

    const blocked = rowButton(0, "Delete");
    expect(blocked).toBeDisabled();
    expect(blocked).toHaveAttribute("title", "Platform:NumberSequenceInUseCannotDelete");
    fireEvent.click(blocked);
    expect(screen.queryByRole("alertdialog")).toBeNull();

    const free = rowButton(1, "Delete");
    expect(free).toBeEnabled();
    fireEvent.click(free);
    expect(within(alertDialog()).getByText(/Platform:DeleteNumberSequenceQuestion Spare/)).toBeInTheDocument();
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(DELETE).toHaveBeenCalledWith("/api/platform/number-sequence/{id}", { params: { path: { id: "n2" } } }));
    expect(DELETE).toHaveBeenCalledTimes(1);
  });

  it("a bulk delete goes to the server, which skips sequences in use", async () => {
    grants = { "Platform.NumberSequences": true, "Platform.NumberSequences.Delete": true };
    gridRows.current = [
      { id: "n1", name: "Invoices", isInUse: true },
      { id: "n2", name: "Spare", isInUse: false },
    ];
    POST.mockImplementation(async () => ok({ deletedCount: 1, skippedRows: [{ id: "n1", name: "Invoices", reason: "In use" }] }));
    renderView(<NumberSequencesView />);
    await screen.findByTestId("mock-grid");
    fireEvent.click(screen.getByText("select-all"));
    fireEvent.click(await screen.findByRole("button", { name: /DeleteNSelected/ }));
    fireEvent.click(within(alertDialog()).getByRole("button", { name: "Delete" }));
    await waitFor(() =>
      expect(POST).toHaveBeenCalledWith("/api/platform/number-sequence/bulk-delete", {
        body: { selection: { mode: "Explicit", explicitIds: ["n1", "n2"], excludedIds: [], filterRequest: undefined } },
      }),
    );
    await waitFor(() => expect(toast.warning).toHaveBeenCalledWith(expect.stringContaining("Invoices: In use")));
  });
});
