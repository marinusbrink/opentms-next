import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { Mock } from "vitest";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* The relation editor (design 43, UI design as revised by the PO on 2026-10-12/13 after the old
 * OpenTMS screen) over the shared MasterDataEditor: client validation, the roles MultiSelect to the
 * flags value, the Archived and Blocked switches with the block reason (rendered only while Blocked is
 * ticked, required then, posted as 0 otherwise), the Name block (code, name, the optional full name:
 * trimmed, null when empty) stacked above the Contact information block in the left column, the
 * Addresses block on the right whose caption is the old header itself (PO 2026-10-13: no "Addresses"
 * heading): "All addresses equal" in the caption style and, while it is off, one caption-styled tab per
 * address type of the tenant beside it, with the same inline AddressFields (a tab's address enters the
 * form once edited; an untouched one is no address; once anything is entered country, street, postal
 * code and city are required, per field after blur or submit, and the editor-level
 * AddressIncomplete marks General); while it is on, the single default address (posted without an
 * address type). The time zone follows the country per address; the Map / Remarks sub-tabs sit in their
 * own caption-less Map section directly below the Addresses block in the same right column and show the
 * active address' coordinates and the description. Then the contact persons staged
 * through their dialog, the bank accounts inline on Financial with exactly one default, the
 * Administration fields, the full DTO on create and update, server errors through
 * getApiErrorMessage, the loaded record over all tabs, the badges (Blocked with its reason), the
 * confirmed Archive / Unarchive and Delete commands. The domain hooks are real; the generated client
 * is the mock; the Tabs (the address types, Map / Remarks) are the real Base UI component.
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
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import { FORM_SECTION_CAPTION } from "@/components/ui/form-section";
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

/** The tenant's address types: one tab each while "all addresses equal" is off. */
const ADDRESS_TYPES = [
  { id: "type-1", name: "Visiting address" },
  { id: "type-2", name: "Postal address" },
];
let addressTypes = ADDRESS_TYPES;

/** The relation's visiting address: the first type's tab. */
const visitingAddress: AddressDto = { id: "a1", addressTypeId: "type-1", addressTypeName: "Visiting address", countryId: "c-1", countryCode: "NL", street: "Main street", houseNumber: "1", houseNumberSuffix: "a", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" };
/** The relation's postal address: the second type's tab. */
const postalAddress: AddressDto = { id: "a2", addressTypeId: "type-2", addressTypeName: "Postal address", countryId: "c-1", countryCode: "NL", street: "PO Box", houseNumber: "99", houseNumberSuffix: null, addressLine2: "Mail room", postalCode: "2600 AA", city: "Delft", latitude: null, longitude: null, timeZoneId: "Europe/Amsterdam" };
/** The relation's default address (no type): the inline fields while "all addresses equal" is on. */
const defaultAddress: AddressDto = { id: "a0", addressTypeId: null, addressTypeName: null, countryId: "c-2", countryCode: "DE", street: "Station street", houseNumber: "12", houseNumberSuffix: "b", addressLine2: "Unit 3", postalCode: "10115", city: "Berlin", latitude: 52.52, longitude: 13.4, timeZoneId: "Europe/Berlin" };

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
  addressTypes = ADDRESS_TYPES;
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/master-data/relation/{id}":
        return ok(detail);
      case "/api/master-data/relation-category/lookup":
        return ok([{ id: "cat-1", name: "Cat A" }, { id: "cat-2", name: "Cat B" }]);
      case "/api/master-data/geographical-region/lookup":
        return ok([{ id: "reg-1", name: "Region X" }]);
      case "/api/master-data/address-type/lookup":
        return ok(addressTypes);
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
/* The editor's own tab strip sits in the ribbon; the address-type tabs and the Map / Remarks sub-tabs on General are tabs too. */
const editorTabs = () => within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getAllByRole("tab");
/* Inactive tab panels are aria-hidden (UI-DLG-015), so role queries need the tab active; label queries do not. */
const tab = (name: string) => fireEvent.click(within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getByRole("tab", { name: new RegExp(`^${escape(name)}`) }));
const region = (name: string) => within(editorDialog()).getByRole("region", { name, hidden: true });
const queryRegion = (name: string) => within(editorDialog()).queryByRole("region", { name, hidden: true });
/* The address-type tabs in the Addresses block's header (real Base UI Tabs; the list is named after the address type). */
const TYPE_TABS = "MasterData:AddressType";
const typeTabList = () => within(region("MasterData:Addresses")).getByRole("tablist", { name: TYPE_TABS, hidden: true });
const queryTypeTabList = () => within(region("MasterData:Addresses")).queryByRole("tablist", { name: TYPE_TABS, hidden: true });
const typeTabs = () => within(typeTabList()).getAllByRole("tab", { hidden: true }).map((el) => el.textContent?.trim());
const activeTypeTab = () => within(typeTabList()).getAllByRole("tab", { hidden: true }).find((el) => el.getAttribute("aria-selected") === "true")?.textContent?.trim();
const typeTab = (name: string) => fireEvent.click(within(typeTabList()).getByRole("tab", { name, hidden: true }));
const waitForTypeTabs = () => waitFor(() => expect(typeTabs()).toEqual(ADDRESS_TYPES.map((x) => x.name)));
/* The Map / Remarks strip (real Base UI Tabs: an inactive panel is unmounted) lives in its own caption-less Map section below the Addresses block. */
const mapSection = () => region("MasterData:Map");
const mapStrip = () => within(mapSection()).getByRole("tablist", { hidden: true });
const subTab = (name: string) => fireEvent.click(within(mapStrip()).getByRole("tab", { name: new RegExp(`^${escape(name)}`), hidden: true }));
const subTabs = () => within(mapStrip()).getAllByRole("tab", { hidden: true }).map((el) => el.textContent?.trim());
/* The textarea by its accessible name, on the Remarks sub-tab; the FormField label names it. */
const remarks = (scope: HTMLElement) => within(scope).getByRole("textbox", { name: "MasterData:Remarks", hidden: true }) as HTMLTextAreaElement;
const openRemarks = (scope: HTMLElement) => {
  subTab("MasterData:Remarks");
  return remarks(scope);
};
const mapFrame = () => screen.queryByTitle("MasterData:Map") as HTMLIFrameElement | null;
const blockReason = () => field(editorDialog(), "MasterData:BlockReason") as unknown as HTMLSelectElement;
/* The block reason is rendered only while Blocked is ticked (PO 2026-10-12): null while it is not. */
const queryBlockReason = () => queryField(editorDialog(), "MasterData:BlockReason") as unknown as HTMLSelectElement | null;
/* The Relation section's fields in DOM order: the FormField labels and the checkbox labels carry a `for`,
 * the MultiSelect mock's option labels do not. The required marker is stripped. */
const relationOrder = () =>
  Array.from(region("MasterData:Relation").querySelectorAll<HTMLLabelElement>("label[for]")).map((el) => el.textContent?.replace(/\s*\*\s*$/, "").trim());
/* The inline address block (one at a time: the default address or the active type's) and its labels in order. */
const addressBlock = () => region("MasterData:Addresses").querySelector<HTMLElement>("[data-slot=address-fields]");
const addressLabels = () => Array.from(addressBlock()!.querySelectorAll<HTMLLabelElement>("label")).map((el) => el.textContent?.replace(/\s*\*\s*$/, "").trim());
const addressLabel = (label: string) => within(region("MasterData:Addresses")).getByText(new RegExp(`^${escape(label)}`), { selector: "label" });
/* The Addresses block's caption row: the "All addresses equal" switch (styled as the caption) and, while it is off, the address-type tabs. */
const addressesCaption = () => checkbox("MasterData:AllAddressesEqual").closest("label")!.parentElement!;
const captionClasses = FORM_SECTION_CAPTION.split(/\s+/);
const generalTab = () => screen.getByRole("tab", { name: /General/ });
const lines = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
const ribbon = (name: string) => within(editorDialog()).getByRole("button", { name });
const header = () => editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-header]")!;

