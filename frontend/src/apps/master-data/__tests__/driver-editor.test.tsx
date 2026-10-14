import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The driver editor (design 43, UI design): client validation, the employer EntityLookup over the
 * relation lookup page narrowed to Carrier | OwnCompany with "Add new relation…" opening the
 * relation editor on top and selecting the saved record, the user-account EntityLookup, the
 * qualification lines with the date cell editor posting ISO dates, the confirmed Anonymise command
 * behind Drivers.Anonymise, and the read-only anonymised record. The domain hooks and the
 * EntityLookup are real; the generated client is the mock. Risk class: Medium (UI flow); the
 * anonymise path is the UI side of a Critical personal-data operation whose effect the server
 * tests prove. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: grants },
      features: { values: { "Apps.MasterData.Fleet.Drivers": "true" } },
      localization: { languages: [{ cultureName: "nl", displayName: "Nederlands" }] },
    },
  }),
}));

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

vi.mock("@/components/ui/lines-grid-lazy", async () => {
  const mocks = await import("./run2-test-mocks");
  return { LazyLinesGrid: mocks.MockLinesGrid };
});
vi.mock("@/components/ui/select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { Select: mocks.MockSelect, SelectTrigger: mocks.MockSelectTrigger, SelectValue: mocks.Nothing, SelectContent: mocks.Nothing, SelectItem: mocks.Nothing };
});
vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});
vi.mock("@/components/ui/multi-select", async () => {
  const mocks = await import("./run2-test-mocks");
  return { MultiSelect: mocks.MockMultiSelect };
});

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { DriverEditor } from "@/apps/master-data/editors/DriverEditor";
import type { DriverDto } from "@/domains/master-data/entities";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;

const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });
const failed = (error: unknown, status = 400) => ({ data: undefined, error, response: { status } });

class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}

class ObserverStub {
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

const stored: DriverDto = {
  id: "d1",
  code: "DRV1",
  firstName: "Jan",
  middleName: null,
  lastName: "Jansen",
  fullName: "Jan Jansen",
  initials: "J.",
  phone: null,
  mobile: "+31 6 12345678",
  email: "jan@acme.nl",
  preferredCulture: "nl",
  employerRelationId: "rel-1",
  employerName: "Acme Transport",
  userId: "u1",
  userName: "jan",
  isArchived: false,
  isAnonymised: false,
  description: null,
  qualifications: [{ id: "q1", kind: 4, number: "C95-1", validFrom: "2024-01-01", validUntil: "2029-01-01", description: null }],
  concurrencyStamp: "stamp-d1",
  creationTime: "2026-10-11T10:00:00Z",
  lastModificationTime: null,
};

let detail: DriverDto = stored;

beforeEach(() => {
  vi.stubGlobal("IntersectionObserver", ObserverStub);
  grants = {};
  detail = stored;
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/master-data/driver/{id}":
        return ok(detail);
      case "/api/master-data/relation/lookup-page":
        return ok({ items: [{ id: "rel-1", code: "ACME", name: "Acme Transport", city: "Delft", countryCode: "NL", roles: 2 }], totalCount: 1 });
      case "/api/master-data/driver/user-lookup-page":
        return ok({ items: [{ id: "u1", name: "Jan Jansen", userName: "jan", email: "jan@acme.nl" }], totalCount: 1 });
      case "/api/platform/administration/organization-units/all":
        return ok([]);
      default:
        return ok([]);
    }
  });
  POST.mockReset().mockImplementation(async (path) => {
    if (path === "/api/master-data/relation") return ok({ id: "rel-new", code: "ZEE", name: "Zeeland Transport", roles: 2, concurrencyStamp: "s" });
    if (path === "/api/master-data/driver") return ok({ ...stored, id: "d-new" });
    return ok(undefined);
  });
  PUT.mockReset().mockResolvedValue(ok(stored));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

function renderEditor(props: Partial<React.ComponentProps<typeof DriverEditor>> = {}) {
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { unmount } = render(
    <QueryClientProvider client={client}>
      <DriverEditor mode="create" readOnly={false} open onClose={onClose} onSaved={onSaved} {...props} />
    </QueryClientProvider>,
  );
  return { onClose, onSaved, unmount };
}

