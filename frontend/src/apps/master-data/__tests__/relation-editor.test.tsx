import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The relation editor (design 43, UI design as revised again by the PO on 2026-10-12) over the
 * shared MasterDataEditor: client validation, the roles MultiSelect to the flags value, the Archived
 * and Blocked switches with the block reason (disabled until Blocked, required while Blocked, posted
 * as 0 otherwise), the Name block (code, name, the optional full name: trimmed, null when empty)
 * stacked above the Contact information block in the left column of row 4, the "all addresses
 * equal" default address inline on General (posted first, without an address type, only while the
 * switch is on and something was entered) with its own required fields and the time zone following
 * the country, the Map / Remarks sub-tabs under the address (the OpenStreetMap embed once both
 * coordinates are valid, the description textarea), the per-type addresses and contact persons
 * staged through their own dialogs, the bank accounts inline on Financial with exactly one default, the Administration
 * fields, the full DTO on create and update, server errors through getApiErrorMessage, the loaded
 * record over all tabs, the badges (Blocked with its reason), the confirmed Archive / Unarchive and
 * Delete commands. The domain hooks are real; the generated client is the mock; the Tabs of the
 * Map / Remarks strip are the real Base UI component.
 * Risk class: Medium (UI flow) — component tests per the matrix. */

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
/* The Base UI Select stands in as a native select: the block reason on General, the role in the contact person dialog. */
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

type AddressDto = NonNullable<RelationDto["addresses"]>[number];