const ADDRESS_LABELS = ["MasterData:Country", "MasterData:PostalCode", "MasterData:City", "MasterData:TimeZone", "MasterData:Street", "MasterData:Nr", "MasterData:HouseNumberSuffix", "MasterData:Building", "MasterData:Longitude", "MasterData:Latitude"];

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

interface AddressInput {
  countryId: string;
  zone: string;
  postalCode: string;
  city: string;
  street: string;
  houseNumber?: string;
  houseNumberSuffix?: string;
  addressLine2?: string;
  longitude?: string;
  latitude?: string;
}

/** Fills the inline address block in front (country first, so the time zone follows). */
const fillAddress = async (input: AddressInput) => {
  const editor = editorDialog();
  fireEvent.change(field(editor, "MasterData:Country"), { target: { value: input.countryId } });
  await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe(input.zone));
  typeInto(editor, "MasterData:PostalCode", input.postalCode);
  typeInto(editor, "MasterData:City", input.city);
  typeInto(editor, "MasterData:Street", input.street);
  if (input.houseNumber != null) typeInto(editor, "MasterData:Nr", input.houseNumber);
  if (input.houseNumberSuffix != null) typeInto(editor, "MasterData:HouseNumberSuffix", input.houseNumberSuffix);
  if (input.addressLine2 != null) typeInto(editor, "MasterData:Building", input.addressLine2);
  if (input.longitude != null) typeInto(editor, "MasterData:Longitude", input.longitude);
  if (input.latitude != null) typeInto(editor, "MasterData:Latitude", input.latitude);
};

const fillDefaultAddress = () => fillAddress({ countryId: "c-1", zone: "Europe/Amsterdam", postalCode: " 2611 AB ", city: "Delft", street: " Station street ", houseNumber: "12", houseNumberSuffix: " b ", addressLine2: "Unit 3", longitude: "4,36", latitude: "52.01" });
const postedDefaultAddress = { id: null, addressTypeId: null, countryId: "c-1", street: "Station street", houseNumber: "12", houseNumberSuffix: "b", addressLine2: "Unit 3", postalCode: "2611 AB", city: "Delft", latitude: 52.01, longitude: 4.36, timeZoneId: "Europe/Amsterdam" };

