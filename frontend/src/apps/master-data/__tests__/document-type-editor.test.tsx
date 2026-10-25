import { fireEvent, render, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The document type editor and the number sequence editor (design 54, UI design). Document type: the code is
 * upper-cased, the Name is the user's language's text of the LocalizedName; transformations in a LinesGrid: at most
 * four, each kind once, an argument required for RenameByParentProperty and SplitPdfByReferencePattern, positions
 * renumbered from the row order on save. Number sequence: the affix fields of the chosen scheme only, minimum digits
 * 1–12, the live example (the mirrored formatter, today's period, counter 1), and once in use the numbering fields
 * read-only with the note, Delete disabled with its reason, and the loaded numbering values sent back unchanged.
 * The domain hooks are real; the generated client, the Select and the LinesGrid are mocks. Risk class: Medium (UI
 * flow); the in-use guard and the transformation rules are enforced and tested on the server. */

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
vi.mock("@/components/ui/lines-grid-lazy", async () => {
  const mocks = await import("./run2-test-mocks");
  return { LazyLinesGrid: mocks.MockLinesGrid };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { DocumentTypeEditor } from "@/apps/master-data/editors/DocumentTypeEditor";
import {
  checkbox,
  enabledControls,
  expectNoSave,
  fieldIn,
  ok,
  panel,
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

const stubFrames = () => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
};

beforeAll(stubFrames);

const DOCUMENT_TYPE = {
  id: "dt1",
  code: "FREIGHT",
  name: "Freight document",
  names: { en: "Freight document", nl: "Vrachtdocument" },
  isFreightDocument: true,
  includeWithInvoice: true,
  showInListIndicator: true,
  isArchived: false,
  description: null,
  /* Out of order and with gaps on purpose: the editor shows them by position and renumbers them. */
  transformations: [
    { position: 7, kind: 4, argument: "^REF(\\d+)$" },
    { position: 2, kind: 1, argument: null },
  ],
  concurrencyStamp: "stamp-dt1",
  creationTime: "2026-10-16T10:00:00Z",
  lastModificationTime: null,
};

const SEQUENCE = {
  id: "ns1",
  name: "Invoices",
  scheme: 5,
  prefix: "INV",
  /* Stored but not used by CounterOnly: it freezes too once the sequence is in use (design 54 build notes). */
  yearPrefix: "Y",
  yearSuffix: null,
  monthPrefix: null,
  monthSuffix: null,
  minimumDigits: 6,
  description: "Sales invoices",
  isInUse: false,
  exampleNumber: "INV000001",
  concurrencyStamp: "stamp-ns1",
  creationTime: "2026-10-16T10:00:00Z",
  lastModificationTime: null,
};

let documentType: Record<string, unknown> = DOCUMENT_TYPE;
let sequence: Record<string, unknown> = SEQUENCE;

beforeEach(() => {
  culture = "en";
  grants = {};
  documentType = DOCUMENT_TYPE;
  sequence = SEQUENCE;
  /* The example is drawn for today: pin today to 15 March 2026 (only Date is faked; timers stay real for waitFor). */
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 2, 15, 12, 0, 0));
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/platform/document-type/{id}":
        return ok(documentType);
      case "/api/platform/number-sequence/{id}":
        return ok(sequence);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => ok(path.includes("document") ? { ...DOCUMENT_TYPE, id: "dt-new" } : { ...SEQUENCE, id: "ns-new" }));
  PUT.mockReset().mockImplementation(async (path) => ok(path.includes("document") ? DOCUMENT_TYPE : SEQUENCE));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  stubFrames();
});

function renderWith(element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}>{element}</QueryClientProvider>);
}

const GENERAL = "Platform:Tab.General";
const TRANSFORMATIONS = "Platform:Tab.Transformations";

const postedBody = (path: string) => POST.mock.calls.find(([p]) => p === path)![1]!.body as Record<string, unknown>;
const putCall = () => PUT.mock.calls[0] as [string, { params: { path: { id: string } }; body: Record<string, unknown> }];

/* ── Document type ─────────────────────────────────────────────────────────────────────────────── */

const renderDocumentType = (props: Partial<React.ComponentProps<typeof DocumentTypeEditor>> = {}) =>
  renderWith(<DocumentTypeEditor mode="create" readOnly={false} open onClose={() => {}} onSaved={() => {}} {...props} />);

const loadedDocumentType = () => waitFor(() => expect(fieldIn(GENERAL, "Platform:Code")).toHaveValue("FREIGHT"));

const lines = () => Array.from(panel(TRANSFORMATIONS).querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const kinds = () => lines().map((line) => Number((within(line).getByLabelText("Platform:Kind") as HTMLSelectElement).value));
const addButton = () => within(panel(TRANSFORMATIONS)).queryByRole("button", { name: /Platform:AddTransformation/ });
const setArgument = (line: HTMLElement, value: string) => fireEvent.change(within(line).getByLabelText("Platform:Argument"), { target: { value } });

describe("DocumentTypeEditor — general", () => {
  it("upper-cases the code while typing and POSTs code, the name in the user's language, the switches and no transformations", async () => {
    renderDocumentType();
    typeIn(GENERAL, "Platform:Code", "cmr-scan");
    expect(fieldIn(GENERAL, "Platform:Code")).toHaveValue("CMR-SCAN");
    typeIn(GENERAL, "Platform:Name", " Scanned CMR ");
    fireEvent.click(checkbox("Platform:IsFreightDocument"));
    fireEvent.click(checkbox("Platform:ShowInListIndicator"));
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/platform/document-type");
    expect(postedBody("/api/platform/document-type")).toEqual({
      code: "CMR-SCAN",
      names: { en: "Scanned CMR" },
      isFreightDocument: true,
      includeWithInvoice: false,
      showInListIndicator: true,
      isArchived: false,
      description: null,
      transformations: [],
    });
  });

  it("refuses an empty code and name", async () => {
    renderDocumentType();
    typeIn(GENERAL, "Platform:Code", "  ");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(GENERAL)).getAllByText("Platform:Required")).toHaveLength(2);
  });

  it("a Dutch user edits the Dutch name and keeps the English one", async () => {
    culture = "nl";
    renderDocumentType({ mode: "edit", id: "dt1" });
    await loadedDocumentType();
    expect(fieldIn(GENERAL, "Platform:Name")).toHaveValue("Vrachtdocument");
    typeIn(GENERAL, "Platform:Name", "Vrachtbrief");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.names).toEqual({ en: "Freight document", nl: "Vrachtbrief" });
    expect(putCall()[1].body.concurrencyStamp).toBe("stamp-dt1");
  });
});

