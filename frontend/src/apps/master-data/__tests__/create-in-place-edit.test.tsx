/// <reference types="node" />
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

let grants: Record<string, boolean> = {};
let features: Record<string, string> = {};
let tenantId: string | null = "t-1";
vi.mock("@/lib/abp/queries", () => ({
  useApplicationConfiguration: () => ({ data: { currentTenant: { id: tenantId }, auth: { grantedPolicies: grants }, features: { values: features } } }),
}));

vi.mock("@/domains/master-data/components/CountryEditor", async () => ({ CountryEditor: (await import("./create-in-place-test-mocks")).stubEditor("Country") }));
vi.mock("@/domains/master-data/components/CurrencyEditor", async () => ({ CurrencyEditor: (await import("./create-in-place-test-mocks")).stubEditor("Currency") }));
vi.mock("@/domains/master-data/components/DriverEditor", async () => ({ DriverEditor: (await import("./create-in-place-test-mocks")).stubEditor("Driver") }));
vi.mock("@/domains/master-data/components/GeographicalRegionEditor", async () => ({ GeographicalRegionEditor: (await import("./create-in-place-test-mocks")).stubEditor("GeographicalRegion") }));
vi.mock("@/domains/master-data/components/ProductEditor", async () => ({ ProductEditor: (await import("./create-in-place-test-mocks")).stubEditor("Product") }));
vi.mock("@/domains/master-data/components/RelationCategoryEditor", async () => ({ RelationCategoryEditor: (await import("./create-in-place-test-mocks")).stubEditor("RelationCategory") }));
vi.mock("@/domains/master-data/components/RelationEditor", async () => ({ RelationEditor: (await import("./create-in-place-test-mocks")).stubEditor("Relation") }));
vi.mock("@/domains/master-data/components/TemperatureEditor", async () => ({ TemperatureEditor: (await import("./create-in-place-test-mocks")).stubEditor("Temperature") }));
vi.mock("@/domains/master-data/components/TransportCharacteristicsEditor", async () => ({
  TransportCharacteristicsEditor: (await import("./create-in-place-test-mocks")).stubEditor("TransportCharacteristics"),
}));
vi.mock("@/domains/master-data/components/TransportUnitEditor", async () => ({ TransportUnitEditor: (await import("./create-in-place-test-mocks")).stubEditor("TransportUnit") }));
vi.mock("@/domains/master-data/components/VehicleEditor", async () => ({ VehicleEditor: (await import("./create-in-place-test-mocks")).stubEditor("Vehicle") }));
vi.mock("@/domains/master-data/components/VehicleTypeEditor", async () => ({ VehicleTypeEditor: (await import("./create-in-place-test-mocks")).stubEditor("VehicleType") }));
vi.mock("@/domains/orders/components/WaypointTypeEditor", async () => ({ WaypointTypeEditor: (await import("./create-in-place-test-mocks")).stubEditor("WaypointType") }));
vi.mock("@/domains/financial/components/VatPercentageEditor", async () => ({ VatPercentageEditor: (await import("./create-in-place-test-mocks")).stubEditor("VatPercentage") }));
vi.mock("@/domains/financial/components/PriceListEditor", async () => ({ PriceListEditor: (await import("./create-in-place-test-mocks")).stubEditor("PriceList") }));
vi.mock("@/domains/financial/components/DiscountOrSurchargeEditor", async () => ({ DiscountOrSurchargeEditor: (await import("./create-in-place-test-mocks")).stubEditor("DiscountOrSurcharge") }));
vi.mock("@/domains/orders/components/ConsignmentNoteFixedDescriptionEditor", async () => ({
  ConsignmentNoteFixedDescriptionEditor: (await import("./create-in-place-test-mocks")).stubEditor("ConsignmentNoteFixedDescription"),
}));
vi.mock("@/domains/platform/components/ReportDefinitionEditor", async () => ({ ReportDefinitionEditor: (await import("./create-in-place-test-mocks")).stubEditor("ReportDefinition") }));
vi.mock("@/domains/master-data/components/VehicleCombinationEditor", async () => ({ VehicleCombinationEditor: (await import("./create-in-place-test-mocks")).stubEditor("VehicleCombination") }));
vi.mock("@/domains/master-data/components/ProductRelationCodeEditor", async () => ({ ProductRelationCodeEditor: (await import("./create-in-place-test-mocks")).stubEditor("ProductRelationCode") }));
vi.mock("@/domains/orders/components/TransportOrderTypeEditor", async () => ({ TransportOrderTypeEditor: (await import("./create-in-place-test-mocks")).stubEditor("TransportOrderType") }));
vi.mock("@/domains/orders/components/TimeSlotEditor", async () => ({ TimeSlotEditor: (await import("./create-in-place-test-mocks")).stubEditor("TimeSlot") }));
vi.mock("@/domains/orders/components/ConsignmentNoteProfileEditor", async () => ({ ConsignmentNoteProfileEditor: (await import("./create-in-place-test-mocks")).stubEditor("ConsignmentNoteProfile") }));
vi.mock("@/domains/planning-execution/components/TransportTripTypeEditor", async () => ({ TransportTripTypeEditor: (await import("./create-in-place-test-mocks")).stubEditor("TransportTripType") }));
vi.mock("@/domains/financial/components/AdditionalServiceEditor", async () => ({ AdditionalServiceEditor: (await import("./create-in-place-test-mocks")).stubEditor("AdditionalService") }));
vi.mock("@/domains/platform/components/OrganizationUnitFormDialog", async () => {
  const { stubDialog, stubEditor } = await import("./create-in-place-test-mocks");
  const edit = stubEditor("OrganizationUnit");
  /* The pencil opens the unit by id (OrganizationUnitByIdDialog); the stub shows the id and read-only state. */
  return {
    OrganizationUnitFormDialog: stubDialog("OrganizationUnit"),
    OrganizationUnitByIdDialog: ({ id, readOnly, onClose, onSuccess }: { id?: string; readOnly: boolean; onClose: () => void; onSuccess: (saved?: unknown) => void }) =>
      edit({ mode: "edit", id, readOnly, open: true, onClose, onSaved: onSuccess }),
  };
});
vi.mock("@/domains/platform/components/UserFormDialog", async () => ({ UserFormDialog: (await import("./create-in-place-test-mocks")).stubDialog("User") }));