const fillVisitingAddress = () => fillAddress({ countryId: "c-1", zone: "Europe/Amsterdam", postalCode: "2611 AA", city: "Delft", street: " Main street ", houseNumber: "1", houseNumberSuffix: "a", latitude: "52,01" });
const postedVisitingAddress = { id: null, addressTypeId: "type-1", countryId: "c-1", street: "Main street", houseNumber: "1", houseNumberSuffix: "a", addressLine2: null, postalCode: "2611 AA", city: "Delft", latitude: 52.01, longitude: null, timeZoneId: "Europe/Amsterdam" };

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
interface PutAddress {
  id: string | null;
  addressTypeId: string | null;
  countryId: string;
  street: string;
  houseNumber: string | null;
  houseNumberSuffix: string | null;
  addressLine2: string | null;
  postalCode: string;
  city: string;
  latitude: number | null;
  longitude: number | null;
  timeZoneId: string | null;
}
type PutBody = Omit<typeof emptyBody, "addresses" | "contactPersons" | "bankAccounts"> & { concurrencyStamp: string; addresses: PutAddress[]; contactPersons: { id: string | null }[]; bankAccounts: { id: string | null; iban: string }[] };
const putBody = () => PUT.mock.calls[0][1]!.body as PutBody;
const loadedAddress = (dto: AddressDto): PutAddress => ({ id: dto.id ?? null, addressTypeId: dto.addressTypeId ?? null, countryId: dto.countryId, street: dto.street, houseNumber: dto.houseNumber ?? null, houseNumberSuffix: dto.houseNumberSuffix ?? null, addressLine2: dto.addressLine2 ?? null, postalCode: dto.postalCode, city: dto.city, latitude: dto.latitude ?? null, longitude: dto.longitude ?? null, timeZoneId: dto.timeZoneId ?? null });