/** The relation's typed (Visiting) address: it belongs on the Addresses tab. */
const visitingAddress: AddressDto = { id: "a1", addressTypeId: "type-1", addressTypeName: "Visiting", countryId: "c-1", countryCode: "NL", street: "Main street", houseNumber: "1", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" };
/** The relation's default address (no type): it belongs in the inline fields on General. */
const defaultAddress: AddressDto = { id: "a0", addressTypeId: null, addressTypeName: null, countryId: "c-2", countryCode: "DE", street: "Station street", houseNumber: "12", addressLine2: "Unit 3", postalCode: "10115", city: "Berlin", latitude: 52.52, longitude: 13.4, timeZoneId: "Europe/Berlin" };

const stored: RelationDto = {
  id: "r1",
  code: "ACME",
  name: "Acme",
  fullName: null,
  /* Customer | OwnCompany: the generated enum names single flags, a combination is the same flags enum to the server. */
  roles: 17 as RelationRoles,
  isArchived: false,
  isBlocked: false,
  blockReason: 0,
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
  addresses: [visitingAddress],
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
  GET.mockReset().mockImplementation(async (path, init) => {
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
        return ok(
          init?.params?.path?.id === "c-2"
            ? { id: "c-2", code: "DE", primaryTimeZoneId: "Europe/Berlin" }
            : { id: "c-1", code: "NL", primaryTimeZoneId: "Europe/Amsterdam" },
        );
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
/* Form controls only: a FormSection carries its title as aria-label too (the "MasterData:Name" section vs. the Name field). */
const CONTROLS = "input, select, textarea";
const field = (scope: HTMLElement, label: string) => within(scope).getByLabelText(new RegExp(`^${escape(label)}`), { selector: CONTROLS }) as HTMLInputElement;
const queryField = (scope: HTMLElement, label: string) => within(scope).queryByLabelText(new RegExp(`^${escape(label)}`), { selector: CONTROLS }) as HTMLInputElement | null;
const typeInto = (scope: HTMLElement, label: string, value: string) => fireEvent.change(field(scope, label), { target: { value } });
/* A MultiSelect by its FormField label: the mock is a <div> group, which a label query refuses to resolve. */
const multiSelect = (scope: HTMLElement, label: string) => {
  const labelElement = within(scope).getByText(new RegExp(`^${escape(label)}`), { selector: "label" }) as HTMLLabelElement;
  return document.getElementById(labelElement.htmlFor)!;
};
/* The roles are a MultiSelect; its mock renders one checkbox per option named after the role key. */
const roleBox = (key: string) => screen.getByRole("checkbox", { name: `MasterData:Role.${key}` });
const checkbox = (name: string) => screen.getByRole("checkbox", { name });
/* The editor's own tab strip sits in the ribbon; the Map / Remarks sub-tabs on General are tabs too. */
const editorTabs = () => within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getAllByRole("tab");
/* Inactive tab panels are aria-hidden (UI-DLG-015), so role queries need the tab active; label queries do not. */
const tab = (name: string) => fireEvent.click(within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getByRole("tab", { name: new RegExp(`^${escape(name)}`) }));
const region = (name: string) => within(editorDialog()).getByRole("region", { name, hidden: true });
const queryRegion = (name: string) => within(editorDialog()).queryByRole("region", { name, hidden: true });
/* The Map / Remarks strip under the inline address (real Base UI Tabs: an inactive panel is unmounted). */
const subTab = (name: string) => fireEvent.click(within(region("MasterData:Addresses")).getByRole("tab", { name: new RegExp(`^${escape(name)}`), hidden: true }));
const subTabs = () => within(region("MasterData:Addresses")).getAllByRole("tab", { hidden: true }).map((el) => el.textContent?.trim());
/* The textarea by its accessible name, on the Remarks sub-tab; the FormField label names it. */
const remarks = (scope: HTMLElement) => within(scope).getByRole("textbox", { name: "MasterData:Remarks", hidden: true }) as HTMLTextAreaElement;
const openRemarks = (scope: HTMLElement) => {
  subTab("MasterData:Remarks");
  return remarks(scope);
};
const mapFrame = () => screen.queryByTitle("MasterData:Map") as HTMLIFrameElement | null;
const blockReason = () => field(editorDialog(), "MasterData:BlockReason") as unknown as HTMLSelectElement;
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const ribbon = (name: string) => within(editorDialog()).getByRole("button", { name });
const header = () => editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-header]")!;

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

/** Fills the inline default address on General (country first, so the time zone follows). */
const fillDefaultAddress = async () => {
  const editor = editorDialog();
  fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
  await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam"));
  typeInto(editor, "MasterData:PostalCode", " 2611 AB ");
  typeInto(editor, "MasterData:City", "Delft");
  typeInto(editor, "MasterData:Street", " Station street ");
  typeInto(editor, "MasterData:HouseNumber", "12");
  typeInto(editor, "MasterData:AddressLine2", "Unit 3");
  typeInto(editor, "MasterData:Longitude", "4,36");
  typeInto(editor, "MasterData:Latitude", "52.01");
};

const postedDefaultAddress = { id: null, addressTypeId: null, countryId: "c-1", street: "Station street", houseNumber: "12", addressLine2: "Unit 3", postalCode: "2611 AB", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" };

const emptyBody = {
  code: "ACME",
  name: "Acme",
  fullName: null,
  roles: 1,
  isArchived: false,
  isBlocked: false,
  blockReason: 0,
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
type PutBody = Omit<typeof emptyBody, "addresses" | "contactPersons" | "bankAccounts"> & { concurrencyStamp: string; addresses: { id: string | null; addressTypeId: string | null }[]; contactPersons: { id: string | null }[]; bankAccounts: { id: string | null; iban: string }[] };
const putBody = () => PUT.mock.calls[0][1]!.body as PutBody;

describe("RelationEditor — layout", () => {
  it("has the tabs General, Administration, Financial, Addresses, Contact persons and the roles as a MultiSelect", () => {
    renderEditor();
    const tabs = editorTabs().map((el) => el.textContent?.trim());
    expect(tabs.slice(0, 5)).toEqual(["General", "MasterData:Administration", "MasterData:Financial", "MasterData:Addresses", "MasterData:ContactPersons"]);
    /* The record identification icon tab closes the strip; the Map / Remarks sub-tabs are not part of it. */
    expect(tabs).toHaveLength(6);

    const editor = editorDialog();
    expect(multiSelect(editor, "MasterData:Roles")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:Categories")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:GeographicalRegions")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(document.querySelector("[data-slot=checkbox-group]")).toBeNull();
    expect(queryField(editor, "MasterData:Description")).toBeNull();
  });

  it("General (PO 2026-10-12): the Relation block with categories, the switches, the block reason, regions and roles; Name above Contact information on the left, Addresses on the right, nothing below", () => {
    renderEditor();
    const editor = editorDialog();

    /* Rows 1-3: one section titled after the entity. */
    const relation = region("MasterData:Relation");
    expect(multiSelect(relation, "MasterData:Categories")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(within(relation).getByRole("checkbox", { name: "MasterData:IsArchived" })).toBeInTheDocument();
    expect(within(relation).getByRole("checkbox", { name: "MasterData:IsBlocked" })).toBeInTheDocument();
    expect(field(relation, "MasterData:BlockReason").tagName).toBe("SELECT");
    expect(multiSelect(relation, "MasterData:GeographicalRegions")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(relation, "MasterData:Roles")).toHaveAttribute("data-slot", "mock-multi-select");

    /* Row 4: the Name block (code, name, full name) next to the Addresses block (the switch, the inline address, the Map / Remarks strip). */
    const name = region("MasterData:Name");
    expect(field(name, "MasterData:Code")).toBeInTheDocument();
    expect(field(name, "MasterData:Name")).toBeInTheDocument();
    expect(field(name, "MasterData:RelationFullName")).toBeInTheDocument();
    const addresses = region("MasterData:Addresses");
    expect(within(addresses).getByRole("checkbox", { name: "MasterData:AllAddressesEqual" })).toBeInTheDocument();
    expect(field(addresses, "MasterData:Street")).toBeInTheDocument();
    expect(subTabs()).toEqual(["MasterData:Map", "MasterData:Remarks"]);
    /* Map is the default sub-tab; without coordinates it explains what to enter. */
    expect(within(addresses).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    expect(addresses.querySelector("[data-slot=address-map]")).not.toBeNull();
    expect(mapFrame()).toBeNull();

    /* The four contact fields in one section, stacked under the Name block in the same (left) column. */
    const contact = region("MasterData:ContactInformation");
    for (const label of ["MasterData:Phone", "MasterData:Mobile", "MasterData:Email", "MasterData:Website"]) {
      expect(field(contact, label)).toBeInTheDocument();
    }
    expect(within(contact).getAllByRole("textbox")).toHaveLength(4);
    const column = name.parentElement!;
    expect(contact.parentElement).toBe(column);
    expect(Array.from(column.children).map((el) => el.getAttribute("aria-label"))).toEqual(["MasterData:Name", "MasterData:ContactInformation"]);
    /* The row holds that column and the Addresses block, nothing else; there is no row 5. */
    const row = column.parentElement!;
    expect(row.children).toHaveLength(2);
    expect(row.firstElementChild).toBe(column);
    expect(row.lastElementChild).toBe(addresses);
    expect(row.nextElementSibling).toBeNull();
    expect(addresses.parentElement).not.toBe(column);
    expect(within(contact).queryByLabelText(/^MasterData:RelationFullName/, { selector: CONTROLS })).toBeNull();

    /* The Telephone, Internet and Remarks sections of the previous layout are gone. */
    expect(queryRegion("MasterData:Telephone")).toBeNull();
    expect(queryRegion("MasterData:Internet")).toBeNull();
    expect(queryRegion("MasterData:Remarks")).toBeNull();
    expect(within(editor).queryByRole("textbox", { name: "MasterData:Remarks", hidden: true })).toBeNull();
  });

  it("the Name section holds code, name and full name in that order; full name is optional, max 128", () => {
    renderEditor();
    const name = region("MasterData:Name");
    const code = field(name, "MasterData:Code");
    const shortName = field(name, "MasterData:Name");
    const fullName = field(name, "MasterData:RelationFullName");
    expect(within(name).getAllByRole("textbox").map((el) => el.id)).toEqual([code.id, shortName.id, fullName.id]);
    expect(fullName.maxLength).toBe(128);
    expect(fullName.value).toBe("");
    expect(fullName).not.toHaveAttribute("aria-required");
    expect(within(name).getByText(/^MasterData:RelationFullName/, { selector: "label" })).not.toHaveTextContent("*");
    /* Code and Name keep their required markers. */
    expect(code).toHaveAttribute("aria-required", "true");
    expect(shortName).toHaveAttribute("aria-required", "true");
    /* The field lives on General only. */
    expect(within(editorDialog()).getAllByLabelText(/^MasterData:RelationFullName/, { selector: CONTROLS })).toHaveLength(1);
  });

  it("the Administration tab holds the legal numbers, the preferred language and the organisation units; Financial holds the bank accounts", async () => {
    renderEditor();
    await waitForLookups();
    tab("MasterData:Administration");
    const legal = region("MasterData:Legal");
    expect(within(legal).getByLabelText(/^MasterData:ChamberOfCommerceNumber/)).toBeInTheDocument();
    expect(within(legal).getByLabelText(/^MasterData:VatNumber/)).toBeInTheDocument();
    const administration = region("MasterData:Administration");
    expect(within(administration).getByLabelText(/^MasterData:PreferredCulture/)).toBeInTheDocument();
    expect(multiSelect(administration, "MasterData:OrganizationUnits")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(within(administration).getByRole("checkbox", { name: "Unit 1" })).toBeInTheDocument();
    /* Nothing of that on General any more. */
    const general = editorDialog().querySelector<HTMLElement>('[data-slot=entity-dialog-panel][aria-label="General"]')!;
    expect(general).not.toBeNull();
    expect(within(general).queryByLabelText(/^MasterData:VatNumber/)).toBeNull();
    expect(within(general).queryByLabelText(/^MasterData:PreferredCulture/)).toBeNull();

    tab("MasterData:Financial");
    const bank = region("MasterData:BankAccounts");
    expect(within(bank).getByText("MasterData:AddBankAccount")).toBeInTheDocument();
    expect(within(bank).getByText("MasterData:Iban")).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /MasterData:BankAccounts/ })).toBeNull();
  });

  it("a new relation starts with 'all addresses equal' on, the inline default address empty and the block reason disabled", () => {
    renderEditor();
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(checkbox("MasterData:IsArchived")).not.toBeChecked();
    expect(blockReason()).toBeDisabled();
    expect(blockReason().value).toBe("");
    for (const label of ["MasterData:Country", "MasterData:PostalCode", "MasterData:City", "MasterData:Street", "MasterData:HouseNumber", "MasterData:AddressLine2", "MasterData:TimeZone", "MasterData:Longitude", "MasterData:Latitude"]) {
      expect(field(editor, label).value).toBe("");
    }
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
  });
});

describe("RelationEditor — validation", () => {
  it("refuses a new relation without code, name and at least one role, marks the tab and sends nothing", async () => {
    renderEditor();
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:CodeRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:NameRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:RolesRequired")).toBeInTheDocument();
    /* An untouched default address is not an error, even with the switch on; neither is an unblocked relation without a reason. */
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(screen.queryByText("MasterData:BlockReasonRequired")).toBeNull();
    expect(screen.getByRole("tab", { name: /General/ }).className).toContain("text-destructive");
  });

  it("validates the e-mail shape on blur and clears the roles error once a role is picked", async () => {
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

  it("the block reason is disabled until Blocked is ticked; blocked without a reason is refused with the General tab marked", async () => {
    renderEditor();
    fillRequired();
    const select = blockReason();
    expect(select).toBeDisabled();
    expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
      "—",
      "MasterData:BlockReason.PaymentArrears",
      "MasterData:BlockReason.CreditLimitExceeded",
      "MasterData:BlockReason.Insolvency",
      "MasterData:BlockReason.Dispute",
      "MasterData:BlockReason.Other",
    ]);

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).toBeChecked();
    expect(select).toBeEnabled();
    /* Required only while blocked: the label gets its marker. */
    expect(within(editorDialog()).getByText(/^MasterData:BlockReason/, { selector: "label" })).toHaveTextContent("*");

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:BlockReasonRequired")).toBeInTheDocument();
    expect(select).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("tab", { name: /General/ }).className).toContain("text-destructive");

    /* Picking a reason clears the error and the save goes through with the reason. */
    fireEvent.change(select, { target: { value: "1" } });
    expect(screen.queryByText("MasterData:BlockReasonRequired")).toBeNull();
    expect(select.value).toBe("1");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody()).toMatchObject({ isBlocked: true, blockReason: 1 });
  });

  it("a partly entered default address needs country, street, postal code and city, and coordinates within range", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:Latitude", "95");
    typeInto(editor, "MasterData:Longitude", "-181");
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:CountryRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:StreetRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:PostalCodeRequired")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:CityRequired")).toBeNull();
    expect(screen.getAllByText("MasterData:InvalidNumber")).toHaveLength(2);
    expect(screen.getByRole("tab", { name: /General/ }).className).toContain("text-destructive");

    /* Completing the address clears the errors and it is posted. */
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(editor, "MasterData:Street", "Station street");
    typeInto(editor, "MasterData:PostalCode", "2611 AB");
    typeInto(editor, "MasterData:Latitude", "52.01");
    typeInto(editor, "MasterData:Longitude", "4.36");
    await waitFor(() => expect(screen.queryByText("MasterData:CountryRequired")).toBeNull());
    expect(screen.queryByText("MasterData:InvalidNumber")).toBeNull();
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toHaveLength(1);
    expect(postedBody().addresses[0]).toMatchObject({ addressTypeId: null, countryId: "c-1", street: "Station street", city: "Delft", postalCode: "2611 AB", latitude: 52.01, longitude: 4.36 });
  });

  it("refuses a bank account line without an IBAN and marks the Financial tab", async () => {
    renderEditor();
    fillRequired();
    tab("MasterData:Financial");
    fireEvent.click(screen.getByText("MasterData:AddBankAccount"));
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:IbanRequired")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /MasterData:Financial/ }).className).toContain("text-destructive");
  });

  it("prefills the name from a lookup's Add new … term (initialName)", () => {
    renderEditor({ initialName: "Zeeland Transport" });
    expect(field(editorDialog(), "MasterData:Name").value).toBe("Zeeland Transport");
  });
});

