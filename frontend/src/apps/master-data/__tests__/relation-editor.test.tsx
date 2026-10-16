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
 * through their dialog, the Administration tab (PO 2026-10-13, the old Administratie tab; the Financial tab
 * is gone): the Company block (debtor and creditor numbers, legal numbers, carrier code, Department,
 * Currency, Language) and the Bank block with the bank accounts inline, exactly one default; the External
 * identification numbers and Transport characteristics grids; a new relation's default currency; the full DTO on create and update, server errors through
 * getApiErrorMessage, the loaded record over all tabs, the badges (Blocked with its reason), the
 * confirmed Archive / Unarchive and Delete commands. The domain hooks are real; the generated client
 * is the mock; the Tabs (the address types, Map / Remarks) are the real Base UI component.
 * Risk class: Medium (UI flow) — component tests per the matrix. The postal-code lookup of design 44
 * (its feature and permission gate: Critical / authorization, the UI half of it; the fill: Medium) is
 * covered at the end of this file, followed by its postal-code suggestions dropdown (PO 2026-10-13;
 * the same gate, the alpha-2 code and the pick posted). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => (k.endsWith("Question") ? `${k} {0}` : k), culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
/* The tenant's feature values: the relations feature on; design 44's address lookup only where a test turns it on. */
let features: Record<string, string> = {};
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({
    data: {
      auth: { grantedPolicies: grants },
      features: { values: features },
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

import { toast } from "sonner";

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

/** The tenant's default country (PO 2026-10-13): new addresses start with it and its time zone; null = none set (the server answers 204). */
const NL_DEFAULT = { id: "c-1", isoAlpha2: "NL", isoAlpha3: "NLD", name: "Netherlands", primaryTimeZoneId: "Europe/Amsterdam", isDefault: true };
let defaultCountry: typeof NL_DEFAULT | null = null;
const noContent = () => ({ data: undefined, error: undefined, response: { status: 204 } });

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

/** The tenant's currencies (lookup) and the first row of GET /api/master-data/currency sorted isDefault desc; null = no default set. */
const CURRENCY_LOOKUP = [
  { id: "cur-eur", code: "EUR", name: "Euro" },
  { id: "cur-usd", code: "USD", name: "US dollar" },
];
const EUR_DEFAULT = { id: "cur-eur", isoCode: "EUR", isDefault: true };
let defaultCurrencyRow: Record<string, unknown> | null = null;
/** The tenant's transport characteristics as GET /api/master-data/transport-characteristics/lookup-page answers them (searched on the server by name or code). */
const CHARACTERISTICS = [
  { id: "tc-1", code: "ADR", name: "Dangerous goods", description: "ADR load" },
  { id: "tc-2", code: "FRIG", name: "Refrigerated", description: null },
];

/** Design 44: what the Integrations module answers to the postal-code lookup (`found` false = nothing to fill); `lookupFails` makes the GET fail. */
const LOOKUP_FOUND = { found: true, street: "Dam", houseNumber: "1", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932, provider: "PDOK" };
let lookupResult: Record<string, unknown> = LOOKUP_FOUND;
let lookupFails = false;
/** The postal-code dropdown (PO 2026-10-13): the lines GET /api/integrations/address-lookup/suggestions answers. */
const SUGGESTED_PC = { label: "1012 AB Dam, Amsterdam", postalCode: "1012 AB", street: "Dam", houseNumber: null, houseNumberSuffix: null, city: "Amsterdam", latitude: 52.3731, longitude: 4.8932 };
const SUGGESTED_ADDRESS = { label: "Dam 1A, 1012 AB Amsterdam", postalCode: "1012 AB", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", city: "Amsterdam", latitude: 52.373, longitude: 4.893 };
let suggestions: Record<string, unknown>[] = [SUGGESTED_PC, SUGGESTED_ADDRESS];

beforeEach(() => {
  grants = { "MasterData.Relations.Delete": true };
  /* With the characteristics node's chain: "Add new transport characteristic…" needs it besides the permission (UI-PERM-006). */
  features = {
    "Apps.MasterData.General.Relations.Relations": "true",
    "Apps.MasterData": "true", "Apps.MasterData.General": "true",
    "Apps.MasterData.General.Fleet": "true",
    "Apps.MasterData.General.Fleet.TransportCharacteristics": "true",
  };
  lookupResult = LOOKUP_FOUND;
  lookupFails = false;
  suggestions = [SUGGESTED_PC, SUGGESTED_ADDRESS];
  detail = stored;
  addressTypes = ADDRESS_TYPES;
  defaultCountry = null;
  defaultCurrencyRow = null;
  GET.mockReset().mockImplementation(async (path, init) => {
    switch (path) {
      case "/api/master-data/relation/{id}":
        return ok(detail);
      case "/api/master-data/country/default":
        return defaultCountry ? ok(defaultCountry) : noContent();
      case "/api/master-data/currency":
        return ok({ rows: defaultCurrencyRow ? [defaultCurrencyRow] : [], lastRow: defaultCurrencyRow ? 1 : 0 });
      case "/api/master-data/currency/lookup":
        return ok(CURRENCY_LOOKUP);
      case "/api/master-data/transport-characteristics/lookup-page": {
        const term = String(init?.params?.query?.term ?? "").toLowerCase();
        const items = CHARACTERISTICS.filter((c) => !term || c.name.toLowerCase().includes(term) || c.code.toLowerCase().includes(term));
        return ok({ items, totalCount: items.length });
      }
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
      case "/api/integrations/address-lookup":
        return lookupFails ? failed({ error: { code: "Forbidden", message: "no" } }, 403) : ok(lookupResult);
      case "/api/integrations/address-lookup/suggestions":
        return ok(suggestions);
      default:
        return ok(undefined);
    }
  });
  POST.mockReset().mockResolvedValue(ok({ ...stored, id: "r-new" }));
  PUT.mockReset().mockResolvedValue(ok(stored));
  DELETE.mockReset().mockResolvedValue(ok(undefined));
});

const newClient = () => new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
function renderEditor(props: Partial<React.ComponentProps<typeof RelationEditor>> = {}, client: QueryClient = newClient()) {
  const onClose = vi.fn();
  const onSaved = vi.fn();
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

/* The long multi-tab flows (several address tabs, country detail round trips, lookup popups, a submit) take 1-4.5 s alone on an idle
 * machine and cross vitest's 5 s default when the machine is busy (measured 2026-10-13); they are slow, not hanging. */
const LONG_FLOW = 20_000;

const submitEditor = () =>
  act(async () => {
    fireEvent.submit(editorDialog().querySelector("form")!);
    await Promise.resolve();
  });

const waitForLookups = () => waitFor(() => expect(screen.getByRole("checkbox", { name: "Cat A" })).toBeInTheDocument());
/* A new relation opens once the default country is known (PO 2026-10-13): the dialog mounts after that GET. */
const waitForEditor = () => waitFor(() => expect(editorDialog()).toBeDefined());

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
  debtorNumber: null as string | null,
  creditorNumber: null as string | null,
  carrierCode: null as string | null,
  currencyId: null as string | null,
  externalIdentifiers: [] as { id: string | null; source: string; identifier: string }[],
  transportCharacteristicIds: [] as string[],
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
  it("has the tabs General, Administration, Contact persons and Record identification (no Financial, no Addresses tab) and the roles as a MultiSelect", async () => {
    renderEditor();
    await waitForEditor();
    const tabs = editorTabs().map((el) => el.textContent?.trim());
    expect(tabs.slice(0, 3)).toEqual(["General", "MasterData:Administration", "MasterData:ContactPersons"]);
    /* The record identification icon tab closes the strip; the address-type and Map / Remarks tabs are not part of it. */
    expect(tabs).toHaveLength(4);
    expect(tabs).not.toContain("MasterData:Addresses");
    expect(tabs).not.toContain("MasterData:Financial");

    const editor = editorDialog();
    expect(multiSelect(editor, "MasterData:Roles")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:Categories")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(multiSelect(editor, "MasterData:GeographicalRegions")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(document.querySelector("[data-slot=checkbox-group]")).toBeNull();
    expect(queryField(editor, "MasterData:Description")).toBeNull();
    expect(screen.queryByText("MasterData:NewAddress")).toBeNull();
  });

  it("General (PO 2026-10-12/13): the Relation block with roles, the switches (the block reason only while blocked), categories and regions in that order; Name above Contact information on the left, Addresses on the right, nothing below", async () => {
    renderEditor();
    await waitForEditor();
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

  it("the Administration tab (PO 2026-10-13): Company and Bank on the left, External identification numbers and Transport characteristics on the right", async () => {
    renderEditor();
    await waitForLookups();
    tab("MasterData:Administration");
    const company = region("MasterData:Company");
    /* The Company block's fields in order; the MultiSelect mock's option labels carry no `for`. */
    const order = Array.from(company.querySelectorAll<HTMLLabelElement>("label[for]")).map((el) => el.textContent?.replace(/\s*\*\s*$/, "").trim());
    expect(order).toEqual([
      "MasterData:DebtorNumber",
      "MasterData:CreditorNumber",
      "MasterData:ChamberOfCommerceNumber",
      "MasterData:VatNumber",
      "MasterData:CarrierCode",
      "MasterData:Department",
      "MasterData:Currency",
      "MasterData:PreferredCulture",
    ]);
    expect(multiSelect(company, "MasterData:Department")).toHaveAttribute("data-slot", "mock-multi-select");
    expect(within(company).getByRole("checkbox", { name: "Unit 1", hidden: true })).toBeInTheDocument();
    /* The currency is a ReferenceCombobox over the currency lookup: code and name. */
    await waitFor(() => expect(Array.from((field(company, "MasterData:Currency") as unknown as HTMLSelectElement).options).map((o) => o.textContent)).toEqual(["—", "EUR Euro", "USD US dollar"]));

    const bank = region("MasterData:Bank");
    expect(within(bank).getByText("MasterData:AddBankAccount")).toBeInTheDocument();
    expect(within(bank).getByText("MasterData:Iban")).toBeInTheDocument();
    const external = region("MasterData:ExternalIdentifiers");
    expect(Array.from(external.querySelectorAll("[data-column]")).map((el) => el.textContent)).toEqual(["MasterData:ExternalIdentifier", "MasterData:ExternalSource"]);
    expect(within(external).getByText("MasterData:AddExternalIdentifier")).toBeInTheDocument();
    const characteristics = region("MasterData:TransportCharacteristics");
    const columns = Array.from(characteristics.querySelectorAll<HTMLElement>("[data-column]"));
    expect(columns.map((el) => el.textContent)).toEqual(["MasterData:Name", "MasterData:Code", "MasterData:Description"]);
    /* Read-only lines: no column is edited, none has a cell editor. */
    expect(columns.map((el) => el.dataset.editable ?? null)).toEqual([null, null, null]);
    expect(columns.map((el) => el.dataset.cellEditor ?? null)).toEqual([null, null, null]);
    /* Lines are added with the EntityLookup in the grid's toolbar, named by an sr-only label and its placeholder. */
    const characteristicLookup = within(characteristics).getByLabelText("MasterData:AddTransportCharacteristic", { selector: "input" }) as HTMLInputElement;
    expect(characteristicLookup.placeholder).toBe("MasterData:AddTransportCharacteristic");
    expect(within(characteristics).getByText("MasterData:AddTransportCharacteristic", { selector: "label" })).toHaveClass("sr-only");
    const grid = characteristics.querySelector<HTMLElement>("[data-slot=mock-lines-grid]")!;
    expect(grid.contains(characteristicLookup)).toBe(true);
    expect(characteristicLookup.compareDocumentPosition(grid.querySelector("[data-slot=mock-lines-grid-columns]")!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    /* Left column: Company above Bank; right column: External identification numbers above Transport characteristics. */
    const left = company.parentElement!;
    const right = external.parentElement!;
    expect(bank.parentElement).toBe(left);
    expect(characteristics.parentElement).toBe(right);
    expect(left.parentElement).toBe(right.parentElement);
    expect(Array.from(left.parentElement!.children)).toEqual([left, right]);
    expect(Array.from(left.children)).toEqual([company, bank]);
    expect(Array.from(right.children)).toEqual([external, characteristics]);

    /* Nothing of that on General; no Legal block, no Financial or Bank accounts tab any more. */
    const general = editorDialog().querySelector<HTMLElement>('[data-slot=entity-dialog-panel][aria-label="General"]')!;
    expect(general).not.toBeNull();
    expect(within(general).queryByLabelText(/^MasterData:VatNumber/)).toBeNull();
    expect(within(general).queryByLabelText(/^MasterData:PreferredCulture/)).toBeNull();
    expect(within(general).queryByLabelText(/^MasterData:DebtorNumber/)).toBeNull();
    expect(queryRegion("MasterData:Legal")).toBeNull();
    expect(queryRegion("MasterData:BankAccounts")).toBeNull();
    expect(screen.queryByRole("tab", { name: /MasterData:Financial/ })).toBeNull();
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
    await waitForEditor();
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
    await waitForEditor();
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
    await waitForEditor();
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
  }, LONG_FLOW);

  it("refuses a bank account line without an IBAN and marks the Administration tab", async () => {
    renderEditor();
    await waitForEditor();
    fillRequired();
    tab("MasterData:Administration");
    fireEvent.click(screen.getByText("MasterData:AddBankAccount"));
    await submitEditor();
    expect(POST).not.toHaveBeenCalled();
    expect(screen.getByText("MasterData:IbanRequired")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /MasterData:Administration/ }).className).toContain("text-destructive");
  });

  it("prefills the name from a lookup's Add new … term (initialName)", async () => {
    renderEditor({ initialName: "Zeeland Transport" });
    await waitForEditor();
    expect(field(editorDialog(), "MasterData:Name").value).toBe("Zeeland Transport");
  });
});

describe("RelationEditor — create", () => {
  it("round-trips the roles MultiSelect to the flags value and posts General and Administration, Blocked with its reason included, as the DTO", async () => {
    const { onSaved, onClose } = renderEditor();
    await waitForLookups();
    fillRequired();
    /* Depot, not Own company: this relation is saved archived, which the own company never is (PO 2026-10-16). */
    fireEvent.click(roleBox("Depot"));
    fireEvent.click(roleBox("Carrier"));
    fireEvent.click(roleBox("Carrier"));
    expect(roleBox("Customer")).toBeChecked();
    expect(roleBox("Depot")).toBeChecked();
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
      roles: 9,
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
  }, LONG_FLOW);

  it("unticking Blocked after a reason was chosen removes the reason select and posts blockReason 0", async () => {
    renderEditor();
    await waitForEditor();
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
    await waitForEditor();
    fillRequired();
    typeInto(region("MasterData:Name"), "MasterData:RelationFullName", "  Acme Transport B.V.  ");
    expect(field(editorDialog(), "MasterData:RelationFullName").value).toBe("  Acme Transport B.V.  ");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody()).toEqual({ ...emptyBody, fullName: "Acme Transport B.V." });
  });

  it("posts the full name as null when it is empty or whitespace only; it is never required", async () => {
    renderEditor();
    await waitForEditor();
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
    await waitForEditor();
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
  }, LONG_FLOW);

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

  it("bank accounts (Administration tab, Bank block): the first line becomes the default, marking another moves it, and IBANs are posted normalised", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    tab("MasterData:Administration");
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
    await waitForEditor();
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

    /* The line grids in tab order: Administration (bank accounts; no external ids or characteristics on this record), Contact persons; the addresses are no grid any more. */
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
    /* Customer | Carrier: the stored default is the own company, which is never archived (PO 2026-10-16). */
    detail = { ...stored, roles: 3 as RelationRoles };
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
    detail = { ...stored, roles: 3 as RelationRoles };
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
    expect(screen.queryByText("MasterData:AddExternalIdentifier")).toBeNull();
    expect(screen.queryByText("MasterData:AddTransportCharacteristic")).toBeNull();
    expect(screen.queryByText("MasterData:NewContactPerson")).toBeNull();
    /* Bank accounts, external ids, transport characteristics, contact persons. */
    expect(document.querySelectorAll("[data-slot=mock-lines-grid][data-readonly=true]")).toHaveLength(4);
    for (const label of ["MasterData:DebtorNumber", "MasterData:CreditorNumber", "MasterData:CarrierCode", "MasterData:Currency", "MasterData:PreferredCulture"]) {
      expect(field(editor, label), label).toBeDisabled();
    }
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

describe("RelationEditor — Administration tab fields (PO 2026-10-13)", () => {
  const currencyCalls = () => GET.mock.calls.filter(([path]) => path === "/api/master-data/currency");
  const currency = () => field(editorDialog(), "MasterData:Currency") as unknown as HTMLSelectElement;
  const shownCurrency = () => currency().selectedOptions[0]?.textContent;
  const administrationTab = () => screen.getByRole("tab", { name: /MasterData:Administration/ });
  const externalRegion = () => region("MasterData:ExternalIdentifiers");
  const externalLines = () => Array.from(externalRegion().querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
  const addExternal = () => fireEvent.click(within(externalRegion()).getByText("MasterData:AddExternalIdentifier"));
  type ExternalColumn = "MasterData:ExternalIdentifier" | "MasterData:ExternalSource";
  const externalInput = (line: HTMLElement, column: ExternalColumn) => within(line).getByLabelText(column) as HTMLInputElement;
  const setExternal = (line: HTMLElement, column: ExternalColumn, value: string) => fireEvent.change(externalInput(line, column), { target: { value } });
  const openEdit = async () => {
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
  };

  describe("currency", () => {
    it("a new relation starts with the tenant's default currency: the Currency field shows EUR and the POST carries its id", async () => {
      defaultCurrencyRow = EUR_DEFAULT;
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      /* The default is the first currency row sorted isDefault desc. */
      expect(currencyCalls()).toHaveLength(1);
      expect(currencyCalls()[0][1]!.params!.query).toEqual({ StartRow: 0, EndRow: 1, SortBy: "isDefault", SortDir: "desc" });
      expect(currency().value).toBe("cur-eur");
      await waitFor(() => expect(shownCurrency()).toBe("EUR Euro"));

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody()).toEqual({ ...emptyBody, currencyId: "cur-eur" });
    });

    it("without a default currency the Currency field stays empty and currencyId null is posted", async () => {
      defaultCurrencyRow = null;
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      await waitFor(() => expect(currency().options).toHaveLength(3));
      expect(currency().value).toBe("");
      expect(shownCurrency()).toBe("—");

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().currencyId).toBeNull();
      expect(postedBody()).toEqual(emptyBody);
    });

    it("a first currency row that is not the default counts as no default; a currency picked by hand is posted", async () => {
      defaultCurrencyRow = { id: "cur-usd", isoCode: "USD", isDefault: false };
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      await waitFor(() => expect(currency().options).toHaveLength(3));
      expect(currency().value).toBe("");

      fireEvent.change(currency(), { target: { value: "cur-usd" } });
      expect(shownCurrency()).toBe("USD US dollar");
      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().currencyId).toBe("cur-usd");
    });

    it("a loaded record keeps its own currency whatever the tenant's default, and sends it with the update", async () => {
      defaultCurrencyRow = EUR_DEFAULT;
      detail = { ...stored, currencyId: "cur-usd" };
      renderEditor({ mode: "edit", id: "r1" });
      await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
      await waitForLookups();
      tab("MasterData:Administration");
      await waitFor(() => expect(shownCurrency()).toBe("USD US dollar"));
      expect(currency().value).toBe("cur-usd");
      typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().currencyId).toBe("cur-usd");
    });

    it("a loaded record without a currency is not given the tenant's default", async () => {
      defaultCurrencyRow = EUR_DEFAULT;
      detail = { ...stored, currencyId: null };
      await openEdit();
      tab("MasterData:Administration");
      await waitFor(() => expect(currency().options).toHaveLength(3));
      expect(currency().value).toBe("");
      typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().currencyId).toBeNull();
    });

    it("the default currency is fetched once the editor is open, not while closed", async () => {
      defaultCurrencyRow = EUR_DEFAULT;
      const { unmount } = renderEditor({ open: false });
      await Promise.resolve();
      expect(currencyCalls()).toHaveLength(0);
      unmount();
      renderEditor();
      await waitFor(() => expect(currencyCalls()).toHaveLength(1));
    });
  });

  describe("debtor number, creditor number, carrier code", () => {
    it("debtor and creditor numbers are upper-cased while typing, the carrier code is not; all three are posted trimmed", async () => {
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      const company = region("MasterData:Company");
      typeInto(company, "MasterData:DebtorNumber", " deb-001 ");
      typeInto(company, "MasterData:CreditorNumber", " cr-9 ");
      typeInto(company, "MasterData:CarrierCode", " acme-nl ");
      expect(field(company, "MasterData:DebtorNumber").value).toBe(" DEB-001 ");
      expect(field(company, "MasterData:CreditorNumber").value).toBe(" CR-9 ");
      expect(field(company, "MasterData:CarrierCode").value).toBe(" acme-nl ");
      expect(field(company, "MasterData:DebtorNumber").maxLength).toBe(32);
      expect(field(company, "MasterData:CreditorNumber").maxLength).toBe(32);
      expect(field(company, "MasterData:CarrierCode").maxLength).toBe(32);

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody()).toEqual({ ...emptyBody, debtorNumber: "DEB-001", creditorNumber: "CR-9", carrierCode: "acme-nl" });
    });

    it("empty or whitespace-only debtor, creditor and carrier code are posted as null; none is required", async () => {
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      const company = region("MasterData:Company");
      typeInto(company, "MasterData:DebtorNumber", "   ");
      typeInto(company, "MasterData:CreditorNumber", " ");
      typeInto(company, "MasterData:CarrierCode", "  ");
      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody()).toMatchObject({ debtorNumber: null, creditorNumber: null, carrierCode: null });
      expect(administrationTab().className).not.toContain("text-destructive");
    });

    it("a loaded record shows its debtor, creditor and carrier code and sends them, changed or cleared, with the update", async () => {
      detail = { ...stored, debtorNumber: "D-100", creditorNumber: "C-200", carrierCode: "Carr1" };
      await openEdit();
      tab("MasterData:Administration");
      const company = region("MasterData:Company");
      expect(field(company, "MasterData:DebtorNumber").value).toBe("D-100");
      expect(field(company, "MasterData:CreditorNumber").value).toBe("C-200");
      expect(field(company, "MasterData:CarrierCode").value).toBe("Carr1");

      typeInto(company, "MasterData:DebtorNumber", " d-101 ");
      typeInto(company, "MasterData:CarrierCode", "");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody()).toMatchObject({ debtorNumber: "D-101", creditorNumber: "C-200", carrierCode: null, concurrencyStamp: "stamp-1" });
    });
  });

  describe("external identification numbers", () => {
    it("adding lines and filling Id and Source posts { id: null, source, identifier } trimmed, in line order; a removed line is not posted", async () => {
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      expect(externalLines()).toHaveLength(0);
      addExternal();
      addExternal();
      addExternal();
      expect(externalLines()).toHaveLength(3);
      setExternal(externalLines()[0], "MasterData:ExternalIdentifier", " 8712345000001 ");
      setExternal(externalLines()[0], "MasterData:ExternalSource", " GLN ");
      setExternal(externalLines()[1], "MasterData:ExternalIdentifier", "dropped");
      setExternal(externalLines()[1], "MasterData:ExternalSource", "X");
      setExternal(externalLines()[2], "MasterData:ExternalIdentifier", "ACME01");
      setExternal(externalLines()[2], "MasterData:ExternalSource", "EDI");
      fireEvent.click(within(externalLines()[1]).getByText("Remove"));
      expect(externalLines()).toHaveLength(2);

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().externalIdentifiers).toEqual([
        { id: null, source: "GLN", identifier: "8712345000001" },
        { id: null, source: "EDI", identifier: "ACME01" },
      ]);
    });

    it("a line missing its Id or Source (empty or whitespace) is refused with ExternalIdentifierIncomplete and the Administration tab marked; completing it posts", async () => {
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      addExternal();

      /* An empty line: refused, not silently dropped. */
      await submitEditor();
      expect(POST).not.toHaveBeenCalled();
      expect(within(externalRegion()).getByText("MasterData:ExternalIdentifierIncomplete")).toBeInTheDocument();
      expect(administrationTab().className).toContain("text-destructive");
      expect(generalTab().className).not.toContain("text-destructive");

      /* Only the Id: still refused. */
      setExternal(externalLines()[0], "MasterData:ExternalIdentifier", "8712345000001");
      await submitEditor();
      expect(POST).not.toHaveBeenCalled();
      expect(screen.getByText("MasterData:ExternalIdentifierIncomplete")).toBeInTheDocument();

      /* A whitespace-only Source is no Source. */
      setExternal(externalLines()[0], "MasterData:ExternalSource", "   ");
      await submitEditor();
      expect(POST).not.toHaveBeenCalled();
      expect(screen.getByText("MasterData:ExternalIdentifierIncomplete")).toBeInTheDocument();
      expect(administrationTab().className).toContain("text-destructive");

      /* Only the Source: refused as well. */
      setExternal(externalLines()[0], "MasterData:ExternalIdentifier", "");
      setExternal(externalLines()[0], "MasterData:ExternalSource", "GLN");
      await submitEditor();
      expect(POST).not.toHaveBeenCalled();
      expect(screen.getByText("MasterData:ExternalIdentifierIncomplete")).toBeInTheDocument();

      /* Both filled: the error and the tab mark go, the line is posted. */
      setExternal(externalLines()[0], "MasterData:ExternalIdentifier", "8712345000001");
      await waitFor(() => expect(screen.queryByText("MasterData:ExternalIdentifierIncomplete")).toBeNull());
      expect(administrationTab().className).not.toContain("text-destructive");
      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().externalIdentifiers).toEqual([{ id: null, source: "GLN", identifier: "8712345000001" }]);
    });

    it("a loaded record shows its lines and puts them with their ids; an edited line keeps its id, a removed one is dropped, a new one has none", async () => {
      detail = {
        ...stored,
        externalIdentifiers: [
          { id: "x1", source: "GLN", identifier: "8712345000001" },
          { id: "x2", source: "EDI", identifier: "ACME01" },
        ],
      };
      await openEdit();
      tab("MasterData:Administration");
      expect(externalLines()).toHaveLength(2);
      expect(externalInput(externalLines()[0], "MasterData:ExternalIdentifier")).toHaveValue("8712345000001");
      expect(externalInput(externalLines()[0], "MasterData:ExternalSource")).toHaveValue("GLN");
      expect(externalInput(externalLines()[1], "MasterData:ExternalIdentifier")).toHaveValue("ACME01");
      expect(externalInput(externalLines()[1], "MasterData:ExternalSource")).toHaveValue("EDI");

      /* Untouched, they travel with their ids. */
      typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().externalIdentifiers).toEqual([
        { id: "x1", source: "GLN", identifier: "8712345000001" },
        { id: "x2", source: "EDI", identifier: "ACME01" },
      ]);

      PUT.mockClear();
      fireEvent.click(within(externalLines()[0]).getByText("Remove"));
      setExternal(externalLines()[0], "MasterData:ExternalIdentifier", " ACME02 ");
      addExternal();
      setExternal(externalLines()[1], "MasterData:ExternalIdentifier", "4711");
      setExternal(externalLines()[1], "MasterData:ExternalSource", "Portal");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().externalIdentifiers).toEqual([
        { id: "x2", source: "EDI", identifier: "ACME02" },
        { id: null, source: "Portal", identifier: "4711" },
      ]);
    });
  });

  describe("transport characteristics", () => {
    const LOOKUP_PAGE = "/api/master-data/transport-characteristics/lookup-page";
    const pageCalls = () => GET.mock.calls.filter(([path]) => path === LOOKUP_PAGE);
    const pageQueries = () => pageCalls().map(([, init]) => init!.params!.query);
    const characteristicsRegion = () => region("MasterData:TransportCharacteristics");
    const lookup = () => within(characteristicsRegion()).getByLabelText("MasterData:AddTransportCharacteristic", { selector: "input" }) as HTMLInputElement;
    const queryLookup = () => within(characteristicsRegion()).queryByLabelText("MasterData:AddTransportCharacteristic", { selector: "input" });
    const characteristicLines = () => Array.from(characteristicsRegion().querySelectorAll<HTMLElement>("[data-slot=mock-line]"));
    /* Each read-only line as [name, code, description]. */
    const lineTexts = () =>
      characteristicLines().map((line) => ["name", "code", "description"].map((f) => line.querySelector(`[data-field=${f}]`)?.textContent ?? null));
    const openPopup = (element: HTMLElement) => {
      fireEvent.pointerDown(element);
      fireEvent.mouseDown(element);
      fireEvent.click(element);
    };
    const typeTerm = (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value }, inputType: "insertText" });
    const option = (name: string) => screen.findByRole("option", { name: new RegExp(`^${escape(name)}`) });
    /* Opens the lookup, picks the option by its name and waits for the line. */
    const pick = async (name: string) => {
      const before = characteristicLines().length;
      openPopup(lookup());
      fireEvent.click(await option(name));
      await waitFor(() => expect(screen.queryByRole("listbox")).toBeNull());
      return before;
    };
    const DANGEROUS = ["Dangerous goods", "ADR", "ADR load"];
    const REFRIGERATED = ["Refrigerated", "FRIG", ""];
    const startNew = async () => {
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
    };

    it("opening the lookup asks the first page with no term; picking adds a read-only line with name, code and description; the lookup stays empty; the ids are posted", async () => {
      await startNew();
      expect(pageCalls()).toHaveLength(0);
      openPopup(lookup());
      await waitFor(() => expect(GET).toHaveBeenCalledWith(LOOKUP_PAGE, { params: { query: { term: undefined, skip: 0, take: 25 } } }));
      /* The option shows the name with code and description below it. */
      expect(await option("Dangerous goods")).toHaveTextContent("ADR, ADR load");
      fireEvent.click(await option("Dangerous goods"));
      await waitFor(() => expect(lineTexts()).toEqual([DANGEROUS]));
      expect(lookup().value).toBe("");

      await pick("Refrigerated");
      await waitFor(() => expect(lineTexts()).toEqual([DANGEROUS, REFRIGERATED]));
      expect(lookup().value).toBe("");
      /* The lines are read-only: no control in them, only the Remove row action. */
      for (const line of characteristicLines()) {
        expect(line.querySelectorAll("input, select, textarea")).toHaveLength(0);
        expect(within(line).getByText("Remove")).toBeInTheDocument();
      }

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody()).toEqual({ ...emptyBody, transportCharacteristicIds: ["tc-1", "tc-2"] });
      /* The capped rows endpoint of the old picker is never asked. */
      expect(GET).not.toHaveBeenCalledWith("/api/master-data/transport-characteristics", expect.anything());
    }, LONG_FLOW);

    it("picking a characteristic that is already a line changes nothing", async () => {
      await startNew();
      await pick("Dangerous goods");
      await waitFor(() => expect(lineTexts()).toEqual([DANGEROUS]));
      await pick("Dangerous goods");
      await act(async () => {
        await Promise.resolve();
      });
      expect(lineTexts()).toEqual([DANGEROUS]);

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().transportCharacteristicIds).toEqual(["tc-1"]);
    }, LONG_FLOW);

    it("removing a line drops it from the POST", async () => {
      await startNew();
      await pick("Dangerous goods");
      await pick("Refrigerated");
      await waitFor(() => expect(lineTexts()).toEqual([DANGEROUS, REFRIGERATED]));
      fireEvent.click(within(characteristicLines()[0]).getByText("Remove"));
      expect(lineTexts()).toEqual([REFRIGERATED]);

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().transportCharacteristicIds).toEqual(["tc-2"]);
    }, LONG_FLOW);

    it("the lookup searches the server with the typed term after the 250 ms debounce, from 2 characters, with skip and take", async () => {
      await startNew();
      const input = lookup();
      openPopup(input);
      await waitFor(() => expect(pageCalls()).toHaveLength(1));

      /* One character: no request for it, the hint to type more. */
      typeTerm(input, "f");
      expect(await screen.findByText("TypeToSearch")).toBeInTheDocument();
      await act(() => new Promise((resolve) => setTimeout(resolve, 300)));
      expect(pageQueries().some((q) => q?.term === "f")).toBe(false);

      /* Two characters: not at once, but after the debounce. */
      typeTerm(input, "fr");
      expect(pageQueries().some((q) => q?.term === "fr")).toBe(false);
      await waitFor(() => expect(GET).toHaveBeenCalledWith(LOOKUP_PAGE, { params: { query: { term: "fr", skip: 0, take: 25 } } }));
      expect(await option("Refrigerated")).toBeInTheDocument();
      await waitFor(() => expect(screen.queryByRole("option", { name: /^Dangerous goods/ })).toBeNull());
      fireEvent.click(await option("Refrigerated"));
      await waitFor(() => expect(lineTexts()).toEqual([REFRIGERATED]));
      expect(lookup().value).toBe("");
    }, LONG_FLOW);

    it("a loaded record shows its characteristics from the detail DTO (name, code, description) and puts their ids; a removed one is dropped", async () => {
      detail = {
        ...stored,
        transportCharacteristicIds: ["tc-2", "tc-1"],
        transportCharacteristics: [
          { id: "tc-2", code: "FRIG", name: "Refrigerated", description: null },
          { id: "tc-1", code: "ADR", name: "Dangerous goods", description: "ADR load" },
        ],
      };
      await openEdit();
      tab("MasterData:Administration");
      expect(lineTexts()).toEqual([REFRIGERATED, DANGEROUS]);
      /* Shown from the DTO itself: no lookup request was needed. */
      expect(pageCalls()).toHaveLength(0);

      typeInto(editorDialog(), "MasterData:Name", "Acme Transport");
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().transportCharacteristicIds).toEqual(["tc-2", "tc-1"]);

      PUT.mockClear();
      fireEvent.click(within(characteristicLines()[0]).getByText("Remove"));
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().transportCharacteristicIds).toEqual(["tc-1"]);
    });

    it("hides Add new transport characteristic… without MasterData.TransportCharacteristics.Create", async () => {
      await startNew();
      openPopup(lookup());
      expect(await option("Dangerous goods")).toBeInTheDocument();
      expect(screen.queryByText("MasterData:AddNewTransportCharacteristics")).toBeNull();
    }, LONG_FLOW);

    it.each(["Apps.MasterData.General.Fleet.TransportCharacteristics", "Apps.MasterData.General.Fleet"])("hides Add new transport characteristic… with Create when %s is off", async (off) => {
      grants = { ...grants, "MasterData.TransportCharacteristics.Create": true };
      features = { ...features, [off]: "false" };
      await startNew();
      openPopup(lookup());
      expect(await option("Dangerous goods")).toBeInTheDocument();
      expect(screen.queryByText("MasterData:AddNewTransportCharacteristics")).toBeNull();
    }, LONG_FLOW);

    it("with Create, Add new… opens the characteristics editor with the typed term as name; its save appends the new characteristic as a line, which is posted", async () => {
      grants = { ...grants, "MasterData.TransportCharacteristics.Create": true };
      POST.mockImplementation(async (path) =>
        path === "/api/master-data/transport-characteristics" ? ok({ id: "tc-new", code: "COOL", name: "Cooled", description: null }) : ok({ ...stored, id: "r-new" }),
      );
      await startNew();
      const input = lookup();
      openPopup(input);
      typeTerm(input, "Cooled");
      fireEvent.click(await screen.findByText("MasterData:AddNewTransportCharacteristics"));

      await waitFor(() => expect(dialogs()).toHaveLength(2));
      const nested = childDialog();
      expect(field(nested, "MasterData:Name").value).toBe("Cooled");
      typeInto(nested, "MasterData:Code", "cool");
      await act(async () => {
        fireEvent.submit(nested.querySelector("form")!);
        await Promise.resolve();
      });
      await waitFor(() =>
        expect(POST).toHaveBeenCalledWith("/api/master-data/transport-characteristics", { body: { code: "COOL", name: "Cooled", description: null } }),
      );
      await waitFor(() => expect(dialogs()).toHaveLength(1));
      await waitFor(() => expect(lineTexts()).toEqual([["Cooled", "COOL", ""]]));

      await submitEditor();
      await waitFor(() => expect(POST.mock.calls.filter(([path]) => path === "/api/master-data/relation")).toHaveLength(1));
      expect(postedBody().transportCharacteristicIds).toEqual(["tc-new"]);
    }, LONG_FLOW);

    it("read-only: the loaded lines show without the lookup or the Remove action", async () => {
      detail = {
        ...stored,
        transportCharacteristicIds: ["tc-1"],
        transportCharacteristics: [{ id: "tc-1", code: "ADR", name: "Dangerous goods", description: "ADR load" }],
      };
      renderEditor({ mode: "edit", id: "r1", readOnly: true });
      await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
      await waitForLookups();
      tab("MasterData:Administration");
      expect(lineTexts()).toEqual([DANGEROUS]);
      expect(queryLookup()).toBeNull();
      expect(within(characteristicsRegion()).queryByText("Remove")).toBeNull();
      expect(pageCalls()).toHaveLength(0);
    });
  });

  describe("department", () => {
    const withTwoUnits = () => {
      const base = GET.getMockImplementation()!;
      GET.mockImplementation(async (path, init) =>
        path === "/api/platform/administration/organization-units/all" ? ok([{ id: "ou-1", displayName: "Unit 1" }, { id: "ou-2", displayName: "Unit 2" }]) : base(path, init),
      );
    };

    it("Department is the organisation-units MultiSelect in the Company block; the ticked units are posted as organizationUnitIds, an unticked one is not", async () => {
      withTwoUnits();
      renderEditor();
      await waitForLookups();
      fillRequired();
      tab("MasterData:Administration");
      const company = region("MasterData:Company");
      const department = multiSelect(company, "MasterData:Department");
      expect(department).toHaveAttribute("data-slot", "mock-multi-select");
      const unit = (name: string) => within(department).getByRole("checkbox", { name });
      await waitFor(() => expect(unit("Unit 2")).toBeInTheDocument());
      expect(unit("Unit 1")).not.toBeChecked();
      fireEvent.click(unit("Unit 2"));
      fireEvent.click(unit("Unit 1"));
      fireEvent.click(unit("Unit 2"));
      fireEvent.click(unit("Unit 2"));
      expect(unit("Unit 1")).toBeChecked();
      expect(unit("Unit 2")).toBeChecked();

      await submitEditor();
      await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
      expect(postedBody().organizationUnitIds).toEqual(["ou-1", "ou-2"]);
      /* There is no separate organisation-units field any more: Department is the one. */
      expect(within(editorDialog()).queryByText(/^MasterData:OrganizationUnits/, { selector: "label" })).toBeNull();
    });

    it("a loaded record shows its units under Department and sends the changed selection with the update", async () => {
      withTwoUnits();
      await openEdit();
      tab("MasterData:Administration");
      const department = multiSelect(region("MasterData:Company"), "MasterData:Department");
      const unit = (name: string) => within(department).getByRole("checkbox", { name });
      await waitFor(() => expect(unit("Unit 2")).toBeInTheDocument());
      expect(unit("Unit 1")).toBeChecked();
      expect(unit("Unit 2")).not.toBeChecked();
      fireEvent.click(unit("Unit 1"));
      fireEvent.click(unit("Unit 2"));
      await submitEditor();
      await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
      expect(putBody().organizationUnitIds).toEqual(["ou-2"]);
    });
  });
});