const dialogs = () => Array.from(document.querySelectorAll<HTMLElement>("[role=dialog]"));
const editorDialog = () => dialogs()[0];
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
/* Form controls only: the nested relation editor's "MasterData:Name" FormSection carries its title as aria-label too. */
const field = (scope: HTMLElement, label: string) => within(scope).getByLabelText(new RegExp(`^${escape(label)}`), { selector: "input, select, textarea" }) as HTMLInputElement;
const typeInto = (scope: HTMLElement, label: string, value: string) => fireEvent.change(field(scope, label), { target: { value } });
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const ribbon = (name: string) => within(editorDialog()).getByRole("button", { name });
const openPopup = (element: HTMLElement) => {
  fireEvent.pointerDown(element);
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};
const typeTerm = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });

const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

const fillRequired = () => {
  typeInto(editorDialog(), "MasterData:Code", "drv9");
  typeInto(editorDialog(), "MasterData:FirstName", "Piet");
  typeInto(editorDialog(), "MasterData:LastName", "Pietersen");
};

const emptyBody = {
  code: "DRV9",
  firstName: "Piet",
  middleName: null,
  lastName: "Pietersen",
  initials: null,
  phone: null,
  mobile: null,
  email: null,
  preferredCulture: null,
  employerRelationId: null,
  userId: null,
  isArchived: false,
  description: null,
  qualifications: [],
};

const postedDriver = () => POST.mock.calls.find(([path]) => path === "/api/master-data/driver")![1]!.body as typeof emptyBody;
const lookupCalls = (path: string) => GET.mock.calls.filter(([p]) => p === path);

describe("DriverEditor — validation", () => {
  it("refuses a new driver without code, first and last name and sends nothing", async () => {
    renderEditor();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:CodeRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:FirstNameRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:LastNameRequired")).toBeInTheDocument();
  });

  it("validates the e-mail shape", async () => {
    renderEditor();
    fillRequired();
    const email = field(editorDialog(), "MasterData:Email");
    fireEvent.change(email, { target: { value: "jan@" } });
    fireEvent.blur(email);
    expect(screen.getByText("MasterData:InvalidEmail")).toBeInTheDocument();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
  });
});

