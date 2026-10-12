import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The relation editor (design 43, UI design) over the shared MasterDataEditor: client validation,
 * the roles checkbox group to the flags value, the Addresses and Contact persons lines staged
 * through their own dialogs, the Bank accounts lines inline with exactly one default, the full
 * DTO on create and update, server errors through getApiErrorMessage, the loaded record over all
 * tabs, the confirmed Archive / Unarchive and Delete commands. The domain hooks are real; the
 * generated client is the mock. Risk class: Medium (UI flow). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: grants },
      features: { values: { "MasterData.Relations": "true" } },
      localization: {
        languages: [
          { cultureName: "nl", displayName: "Nederlands" },
          { cultureName: "en", displayName: "English" },
        ],
      },
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
import { AddressDialog, newAddressRow } from "@/apps/master-data/editors/AddressDialog";
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import type { RelationDto, RelationRoles } from "@/domains/master-data/entities";

type ClientCall = (path: string, init?: { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> }; body?: unknown }) => Promise<unknown>;
const GET = apiClient.GET as unknown as Mock<ClientCall>;
const POST = apiClient.POST as unknown as Mock<ClientCall>;
const PUT = apiClient.PUT as unknown as Mock<ClientCall>;
const DELETE = apiClient.DELETE as unknown as Mock<ClientCall>;

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

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
});