describe("DocumentTypeEditor — transformations", () => {
  it("the grid has position, kind (the four kinds, a fixed list) and argument", () => {
    renderDocumentType();
    tab(TRANSFORMATIONS);
    const columns = Array.from(panel(TRANSFORMATIONS).querySelectorAll("[data-slot=mock-lines-grid-columns] > span"));
    expect(columns.map((c) => c.getAttribute("data-column"))).toEqual(["position", "kind", "argument"]);
    expect(columns[1].getAttribute("data-cell-editor")).toBe("agRichSelectCellEditor");
    fireEvent.click(addButton()!);
    const options = Array.from(within(lines()[0]).getByLabelText("Platform:Kind").querySelectorAll("option")).map((o) => o.textContent);
    expect(options).toEqual([
      "Platform:DocumentTransformationKind.ImageToPdf",
      "Platform:DocumentTransformationKind.LinkByFileName",
      "Platform:DocumentTransformationKind.RenameByParentProperty",
      "Platform:DocumentTransformationKind.SplitPdfByReferencePattern",
    ]);
  });

  it("adds up to four rows, each with the next unused kind, then offers no more", () => {
    renderDocumentType();
    tab(TRANSFORMATIONS);
    for (let i = 0; i < 4; i++) fireEvent.click(addButton()!);
    expect(kinds()).toEqual([1, 2, 3, 4]);
    expect(addButton()).toBeNull();
    /* Removing one frees its kind for the next row. */
    fireEvent.click(within(lines()[1]).getByText("Remove"));
    expect(kinds()).toEqual([1, 3, 4]);
    fireEvent.click(addButton()!);
    expect(kinds()).toEqual([1, 3, 4, 2]);
  });

  it("requires the argument for RenameByParentProperty and SplitPdfByReferencePattern, not for the others", async () => {
    renderDocumentType();
    typeIn(GENERAL, "Platform:Code", "POD");
    typeIn(GENERAL, "Platform:Name", "Proof of delivery");
    tab(TRANSFORMATIONS);
    for (let i = 0; i < 4; i++) fireEvent.click(addButton()!);

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(TRANSFORMATIONS)).getByText("Platform:TransformationArgumentMissing")).toBeInTheDocument();

    setArgument(lines()[2], "  OrderNumber ");
    setArgument(lines()[3], "   ");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();

    setArgument(lines()[3], "^REF(\\d+)$");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody("/api/platform/document-type").transformations).toEqual([
      { position: 1, kind: 1, argument: null },
      { position: 2, kind: 2, argument: null },
      { position: 3, kind: 3, argument: "OrderNumber" },
      { position: 4, kind: 4, argument: "^REF(\\d+)$" },
    ]);
  });

  it("refuses the same kind twice", async () => {
    renderDocumentType();
    typeIn(GENERAL, "Platform:Code", "POD");
    typeIn(GENERAL, "Platform:Name", "Proof of delivery");
    tab(TRANSFORMATIONS);
    fireEvent.click(addButton()!);
    fireEvent.click(addButton()!);
    fireEvent.change(within(lines()[1]).getByLabelText("Platform:Kind"), { target: { value: "1" } });
    expect(kinds()).toEqual([1, 1]);
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(within(panel(TRANSFORMATIONS)).getByText("Platform:TransformationKindTwice")).toBeInTheDocument();
  });

  it("shows stored rows by position, numbered 1..n, and renumbers them on save after a removal", async () => {
    renderDocumentType({ mode: "edit", id: "dt1" });
    await loadedDocumentType();
    expect(kinds()).toEqual([1, 4]);
    expect(lines().map((line) => (within(line).getByLabelText("Platform:Position") as HTMLInputElement).value)).toEqual(["1", "2"]);

    tab(TRANSFORMATIONS);
    fireEvent.click(within(lines()[0]).getByText("Remove"));
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.transformations).toEqual([{ position: 1, kind: 4, argument: "^REF(\\d+)$" }]);
  });

  it("orders the rows by the position the user gives them and sends 1..n", async () => {
    renderDocumentType({ mode: "edit", id: "dt1" });
    await loadedDocumentType();
    tab(TRANSFORMATIONS);
    /* Move ImageToPdf behind the split. */
    fireEvent.change(within(lines()[0]).getByLabelText("Platform:Position"), { target: { value: "9" } });
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putCall()[1].body.transformations).toEqual([
      { position: 1, kind: 4, argument: "^REF(\\d+)$" },
      { position: 2, kind: 1, argument: null },
    ]);
  });

  it("read-only: no add, no remove, no editable cell, no Save", async () => {
    renderDocumentType({ mode: "edit", id: "dt1", readOnly: true });
    await loadedDocumentType();
    expect(addButton()).toBeNull();
    expect(within(panel(TRANSFORMATIONS)).queryByText("Remove")).toBeNull();
    expect(enabledControls()).toEqual([]);
    expectNoSave();
  });
});