describe("DriverEditor — create", () => {
  it("posts the person, contact and employment fields as the DTO (code upper-cased, blanks as null)", async () => {
    const { onSaved, onClose } = renderEditor();
    fillRequired();
    const editor = editorDialog();
    typeInto(editor, "MasterData:MiddleName", " van ");
    typeInto(editor, "MasterData:Initials", "P.");
    typeInto(editor, "MasterData:Mobile", "+31 6 1");
    typeInto(editor, "MasterData:Email", "piet@acme.nl");
    typeInto(editor, "MasterData:Description", "Night shifts");
    fireEvent.change(field(editor, "MasterData:PreferredCulture"), { target: { value: "nl" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "MasterData:IsArchived" }));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver", expect.anything()));
    expect(postedDriver()).toEqual({ ...emptyBody, middleName: "van", initials: "P.", mobile: "+31 6 1", email: "piet@acme.nl", preferredCulture: "nl", isArchived: true, description: "Night shifts" });
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ id: "d-new" })));
    expect(onClose).toHaveBeenCalled();
  });

  it("the employer lookup fetches relations with roles=18 (Carrier | OwnCompany) and the pick travels as employerRelationId", async () => {
    renderEditor();
    fillRequired();
    const employer = field(editorDialog(), "MasterData:Employer");
    expect(lookupCalls("/api/master-data/relation/lookup-page")).toHaveLength(0);
    openPopup(employer);
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/relation/lookup-page", { params: { query: { term: undefined, skip: 0, take: 25, roles: 18 } } }));
    const option = await screen.findByText("Acme Transport");
    expect(option.closest("[role=option]")).toHaveTextContent("ACME, Delft NL");
    fireEvent.click(option.closest("[role=option]")!);
    expect(employer).toHaveValue("Acme Transport");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver", expect.anything()));
    expect(postedDriver().employerRelationId).toBe("rel-1");
  });

  it("Add new relation… opens the relation editor with the typed name on top and selects the saved relation as employer", async () => {
    grants = { "MasterData.Relations.Create": true };
    renderEditor();
    fillRequired();
    const employer = field(editorDialog(), "MasterData:Employer");
    typeTerm(employer, "Zeeland Transport");
    fireEvent.click(await screen.findByText("MasterData:AddNewRelation"));

    await waitFor(() => expect(dialogs()).toHaveLength(2));
    const relationDialog = dialogs()[1];
    expect(within(relationDialog).getByText("MasterData:NewRelation")).toBeInTheDocument();
    expect(field(relationDialog, "MasterData:Name").value).toBe("Zeeland Transport");
    typeInto(relationDialog, "MasterData:Code", "zee");
    fireEvent.click(within(relationDialog).getByRole("checkbox", { name: "MasterData:Role.Carrier" }));
    await act(async () => {
      fireEvent.submit(relationDialog.querySelector("form")!);
      await Promise.resolve();
    });
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/relation", expect.objectContaining({ body: expect.objectContaining({ code: "ZEE", name: "Zeeland Transport", roles: 2 }) })));
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    await waitFor(() => expect(field(editorDialog(), "MasterData:Employer")).toHaveValue("Zeeland Transport"));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver", expect.anything()));
    expect(postedDriver().employerRelationId).toBe("rel-new");
  });

  it("hides Add new relation… without Relations.Create", async () => {
    renderEditor();
    const employer = field(editorDialog(), "MasterData:Employer");
    openPopup(employer);
    await screen.findByText("Acme Transport");
    expect(screen.queryByText("MasterData:AddNewRelation")).toBeNull();
  });

  it("the user-account lookup fetches the tenant's users and the pick travels as userId", async () => {
    renderEditor();
    fillRequired();
    const user = field(editorDialog(), "MasterData:UserAccount");
    openPopup(user);
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/driver/user-lookup-page", { params: { query: { term: undefined, skip: 0, take: 25 } } }));
    const option = await screen.findByText("jan, jan@acme.nl");
    fireEvent.click(option.closest("[role=option]")!);
    expect(user).toHaveValue("Jan Jansen");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver", expect.anything()));
    expect(postedDriver().userId).toBe("u1");
  });

  it("stages a qualification line with kind, number and ISO dates through the date cell editor and posts it", async () => {
    renderEditor();
    fillRequired();
    tab("MasterData:Qualifications");
    const columns = Array.from(document.querySelectorAll("[data-slot=mock-lines-grid-columns] > span")).map((el) => ({
      column: el.getAttribute("data-column"),
      editor: el.getAttribute("data-cell-editor"),
      popup: el.getAttribute("data-popup"),
    }));
    expect(columns.map((c) => c.column)).toEqual(["kind", "number", "validFrom", "validUntil", "description"]);
    expect(columns[2]).toEqual({ column: "validFrom", editor: "DatePickerCellEditor", popup: "true" });
    expect(columns[3]).toEqual({ column: "validUntil", editor: "DatePickerCellEditor", popup: "true" });

    fireEvent.click(screen.getByText("MasterData:AddQualification"));
    const line = lines()[0];
    const kind = within(line).getByLabelText("MasterData:Kind") as HTMLSelectElement;
    expect(within(kind).getByText("MasterData:QualificationKind.DrivingLicenceCE")).toBeInTheDocument();
    fireEvent.change(kind, { target: { value: "3" } });
    fireEvent.change(within(line).getByLabelText("MasterData:Number"), { target: { value: " CE-77 " } });
    fireEvent.change(within(line).getByLabelText("MasterData:ValidFrom"), { target: { value: "2026-01-01" } });
    fireEvent.change(within(line).getByLabelText("MasterData:ValidUntil"), { target: { value: "2030-12-31" } });

    fireEvent.click(screen.getByText("MasterData:AddQualification"));
    fireEvent.click(within(lines()[1]).getByText("Remove"));
    expect(lines()).toHaveLength(1);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver", expect.anything()));
    expect(postedDriver().qualifications).toEqual([{ id: null, kind: 3, number: "CE-77", validFrom: "2026-01-01", validUntil: "2030-12-31", description: null }]);
  });

  it("shows a server conflict through getApiErrorMessage and keeps the editor open", async () => {
    POST.mockResolvedValue(failed({ error: { code: "MasterData:DriverCodeAlreadyExists", message: "A driver with code DRV9 already exists." } }, 409));
    const { onClose } = renderEditor();
    fillRequired();
    await submitEditor();
    expect(await screen.findByText("A driver with code DRV9 already exists.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("DriverEditor — edit", () => {
  const loaded = () => waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("DRV1"));

  it("loads the record with its employer, user and qualification line, and updates with the concurrency stamp", async () => {
    const { onSaved } = renderEditor({ mode: "edit", id: "d1" });
    await loaded();
    const editor = editorDialog();
    expect(editor.querySelector("[data-slot=entity-dialog-header]")).toHaveTextContent("MasterData:Driver Jan Jansen");
    expect(field(editor, "MasterData:FirstName").value).toBe("Jan");
    expect(field(editor, "MasterData:Employer")).toHaveValue("Acme Transport");
    expect(field(editor, "MasterData:UserAccount")).toHaveValue("jan");
    expect(lines()).toHaveLength(1);
    expect(within(lines()[0]).getByLabelText("MasterData:Number")).toHaveValue("C95-1");
    expect(within(lines()[0]).getByLabelText("MasterData:ValidUntil")).toHaveValue("2029-01-01");

    typeInto(editor, "MasterData:LastName", "Janssen");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    const [path, init] = PUT.mock.calls[0];
    expect(path).toBe("/api/master-data/driver/{id}");
    expect(init!.params!.path).toEqual({ id: "d1" });
    expect(init!.body).toEqual(
      expect.objectContaining({
        concurrencyStamp: "stamp-d1",
        lastName: "Janssen",
        employerRelationId: "rel-1",
        userId: "u1",
        qualifications: [{ id: "q1", kind: 4, number: "C95-1", validFrom: "2024-01-01", validUntil: "2029-01-01", description: null }],
      }),
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it("hides Anonymise without Drivers.Anonymise", async () => {
    renderEditor({ mode: "edit", id: "d1" });
    await loaded();
    expect(within(editorDialog()).getByRole("button", { name: "Save" })).toBeInTheDocument();
    expect(within(editorDialog()).queryByRole("button", { name: "MasterData:Anonymise" })).toBeNull();
  });

  it("with the permission, Anonymise confirms destructively, calls the anonymise endpoint and closes", async () => {
    grants = { "MasterData.Drivers.Anonymise": true };
    const { onSaved, onClose } = renderEditor({ mode: "edit", id: "d1" });
    await loaded();
    fireEvent.click(ribbon("MasterData:Anonymise"));
    const confirm = screen.getByRole("alertdialog");
    expect(within(confirm).getByText("MasterData:AnonymiseDriver")).toBeInTheDocument();
    expect(within(confirm).getByText(/Jan Jansen/)).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Cancel"));

    fireEvent.click(within(confirm).getByRole("button", { name: "MasterData:Anonymise" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/driver/{id}/anonymise", { params: { path: { id: "d1" } } }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onSaved).toHaveBeenCalled();
    expect(PUT).not.toHaveBeenCalled();
  });

  it("a failing anonymise shows the server message in the editor and keeps it open", async () => {
    grants = { "MasterData.Drivers.Anonymise": true };
    POST.mockResolvedValue(failed({ error: { message: "Driver is on an active trip" } }, 409));
    const { onClose } = renderEditor({ mode: "edit", id: "d1" });
    await loaded();
    fireEvent.click(ribbon("MasterData:Anonymise"));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "MasterData:Anonymise" }));
    expect(await screen.findByText("Driver is on an active trip")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("an anonymised record opens read-only with the notice and badge: no Save, no Anonymise, no Delete, fields disabled", async () => {
    grants = { "MasterData.Drivers.Anonymise": true, "MasterData.Drivers.Delete": true };
    detail = { ...stored, firstName: "Anonymised", lastName: "Driver", fullName: "Anonymised driver", isAnonymised: true, email: null, mobile: null, userId: null, userName: null, qualifications: [] };
    renderEditor({ mode: "edit", id: "d1" });
    await loaded();
    const editor = editorDialog();
    expect(within(editor).getByText("MasterData:AnonymisedReadOnly")).toBeInTheDocument();
    expect(within(editor.querySelector("[data-slot=entity-dialog-header]") as HTMLElement).getByText("MasterData:Anonymised")).toBeInTheDocument();
    expect(within(editor).queryByRole("button", { name: "Save" })).toBeNull();
    expect(within(editor).queryByRole("button", { name: "MasterData:Anonymise" })).toBeNull();
    expect(within(editor).queryByRole("button", { name: "Delete" })).toBeNull();
    expect(field(editor, "MasterData:Code")).toBeDisabled();
    expect(field(editor, "MasterData:FirstName")).toBeDisabled();
    expect(field(editor, "MasterData:Employer")).toBeDisabled();
    expect(screen.queryByText("MasterData:AddQualification")).toBeNull();
  });

  it("is read-only without Update (UI-PERM-004) even with Anonymise: the command needs an editable record", async () => {
    grants = { "MasterData.Drivers.Anonymise": true };
    renderEditor({ mode: "edit", id: "d1", readOnly: true });
    await loaded();
    expect(within(editorDialog()).queryByRole("button", { name: "Save" })).toBeNull();
    expect(within(editorDialog()).queryByRole("button", { name: "MasterData:Anonymise" })).toBeNull();
    expect(field(editorDialog(), "MasterData:Code")).toBeDisabled();
  });
});