describe("RelationEditor — create", () => {
  it("round-trips the roles MultiSelect to the flags value and posts General and Administration, Blocked with its reason included, as the DTO", async () => {
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
    typeInto(editor, "MasterData:Mobile", "+31 6 12345678");
    typeInto(editor, "MasterData:Email", "info@acme.nl");
    typeInto(editor, "MasterData:Website", "https://acme.nl");
    fireEvent.change(openRemarks(editor), { target: { value: "Main customer" } });
    fireEvent.click(checkbox("Cat A"));
    fireEvent.click(checkbox("Region X"));
    fireEvent.click(checkbox("MasterData:IsArchived"));
    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).toBeChecked();
    fireEvent.change(blockReason(), { target: { value: "3" } });

    tab("MasterData:Administration");
    typeInto(editor, "MasterData:VatNumber", "nl123456789b01");
    typeInto(editor, "MasterData:ChamberOfCommerceNumber", "12345678");
    fireEvent.change(field(editor, "MasterData:PreferredCulture"), { target: { value: "nl" } });
    fireEvent.click(checkbox("Unit 1"));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(POST.mock.calls[0][0]).toBe("/api/master-data/relation");
    expect(postedBody()).toEqual({
      ...emptyBody,
      roles: 17,
      isArchived: true,
      isBlocked: true,
      blockReason: 3,
      phone: "+31 10 1234567",
      mobile: "+31 6 12345678",
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

  it("unticking Blocked after a reason was chosen disables the reason again and posts blockReason 0", async () => {
    renderEditor();
    fillRequired();
    fireEvent.click(checkbox("MasterData:IsBlocked"));
    fireEvent.change(blockReason(), { target: { value: "2" } });
    expect(blockReason().value).toBe("2");

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(blockReason()).toBeDisabled();
    expect(blockReason().value).toBe("");
    expect(within(editorDialog()).getByText(/^MasterData:BlockReason/, { selector: "label" })).not.toHaveTextContent("*");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("MasterData:BlockReasonRequired")).toBeNull();
    expect(postedBody()).toMatchObject({ isBlocked: false, blockReason: 0 });
  });

  it("posts the full name trimmed", async () => {
    renderEditor();
    fillRequired();
    typeInto(region("MasterData:Name"), "MasterData:RelationFullName", "  Acme Transport B.V.  ");
    expect(field(editorDialog(), "MasterData:RelationFullName").value).toBe("  Acme Transport B.V.  ");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody()).toEqual({ ...emptyBody, fullName: "Acme Transport B.V." });
  });

  it("posts the full name as null when it is empty or whitespace only; it is never required", async () => {
    renderEditor();
    fillRequired();
    typeInto(region("MasterData:Name"), "MasterData:RelationFullName", "   ");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().fullName).toBeNull();
    expect(screen.queryByText(/Required$/)).toBeNull();
  });

  it("the Map sub-tab shows the no-coordinates text until both coordinates are valid, then the OpenStreetMap embed; 'all addresses equal' off removes the map", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    const addresses = region("MasterData:Addresses");
    expect(within(addresses).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    expect(mapFrame()).toBeNull();

    /* One coordinate is not enough. */
    typeInto(editor, "MasterData:Latitude", "52.01");
    expect(mapFrame()).toBeNull();
    expect(within(addresses).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();

    typeInto(editor, "MasterData:Longitude", "4,36");
    const frame = mapFrame();
    expect(frame).not.toBeNull();
    expect(frame!.src).toContain("openstreetmap.org");
    expect(frame!.src).toContain("marker=52.01000,4.36000");
    expect(frame!.src).toContain("bbox=4.35000,52.00000,4.37000,52.02000");
    expect(within(addresses).queryByText("MasterData:MapNoCoordinates")).toBeNull();

    /* An out-of-range coordinate drops the map again. */
    typeInto(editor, "MasterData:Latitude", "95");
    expect(mapFrame()).toBeNull();
    expect(within(addresses).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    typeInto(editor, "MasterData:Latitude", "52.01");
    expect(mapFrame()).not.toBeNull();

    /* Without the default address there is nothing to map. */
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(mapFrame()).toBeNull();
    expect(within(region("MasterData:Addresses")).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(mapFrame()!.src).toContain("marker=52.01000,4.36000");
  });

  it("the Remarks sub-tab holds the description textarea, keeps it across the sub-tabs and posts it", async () => {
    renderEditor();
    fillRequired();
    const editor = editorDialog();
    expect(within(editor).queryByRole("textbox", { name: "MasterData:Remarks", hidden: true })).toBeNull();

    const textarea = openRemarks(editor);
    expect(textarea.tagName).toBe("TEXTAREA");
    expect(textarea.maxLength).toBe(2500);
    /* The map panel is unmounted while Remarks is in front. */
    expect(region("MasterData:Addresses").querySelector("[data-slot=address-map]")).toBeNull();
    fireEvent.change(textarea, { target: { value: " Deliver before 10:00 " } });

    subTab("MasterData:Map");
    expect(within(editor).queryByRole("textbox", { name: "MasterData:Remarks", hidden: true })).toBeNull();
    expect(region("MasterData:Addresses").querySelector("[data-slot=address-map]")).not.toBeNull();
    expect(openRemarks(editor).value).toBe(" Deliver before 10:00 ");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().description).toBe("Deliver before 10:00");
  });

  it("posts the inline default address first, without an address type, ahead of the typed addresses", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    await fillDefaultAddress();

    tab("MasterData:Addresses");
    fireEvent.click(screen.getByText("MasterData:NewAddress"));
    const child = childDialog();
    await waitFor(() => expect(within(field(child, "MasterData:AddressType")).getByText("Postal")).toBeInTheDocument());
    fireEvent.change(field(child, "MasterData:AddressType"), { target: { value: "type-2" } });
    fireEvent.change(field(child, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(child, "MasterData:Street", "PO Box");
    typeInto(child, "MasterData:HouseNumber", "99");
    typeInto(child, "MasterData:PostalCode", "2600 AA");
    typeInto(child, "MasterData:City", "Delft");
    fireEvent.click(within(child).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    /* The default address is not a line on the Addresses tab. */
    expect(lines()).toHaveLength(1);
    expect(lines()[0]).toHaveTextContent("Postal");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      postedDefaultAddress,
      { id: null, addressTypeId: "type-2", countryId: "c-1", street: "PO Box", houseNumber: "99", addressLine2: null, postalCode: "2600 AA", city: "Delft", latitude: null, longitude: null, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("'all addresses equal' off hides the inline address, shows the per-type hint and posts no default address even when one was entered", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    /* Enter a partial address first: with the switch off neither its fields nor its validation apply. */
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(editor, "MasterData:City", "Delft");

    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    expect(screen.getByText("MasterData:AddressesPerTypeHint")).toBeInTheDocument();
    for (const label of ["MasterData:Country", "MasterData:PostalCode", "MasterData:City", "MasterData:Street", "MasterData:HouseNumber", "MasterData:AddressLine2", "MasterData:TimeZone", "MasterData:Longitude", "MasterData:Latitude"]) {
      expect(queryField(editor, label)).toBeNull();
    }
    /* The Map / Remarks strip stays with the section. */
    expect(subTabs()).toEqual(["MasterData:Map", "MasterData:Remarks"]);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(postedBody().addresses).toEqual([]);
  });

  it("switching 'all addresses equal' back on brings the inline fields back with what was entered", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    typeInto(editor, "MasterData:City", "Delft");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(queryField(editor, "MasterData:City")).toBeNull();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(field(editor, "MasterData:City").value).toBe("Delft");
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
  });

  it("the inline address's time zone follows the chosen country until the user picks one by hand", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    const country = field(editor, "MasterData:Country");
    const zone = field(editor, "MasterData:TimeZone");
    expect(zone.value).toBe("");

    fireEvent.change(country, { target: { value: "c-1" } });
    await waitFor(() => expect(zone.value).toBe("Europe/Amsterdam"));
    expect(GET).toHaveBeenCalledWith("/api/master-data/country/{id}", { params: { path: { id: "c-1" } } });

    /* A zone a previous country filled in follows the next country. */
    fireEvent.change(country, { target: { value: "c-2" } });
    await waitFor(() => expect(zone.value).toBe("Europe/Berlin"));

    /* A zone the user chose stays. */
    fireEvent.change(zone, { target: { value: "Europe/London" } });
    expect(zone.value).toBe("Europe/London");
    fireEvent.change(country, { target: { value: "c-1" } });
    await waitFor(() => expect(GET.mock.calls.filter(([path, init]) => path === "/api/master-data/country/{id}" && init?.params?.path?.id === "c-1")).toHaveLength(2));
    expect(zone.value).toBe("Europe/London");
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
    /* No default address was entered, so only the typed one travels. */
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

  it("bank accounts (Financial tab): the first line becomes the default, marking another moves it, and IBANs are posted normalised", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    tab("MasterData:Financial");
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
  it("loads the record into every tab and shows the roles and Archived as badges; without a typeless address the hint shows", async () => {
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
    const contact = region("MasterData:ContactInformation");
    expect(field(contact, "MasterData:Phone").value).toBe("+31 10 1234567");
    expect(field(contact, "MasterData:Mobile").value).toBe("");
    expect(field(contact, "MasterData:Email").value).toBe("info@acme.nl");
    expect(field(contact, "MasterData:Website").value).toBe("https://acme.nl");
    expect(openRemarks(editor).value).toBe("Main customer");
    expect(checkbox("Cat A")).toBeChecked();
    expect(checkbox("Cat B")).not.toBeChecked();
    expect(checkbox("Region X")).toBeChecked();
    expect(checkbox("MasterData:IsArchived")).toBeChecked();
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(blockReason()).toBeDisabled();

    /* Only a typed address: "all addresses equal" is off and the per-type hint replaces the inline fields. */
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    expect(screen.getByText("MasterData:AddressesPerTypeHint")).toBeInTheDocument();
    expect(queryField(editor, "MasterData:Country")).toBeNull();

    expect(field(editor, "MasterData:PreferredCulture").value).toBe("nl");
    expect(field(editor, "MasterData:VatNumber").value).toBe("NL123456789B01");
    expect(field(editor, "MasterData:ChamberOfCommerceNumber").value).toBe("12345678");
    tab("MasterData:Administration");
    expect(checkbox("Unit 1")).toBeChecked();

    expect(header()).toHaveTextContent("MasterData:Relation Acme");
    expect(within(header()).getByText("MasterData:Role.Customer")).toBeInTheDocument();
    expect(within(header()).getByText("MasterData:Role.OwnCompany")).toBeInTheDocument();
    expect(within(header()).getByText("MasterData:Archived")).toBeInTheDocument();
    expect(within(header()).queryByText(/MasterData:IsBlocked/)).toBeNull();

    /* The line grids in tab order: Financial, Addresses, Contact persons. */
    const all = lines();
    expect(all).toHaveLength(3);
    expect(within(all[0]).getByLabelText("MasterData:Iban")).toHaveValue("NL91ABNA0417164300");
    expect(within(all[0]).getByLabelText("MasterData:IsDefault")).toBeChecked();
    expect(all[1]).toHaveTextContent("Visiting");
    expect(all[1]).toHaveTextContent("Main street");
    expect(all[2]).toHaveTextContent("Jan van Jansen");
    expect(all[2]).toHaveTextContent("MasterData:ContactPersonRole.Invoicing");
  });

  it("a loaded typeless address fills the inline default address and its map, the typed one goes to Addresses, and the update keeps both ids", async () => {
    detail = { ...stored, addresses: [visitingAddress, defaultAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
    await waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-2"));
    expect(field(editor, "MasterData:PostalCode").value).toBe("10115");
    expect(field(editor, "MasterData:City").value).toBe("Berlin");
    expect(field(editor, "MasterData:Street").value).toBe("Station street");
    expect(field(editor, "MasterData:HouseNumber").value).toBe("12");
    expect(field(editor, "MasterData:AddressLine2").value).toBe("Unit 3");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin");
    expect(field(editor, "MasterData:Latitude").value).toBe("52.52");
    expect(field(editor, "MasterData:Longitude").value).toBe("13.4");
    /* The Map sub-tab shows the loaded coordinates straight away. */
    expect(mapFrame()!.src).toContain("marker=52.52000,13.40000");

    /* The Addresses tab shows the typed address only. */
    const addressLines = lines().filter((line) => line.textContent?.includes("Visiting") || line.textContent?.includes("Station street"));
    expect(addressLines).toHaveLength(1);
    expect(addressLines[0]).toHaveTextContent("Visiting");

    typeInto(editor, "MasterData:HouseNumber", "14");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses).toEqual([
      { id: "a0", addressTypeId: null, countryId: "c-2", street: "Station street", houseNumber: "14", addressLine2: "Unit 3", postalCode: "10115", city: "Berlin", latitude: 52.52, longitude: 13.4, timeZoneId: "Europe/Berlin" },
      { id: "a1", addressTypeId: "type-1", countryId: "c-1", street: "Main street", houseNumber: "1", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("turning 'all addresses equal' off on a loaded record drops its default address from the update", async () => {
    detail = { ...stored, addresses: [defaultAddress, visitingAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    await waitFor(() => expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked());
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(screen.getByText("MasterData:AddressesPerTypeHint")).toBeInTheDocument();
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses.map((a) => a.id)).toEqual(["a1"]);
  });

  it("a blocked record shows a destructive Blocked badge with its reason and the reason selected; unticking Blocked updates isBlocked and posts blockReason 0", async () => {
    detail = { ...stored, isBlocked: true, blockReason: 1 };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const badge = within(header()).getByText("MasterData:IsBlocked: MasterData:BlockReason.PaymentArrears");
    expect(badge.className).toContain("text-destructive");
    expect(within(header()).queryByText("MasterData:Archived")).toBeNull();
    expect(checkbox("MasterData:IsBlocked")).toBeChecked();
    expect(blockReason()).toBeEnabled();
    expect(blockReason().value).toBe("1");

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(blockReason()).toBeDisabled();
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().isBlocked).toBe(false);
    expect(putBody().blockReason).toBe(0);
    expect(putBody().isArchived).toBe(false);
  });

  it("changing the reason of a blocked record updates blockReason; a blocked record without a known reason shows the plain Blocked badge", async () => {
    detail = { ...stored, isBlocked: true, blockReason: 4 };
    const first = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(header()).getByText("MasterData:IsBlocked: MasterData:BlockReason.Dispute")).toBeInTheDocument();
    fireEvent.change(blockReason(), { target: { value: "5" } });
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody()).toMatchObject({ isBlocked: true, blockReason: 5 });
    first.unmount();

    detail = { ...stored, isBlocked: true, blockReason: 0 };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(within(header()).getByText("MasterData:IsBlocked")).toBeInTheDocument();
    expect(blockReason()).toBeEnabled();
    expect(blockReason().value).toBe("");
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
    const body = putBody();
    expect(body.concurrencyStamp).toBe("stamp-1");
    expect(body.name).toBe("Acme Transport");
    expect(body.roles).toBe(17);
    expect(body.isBlocked).toBe(false);
    expect(body.blockReason).toBe(0);
    expect(body.description).toBe("Main customer");
    expect(body.addresses.map((a) => a.id)).toEqual(["a1"]);
    expect(body.contactPersons.map((c) => c.id)).toEqual(["cp1"]);
    expect(body.bankAccounts).toEqual([{ id: "b1", iban: "NL91ABNA0417164300", bic: "ABNANL2A", bankName: "ABN", accountHolder: "Acme BV", isDefault: true }]);
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
  });

  it("loads the full name into the Name section and sends it, changed, with the update", async () => {
    detail = { ...stored, fullName: "Acme Transport B.V." };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const name = region("MasterData:Name");
    expect(field(name, "MasterData:RelationFullName").value).toBe("Acme Transport B.V.");
    expect(field(name, "MasterData:Name").value).toBe("Acme");
    expect(header()).toHaveTextContent("MasterData:Relation Acme");

    /* Untouched, the loaded value travels with the update. */
    typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody()).toMatchObject({ name: "Acme Transport", fullName: "Acme Transport B.V.", concurrencyStamp: "stamp-1" });

    /* Changed, the new value travels trimmed. */
    PUT.mockClear();
    typeInto(name, "MasterData:RelationFullName", " Acme Transport Holding B.V. ");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().fullName).toBe("Acme Transport Holding B.V.");
  });

  it("clearing the full name of a loaded record updates it to null", async () => {
    detail = { ...stored, fullName: "Acme Transport B.V." };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:RelationFullName").value).toBe("Acme Transport B.V."));
    await waitForLookups();
    typeInto(editorDialog(), "MasterData:RelationFullName", "");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().fullName).toBeNull();
    expect(putBody().name).toBe("Acme");
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
    detail = { ...stored, isBlocked: true, blockReason: 2, addresses: [defaultAddress, visitingAddress] };
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const editor = editorDialog();
    expect(within(editor).queryByRole("button", { name: "Save" })).toBeNull();
    expect(within(editor).queryByRole("button", { name: "MasterData:Archive" })).toBeNull();
    expect(field(editor, "MasterData:Code")).toBeDisabled();
    expect(roleBox("Customer")).toBeDisabled();
    expect(checkbox("Cat A")).toBeDisabled();
    expect(checkbox("MasterData:IsBlocked")).toHaveAttribute("data-disabled");
    /* Blocked and a reason set, yet read-only keeps the reason disabled. */
    expect(blockReason()).toBeDisabled();
    expect(blockReason().value).toBe("2");
    expect(checkbox("MasterData:AllAddressesEqual")).toHaveAttribute("data-disabled");
    expect(field(editor, "MasterData:Country")).toBeDisabled();
    expect(field(editor, "MasterData:Street")).toBeDisabled();
    expect(field(editor, "MasterData:TimeZone")).toBeDisabled();
    expect(field(editor, "MasterData:Phone")).toBeDisabled();
    expect(field(editor, "MasterData:RelationFullName")).toBeDisabled();
    expect(mapFrame()).not.toBeNull();
    expect(openRemarks(editor)).toBeDisabled();
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