describe("RelationEditor — default country (PO 2026-10-13)", () => {
  const defaultCalls = () => GET.mock.calls.filter(([path]) => path === "/api/master-data/country/default");
  const waitForPrefill = (editor: HTMLElement) => waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-1"));

  it("a new relation starts its default address with the tenant's default country and its time zone, untouched: no required markers, nothing posted without further input", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    expect(defaultCalls()).toHaveLength(1);
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    for (const label of ADDRESS_LABELS.filter((l) => l !== "MasterData:Country" && l !== "MasterData:TimeZone")) {
      expect(field(editor, label).value, label).toBe("");
    }
    /* The prefill is no input: no markers, no errors. */
    expect(addressLabel("MasterData:Country")).not.toHaveTextContent("*");
    expect(addressLabel("MasterData:Street")).not.toHaveTextContent("*");
    expect(addressLabel("MasterData:City")).not.toHaveTextContent("*");

    fillRequired();
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();
    expect(postedBody()).toEqual(emptyBody);
  });

  it("a new relation with only the prefill closes without the dirty guard: nothing was entered (UI-FORM-009)", async () => {
    defaultCountry = NL_DEFAULT;
    const { onClose } = renderEditor();
    await waitForLookups();
    await waitFor(() => expect(defaultCalls()).toHaveLength(1));
    /* The ribbon Close (the header X is a Close too). */
    fireEvent.click(within(editorDialog().querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getByRole("button", { name: "Close" }));
    expect(screen.queryByText("UnsavedChanges")).toBeNull();
    expect(screen.queryByRole("alertdialog")).toBeNull();
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("entering the rest of the default address posts it with the default country and its zone, without an address type", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fillRequired();
    typeInto(editor, "MasterData:PostalCode", "2611 AB");
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:Street", "Station street");
    /* Now it is an address: the country marker is on and already satisfied. */
    expect(addressLabel("MasterData:Country")).toHaveTextContent("*");
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    /* No country was picked by hand: the per-country detail was never needed. */
    expect(GET).not.toHaveBeenCalledWith("/api/master-data/country/{id}", expect.anything());
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: null, countryId: "c-1", street: "Station street", houseNumber: null, houseNumberSuffix: null, addressLine2: null, postalCode: "2611 AB", city: "Delft", latitude: null, longitude: null, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("an address-type tab's empty row is prefilled the same way; untouched tabs post nothing, a filled one posts the default country with its type", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fillRequired();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(field(editor, "MasterData:Street").value).toBe("");
    expect(addressLabel("MasterData:Street")).not.toHaveTextContent("*");
    typeTab("Postal address");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(addressLabel("MasterData:Street")).not.toHaveTextContent("*");

    /* Both tabs prefilled only: nothing travels. */
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("MasterData:AddressIncomplete")).toBeNull();
    expect(postedBody().addresses).toEqual([]);

    /* Filling the Postal tab posts it with its type and the default country; the Visiting tab still posts nothing. */
    POST.mockClear();
    typeInto(editor, "MasterData:PostalCode", "2600 AA");
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:Street", "PO Box");
    typeInto(editor, "MasterData:Nr", "99");
    typeTab("Visiting address");
    expect(field(editor, "MasterData:Street").value).toBe("");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: "type-2", countryId: "c-1", street: "PO Box", houseNumber: "99", houseNumberSuffix: null, addressLine2: null, postalCode: "2600 AA", city: "Delft", latitude: null, longitude: null, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("picking another country over the prefill lets the time zone follow it: the prefilled zone was the default country's, not the user's", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-2" } });
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/country/{id}", { params: { path: { id: "c-2" } } }));
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin"));
  });

  it("clearing the prefilled country leaves it empty (no re-prefill) and the address stays no address", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fillRequired();
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "" } });
    expect(field(editor, "MasterData:Country").value).toBe("");
    expect(screen.queryByText("MasterData:CountryRequired")).toBeNull();
    /* The same on a type tab: a cleared country is kept as cleared. */
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "" } });
    typeTab("Postal address");
    typeTab("Visiting address");
    expect(field(editor, "MasterData:Country").value).toBe("");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    expect(field(editor, "MasterData:Country").value).toBe("");

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([]);
  });

  it("a loaded record is not overwritten: its default address keeps its own country and zone; an empty type tab is prefilled but not posted", async () => {
    defaultCountry = NL_DEFAULT;
    detail = { ...stored, addresses: [defaultAddress, visitingAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    await waitFor(() => expect(defaultCalls()).toHaveLength(1));
    const editor = editorDialog();
    expect(checkbox("MasterData:AllAddressesEqual")).toBeChecked();
    expect(field(editor, "MasterData:Country").value).toBe("c-2");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin");
    expect(field(editor, "MasterData:City").value).toBe("Berlin");

    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:Street").value).toBe("Main street");
    typeTab("Postal address");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(field(editor, "MasterData:Street").value).toBe("");
    expect(addressLabel("MasterData:Street")).not.toHaveTextContent("*");

    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses).toEqual([loadedAddress(visitingAddress)]);
  });

  it("a loaded record with only a typed address keeps its hidden default address empty when the switch is turned on in edit mode", async () => {
    defaultCountry = NL_DEFAULT;
    detail = { ...stored, addresses: [visitingAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    await waitForLookups();
    await waitFor(() => expect(defaultCalls()).toHaveLength(1));
    const editor = editorDialog();
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:Street").value).toBe("Main street");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    /* Edit mode: the stored data rules; the default address was never prefilled. */
    expect(field(editor, "MasterData:Country").value).toBe("");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    await submitEditor();
    await waitFor(() => expect(PUT).toHaveBeenCalledTimes(1));
    expect(putBody().addresses).toEqual([]);
  });

  it("without a default country (204) the fields stay empty on the default address and on every type tab", async () => {
    defaultCountry = null;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitFor(() => expect(defaultCalls()).toHaveLength(1));
    expect(field(editor, "MasterData:Country").value).toBe("");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Country").value).toBe("");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    typeTab("Postal address");
    expect(field(editor, "MasterData:Country").value).toBe("");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    fillRequired();
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([]);
  });

  it("a default country without a time zone prefills the country only", async () => {
    defaultCountry = { ...NL_DEFAULT, primaryTimeZoneId: null as unknown as string };
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("");
  });

  it("the default country is fetched once the editor is open, not while closed", async () => {
    defaultCountry = NL_DEFAULT;
    const { unmount } = renderEditor({ open: false });
    await Promise.resolve();
    expect(defaultCalls()).toHaveLength(0);
    unmount();
    renderEditor();
    await waitFor(() => expect(defaultCalls()).toHaveLength(1));
  });
});

describe("RelationEditor — default country, warm cache", () => {
  it("a second open on the same query client (the default country cached) starts with the default country and zone", async () => {
    defaultCountry = NL_DEFAULT;
    const client = newClient();
    const first = renderEditor({}, client);
    await waitForLookups();
    await waitFor(() => expect(GET.mock.calls.filter(([path]) => path === "/api/master-data/country/default")).toHaveLength(1));
    first.unmount();

    renderEditor({}, client);
    await waitForLookups();
    const editor = editorDialog();
    await waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-1"));
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    /* Untouched: the ribbon Close leaves without the dirty guard. */
    fireEvent.click(within(editor.querySelector<HTMLElement>("[data-slot=entity-dialog-ribbon]")!).getByRole("button", { name: "Close" }));
    expect(screen.queryByText("UnsavedChanges")).toBeNull();
  });

  it("warm cache: picking another country over the prefilled one lets the time zone follow it (the prefilled zone was the default country's, not the user's)", async () => {
    defaultCountry = NL_DEFAULT;
    const client = newClient();
    const first = renderEditor({}, client);
    await waitForLookups();
    await waitFor(() => expect(GET.mock.calls.filter(([path]) => path === "/api/master-data/country/default")).toHaveLength(1));
    first.unmount();

    renderEditor({}, client);
    await waitForLookups();
    const editor = editorDialog();
    await waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-1"));
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-2" } });
    await waitFor(() => expect(GET).toHaveBeenCalledWith("/api/master-data/country/{id}", { params: { path: { id: "c-2" } } }));
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin"));
  });
});

describe("RelationEditor — postal-code lookup (design 44)", () => {
  const LOOKUP_PATH = "/api/integrations/address-lookup";
  const lookupCalls = () => GET.mock.calls.filter(([path]) => path === LOOKUP_PATH);
  const lookupQuery = (index = 0) => lookupCalls()[index][1]!.params!.query;
  const waitForPrefill = (editor: HTMLElement) => waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-1"));
  /* Both halves of the gate: the tenant's feature and the user's permission (design 44: "only while the feature is on for the tenant and the user holds the permission"). */
  const featureOn = () => {
    features = { ...features, "Integrations.AddressLookup": "true" };
  };
  const permissionOn = () => {
    grants = { ...grants, "Integrations.AddressLookup": true };
  };
  const blur = (editor: HTMLElement, label: string) => fireEvent.blur(field(editor, label));
  const filled = (editor: HTMLElement) => ({
    street: field(editor, "MasterData:Street").value,
    houseNumber: field(editor, "MasterData:Nr").value,
    city: field(editor, "MasterData:City").value,
    latitude: field(editor, "MasterData:Latitude").value,
    longitude: field(editor, "MasterData:Longitude").value,
  });
  const EMPTY = { street: "", houseNumber: "", city: "", latitude: "", longitude: "" };
  const FILLED = { street: "Dam", houseNumber: "1", city: "Amsterdam", latitude: "52.3731", longitude: "4.8932" };
  /* Lets a pending lookup (or its absence) settle inside act before asserting that nothing happened. */
  const settle = () => act(async () => { await Promise.resolve(); await Promise.resolve(); });

  it("feature on and permission held: blurring the postal code of the default address asks GET /api/integrations/address-lookup with the country's alpha-2 code and fills the empty city, street, house number and coordinates; the map shows the place; the fill is posted", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    expect(mapFrame()).toBeNull();
    /* Before the postal code is typed: once its suggestions dropdown (same gate) opens, the rest of the dialog is hidden from role queries. */
    fillRequired();

    typeInto(editor, "MasterData:PostalCode", "1012AB");
    expect(lookupCalls()).toHaveLength(0);
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    expect(lookupQuery()).toEqual({ countryCode: "NL", postalCode: "1012AB", houseNumber: undefined });
    expect(Object.values(lookupQuery()!)).not.toContain("null");
    await waitFor(() => expect(field(editor, "MasterData:City").value).toBe("Amsterdam"));
    expect(filled(editor)).toEqual(FILLED);
    expect(field(editor, "MasterData:PostalCode").value).toBe("1012AB");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("");
    expect(field(editor, "MasterData:Building").value).toBe("");
    await waitFor(() => expect(mapFrame()).not.toBeNull());
    expect(screen.queryByText("MasterData:CityRequired")).toBeNull();
    expect(screen.queryByText("MasterData:StreetRequired")).toBeNull();

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: null, countryId: "c-1", street: "Dam", houseNumber: "1", houseNumberSuffix: null, addressLine2: null, postalCode: "1012AB", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("a typed house number travels with the request on its own blur; what the user typed (city, house number) stays, the rest is filled", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:PostalCode", " 1012AB ");
    typeInto(editor, "MasterData:Nr", "7");
    blur(editor, "MasterData:Nr");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    expect(lookupQuery()).toEqual({ countryCode: "NL", postalCode: " 1012AB ", houseNumber: "7" });
    await waitFor(() => expect(field(editor, "MasterData:Street").value).toBe("Dam"));
    expect(filled(editor)).toEqual({ street: "Dam", houseNumber: "7", city: "Delft", latitude: "52.3731", longitude: "4.8932" });
  }, LONG_FLOW);

  it("another country sends its own alpha-2 code from the country lookup", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-2" } });
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin"));
    typeInto(editor, "MasterData:PostalCode", "10115");
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    expect(lookupQuery()).toEqual({ countryCode: "DE", postalCode: "10115", houseNumber: undefined });
  });

  it("feature off: no request and nothing filled, even with the permission", async () => {
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:PostalCode", "1012AB");
    typeInto(editor, "MasterData:Nr", "1");
    blur(editor, "MasterData:PostalCode");
    blur(editor, "MasterData:Nr");
    await settle();
    expect(lookupCalls()).toHaveLength(0);
    expect(filled(editor)).toEqual({ ...EMPTY, houseNumber: "1" });
    expect(addressBlock()).not.toHaveAttribute("aria-busy");
  });

  it("permission missing: no request and nothing filled, even with the feature on (UI-PERM-001: hidden, never a refused call)", async () => {
    featureOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:PostalCode", "1012AB");
    blur(editor, "MasterData:PostalCode");
    blur(editor, "MasterData:Nr");
    await settle();
    expect(lookupCalls()).toHaveLength(0);
    expect(filled(editor)).toEqual(EMPTY);
    expect(addressBlock()).not.toHaveAttribute("aria-busy");
  });

  it("neither the feature nor the permission: no request", async () => {
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:PostalCode", "1012AB");
    blur(editor, "MasterData:PostalCode");
    await settle();
    expect(lookupCalls()).toHaveLength(0);
    expect(filled(editor)).toEqual(EMPTY);
  });

  it("the address-type tabs ask the same lookup: the Visiting tab's postal code fills its own fields and the fill is posted with the type", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fillRequired();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:PostalCode", "1012AB");
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    expect(lookupQuery()).toEqual({ countryCode: "NL", postalCode: "1012AB", houseNumber: undefined });
    await waitFor(() => expect(field(editor, "MasterData:City").value).toBe("Amsterdam"));
    expect(filled(editor)).toEqual(FILLED);
    /* The other tab is untouched. */
    typeTab("Postal address");
    expect(filled(editor)).toEqual(EMPTY);
    typeTab("Visiting address");
    expect(filled(editor)).toEqual(FILLED);

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: "type-1", countryId: "c-1", street: "Dam", houseNumber: "1", houseNumberSuffix: null, addressLine2: null, postalCode: "1012AB", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("a read-only editor never asks, feature and permission notwithstanding", async () => {
    featureOn();
    permissionOn();
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitForLookups();
    const editor = editorDialog();
    await waitForTypeTabs();
    await waitFor(() => expect(field(editor, "MasterData:PostalCode").value).toBe("2611 AA"));
    blur(editor, "MasterData:PostalCode");
    blur(editor, "MasterData:Nr");
    await settle();
    expect(lookupCalls()).toHaveLength(0);
    expect(addressBlock()).not.toHaveAttribute("aria-busy");
  });

  it("a loaded address only gets what it lacks: the postal address without coordinates receives them, its street and city stay", async () => {
    featureOn();
    permissionOn();
    detail = { ...stored, addresses: [visitingAddress, postalAddress] };
    renderEditor({ mode: "edit", id: "r1" });
    await waitForLookups();
    const editor = editorDialog();
    await waitForTypeTabs();
    typeTab("Postal address");
    await waitFor(() => expect(field(editor, "MasterData:PostalCode").value).toBe("2600 AA"));
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    expect(lookupQuery()).toEqual({ countryCode: "NL", postalCode: "2600 AA", houseNumber: "99" });
    await waitFor(() => expect(field(editor, "MasterData:Latitude").value).toBe("52.3731"));
    expect(filled(editor)).toEqual({ street: "PO Box", houseNumber: "99", city: "Delft", latitude: "52.3731", longitude: "4.8932" });
    /* The visiting address, complete, is untouched. */
    typeTab("Visiting address");
    expect(filled(editor)).toEqual({ street: "Main street", houseNumber: "1", city: "Delft", latitude: "52.01", longitude: "4.36" });
  });

  it("nothing found: the request is made, nothing is filled, nothing is reported", async () => {
    featureOn();
    permissionOn();
    lookupResult = { found: false, provider: "PDOK" };
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    typeInto(editor, "MasterData:PostalCode", "9999ZZ");
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    await waitFor(() => expect(addressBlock()).not.toHaveAttribute("aria-busy"));
    expect(filled(editor)).toEqual(EMPTY);
    expect(toast.error).not.toHaveBeenCalled();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("a failing request (the server refusing, the provider down) is silent: nothing filled, no toast, no alert, the editor stays usable", async () => {
    featureOn();
    permissionOn();
    lookupFails = true;
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    /* Before the postal code is typed: once its suggestions dropdown (same gate) opens, the rest of the dialog is hidden from role queries. */
    fillRequired();
    typeInto(editor, "MasterData:City", "Delft");
    typeInto(editor, "MasterData:PostalCode", "1012AB");
    blur(editor, "MasterData:PostalCode");
    await waitFor(() => expect(lookupCalls()).toHaveLength(1));
    await waitFor(() => expect(addressBlock()).not.toHaveAttribute("aria-busy"));
    expect(filled(editor)).toEqual({ ...EMPTY, city: "Delft" });
    expect(toast.error).not.toHaveBeenCalled();
    expect(screen.queryByRole("alert")).toBeNull();
    /* Still usable: the rest can be entered and posted. */
    typeInto(editor, "MasterData:Street", "Dam");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses[0]).toMatchObject({ postalCode: "1012AB", city: "Delft", street: "Dam", latitude: null, longitude: null });
  });
});

describe("RelationEditor — postal-code suggestions (PO 2026-10-13)", () => {
  const SUGGEST_PATH = "/api/integrations/address-lookup/suggestions";
  const suggestCalls = () => GET.mock.calls.filter(([path]) => path === SUGGEST_PATH);
  const suggestQuery = (index = 0) => suggestCalls()[index][1]!.params!.query;
  const waitForPrefill = (editor: HTMLElement) => waitFor(() => expect(field(editor, "MasterData:Country").value).toBe("c-1"));
  const featureOn = () => {
    features = { ...features, "Integrations.AddressLookup": "true" };
  };
  const permissionOn = () => {
    grants = { ...grants, "Integrations.AddressLookup": true };
  };
  const postalCode = (editor: HTMLElement) => field(editor, "MasterData:PostalCode");
  /* Base UI reads `inputType` to tell typing from autofill. */
  const typePostal = (editor: HTMLElement, value: string) => fireEvent.input(postalCode(editor), { target: { value }, inputType: "insertText" });
  const suggestionItems = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=combobox-item][role=option]"));
  const options = () => suggestionItems().map((el) => el.textContent?.trim());
  /* Longer than the 250 ms debounce, in real time, inside act. */
  const pastDebounce = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 400)); });

  it("feature on and permission held: typing 1012 in the default address asks the suggestions with the country's alpha-2 code once; picking a line fills postal code, street, number, suffix, city and coordinates, and they are posted", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    expect(postalCode(editor)).toHaveAttribute("role", "combobox");

    typePostal(editor, "1");
    typePostal(editor, "10");
    typePostal(editor, "101");
    typePostal(editor, "1012");
    expect(postalCode(editor).value).toBe("1012");
    expect(suggestCalls()).toHaveLength(0);
    await waitFor(() => expect(options()).toEqual([SUGGESTED_PC.label, SUGGESTED_ADDRESS.label]));
    expect(suggestCalls()).toHaveLength(1);
    expect(suggestQuery()).toEqual({ countryCode: "NL", term: "1012" });

    fireEvent.click(suggestionItems().find((el) => el.textContent?.trim() === SUGGESTED_ADDRESS.label)!);
    await waitFor(() => expect(field(editor, "MasterData:Street").value).toBe("Dam"));
    expect(postalCode(editor).value).toBe("1012 AB");
    expect(field(editor, "MasterData:Nr").value).toBe("1");
    expect(field(editor, "MasterData:HouseNumberSuffix").value).toBe("A");
    expect(field(editor, "MasterData:City").value).toBe("Amsterdam");
    expect(field(editor, "MasterData:Latitude").value).toBe("52.373");
    expect(field(editor, "MasterData:Longitude").value).toBe("4.893");
    expect(field(editor, "MasterData:Country").value).toBe("c-1");
    expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    await waitFor(() => expect(options()).toEqual([]));
    await pastDebounce();
    expect(suggestCalls()).toHaveLength(1);

    fillRequired();
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: null, countryId: "c-1", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", addressLine2: null, postalCode: "1012 AB", city: "Amsterdam", latitude: 52.373, longitude: 4.893, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  it("another country sends its own alpha-2 code; a line without a label shows its postal code", async () => {
    featureOn();
    permissionOn();
    suggestions = [{ ...SUGGESTED_PC, label: null, postalCode: "10115", street: null, city: "Berlin" }];
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fireEvent.change(field(editor, "MasterData:Country"), { target: { value: "c-2" } });
    await waitFor(() => expect(field(editor, "MasterData:TimeZone").value).toBe("Europe/Berlin"));
    typePostal(editor, "101");
    await waitFor(() => expect(suggestCalls()).toHaveLength(1));
    expect(suggestQuery()).toEqual({ countryCode: "DE", term: "101" });
    await waitFor(() => expect(options()).toEqual(["10115"]));
  });

  it("the address-type tabs get the dropdown too: the Visiting tab's pick is posted with its type", async () => {
    featureOn();
    permissionOn();
    defaultCountry = NL_DEFAULT;
    renderEditor();
    await waitForLookups();
    const editor = editorDialog();
    await waitForPrefill(editor);
    fillRequired();
    fireEvent.click(checkbox("MasterData:AllAddressesEqual"));
    await waitForTypeTabs();
    expect(activeTypeTab()).toBe("Visiting address");
    await waitForPrefill(editor);
    expect(postalCode(editor)).toHaveAttribute("role", "combobox");
    typePostal(editor, "1012");
    await waitFor(() => expect(options()).toEqual([SUGGESTED_PC.label, SUGGESTED_ADDRESS.label]));
    expect(suggestQuery()).toEqual({ countryCode: "NL", term: "1012" });
    fireEvent.click(suggestionItems().find((el) => el.textContent?.trim() === SUGGESTED_PC.label)!);
    await waitFor(() => expect(field(editor, "MasterData:Street").value).toBe("Dam"));
    typeInto(editor, "MasterData:Nr", "5");
    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody().addresses).toEqual([
      { id: null, addressTypeId: "type-1", countryId: "c-1", street: "Dam", houseNumber: "5", houseNumberSuffix: null, addressLine2: null, postalCode: "1012 AB", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932, timeZoneId: "Europe/Amsterdam" },
    ]);
  });

  for (const [name, setup] of [
    ["feature off, permission held", () => permissionOn()],
    ["permission missing, feature on (UI-PERM-001: hidden, never a refused call)", () => featureOn()],
    ["neither the feature nor the permission", () => {}],
  ] as const) {
    it(`${name}: the postal code is a plain textbox and typing asks no suggestions`, async () => {
      setup();
      defaultCountry = NL_DEFAULT;
      renderEditor();
      await waitForLookups();
      const editor = editorDialog();
      await waitForPrefill(editor);
      expect(postalCode(editor)).not.toHaveAttribute("role");
      expect(within(editor).getByRole("textbox", { name: /^MasterData:PostalCode/ })).toBe(postalCode(editor));
      typePostal(editor, "1012");
      expect(postalCode(editor).value).toBe("1012");
      await pastDebounce();
      expect(suggestCalls()).toHaveLength(0);
      expect(options()).toEqual([]);
    });
  }

  it("a read-only editor keeps the plain textbox, feature and permission notwithstanding", async () => {
    featureOn();
    permissionOn();
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitForLookups();
    const editor = editorDialog();
    await waitForTypeTabs();
    await waitFor(() => expect(postalCode(editor).value).toBe("2611 AA"));
    expect(postalCode(editor)).not.toHaveAttribute("role");
    expect(postalCode(editor)).toBeDisabled();
    await pastDebounce();
    expect(suggestCalls()).toHaveLength(0);
  });
});