import { useCreateInPlace, type CreatableEntity, type CreateInPlaceOptions, type OfferOptions } from "@/domains/master-data/components/create-in-place";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { masterDataQueryKey, type LookupItem } from "@/domains/master-data/shared";
import { featureChain } from "@/app/app-features";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { ALL_CREATE_FEATURES, CREATE_NODE_FEATURES, resetSavedDtos, savedDtos } from "./create-in-place-test-mocks";

/* The edit pencil of UI-LOOK-005 through `offer` (design 61 "Order page review" 2): `onEdit` opens the selected record's
 * editor on top in edit mode with the entity's Update permission ("Edit") and read-only with only its view permission
 * ("Open"), never with the Create permission alone, never while the node's feature chain is off, never for Platform users
 * (no read by id), for a child record (address, contact person) only with a picked relation, and then on the relation
 * on the matching tab without the new-contact-person dialog; `editId` opens a record a picker holds by its code; a
 * read-only form keeps the pencil (opening changes nothing in it). After a save the lookups are refetched, the cached
 * lookup gets the new name and `editedLabel` gives it to an EntityLookup; the form underneath keeps its unsaved values.
 * The view and Update permissions are derived from the Create permission (`<Base>.Create` → `<Base>`, `<Base>.Update`):
 * every base is listed here with its real name, so a derivation that misses the module's permission fails. Risk class:
 * Medium (UI flow), the gate is the UI half of the server's view/update authorization. */

/* Every entity with the pencil: its permission base (the module's `<Base>` view permission; `<Base>.Update` edits). */
const BASES: [CreatableEntity, string][] = [
  ["Country", "MasterData.Countries"],
  ["Currency", "MasterData.Currencies"],
  ["Driver", "MasterData.Drivers"],
  ["GeographicalRegion", "MasterData.GeographicalRegions"],
  ["Product", "MasterData.Products"],
  ["Relation", "MasterData.Relations"],
  ["RelationCategory", "MasterData.RelationCategories"],
  ["Temperature", "MasterData.Temperatures"],
  ["TransportCharacteristics", "MasterData.TransportCharacteristics"],
  ["TransportUnit", "MasterData.TransportUnits"],
  ["Vehicle", "MasterData.Vehicles"],
  ["VehicleType", "MasterData.VehicleTypes"],
  ["VehicleCombination", "MasterData.VehicleCombinations"],
  ["ProductRelationCode", "MasterData.ProductRelationCodes"],
  ["WaypointType", "Orders.WaypointTypes"],
  ["TransportOrderType", "Orders.TransportOrderTypes"],
  ["TimeSlot", "Orders.TimeSlots"],
  ["ConsignmentNoteProfile", "Orders.ConsignmentNoteProfiles"],
  ["ConsignmentNoteFixedDescription", "Orders.ConsignmentNoteFixedDescriptions"],
  ["TransportTripType", "PlanningExecution.TransportTripTypes"],
  ["VatPercentage", "Financial.VatPercentages"],
  ["PriceList", "Financial.PriceLists"],
  ["AdditionalService", "Financial.AdditionalServices"],
  ["DiscountOrSurcharge", "Financial.DiscountsOrSurcharges"],
  ["ReportDefinition", "Platform.ReportDefinitions"],
  ["OrganizationUnit", "Platform.Administration.OrganizationUnits"],
];