const stored: RelationDto = {
  id: "r1",
  code: "ACME",
  name: "Acme",
  /* Customer | OwnCompany: the generated enum names single flags, a combination is the same flags enum to the server. */
  roles: 17 as RelationRoles,
  isArchived: false,
  phone: "+31 10 1234567",
  mobile: null,
  email: "info@acme.nl",
  website: "https://acme.nl",
  preferredCulture: "nl",
  chamberOfCommerceNumber: "12345678",
  vatNumber: "NL123456789B01",
  description: "Main customer",
  categoryIds: ["cat-1"],
  geographicalRegionIds: ["reg-1"],
  organizationUnitIds: ["ou-1"],
  addresses: [
    { id: "a1", addressTypeId: "type-1", addressTypeName: "Visiting", countryId: "c-1", countryCode: "NL", street: "Main street", houseNumber: "1", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" },
  ],
  contactPersons: [{ id: "cp1", firstName: "Jan", middleName: "van", lastName: "Jansen", role: 2, jobTitle: "Controller", department: null, email: "jan@acme.nl", phone: null, mobile: null }],
  bankAccounts: [{ id: "b1", iban: "NL91ABNA0417164300", bic: "ABNANL2A", bankName: "ABN", accountHolder: "Acme BV", isDefault: true }],
  concurrencyStamp: "stamp-1",
  creationTime: "2026-10-11T10:00:00Z",
  lastModificationTime: null,
};

let detail: RelationDto = stored;

beforeEach(() => {
  grants = { "MasterData.Relations.Delete": true };
  detail = stored;
  GET.mockReset().mockImplementation(async (path) => {
    switch (path) {
      case "/api/master-data/relation/{id}":
        return ok(detail);
      case "/api/master-data/relation-category/lookup":
        return ok([{ id: "cat-1", name: "Cat A" }, { id: "cat-2", name: "Cat B" }]);
      case "/api/master-data/geographical-region/lookup":
        return ok([{ id: "reg-1", name: "Region X" }]);
      case "/api/master-data/address-type/lookup":
        return ok([{ id: "type-1", name: "Visiting" }, { id: "type-2", name: "Postal" }]);
      case "/api/master-data/country/lookup":
        return ok([{ id: "c-1", code: "NL", name: "Netherlands" }, { id: "c-2", code: "DE", name: "Germany" }]);
      case "/api/master-data/country/{id}":
        return ok({ id: "c-1", code: "NL", primaryTimeZoneId: "Europe/Amsterdam" });
      case "/api/platform/administration/organization-units/all":
        return ok([{ id: "ou-1", displayName: "Unit 1" }]);
      default:
        return ok(undefined);
    }
  });
  POST.mockReset().mockResolvedValue(ok({ ...stored, id: "r-new" }));
  PUT.mockReset().mockResolvedValue(ok(stored));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

function renderEditor(props: Partial<React.ComponentProps<typeof RelationEditor>> = {}) {
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { unmount } = render(
    <QueryClientProvider client={client}>
      <RelationEditor mode="create" readOnly={false} open onClose={onClose} onSaved={onSaved} {...props} />
    </QueryClientProvider>,
  );
  return { onClose, onSaved, unmount };
}

const dialogs = () => Array.from(document.querySelectorAll<HTMLElement>("[role=dialog]"));
const editorDialog = () => dialogs()[0];
const childDialog = () => dialogs()[1];
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const field = (scope: HTMLElement, label: string) => within(scope).getByLabelText(new RegExp(`^${escape(label)}`)) as HTMLInputElement;
const typeInto = (scope: HTMLElement, label: string, value: string) => fireEvent.change(field(scope, label), { target: { value } });
const roleBox = (key: string) => screen.getByRole("checkbox", { name: `MasterData:Role.${key}` });
const tab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const ribbon = (name: string) => within(editorDialog()).getByRole("button", { name });

const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

const waitForLookups = () => waitFor(() => expect(screen.getByRole("checkbox", { name: "Cat A" })).toBeInTheDocument());

const fillRequired = () => {
  typeInto(editorDialog(), "MasterData:Code", "acme");
  typeInto(editorDialog(), "MasterData:Name", "Acme");
  fireEvent.click(roleBox("Customer"));
};

const emptyBody = {
  code: "ACME",
  name: "Acme",
  roles: 1,
  isArchived: false,
  phone: null,
  mobile: null,
  email: null,
  website: null,
  preferredCulture: null,
  chamberOfCommerceNumber: null,
  vatNumber: null,
  description: null,
  categoryIds: [],
  geographicalRegionIds: [],
  organizationUnitIds: [],
  addresses: [],
  contactPersons: [],
  bankAccounts: [],
};

const postedBody = () => POST.mock.calls.find(([path]) => path === "/api/master-data/relation")![1]!.body as typeof emptyBody;

describe("RelationEditor — validation", () => {
  it("refuses a new relation without code, name and at least one role, marks the tab and sends nothing", async () => {
    renderEditor();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:CodeRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:NameRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:RolesRequired")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /General/ }).className).toContain("text-destructive");
  });

  it("validates the e-mail shape on blur and clears the roles error once a role is ticked", async () => {
    renderEditor();
    const email = field(editorDialog(), "MasterData:Email");
    fireEvent.change(email, { target: { value: "not-an-address" } });
    expect(screen.queryByText("MasterData:InvalidEmail")).toBeNull();
    fireEvent.blur(email);
    expect(screen.getByText("MasterData:InvalidEmail")).toBeInTheDocument();
    fireEvent.change(email, { target: { value: "info@acme.nl" } });
    expect(screen.queryByText("MasterData:InvalidEmail")).toBeNull();

    await submitEditor();
    expect(screen.getByText("MasterData:RolesRequired")).toBeInTheDocument();
    fireEvent.click(roleBox("Carrier"));
    expect(screen.queryByText("MasterData:RolesRequired")).toBeNull();
    expect(POST).not.toHaveBeenCalled();
  });

  it("refuses a bank account line without an IBAN", async () => {
    renderEditor();
    fillRequired();
    tab("MasterData:BankAccounts");
    fireEvent.click(screen.getByText("MasterData:AddBankAccount"));
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:IbanRequired")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /MasterData:BankAccounts/ }).className).toContain("text-destructive");
  });

  it("prefills the name from a lookup's Add new … term (initialName)", () => {
    renderEditor({ initialName: "Zeeland Transport" });
    expect(field(editorDialog(), "MasterData:Name").value).toBe("Zeeland Transport");
  });
});

describe("RelationEditor — create", () => {
  it("round-trips the roles checkbox group to the flags value and posts the general tab as the DTO", async () => {
    const { onSaved, onClose } = renderEditor();
    await waitForLookups();
    fillRequired();
    fireEvent.click(roleBox("OwnCompany"));
    fireEvent.click(roleBox("Carrier"));
    fireEvent.click(roleBox("Carrier"));
    expect(roleBox("Customer")).toBeChecked();
    expect(roleBox("OwnCompany")).toBeChecked();
    expect(roleBox("Carrier")).not.toBeChecked();

    const editor = editorDialog();
    typeInto(editor, "MasterData:Phone", " +31 10 1234567 ");
    typeInto(editor, "MasterData:Email", "info@acme.nl");
    typeInto(editor, "MasterData:Website", "https://acme.nl");
    typeInto(editor, "MasterData:VatNumber", "nl123456789b01");
    typeInto(editor, "MasterData:ChamberOfCommerceNumber", "12345678");
    typeInto(editor, "MasterData:Description", "Main customer");
    fireEvent.change(field(editor, "MasterData:PreferredCulture"), { target: { value: "nl" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "Cat A" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Region X" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Unit 1" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "MasterData:IsArchived" }));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/master-data/relation");
    expect(postedBody()).toEqual({
      ...emptyBody,
      roles: 17,
      isArchived: true,
      phone: "+31 10 1234567",
      email: "info@acme.nl",
      website: "https://acme.nl",
      preferredCulture: "nl",
      chamberOfCommerceNumber: "12345678",
      vatNumber: "NL123456789B01",
      description: "Main customer",
      categoryIds: ["cat-1"],
      geographicalRegionIds: ["reg-1"],
      organizationUnitIds: ["ou-1"],
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ id: "r-new" })));
    expect(onClose).toHaveBeenCalled();
  });

  it("stages an address through the AddressDialog (time zone from the country) and posts it with the relation", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    tab("MasterData:Addresses");
    fireEvent.click(screen.getByText("MasterData:NewAddress"));
    const child = childDialog();
    expect(within(child).getByText("MasterData:NewAddress")).toBeInTheDocument();
    await waitFor(() => expect(within(field(child, "MasterData:AddressType")).getByText("Visiting")).toBeInTheDocument());

    /* An empty address is refused inside the dialog; nothing reaches the editor. */
    fireEvent.click(within(child).getByRole("button", { name: "Save" }));
    expect(within(child).getByText("MasterData:AddressTypeRequired")).toBeInTheDocument();
    expect(within(child).getByText("MasterData:CountryRequired")).toBeInTheDocument();
    expect(within(child).getByText("MasterData:StreetRequired")).toBeInTheDocument();
    expect(within(child).getByText("MasterData:PostalCodeRequired")).toBeInTheDocument();
    expect(within(child).getByText("MasterData:CityRequired")).toBeInTheDocument();
    expect(lines()).toHaveLength(0);

    fireEvent.change(field(child, "MasterData:AddressType"), { target: { value: "type-1" } });
    fireEvent.change(field(child, "MasterData:Country"), { target: { value: "c-1" } });
    await waitFor(() => expect(field(child, "MasterData:TimeZone").value).toBe("Europe/Amsterdam"));
    typeInto(child, "MasterData:Street", " Main street ");
    typeInto(child, "MasterData:HouseNumber", "1");
    typeInto(child, "MasterData:PostalCode", "2611 AA");
    typeInto(child, "MasterData:City", "Delft");
    typeInto(child, "MasterData:Latitude", "52,01");
    fireEvent.click(within(child).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(dialogs()).toHaveLength(1));

    expect(lines()).toHaveLength(1);
    expect(lines()[0]).toHaveTextContent("Visiting");
    expect(lines()[0]).toHaveTextContent("Main street");
    expect(lines()[0]).toHaveTextContent("NL");

    /* A second address may not reuse the type: the dialog no longer offers Visiting. */
    fireEvent.click(screen.getByText("MasterData:NewAddress"));
    const second = childDialog();
    await waitFor(() => expect(within(field(second, "MasterData:AddressType")).getByText("Postal")).toBeInTheDocument());
    expect(within(field(second, "MasterData:AddressType")).queryByText("Visiting")).toBeNull();
    /* The header X and the ribbon Close command share the name; either closes an untouched dialog. */
    fireEvent.click(within(second).getAllByRole("button", { name: "Close" })[0]);
    await waitFor(() => expect(dialogs()).toHaveLength(1));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: "type-1", countryId: "c-1", street: "Main street", houseNumber: "1", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: null, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("stages a contact person through its dialog and posts it with the relation", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    tab("MasterData:ContactPersons");
    fireEvent.click(screen.getByText("MasterData:NewContactPerson"));
    const child = childDialog();
    fireEvent.click(within(child).getByRole("button", { name: "Save" }));
    expect(within(child).getByText("MasterData:FirstNameRequired")).toBeInTheDocument();
    expect(within(child).getByText("MasterData:LastNameRequired")).toBeInTheDocument();

    typeInto(child, "MasterData:FirstName", "Jan");
    typeInto(child, "MasterData:LastName", "Jansen");
    typeInto(child, "MasterData:Email", "jan@acme.nl");
    fireEvent.change(field(child, "MasterData:Role"), { target: { value: "2" } });
    fireEvent.click(within(child).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    expect(lines()).toHaveLength(1);
    expect(lines()[0]).toHaveTextContent("Jan Jansen");
    expect(lines()[0]).toHaveTextContent("MasterData:ContactPersonRole.Invoicing");

    /* Opening the line again edits it in place instead of adding a second one. */
    fireEvent.click(within(lines()[0]).getByText("Edit"));
    const again = childDialog();
    expect(within(again).getByText("MasterData:EditContactPerson")).toBeInTheDocument();
    expect(field(again, "MasterData:FirstName").value).toBe("Jan");
    typeInto(again, "MasterData:JobTitle", "Controller");
    fireEvent.click(within(again).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    expect(lines()).toHaveLength(1);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().contactPersons).toEqual([
      { id: null, firstName: "Jan", middleName: null, lastName: "Jansen", role: 2, jobTitle: "Controller", department: null, email: "jan@acme.nl", phone: null, mobile: null },
    ]);
  });

  it("bank accounts: the first line becomes the default, marking another moves it, and IBANs are posted normalised", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    tab("MasterData:BankAccounts");
    fireEvent.click(screen.getByText("MasterData:AddBankAccount"));
    expect(within(lines()[0]).getByRole("checkbox", { name: "MasterData:IsDefault" })).toBeChecked();

    fireEvent.click(screen.getByText("MasterData:AddBankAccount"));
    expect(lines()).toHaveLength(2);
    expect(within(lines()[1]).getByRole("checkbox", { name: "MasterData:IsDefault" })).not.toBeChecked();

    fireEvent.click(within(lines()[1]).getByRole("checkbox", { name: "MasterData:IsDefault" }));
    expect(within(lines()[0]).getByRole("checkbox", { name: "MasterData:IsDefault" })).not.toBeChecked();
    expect(within(lines()[1]).getByRole("checkbox", { name: "MasterData:IsDefault" })).toBeChecked();

    fireEvent.change(within(lines()[0]).getByLabelText("MasterData:Iban"), { target: { value: "nl91 abna 0417 1643 00" } });
    fireEvent.change(within(lines()[0]).getByLabelText("MasterData:Bic"), { target: { value: "ABNANL2A" } });
    fireEvent.change(within(lines()[1]).getByLabelText("MasterData:Iban"), { target: { value: "NL20INGB0001234567" } });
    fireEvent.change(within(lines()[1]).getByLabelText("MasterData:AccountHolder"), { target: { value: "Acme BV" } });

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().bankAccounts).toEqual([
      { id: null, iban: "NL91ABNA0417164300", bic: "ABNANL2A", bankName: null, accountHolder: null, isDefault: false },
      { id: null, iban: "NL20INGB0001234567", bic: null, bankName: null, accountHolder: "Acme BV", isDefault: true },
    ]);
  });

  it("shows a server conflict (409) through getApiErrorMessage and keeps the editor open", async () => {
    POST.mockResolvedValue(failed({ error: { code: "MasterData:RelationCodeAlreadyExists", message: "A relation with code ACME already exists." } }, 409));
    const { onSaved, onClose } = renderEditor();
    fillRequired();
    await submitEditor();
    expect(await screen.findByText("A relation with code ACME already exists.")).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    expect(within(editorDialog()).getByRole("button", { name: "Save" })).toBeInTheDocument();
  });
});