/* PO 2026-10-16: the own company (the relation with the Own company role) is never archived. The Archived switch is
 * disabled with a short hint while the role is picked; a switch that is already on stays enabled so it can be
 * cleared. The server refuses an archived own company anyway (OwnCompanyCannotBeArchived). */
describe("RelationEditor — the own company is never archived", () => {
  const HINT = "MasterData:OwnCompanyCannotBeArchivedHint";
  /* The Archived switch is the real Base UI checkbox (a span): disabled shows as aria-disabled. */
  const archivedSwitch = () => checkbox("MasterData:IsArchived");

  it("picking Own company disables the Archived switch with the hint; dropping the role enables it again", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    expect(archivedSwitch()).not.toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByText(HINT)).toBeNull();

    fireEvent.click(roleBox("OwnCompany"));
    const archived = checkbox("MasterData:IsArchived");
    expect(archived).toHaveAttribute("aria-disabled", "true");
    expect(archived).not.toBeChecked();
    const hint = within(region("MasterData:Relation")).getByText(HINT);
    expect(archived).toHaveAttribute("aria-describedby", hint.id);
    fireEvent.click(archived);
    expect(checkbox("MasterData:IsArchived")).not.toBeChecked();

    fireEvent.click(roleBox("OwnCompany"));
    expect(archivedSwitch()).not.toHaveAttribute("aria-disabled", "true");
    expect(checkbox("MasterData:IsArchived")).not.toHaveAttribute("aria-describedby");
    expect(screen.queryByText(HINT)).toBeNull();
  });

  it("a new own company is posted not archived", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    fireEvent.click(roleBox("OwnCompany"));
    fireEvent.click(checkbox("MasterData:IsArchived"));

    await submitEditor();
    await waitFor(() => expect(POST).toHaveBeenCalledTimes(1));
    expect(postedBody()).toMatchObject({ roles: 17, isArchived: false });
  });

  it("an archived relation that gets the role keeps the switch enabled so it can be cleared, with the hint", async () => {
    renderEditor();
    await waitForLookups();
    fillRequired();
    fireEvent.click(checkbox("MasterData:IsArchived"));
    fireEvent.click(roleBox("OwnCompany"));

    expect(checkbox("MasterData:IsArchived")).toBeChecked();
    expect(archivedSwitch()).not.toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText(HINT)).toBeInTheDocument();

    fireEvent.click(checkbox("MasterData:IsArchived"));
    expect(checkbox("MasterData:IsArchived")).not.toBeChecked();
    expect(archivedSwitch()).toHaveAttribute("aria-disabled", "true");
  });

  it("the stored own company opens with the switch disabled and the hint; another relation's switch is untouched", async () => {
    /* stored: Customer | Own company, not archived. */
    const first = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(archivedSwitch()).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText(HINT)).toBeInTheDocument();
    first.unmount();

    detail = { ...stored, roles: 3 as RelationRoles };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(archivedSwitch()).not.toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByText(HINT)).toBeNull();
  });

  it("the ribbon's Archive is disabled with the hint for the stored own company; an archived one still offers Unarchive", async () => {
    const first = renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    const archive = ribbon("MasterData:Archive");
    /* Ribbon buttons stay focusable when disabled (aria-disabled), so the reason shows as their tooltip. */
    expect(archive).toHaveAttribute("aria-disabled", "true");
    expect(archive).toHaveAttribute("title", HINT);
    fireEvent.click(archive);
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(POST).not.toHaveBeenCalled();
    first.unmount();

    /* Data from before the rule: an archived own company can be unarchived. */
    detail = { ...stored, isArchived: true };
    renderEditor({ mode: "edit", id: "r1" });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(ribbon("MasterData:Unarchive")).not.toHaveAttribute("aria-disabled", "true");
  });

  it("read-only keeps the switch disabled without depending on the role", async () => {
    detail = { ...stored, roles: 3 as RelationRoles };
    renderEditor({ mode: "edit", id: "r1", readOnly: true });
    await waitFor(() => expect(field(editorDialog(), "MasterData:Code").value).toBe("ACME"));
    expect(archivedSwitch()).toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByText(HINT)).toBeNull();
  });
});