function Pencil({ entity, readOnly = false, options }: { entity: CreatableEntity; readOnly?: boolean; options?: OfferOptions }) {
  const { offer, dialog } = useCreateInPlace();
  const props = offer(entity, readOnly, () => undefined, options);
  return (
    <>
      <span data-testid="edit-label">{props.editLabel ?? ""}</span>
      {props.onEdit && (
        <button type="button" onClick={() => props.onEdit!("rec-1")}>
          pencil
        </button>
      )}
      {dialog}
    </>
  );
}

function setup(entity: CreatableEntity, options: { readOnly?: boolean; offer?: OfferOptions } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <Pencil entity={entity} readOnly={options.readOnly} options={options.offer} />
    </QueryClientProvider>,
  );
  return client;
}

const pencil = () => screen.queryByRole("button", { name: "pencil" });
const stub = () => document.querySelector<HTMLElement>("[data-stub-editor]");

beforeEach(() => {
  grants = {};
  features = { ...ALL_CREATE_FEATURES };
  tenantId = "t-1";
  resetSavedDtos();
});

describe("useCreateInPlace — the pencil (UI-LOOK-005)", () => {
  it.each(BASES)("%s: Update opens the record in edit mode (Edit)", (entity, base) => {
    grants = { [`${base}.Update`]: true, [base]: true };
    setup(entity);
    expect(screen.getByTestId("edit-label")).toHaveTextContent("Edit");
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-mode", "edit");
    expect(stub()).toHaveAttribute("data-read-only", "false");
  });

  it.each(BASES)("%s: only the view permission opens it read-only (Open)", (entity, base) => {
    grants = { [base]: true };
    setup(entity);
    expect(screen.getByTestId("edit-label")).toHaveTextContent("Open");
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-mode", "edit");
    expect(stub()).toHaveAttribute("data-read-only", "true");
  });

  it.each(BASES)("%s: no pencil with the Create permission alone or with no permission", (entity, base) => {
    grants = { [`${base}.Create`]: true };
    setup(entity);
    expect(pencil()).toBeNull();
    expect(screen.getByTestId("edit-label")).toHaveTextContent("");
    cleanup();
    grants = {};
    setup(entity);
    expect(pencil()).toBeNull();
  });

  it.each(BASES)("%s: no pencil while the node's feature or an ancestor is off", (entity, base) => {
    grants = { [`${base}.Update`]: true, [base]: true };
    for (const feature of featureChain(CREATE_NODE_FEATURES[entity as keyof typeof CREATE_NODE_FEATURES])) {
      features = { ...ALL_CREATE_FEATURES, [feature]: "false" };
      setup(entity);
      expect(pencil(), `${entity} with ${feature} off`).toBeNull();
      cleanup();
    }
  });

  it("the record opened is the picked one (the stub shows its id)", () => {
    grants = { "MasterData.Currencies.Update": true };
    setup("Currency");
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-record-id", "rec-1");
  });

  it("a read-only form keeps the pencil: viewing or editing the record changes nothing in the form", () => {
    grants = { "MasterData.Vehicles.Update": true };
    setup("Vehicle", { readOnly: true });
    expect(screen.getByTestId("edit-label")).toHaveTextContent("Edit");
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-read-only", "false");
  });

  it("a Platform user has no pencil (no read by id), whatever the permissions", () => {
    grants = { "Platform.Administration.Users": true, "Platform.Administration.Users.Update": true, "Platform.Administration.Users.Create": true };
    setup("User");
    expect(pencil()).toBeNull();
  });

  it("editId opens the record a picker holds by its code; null shows no pencil", () => {
    grants = { "MasterData.Currencies.Update": true };
    setup("Currency", { offer: { editId: "cur-chf" } });
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-record-id", "cur-chf");
    cleanup();
    setup("Currency", { offer: { editId: null } });
    expect(pencil()).toBeNull();
  });

  it.each([
    { entity: "RelationAddress" as const, tab: "general" },
    { entity: "RelationContactPerson" as const, tab: "contactPersons" },
  ])("$entity: opens the relation on the matching tab, read-only without its Update, never without a picked relation", ({ entity, tab }) => {
    grants = { "MasterData.Relations": true, "MasterData.Relations.Update": true };
    setup(entity, { offer: { parentId: null } });
    expect(pencil()).toBeNull();
    cleanup();

    setup(entity, { offer: { parentId: "rel-1" } });
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-stub-editor", "Relation");
    expect(stub()).toHaveAttribute("data-record-id", "rel-1");
    expect(stub()).toHaveAttribute("data-initial-tab", tab);
    /* The pencil opens the tab only, no new contact person. */
    expect(stub()).toHaveAttribute("data-new-contact-person", "");
    expect(stub()).toHaveAttribute("data-read-only", "false");
    cleanup();

    grants = { "MasterData.Relations": true };
    setup(entity, { offer: { parentId: "rel-1" } });
    expect(screen.getByTestId("edit-label")).toHaveTextContent("Open");
    fireEvent.click(pencil()!);
    expect(stub()).toHaveAttribute("data-read-only", "true");
  });
});