describe("RelationEditor — edit", () => {
  it("loads the record into every tab and shows the roles and Archived as badges", async () => {
    detail = { ...stored, isArchived: true };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const editor = editorDialog();
    expect(GET).toHaveBeenCalledWith("/api/master-data/relation/{id}", { params: { path: { id: "r1" } } });
    expect(field(editor, "MasterData:Name").value).toBe("Acme");
    expect(roleBox("Customer")).toBeChecked();
    expect(roleBox("OwnCompany")).toBeChecked();
    expect(roleBox("Carrier")).not.toBeChecked();
    expect(field(editor, "MasterData:Phone").value).toBe("+31 10 1234567");
    expect(field(editor, "MasterData:Email").value).toBe("info@acme.nl");
    expect(field(editor, "MasterData:PreferredCulture").value).toBe("nl");
    expect(field(editor, "MasterData:VatNumber").value).toBe("NL123456789B01");
    expect(field(editor, "MasterData:Description").value).toBe("Main customer");
    expect(screen.getByRole("checkbox", { name: "Cat A" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Cat B" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Region X" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Unit 1" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "MasterData:IsArchived" })).toBeChecked();

    const header = editor.querySelector("[data-slot=entity-dialog-header]")!;
    expect(header).toHaveTextContent("MasterData:Relation Acme");
    expect(within(header as HTMLElement).getByText("MasterData:Role.Customer")).toBeInTheDocument();
    expect(within(header as HTMLElement).getByText("MasterData:Role.OwnCompany")).toBeInTheDocument();
    expect(within(header as HTMLElement).getByText("MasterData:Archived")).toBeInTheDocument();

    const all = lines();
    expect(all).toHaveLength(3);
    expect(all[0]).toHaveTextContent("Visiting");
    expect(all[0]).toHaveTextContent("Main street");
    expect(all[1]).toHaveTextContent("Jan van Jansen");
    expect(all[1]).toHaveTextContent("MasterData:ContactPersonRole.Invoicing");
    expect(within(all[2]).getByLabelText("MasterData:Iban")).toHaveValue("NL91ABNA0417164300");
    expect(within(all[2]).getByLabelText("MasterData:IsDefault")).toBeChecked();
  });

  it("updates with the concurrency stamp and the line ids, keeping the loaded children", async () => {
    const { onSaved } = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    const [path, init] = PUT.mock.calls[0];
    expect(path).toBe("/api/master-data/relation/{id}");
    expect(init!.params!.path).toEqual({ id: "r1" });
    const body = init!.body as Record<string, unknown> & { addresses: { id: string | null }[]; contactPersons: { id: string | null }[]; bankAccounts: { id: string | null; iban: string }[] };
    expect(body.concurrencyStamp).toBe("stamp-1");
    expect(body.name).toBe("Acme Transport");
    expect(body.roles).toBe(17);
    expect(body.addresses.map((a) => a.id)).toEqual(["a1"]);
    expect(body.contactPersons.map((c) => c.id)).toEqual(["cp1"]);
    expect(body.bankAccounts).toEqual([{ id: "b1", iban: "NL91ABNA0417164300", bic: "ABNANL2A", bankName: "ABN", accountHolder: "Acme BV", isDefault: true }]);
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it("Archive confirms, calls the archive endpoint and closes; an archived record offers Unarchive", async () => {
    const { onSaved, onClose } = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(editorDialog()).queryByRole("button", { name: "MasterData:Unarchive" })).toBeNull();
    fireEvent.click(ribbon("MasterData:Archive"));
    const confirm = screen.getByRole("alertdialog");
    expect(within(confirm).getByText("MasterData:ArchiveRelation")).toBeInTheDocument();
    expect(within(confirm).getByText(/Acme/)).toBeInTheDocument();
    expect(POST).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole("button", { name: "MasterData:Archive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/relation/{id}/archive", { params: { path: { id: "r1" } } }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onSaved).toHaveBeenCalled();
  });

  it("an archived record offers Unarchive and calls the unarchive endpoint", async () => {
    detail = { ...stored, isArchived: true };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(editorDialog()).queryByRole("button", { name: "MasterData:Archive" })).toBeNull();
    fireEvent.click(ribbon("MasterData:Unarchive"));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "MasterData:Unarchive" }));
    await waitFor(() => expect(POST).toHaveBeenCalledWith("/api/master-data/relation/{id}/unarchive", { params: { path: { id: "r1" } } }));
  });

  it("a failing archive shows the server message in the editor and stays open", async () => {
    POST.mockResolvedValue(failed({ error: { message: "Relation has open orders" } }, 409));
    const { onClose } = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    fireEvent.click(ribbon("MasterData:Archive"));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "MasterData:Archive" }));
    expect(await screen.findByText("Relation has open orders")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Delete needs the Delete permission, confirms and calls the delete endpoint", async () => {
    grants = {};
    const first = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(editorDialog()).queryByRole("button", { name: "Delete" })).toBeNull();
    expect(first.onClose).not.toHaveBeenCalled();
    first.unmount();

    grants = { "MasterData.Relations.Delete": true };
    const { onClose } = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    fireEvent.click(ribbon("Delete"));
    const confirm = screen.getByRole("alertdialog");
    expect(within(confirm).getByText("MasterData:DeleteRelation")).toBeInTheDocument();
    fireEvent.click(within(confirm).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(DELETE).toHaveBeenCalledWith("/api/master-data/relation/{id}", { params: { path: { id: "r1" } } }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("is read-only without Update: no Save, no Archive, disabled fields, no line toolbar (UI-PERM-004)", async () => {
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(editorDialog()).queryByRole("button", { name: "Save" })).toBeNull();
    expect(within(editorDialog()).queryByRole("button", { name: "MasterData:Archive" })).toBeNull();
    expect(field(editorDialog(), "MasterData:Code")).toBeDisabled();
    expect(roleBox("Customer")).toHaveAttribute("data-disabled");
    expect(screen.queryByText("MasterData:NewAddress")).toBeNull();
    expect(screen.queryByText("MasterData:AddBankAccount")).toBeNull();
    expect(document.querySelectorAll("[data-slot=mock-lines-grid][data-readonly=true]")).toHaveLength(3);
  });
});

describe("AddressDialog — one address per type", () => {
  it("refuses an address whose type another address of the relation already uses (AddressTypeAlreadyUsed)", async () => {
    const onSave = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const row = { ...newAddressRow(), addressTypeId: "type-1", countryId: "c-1", street: "Main street", postalCode: "2611 AA", city: "Delft" };
    render(
      <QueryClientProvider client={client}>
        <AddressDialog open row={row} usedAddressTypeIds={["type-1"]} readOnly={false} onClose={() => {}} onSave={onSave} />
      </QueryClientProvider>,
    );
    const dialog = dialogs()[0];
    await waitFor(() => expect(within(field(dialog, "MasterData:Country")).getByText("NL Netherlands")).toBeInTheDocument());
    fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    expect(within(dialog).getByText("MasterData:AddressTypeAlreadyUsed")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
    /* The used type is not even offered. */
    expect(within(field(dialog, "MasterData:AddressType")).queryByText("Visiting")).toBeNull();
    expect(within(field(dialog, "MasterData:AddressType")).getByText("Postal")).toBeInTheDocument();
  });

  it("validates coordinates and resolves the type name and country code for the staged line", async () => {
    const onSave = vi.fn();
    const onClose = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <AddressDialog open row={null} usedAddressTypeIds={[]} readOnly={false} onClose={onClose} onSave={onSave} />
      </QueryClientProvider>,
    );
    const dialog = dialogs()[0];
    await waitFor(() => expect(within(field(dialog, "MasterData:AddressType")).getByText("Postal")).toBeInTheDocument());
    fireEvent.change(field(dialog, "MasterData:AddressType"), { target: { value: "type-2" } });
    fireEvent.change(field(dialog, "MasterData:Country"), { target: { value: "c-2" } });
    typeInto(dialog, "MasterData:Street", "Hauptstrasse");
    typeInto(dialog, "MasterData:PostalCode", "10115");
    typeInto(dialog, "MasterData:City", "Berlin");
    typeInto(dialog, "MasterData:Latitude", "95");
    fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    expect(within(dialog).getByText("MasterData:InvalidNumber")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();

    typeInto(dialog, "MasterData:Latitude", "");
    fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0][0]).toEqual(expect.objectContaining({ addressTypeId: "type-2", addressTypeName: "Postal", countryId: "c-2", countryCode: "DE", street: "Hauptstrasse", city: "Berlin" }));
    expect(onClose).toHaveBeenCalled();
  });
});