describe("RelationEditor — layout", () => {
  it("has the tabs General, Administration, Financial, Contact persons (no Addresses tab) and the roles as a MultiSelect", () => {
    renderEditor();
    const tabs = editorTabs().map((el) => el.textContent?.trim());
    expect(tabs.slice(0, 4)).toEqual(["General", "MasterData:Administration", "MasterData:Financial", "MasterData:ContactPersons"]);
    /* The record identification icon tab closes the strip; the address-type and Map / Remarks tabs are not part of it. */
    expect(tabs).toHaveLength(5);
    expect(tabs).not.toContain("MasterData:Addresses");

    const editor = editorDialog();
    expect(multiSelect(editor, "MasterData:Roles")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:Categories")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:GeographicalRegions")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(document.querySelector("[data-slot=checkbox-group]")).toBeNull();
    expect(queryField(editor, "MasterData:Description")).toBeNull();
    expect(screen.queryByText("MasterData:NewAddress")).toBeNull();
  });

  it("General (PO 2026-10-12/13): the Relation block with roles, the switches (the block reason only while blocked), categories and regions in that order; Name above Contact information on the left, Addresses on the right, nothing below", () => {
    renderEditor();
    const editor = editorDialog();

    /* Rows 1-2: one section titled after the entity. */
    const relation = region("MasterData:Relation");
    expect(multiSelect(relation, "MasterData:Roles")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(within(relation).getByRole("checkbox", { name: "MasterData:IsArchived" })).toBeInTheDocument();
    expect(within(relation).getByRole("checkbox", { name: "MasterData:IsBlocked" })).toBeInTheDocument();
    expect(multiSelect(relation, "MasterData:Categories")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(relation, "MasterData:GeographicalRegions")).toHaveAttribute("data-slot", "mock-multi-select");
    /* Row 1: roles, Archived, Blocked; row 2: categories, regions. Not blocked, so no block reason anywhere. */
    expect(queryBlockReason()).toBeNull();
    expect(relationOrder()).toEqual(["MasterData:Roles", "MasterData:IsArchived", "MasterData:IsBlocked", "MasterData:Categories", "MasterData:GeographicalRegions"]);
    /* Ticking Blocked slots the reason select in after the Blocked switch, inside the same section. */
    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(field(relation, "MasterData:BlockReason").tagName).toBe("SELECT");
    expect(relationOrder()).toEqual(["MasterData:Roles", "MasterData:IsArchived", "MasterData:IsBlocked", "MasterData:BlockReason", "MasterData:Categories", "MasterData:GeographicalRegions"]);
    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(queryBlockReason()).toBeNull();

    /* The Name block (code, name, full name) next to the Addresses block (the switch as its caption, the inline address) and the Map section (the Map / Remarks strip). */
    const name = region("MasterData:Name");
    expect(field(name, "MasterData:Code")).toBeInTheDocument();
    expect(field(name, "MasterData:Name")).toBeInTheDocument();
    expect(field(name, "MasterData:RelationFullName")).toBeInTheDocument();
    const addresses = region("MasterData:Addresses");
    const switchBox = within(addresses).getByRole("checkbox", { name: "MasterData:AllAddressesEqual" });
    expect(switchBox).toBeInTheDocument();
    expect(field(addresses, "MasterData:Street")).toBeInTheDocument();
    /* No "Addresses" heading (PO 2026-10-13): the switch is the caption, in the caption style, on the section's first row. */
    expect(addresses.querySelector("h3")).toBeNull();
    expect(within(addresses).queryByRole("heading", { hidden: true })).toBeNull();
    const switchLabel = switchBox.closest("label")!;
    for (const cls of captionClasses) expect(switchLabel, cls).toHaveClass(cls);
    expect(addresses.firstElementChild).toBe(addressesCaption());
    expect(addressesCaption().contains(switchLabel)).toBe(true);
    /* The Map / Remarks strip and the map itself are in the Map section, not in the Addresses block; that section has no heading either. */
    const map = mapSection();
    expect(map.querySelector("h3")).toBeNull();
    expect(within(addresses).queryByRole("tablist", { hidden: true })).toBeNull();
    expect(mapStrip().parentElement!.closest("section")).toBe(map);
    expect(subTabs()).toEqual(["MasterData:Map", "MasterData:Remarks"]);
    /* Map is the default sub-tab; without coordinates it explains what to enter. */
    expect(within(map).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    expect(map.querySelector("[data-slot=address-map]")).not.toBeNull();
    expect(addresses.querySelector("[data-slot=address-map]")).toBeNull();
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
    /* The row holds that column and the right column (the Addresses block, then the Map section), nothing else; there is no row below. */
    const row = column.parentElement!;
    expect(row.children).toHaveLength(2);
    expect(row.firstElementChild).toBe(column);
    const right = row.lastElementChild as HTMLElement;
    expect(right).not.toBe(column);
    expect(right.tagName).toBe("DIV");
    expect(addresses.parentElement).toBe(right);
    expect(map.parentElement).toBe(right);
    expect(Array.from(right.children).map((el) => el.getAttribute("aria-label"))).toEqual(["MasterData:Addresses", "MasterData:Map"]);
    expect(addresses.nextElementSibling).toBe(map);
    expect(row.nextElementSibling).toBeNull();
    expect(within(contact).queryByLabelText(/^MasterData:RelationFullName/, { selector: CONTROLS })).toBeNull();

    /* The Telephone, Internet and Remarks sections of the previous layout are gone. */
    expect(queryRegion("MasterData:Telephone")).toBeNull();
    expect(queryRegion("MasterData:Internet")).toBeNull();
    expect(queryRegion("MasterData:Remarks")).toBeNull();
    expect(within(editor).queryByRole("textbox", { name: "MasterData:Remarks", hidden: true })).toBeNull();
  });

  it("the Addresses block (PO 2026-10-13): the old field rows and labels, the control ids under the default address, the country options as codes only, the switch before the address", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    const addresses = region("MasterData:Addresses");
    /* Country, Postal code, City, Time zone / Street, No., Suffix / Building, Longitude, Latitude. */
    expect(addressLabels()).toEqual(ADDRESS_LABELS);
    /* The old labels are gone (exact: "HouseNumber" is a prefix of the new suffix label). */
    expect(within(editor).queryByLabelText(/^MasterData:HouseNumber(\s*\*)?$/, { selector: CONTROLS })).toBeNull();
    expect(within(editor).queryByLabelText(/^MasterData:AddressLine2(\s*\*)?$/, { selector: CONTROLS })).toBeNull();
    /* One block, after the caption row with the switch (the section's first row; no heading before it). */
    expect(addresses.querySelectorAll("[data-slot=address-fields]")).toHaveLength(1);
    const header = addressesCaption();
    expect(addresses.firstElementChild).toBe(header);
    expect(header.compareDocumentPosition(addressBlock()!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    /* The ids: `${form.id("defaultAddress")}-${field}`, the editor's instance prefix taken from the Code field. */
    const prefix = field(editor, "MasterData:Code").id.replace(/-code$/, "");
    expect(prefix).not.toBe(field(editor, "MasterData:Code").id);
    expect(field(editor, "MasterData:Country").id).toBe(`${prefix}-defaultAddress-countryId`);
    expect(field(editor, "MasterData:Street").id).toBe(`${prefix}-defaultAddress-street`);
    expect(field(editor, "MasterData:Nr").id).toBe(`${prefix}-defaultAddress-houseNumber`);
    expect(field(editor, "MasterData:HouseNumberSuffix").id).toBe(`${prefix}-defaultAddress-houseNumberSuffix`);
    expect(field(editor, "MasterData:Building").id).toBe(`${prefix}-defaultAddress-addressLine2`);
    expect(field(editor, "MasterData:TimeZone").id).toBe(`${prefix}-defaultAddress-timeZoneId`);
    /* The country shows the two-letter code only (the old screen). */
    await waitFor(() => expect(Array.from((field(editor, "MasterData:Country") as unknown as HTMLSelectElement).options).map((o) => o.textContent)).toEqual(["—", "NL", "DE"]));
    expect(within(addresses).queryByText(/Netherlands/)).toBeNull();
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
    expect(screen.queryByRole("tab", { name: /^MasterData:Addresses/ })).toBeNull();
  });

  it("a new relation starts with 'all addresses equal' on, the inline default address empty, no address-type tabs and no block reason select", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(checkbox("MasterData:IsArchived")).not.toBeChecked();
    expect(queryBlockReason()).toBeNull();
    expect(within(editor).queryByText(/^MasterData:BlockReason/, { selector: "label" })).toBeNull();
    for (const label of ADDRESS_LABELS) {
      expect(field(editor, label).value, label).toBe("");
    }
    /* Untouched: no required markers on the address yet. */
    expect(addressLabel("MasterData:Country")).not.toHaveTextContent("*");
    expect(addressLabel("MasterData:Street")).not.toHaveTextContent("*");
    expect(queryTypeTabList()).toBeNull();
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
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(screen.queryByText("MasterData:BlockReasonRequired")).toBeNull();
    expect(generalTab().className).toContain("text-destructive");
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

  it("the block reason is absent until Blocked is ticked, then appears enabled and required; blocked without a reason is refused with the General tab marked", async () => {
    renderEditor();
    fillRequired();
    expect(queryBlockReason()).toBeNull();

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).toBeChecked();
    const select = blockReason();
    expect(select).toBeEnabled();
    expect(select.value).toBe("");
    expect(Array.from(select.options).map((o) => o.textContent)).toEqual([
      "—",
      "MasterData:BlockReason.PaymentArrears",
      "MasterData:BlockReason.CreditLimitExceeded",
      "MasterData:BlockReason.Insolvency",
      "MasterData:BlockReason.Dispute",
      "MasterData:BlockReason.Other",
    ]);
    /* Required while blocked: the label carries its marker. */
    expect(within(editorDialog()).getByText(/^MasterData:BlockReason/, { selector: "label" })).toHaveTextContent("*");

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:BlockReasonRequired")).toBeInTheDocument();
    expect(select).toHaveAttribute("aria-invalid", "true");
    expect(generalTab().className).toContain("text-destructive");

    /* Picking a reason clears the error and the save goes through with the reason. */
    fireEvent.change(select, { target: { value: "1" } });
    expect(screen.queryByText("MasterData:BlockReasonRequired")).toBeNull();
    expect(select.value).toBe("1");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody()).toMatchObject({ isBlocked: true, blockReason: 1 });
  });

  it("a partly entered default address needs country, street, postal code and city and coordinates within range: per field and as AddressIncomplete on submit, General marked", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:Latitude", "95");
    typeInto(editor, "MasterData:Longitude", "-181");
    /* Entering something makes the four required: the markers appear. */
    expect(addressLabel("MasterData:Country")).toHaveTextContent("*");
    expect(addressLabel("MasterData:Street")).toHaveTextContent("*");
    expect(addressLabel("MasterData:PostalCode")).toHaveTextContent("*");
    expect(addressLabel("MasterData:City")).toHaveTextContent("*");
    expect(addressLabel("MasterData:Nr")).not.toHaveTextContent("*");
    /* Nothing was blurred and nothing submitted: no errors yet. */
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();
    expect(screen.queryByText("MasterData:InvalidNumber")).toBeNull();

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:AddressIncomplete")).toBeInTheDocument();
    expect(screen.getByText("MasterData:CountryRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:StreetRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:PostalCodeRequired")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:CityRequired")).toBeNull();
    expect(screen.getAllByText("MasterData:InvalidNumber")).toHaveLength(2);
    expect(field(editor, "MasterData:Country")).toHaveAttribute("aria-invalid", "true");
    expect(field(editor, "MasterData:City")).not.toHaveAttribute("aria-invalid");
    expect(generalTab().className).toContain("text-destructive");

    /* Completing the address clears the errors and it is posted. */
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(editor, "MasterData:Street", "Station street");
    typeInto(editor, "MasterData:PostalCode", "2611 AB");
    typeInto(editor, "MasterData:Latitude", "52.01");
    typeInto(editor, "MasterData:Longitude", "4.36");
    await waitFor(() => expect(screen.queryByText("MasterData:CountryRequired")).toBeNull());
    expect(screen.queryByText("MasterData:InvalidNumber")).toBeNull();
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(generalTab().className).not.toContain("text-destructive");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toHaveLength(1);
    expect(postedBody().addresses[0]).toMatchObject({ addressTypeId: null, countryId: "c-1", street: "Station street", city: "Delft", postalCode: "2611 AB", latitude: 52.01, longitude: 4.36, houseNumberSuffix: null, addressLine2: null });
  });

  it("an address field shows its own error after blur, before any submit, without the editor-level message or the tab mark", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    typeInto(editor, "MasterData:City", "Delft");
    fireEvent.blur(field(editor, "MasterData:Street"));
    expect(screen.getByText("MasterData:StreetRequired")).toBeInTheDocument();
    expect(field(editor, "MasterData:Street")).toHaveAttribute("aria-invalid", "true");
    /* Only the blurred field; the editor-level error and the tab mark wait for the submit. */
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();
    expect(screen.queryByText("MasterData:PostalCodeRequired")).toBeNull();
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(generalTab().className).not.toContain("text-destructive");
    typeInto(editor, "MasterData:Street", "Station street");
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
  });

  it("an incomplete typed address (switch off) is refused with AddressIncomplete and its per-field errors on its tab; another tab stays clean; completing it posts the address with its type", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    typeInto(editor, "MasterData:City", "Delft");

    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:AddressIncomplete")).toBeInTheDocument();
    expect(screen.getByText("MasterData:CountryRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:StreetRequired")).toBeInTheDocument();
    expect(screen.getByText("MasterData:PostalCodeRequired")).toBeInTheDocument();
    expect(screen.queryByText("MasterData:CityRequired")).toBeNull();
    expect(generalTab().className).toContain("text-destructive");

    /* The untouched Postal tab carries no error of its own; the editor-level message stays. */
    typeTab("Postal address");
    expect(activeTypeTab()).toBe("Postal address");
    expect(field(editor, "MasterData:City").value).toBe("");
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(screen.getByText("MasterData:AddressIncomplete")).toBeInTheDocument();
    expect(generalTab().className).toContain("text-destructive");

    /* Back on the Visiting tab the errors are still there; completing it clears everything. */
    typeTab("Visiting address");
    expect(field(editor, "MasterData:City").value).toBe("Delft");
    expect(screen.getByText("MasterData:StreetRequired")).toBeInTheDocument();
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(editor, "MasterData:Street", "Main street");
    typeInto(editor, "MasterData:PostalCode", "2611 AA");
    await waitFor(() => expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull());
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(generalTab().className).not.toContain("text-destructive");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toHaveLength(1);
    expect(postedBody().addresses[0]).toMatchObject({ addressTypeId: "type-1", countryId: "c-1", street: "Main street", postalCode: "2611 AA", city: "Delft" });
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

  it("unticking Blocked after a reason was chosen removes the reason select and posts blockReason 0", async () => {
    renderEditor();
    fillRequired();
    fireEvent.click(checkbox("MasterData:IsBlocked"));
    fireEvent.change(blockReason(), { target: { value: "2" } });
    expect(blockReason().value).toBe("2");

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(queryBlockReason()).toBeNull();
    expect(within(editorDialog()).queryByText(/^MasterData:BlockReason/, { selector: "label" })).toBeNull();

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

  it("the Map sub-tab shows the no-coordinates text until both coordinates are valid, then the OpenStreetMap embed; 'all addresses equal' off shows the (empty) active type's map instead", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    const map = mapSection();
    expect(within(map).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    expect(mapFrame()).toBeNull();

    /* One coordinate is not enough. */
    typeInto(editor, "MasterData:Latitude", "52.01");
    expect(mapFrame()).toBeNull();
    expect(within(map).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();

    typeInto(editor, "MasterData:Longitude", "4,36");
    const frame = mapFrame();
    expect(frame).not.toBeNull();
    expect(frame!.src).toContain("openstreetmap.org");
    expect(frame!.src).toContain("marker=52.01000,4.36000");
    expect(frame!.src).toContain("bbox=4.35000,52.00000,4.37000,52.02000");
    expect(map.contains(frame)).toBe(true);
    expect(within(map).queryByText("MasterData:MapNoCoordinates")).toBeNull();

    /* An out-of-range coordinate drops the map again. */
    typeInto(editor, "MasterData:Latitude", "95");
    expect(mapFrame()).toBeNull();
    expect(within(map).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    typeInto(editor, "MasterData:Latitude", "52.01");
    expect(mapFrame()).not.toBeNull();

    /* With the switch off the map follows the active type's address, which is empty. */
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(mapFrame()).toBeNull();
    expect(within(mapSection()).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(mapFrame()!.src).toContain("marker=52.01000,4.36000");
  });

  it("the map follows the active address-type tab", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    typeInto(editor, "MasterData:Latitude", "52.01");
    typeInto(editor, "MasterData:Longitude", "4.36");
    expect(mapFrame()!.src).toContain("marker=52.01000,4.36000");

    typeTab("Postal address");
    expect(mapFrame()).toBeNull();
    expect(within(mapSection()).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();
    typeInto(editor, "MasterData:Latitude", "52.52");
    typeInto(editor, "MasterData:Longitude", "13.4");
    expect(mapFrame()!.src).toContain("marker=52.52000,13.40000");

    typeTab("Visiting address");
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
    /* The textarea sits in the Map section; the map panel is unmounted while Remarks is in front. */
    expect(mapSection().contains(textarea)).toBe(true);
    expect(mapSection().querySelector("[data-slot=address-map]")).toBeNull();
    fireEvent.change(textarea, { target: { value: " Deliver before 10:00 " } });

    subTab("MasterData:Map");
    expect(within(editor).queryByRole("textbox", { name: "MasterData:Remarks", hidden: true })).toBeNull();
    expect(mapSection().querySelector("[data-slot=address-map]")).not.toBeNull();
    expect(region("MasterData:Addresses").querySelector("[data-slot=address-map]")).toBeNull();
    expect(openRemarks(editor).value).toBe(" Deliver before 10:00 ");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().description).toBe("Deliver before 10:00");
  });

  it("posts the inline default address, trimmed, with the suffix and the building, without an address type, and nothing else while the switch is on", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    /* A typed address entered earlier does not travel while "all addresses equal" is on. */
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    await fillVisitingAddress();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(queryTypeTabList()).toBeNull();
    await fillDefaultAddress();
    expect(field(editorDialog(), "MasterData:HouseNumberSuffix").value).toBe(" b ");
    expect(field(editorDialog(), "MasterData:Building").value).toBe("Unit 3");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([postedDefaultAddress]);
  });

  it("'all addresses equal' off hides the default address and shows one tab per address type of the tenant with empty inline fields; an untouched tab posts nothing", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    /* Enter a partial default address first: with the switch off neither its fields nor its validation apply. */
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    typeInto(editor, "MasterData:City", "Delft");

    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    /* The tabs sit in the Addresses section's first row beside the switch, before the address fields, in the caption style. */
    const header = addressesCaption();
    expect(region("MasterData:Addresses").firstElementChild).toBe(header);
    expect(header.contains(typeTabList())).toBe(true);
    for (const tabEl of within(typeTabList()).getAllByRole("tab", { hidden: true })) {
      for (const cls of captionClasses) expect(tabEl, cls).toHaveClass(cls);
    }
    expect(header.compareDocumentPosition(addressBlock()!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
    /* The same inline fields, now for the Visiting tab and empty: the default address is out of sight. */
    expect(addressLabels()).toEqual(ADDRESS_LABELS);
    for (const label of ADDRESS_LABELS) {
      expect(field(editor, label).value, label).toBe("");
    }
    const prefix = field(editor, "MasterData:Code").id.replace(/-code$/, "");
    expect(field(editor, "MasterData:Country").id).toBe(`${prefix}-address-type-1-countryId`);
    typeTab("Postal address");
    expect(field(editor, "MasterData:Country").id).toBe(`${prefix}-address-type-2-countryId`);
    expect(field(editor, "MasterData:City").value).toBe("");
    /* The Map / Remarks strip stays in the Map section below. */
    expect(subTabs()).toEqual(["MasterData:Map", "MasterData:Remarks"]);
    expect(within(region("MasterData:Addresses")).getAllByRole("tablist", { hidden: true })).toHaveLength(1);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(postedBody().addresses).toEqual([]);
  });

  it("without address types for the tenant, the switch off shows the per-type hint and nothing is posted", async () => {
    addressTypes = [];
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(screen.getByText("MasterData:AddressesPerTypeHint")).toBeInTheDocument();
    expect(queryTypeTabList()).toBeNull();
    expect(addressBlock()).toBeNull();
    expect(queryField(editor, "MasterData:Street")).toBeNull();
    expect(mapFrame()).toBeNull();
    expect(within(mapSection()).getByText("MasterData:MapNoCoordinates")).toBeInTheDocument();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([]);
  });

  it("fills the Visiting tab (time zone from its country) and posts it with its address type; the empty Postal tab posts nothing; each tab keeps its own values", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    await fillVisitingAddress();
    expect(GET).toHaveBeenCalledWith("/api/master-data/country/{id}", { params: { path: { id: "c-1" } } });

    /* The Postal tab is its own, empty address; back on Visiting the values are still there. */
    typeTab("Postal address");
    expect(field(editor, "MasterData:Street").value).toBe("");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    expect(addressLabel("MasterData:Country")).not.toHaveTextContent("*");
    typeTab("Visiting address");
    expect(field(editor, "MasterData:Street").value).toBe(" Main street ");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("a");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([postedVisitingAddress]);
  });

  it("fills both tabs and posts both typed addresses, each with its type, in the order they were entered", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    typeTab("Postal address");
    await fillAddress({ countryId: "c-2", zone: "Europe/Berlin", postalCode: "10115", city: "Berlin", street: "PO Box", houseNumber: "99", addressLine2: " Mail room " });
    typeTab("Visiting address");
    await fillVisitingAddress();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: "type-2", countryId: "c-2", street: "PO Box", houseNumber: "99", houseNumberSuffix: null, addressLine2: "Mail room", postalCode: "10115", city: "Berlin", latitude: null, longitude: null, timeZoneId: "Europe/Berlin" },
      postedVisitingAddress,
    ]);
  });

  it("switching 'all addresses equal' back on brings the inline default fields back with what was entered", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    typeInto(editor, "MasterData:City", "Delft");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(field(editor, "MasterData:City").value).toBe("");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(field(editor, "MasterData:City").value).toBe("Delft");
    expect(queryTypeTabList()).toBeNull();
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
  });

  it("the default address's time zone follows the chosen country until the user picks one by hand", async () => {
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

  it("a typed address's time zone follows its own country, apart from the other tab's and the default address's", async () => {
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();

    /* Visiting: NL, then DE replaces the zone the country filled in. */
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam"));
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-2" } });
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin"));

    /* Postal: untouched by that; its own pick fills its own zone, a hand-picked zone stays. */
    typeTab("Postal address");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    fireEvent.change(field(editor, "MasterData:TimeZone"), { target: { value: "Europe/London" } });
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-1" } });
    await waitFor(() => expect(GET.mock.calls.filter(([path, init]) => path === "/api/master-data/country/{id}" && init?.params?.path?.id === "c-1")).toHaveLength(2));
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/London");
    typeTab("Visiting address");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin");

    /* The default address did not follow either country. */
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    expect(field(editor, "MasterData:Country").value).toBe("");
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
  it("loads the record into every tab and shows the roles and Archived as badges; with only a typed address the switch is off and its tab is filled", async () => {
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
    expect(queryBlockReason()).toBeNull();

    /* Only a typed address: "all addresses equal" is off, the Visiting tab holds it, the Postal tab is empty. */
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:Street").value).toBe("Main street");
    expect(field(editor, "MasterData:Nr").value).toBe("1");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("a");
    expect(field(editor, "MasterData:Building").value).toBe("");
    expect(field(editor, "MasterData:PostalCode").value).toBe("2611 AA");
    expect(field(editor, "MasterData:City").value).toBe("Delft");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(field(editor, "MasterData:Latitude").value).toBe("52.01");
    expect(field(editor, "MasterData:Longitude").value).toBe("4.36");
    typeTab("Postal address");
    expect(field(editor, "MasterData:Street").value).toBe("");
    typeTab("Visiting address");

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

    /* The line grids in tab order: Financial, Contact persons; the addresses are no grid any more. */
    const all = lines();
    expect(all).toHaveLength(2);
    expect(within(all[0]).getByLabelText("MasterData:Iban")).toHaveValue("NL91ABNA0417164300");
    expect(within(all[0]).getByLabelText("MasterData:IsDefault")).toBeChecked();
    expect(all[1]).toHaveTextContent("Jan van Jansen");
    expect(all[1]).toHaveTextContent("MasterData:ContactPersonRole.Invoicing");
  });

  it("a loaded typeless address opens with the switch on, fills the inline default address (suffix and building included) and its map; the update keeps its id and posts the default only", async () => {
    detail = { ...stored, addresses: [visitingAddress, defaultAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    expect(queryTypeTabList()).toBeNull();
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
    await waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-2"));
    expect(field(editor, "MasterData:PostalCode").value).toBe("10115");
    expect(field(editor, "MasterData:City").value).toBe("Berlin");
    expect(field(editor, "MasterData:Street").value).toBe("Station street");
    expect(field(editor, "MasterData:Nr").value).toBe("12");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("b");
    expect(field(editor, "MasterData:Building").value).toBe("Unit 3");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin");
    expect(field(editor, "MasterData:Latitude").value).toBe("52.52");
    expect(field(editor, "MasterData:Longitude").value).toBe("13.4");
    /* The Map sub-tab shows the loaded coordinates straight away. */
    expect(mapFrame()!.src).toContain("marker=52.52000,13.40000");

    typeInto(editor, "MasterData:Nr", "14");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    /* "All addresses equal" on: the single default address travels, the typed one does not. */
    expect(putBody().addresses).toEqual([{ ...loadedAddress(defaultAddress), houseNumber: "14" }]);
  });

  it("turning 'all addresses equal' off on a loaded record shows its typed address on its tab and drops the default address from the update", async () => {
    detail = { ...stored, addresses: [defaultAddress, visitingAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    await waitFor(() => expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked());
    const editor = editorDialog();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(screen.queryByText("MasterData:AddressesPerTypeHint")).toBeNull();
    expect(field(editor, "MasterData:Street").value).toBe("Main street");
    expect(mapFrame()!.src).toContain("marker=52.01000,4.36000");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses).toEqual([loadedAddress(visitingAddress)]);
  });

  it("a loaded record with a visiting and a postal address shows both tabs filled; editing the suffix and the building updates both with their ids", async () => {
    detail = { ...stored, addresses: [visitingAddress, postalAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Street").value).toBe("Main street");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("a");

    typeTab("Postal address");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:Street").value).toBe("PO Box");
    expect(field(editor, "MasterData:Nr").value).toBe("99");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("");
    expect(field(editor, "MasterData:Building").value).toBe("Mail room");
    expect(field(editor, "MasterData:PostalCode").value).toBe("2600 AA");
    expect(field(editor, "MasterData:Latitude").value).toBe("");
    expect(mapFrame()).toBeNull();
    typeInto(editor, "MasterData:HouseNumberSuffix", " bis ");
    typeInto(editor, "MasterData:Building", "");

    typeTab("Visiting address");
    typeInto(editor, "MasterData:Building", " Building B ");
    typeInto(editor, "MasterData:HouseNumberSuffix", "");

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses).toEqual([
      { ...loadedAddress(visitingAddress), houseNumberSuffix: null, addressLine2: "Building B" },
      { ...loadedAddress(postalAddress), houseNumberSuffix: "bis", addressLine2: null },
    ]);
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
    expect(within(region("MasterData:Relation")).getByText(/^MasterData:BlockReason/, { selector: "label" })).toHaveTextContent("*");

    fireEvent.click(checkbox("MasterData:IsBlocked"));
    expect(checkbox("MasterData:IsBlocked")).not.toBeChecked();
    expect(queryBlockReason()).toBeNull();
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
    expect(body.addresses).toEqual([loadedAddress(visitingAddress)]);
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

  it("is read-only without Update: no Save, no Archive, disabled fields (the address fields included), no line toolbar (UI-PERM-004)", async () => {
    detail = { ...stored, isBlocked: true, blockReason: 2, addresses: [defaultAddress, visitingAddress] };
    const first = renderEditor({ mode: "edit", id: "r1", readOnly: true });
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
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    for (const label of ADDRESS_LABELS) {
      expect(field(editor, label), label).toBeDisabled();
    }
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("b");
    expect(field(editor, "MasterData:Phone")).toBeDisabled();
    expect(field(editor, "MasterData:RelationFullName")).toBeDisabled();
    expect(mapFrame()).not.toBeNull();
    expect(openRemarks(editor)).toBeDisabled();
    expect(screen.queryByText("MasterData:AddBankAccount")).toBeNull();
    expect(screen.queryByText("MasterData:NewContactPerson")).toBeNull();
    expect(document.querySelectorAll("[data-slot=mock-lines-grid][data-readonly=true]")).toHaveLength(2);
    first.unmount();

    /* Typed addresses only: the tabs still switch, their fields stay disabled. */
    detail = { ...stored, addresses: [visitingAddress, postalAddress] };
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    await waitForTypeTabs();
    const again = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).not.toBeChecked();
    expect(field(again, "MasterData:Street")).toBeDisabled();
    expect(field(again, "MasterData:Street").value).toBe("Main street");
    typeTab("Postal address");
    expect(field(again, "MasterData:Street")).toBeDisabled();
    expect(field(again, "MasterData:Street").value).toBe("PO Box");
    expect(field(again, "MasterData:Building")).toBeDisabled();
  });
});