/* ── In a form: the record opens on top, the form keeps its values, the picker shows the new name ───────────── */

type Item = { id: string; name: string };

function Form({ options }: { options?: CreateInPlaceOptions }) {
  const { offer, dialog } = useCreateInPlace();
  const [remarks, setRemarks] = useState("");
  const [currencyId, setCurrencyId] = useState<string | null>("cur-eur");
  const [relation, setRelation] = useState<Item | null>({ id: "rel-1", name: "Acme" });
  const currencies = useQuery<LookupItem[]>({ queryKey: masterDataQueryKey("currencies", "lookup"), queryFn: async () => [{ id: "cur-eur", code: "EUR", name: "Euro" }], staleTime: Infinity });
  const relationProps = offer("Relation", false, () => undefined, options);
  return (
    <>
      <input aria-label="remarks" value={remarks} onChange={(e) => setRemarks(e.target.value)} />
      <ReferenceCombobox
        id="currency"
        value={currencyId}
        onChange={setCurrencyId}
        options={(currencies.data ?? []).map((c) => ({ value: c.id ?? "", label: c.name ?? "" }))}
        {...offer("Currency", false, () => undefined)}
      />
      <EntityLookup<Item>
        id="relation"
        value={relation}
        onChange={setRelation}
        fetchPage={async () => ({ items: [], totalCount: 0 })}
        getLabel={(r) => r.name}
        onEdit={relationProps.onEdit as ((item: Item) => void) | undefined}
        editLabel={relationProps.editLabel}
        editedLabel={relationProps.editedLabel}
      />
      {dialog}
    </>
  );
}

describe("useCreateInPlace — the pencil in a form", () => {
  it("opens the record on top without touching the form, and after a save the pickers show the new names", async () => {
    vi.stubGlobal("IntersectionObserver", class { observe() {} disconnect() {} unobserve() {} });
    grants = { "MasterData.Currencies.Update": true, "MasterData.Relations.Update": true };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(client, "invalidateQueries");
    render(
      <QueryClientProvider client={client}>
        <Form />
      </QueryClientProvider>,
    );
    fireEvent.change(screen.getByLabelText("remarks"), { target: { value: "unsaved remark" } });
    await screen.findByDisplayValue("Euro");

    /* The currency (a ReferenceCombobox over the cached lookup). */
    savedDtos.Currency = { id: "cur-eur", isoCode: "EUR", names: { en: "Euro (renamed)" } };
    const editButtons = () => screen.getAllByRole("button", { name: "Edit" });
    fireEvent.click(editButtons()[0]);
    expect(stub()).toHaveAttribute("data-stub-editor", "Currency");
    expect(stub()).toHaveAttribute("data-record-id", "cur-eur");
    expect(screen.getByLabelText("remarks")).toHaveValue("unsaved remark");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-Currency" }));
    });
    expect(stub()).toBeNull();
    expect(client.getQueryData<LookupItem[]>(masterDataQueryKey("currencies", "lookup"))).toEqual([{ id: "cur-eur", code: "EUR", name: "Euro (renamed)" }]);
    expect(await screen.findByDisplayValue("Euro (renamed)")).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["entity-lookup"] });

    /* The relation (an EntityLookup whose value holds its old name): editedLabel shows the saved one. */
    savedDtos.Relation = { id: "rel-1", code: "ACME", name: "Acme Logistics" };
    fireEvent.click(editButtons()[1]);
    expect(stub()).toHaveAttribute("data-stub-editor", "Relation");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-Relation" }));
    });
    expect(screen.getByDisplayValue("Acme Logistics")).toBeInTheDocument();
    /* The form underneath keeps its unsaved values. */
    expect(screen.getByLabelText("remarks")).toHaveValue("unsaved remark");
  });

  it("a cancelled editor leaves the names and the form as they were", async () => {
    vi.stubGlobal("IntersectionObserver", class { observe() {} disconnect() {} unobserve() {} });
    grants = { "MasterData.Relations": true };
    render(
      <QueryClientProvider client={new QueryClient()}>
        <Form />
      </QueryClientProvider>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Open" }));
    expect(stub()).toHaveAttribute("data-read-only", "true");
    fireEvent.click(screen.getByRole("button", { name: "stub-cancel-Relation" }));
    expect(stub()).toBeNull();
    expect(screen.getByDisplayValue("Acme")).toBeInTheDocument();
  });
});
