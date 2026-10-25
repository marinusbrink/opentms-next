/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* useCreateInPlace (UI-LOOK-004, PO decision 2026-10-16): `offer(entity, readOnly, onCreated)` gives a picker its
 * "Add new …" row only with the entity's Create permission and outside a read-only form; the row opens that entity's
 * editor on top in create mode with the typed term; a save hands { id, code, name, dto } to the picker and puts the
 * record into the cached lookup the picker reads; `offer` keeps its identity across renders (grid column definitions
 * depend on it). The nested editors are stubs; the real ones are covered by their own tests and by
 * create-in-place-editors.test.tsx. The row also needs the feature chain of the entity's navigation node (UI-PERM-006),
 * as the server's RequiresFeature / RequiresTenantAppFeature on the create endpoint does; on the host side the
 * Administration app (hostAlwaysEnabled) counts as on, as in the shell. Risk class: Medium (UI flow); the permission
 * and feature gate is the UI half of an authorization rule the server enforces on the create endpoints. */

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
vi.mock("@/domains/platform/components/OrganizationUnitFormDialog", async () => ({ OrganizationUnitFormDialog: (await import("./create-in-place-test-mocks")).stubDialog("OrganizationUnit") }));
vi.mock("@/domains/platform/components/UserFormDialog", async () => ({ UserFormDialog: (await import("./create-in-place-test-mocks")).stubDialog("User") }));

import { useCreateInPlace, withId, type CreatableEntity, type CreatedRecord, type CreateInPlaceProps } from "@/domains/master-data/components/create-in-place";
import {
  countries,
  currencies,
  drivers,
  geographicalRegions,
  products,
  relationCategories,
  relations,
  temperatures,
  transportCharacteristics,
  transportUnits,
  vehicles,
  vehicleTypes,
} from "@/domains/master-data/entities";
import { masterDataQueryKey, type LookupItem } from "@/domains/master-data/shared";
import { waypointTypes } from "@/domains/orders/reference-data";

import { APPS, type AppView } from "@/app/apps.config";
import { featureChain } from "@/app/app-features";

import { ALL_CREATE_FEATURES, CREATE_NODE_FEATURES, resetSavedDtos, savedDtos, TYPED_TERM } from "./create-in-place-test-mocks";

const ORG_UNITS_KEY = ["administration", "organization-units", "all"];

interface Case {
  entity: CreatableEntity;
  permission: string;
  labelKey: string;
  /** The record the picker gets for the stub's saved DTO (culture "en"). */
  record: { id: string; code: string | null; name: string };
  /** The cached list the record joins; null when the picker's list is a paged EntityLookup. */
  cacheKey: readonly unknown[] | null;
}

/* Every entity the registry offers, with its Create permission spelled out (MasterDataPermissions / PlatformPermissions). */
const CASES: Case[] = [
  { entity: "Country", permission: "MasterData.Countries.Create", labelKey: "MasterData:AddNewCountry", record: { id: "new-country", code: "XK", name: "Kosovo" }, cacheKey: masterDataQueryKey(countries.entity, "lookup") },
  { entity: "Currency", permission: "MasterData.Currencies.Create", labelKey: "MasterData:AddNewCurrency", record: { id: "new-currency", code: "CHF", name: "Swiss franc" }, cacheKey: masterDataQueryKey(currencies.entity, "lookup") },
  { entity: "Driver", permission: "MasterData.Drivers.Create", labelKey: "MasterData:AddNewDriver", record: { id: "new-driver", code: "D9", name: "Jan Smit" }, cacheKey: masterDataQueryKey(drivers.entity, "lookup") },
  {
    entity: "GeographicalRegion",
    permission: "MasterData.GeographicalRegions.Create",
    labelKey: "MasterData:AddNewGeographicalRegion",
    record: { id: "new-region", code: null, name: "Randstad" },
    cacheKey: masterDataQueryKey(geographicalRegions.entity, "lookup"),
  },
  { entity: "Product", permission: "MasterData.Products.Create", labelKey: "MasterData:AddNewProduct", record: { id: "new-product", code: "P1", name: "Pallets" }, cacheKey: masterDataQueryKey(products.entity, "lookup") },
  { entity: "Relation", permission: "MasterData.Relations.Create", labelKey: "MasterData:AddNewRelation", record: { id: "new-relation", code: "NEW", name: "New Relation" }, cacheKey: masterDataQueryKey(relations.entity, "lookup") },
  {
    entity: "RelationCategory",
    permission: "MasterData.RelationCategories.Create",
    labelKey: "MasterData:AddNewRelationCategory",
    record: { id: "new-category", code: null, name: "Category N" },
    cacheKey: masterDataQueryKey(relationCategories.entity, "lookup"),
  },
  { entity: "Temperature", permission: "MasterData.Temperatures.Create", labelKey: "MasterData:AddNewTemperature", record: { id: "new-temperature", code: null, name: "Frozen" }, cacheKey: masterDataQueryKey(temperatures.entity, "lookup") },
  {
    entity: "TransportCharacteristics",
    permission: "MasterData.TransportCharacteristics.Create",
    labelKey: "MasterData:AddNewTransportCharacteristics",
    record: { id: "new-characteristic", code: "TL", name: "Tail lift" },
    cacheKey: masterDataQueryKey(transportCharacteristics.entity, "lookup"),
  },
  { entity: "TransportUnit", permission: "MasterData.TransportUnits.Create", labelKey: "MasterData:AddNewTransportUnit", record: { id: "new-unit", code: null, name: "Box pallet" }, cacheKey: masterDataQueryKey(transportUnits.entity, "lookup") },
  { entity: "Vehicle", permission: "MasterData.Vehicles.Create", labelKey: "MasterData:AddNewVehicle", record: { id: "new-vehicle", code: null, name: "Truck 42" }, cacheKey: masterDataQueryKey(vehicles.entity, "lookup") },
  { entity: "VehicleType", permission: "MasterData.VehicleTypes.Create", labelKey: "MasterData:AddNewVehicleType", record: { id: "new-type", code: null, name: "Low loader" }, cacheKey: masterDataQueryKey(vehicleTypes.entity, "lookup") },
  { entity: "VatPercentage", permission: "Financial.VatPercentages.Create", labelKey: "Financial:AddNewVatPercentage", record: { id: "new-vat", code: "HIGH", name: "High" }, cacheKey: masterDataQueryKey("vatPercentages", "lookup") },
  {
    entity: "WaypointType",
    permission: "Orders.WaypointTypes.Create",
    labelKey: "Orders:AddNewWaypointType",
    record: { id: "new-waypoint-type", code: "DEPOT", name: "Depot stop" },
    cacheKey: masterDataQueryKey(waypointTypes.entity, "lookup"),
  },
  /* Design 61: the Transport run's pickers (a relation's price lists and discounts, a CMR profile's fixed texts and report definition). */
  { entity: "PriceList", permission: "Financial.PriceLists.Create", labelKey: "Financial:AddNewPriceList", record: { id: "new-price-list", code: "PL1", name: "Benelux 2026" }, cacheKey: null },
  {
    entity: "DiscountOrSurcharge",
    permission: "Financial.DiscountsOrSurcharges.Create",
    labelKey: "Financial:AddNewDiscountOrSurcharge",
    record: { id: "new-discount", code: "FUEL", name: "Fuel surcharge" },
    cacheKey: masterDataQueryKey("discountsOrSurcharges", "lookup"),
  },
  {
    entity: "ConsignmentNoteFixedDescription",
    permission: "Orders.ConsignmentNoteFixedDescriptions.Create",
    labelKey: "Orders:AddNewConsignmentNoteFixedDescription",
    record: { id: "new-fixed-text", code: null, name: "Fragile" },
    cacheKey: masterDataQueryKey("consignmentNoteFixedDescriptions", "lookup"),
  },
  { entity: "ReportDefinition", permission: "Platform.ReportDefinitions.Create", labelKey: "Platform:AddNewReportDefinition", record: { id: "new-report", code: null, name: "CMR (nl)" }, cacheKey: null },
  {
    entity: "OrganizationUnit",
    permission: "Platform.Administration.OrganizationUnits.Create",
    labelKey: "Administration:AddNewOrganizationUnit",
    record: { id: "new-ou", code: "00009", name: "Depot Venlo" },
    cacheKey: ORG_UNITS_KEY,
  },
  { entity: "User", permission: "Platform.Administration.Users.Create", labelKey: "Administration:AddNewUser", record: { id: "new-user", code: "jdoe", name: "Jane Doe" }, cacheKey: null },
];

const ALL_PERMISSIONS = CASES.map((c) => c.permission);

/* ── Harness ──────────────────────────────────────────────────────────────────────────────────────────────── */

const offers: unknown[] = [];

function Harness({ entity, readOnly = false, onCreated }: { entity: CreatableEntity; readOnly?: boolean; onCreated: (r: CreatedRecord) => void }) {
  const { offer, dialog } = useCreateInPlace();
  offers.push(offer);
  const props: CreateInPlaceProps = offer(entity, readOnly, onCreated);
  return (
    <>
      <span data-testid="create-label">{props.createLabel}</span>
      {props.onCreateNew && (
        <button type="button" onClick={() => props.onCreateNew!(TYPED_TERM)}>
          add-new
        </button>
      )}
      {dialog}
    </>
  );
}

function setup(entity: CreatableEntity, options: { readOnly?: boolean; seed?: (client: QueryClient) => void } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  options.seed?.(client);
  const invalidate = vi.spyOn(client, "invalidateQueries");
  const onCreated = vi.fn();
  const wrap = (node: ReactNode) => <QueryClientProvider client={client}>{node}</QueryClientProvider>;
  const view = render(wrap(<Harness entity={entity} readOnly={options.readOnly} onCreated={onCreated} />));
  return { client, invalidate, onCreated, rerender: (readOnly = options.readOnly) => view.rerender(wrap(<Harness entity={entity} readOnly={readOnly} onCreated={onCreated} />)) };
}

const addNew = () => screen.queryByRole("button", { name: "add-new" });
const stub = () => document.querySelector<HTMLElement>("[data-stub-editor]");

beforeEach(() => {
  grants = {};
  features = { ...ALL_CREATE_FEATURES };
  tenantId = "t-1";
  offers.length = 0;
  resetSavedDtos();
});

/* ── The gate ─────────────────────────────────────────────────────────────────────────────────────────────── */

describe("useCreateInPlace — offer", () => {
  it.each(CASES)("$entity: no row without $permission, even with every other Create permission", ({ entity, permission, labelKey }) => {
    grants = Object.fromEntries(ALL_PERMISSIONS.filter((p) => p !== permission).map((p) => [p, true]));
    setup(entity);
    expect(addNew()).toBeNull();
    /* The label is there regardless; without onCreateNew the picker shows no row. */
    expect(screen.getByTestId("create-label")).toHaveTextContent(labelKey);
  });

  it.each(CASES)("$entity: no row in a read-only form, even with $permission", ({ entity, permission }) => {
    grants = { [permission]: true };
    setup(entity, { readOnly: true });
    expect(addNew()).toBeNull();
  });

  it("follows a permission and read-only change on the next render", () => {
    const { rerender } = setup("Currency");
    expect(addNew()).toBeNull();
    grants = { "MasterData.Currencies.Create": true };
    rerender(false);
    expect(addNew()).not.toBeNull();
    rerender(true);
    expect(addNew()).toBeNull();
  });

  it.each(CASES)("$entity: no row when its navigation node's feature is off, even with $permission", ({ entity, permission }) => {
    grants = { [permission]: true };
    features = { ...ALL_CREATE_FEATURES, [CREATE_NODE_FEATURES[entity]]: "false" };
    setup(entity);
    expect(addNew()).toBeNull();
  });

  it.each(CASES)("$entity: no row when any ancestor of its node is off, even with $permission", ({ entity, permission }) => {
    grants = { [permission]: true };
    const ancestors = featureChain(CREATE_NODE_FEATURES[entity]).slice(0, -1);
    expect(ancestors.length).toBeGreaterThan(0);
    for (const ancestor of ancestors) {
      features = { ...ALL_CREATE_FEATURES, [ancestor]: "false" };
      setup(entity);
      expect(addNew(), `${entity} with ${ancestor} off`).toBeNull();
      cleanup();
    }
  });

  it.each(CASES)("$entity: the row with $permission and only its node's chain on", ({ entity, permission }) => {
    grants = { [permission]: true };
    features = Object.fromEntries(featureChain(CREATE_NODE_FEATURES[entity]).map((f) => [f, "true"]));
    setup(entity);
    expect(addNew()).not.toBeNull();
  });

  it("host side: the Administration entities need no feature (hostAlwaysEnabled), the master-data ones still do", () => {
    tenantId = null;
    features = {};
    grants = Object.fromEntries(ALL_PERMISSIONS.map((p) => [p, true]));
    for (const { entity } of CASES) {
      setup(entity);
      const administration = CREATE_NODE_FEATURES[entity].startsWith("Apps.Administration.");
      expect(addNew() != null, `${entity} on the host side`).toBe(administration);
      cleanup();
    }
  });

  it("every node feature is a node of apps.config.ts", () => {
    const nodes = (views: readonly AppView[] | undefined): string[] => (views ?? []).flatMap((v) => [...(v.feature ? [v.feature] : []), ...nodes(v.children)]);
    const all = new Set(APPS.flatMap((app) => nodes(app.views)));
    for (const { entity } of CASES) expect(all.has(CREATE_NODE_FEATURES[entity]), entity).toBe(true);
  });

  it("follows a feature change on the next render", () => {
    grants = { "MasterData.Currencies.Create": true };
    features = { ...ALL_CREATE_FEATURES, "Apps.MasterData.General.Reference": "false" };
    const { rerender } = setup("Currency");
    expect(addNew()).toBeNull();
    features = { ...ALL_CREATE_FEATURES };
    rerender(false);
    expect(addNew()).not.toBeNull();
  });

  it("keeps offer referentially stable across renders", () => {
    grants = { "MasterData.Currencies.Create": true };
    const { rerender } = setup("Currency");
    rerender(false);
    rerender(true);
    expect(offers.length).toBeGreaterThanOrEqual(3);
    expect(new Set(offers).size).toBe(1);
  });

  it("every label key exists in the en and nl resources", () => {
    const repoRoot = resolve((import.meta as unknown as { dirname: string }).dirname, "../../../../..");
    const texts = (file: string) => (JSON.parse(readFileSync(resolve(repoRoot, file), "utf-8")) as { texts: Record<string, string> }).texts;
    for (const culture of ["en", "nl"]) {
      const all = {
        ...texts(`backend/modules/master-data/OpenTms.MasterData.Domain.Shared/Localization/MasterData/${culture}.json`),
        ...texts(`backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/${culture}.json`),
        ...texts(`backend/modules/orders/OpenTms.Orders.Domain.Shared/Localization/Orders/${culture}.json`),
        ...texts(`backend/modules/financial/OpenTms.Financial.Domain.Shared/Localization/Financial/${culture}.json`),
      };
      for (const { labelKey } of CASES) expect(all[labelKey]?.trim(), `${culture}: ${labelKey}`).toBeTruthy();
      expect(all["Editions:AddNewEdition"]?.trim(), `${culture}: Editions:AddNewEdition`).toBeTruthy();
    }
  });
});

/* ── Open, save, hand back ────────────────────────────────────────────────────────────────────────────────── */

describe("useCreateInPlace — the editor on top", () => {
  it.each(CASES)("$entity: opens its editor in create mode with the typed term; a save hands back the record and fills the cache", async ({ entity, permission, record, cacheKey }) => {
    grants = { [permission]: true };
    const existing = entity === "OrganizationUnit" ? [{ id: "ou-1", displayName: "Head office" }] : [{ id: "old", code: null, name: "Existing" }];
    const { client, invalidate, onCreated } = setup(entity, { seed: (c) => cacheKey && c.setQueryData(cacheKey, existing) });

    expect(stub()).toBeNull();
    fireEvent.click(addNew()!);
    expect(stub()).toHaveAttribute("data-stub-editor", entity);
    expect(stub()).toHaveAttribute("data-mode", "create");
    expect(stub()).toHaveAttribute("data-initial-name", TYPED_TERM);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: `stub-save-${entity}` }));
    });
    expect(onCreated).toHaveBeenCalledTimes(1);
    expect(onCreated).toHaveBeenCalledWith({ ...record, dto: savedDtos[entity] });
    expect(stub()).toBeNull();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["entity-lookup"] });

    if (cacheKey) {
      const cached = client.getQueryData<unknown[]>(cacheKey)!;
      expect(cached).toHaveLength(2);
      expect(cached[0]).toEqual(existing[0]);
      if (entity === "OrganizationUnit") expect(cached[1]).toEqual(savedDtos.OrganizationUnit);
      else expect(cached[1]).toEqual({ id: record.id, code: record.code, name: record.name });
    }
  });

  it("does not add the record twice when the refetch already brought it", async () => {
    grants = { "MasterData.VehicleTypes.Create": true };
    const key = masterDataQueryKey(vehicleTypes.entity, "lookup");
    const { client } = setup("VehicleType", { seed: (c) => c.setQueryData<LookupItem[]>(key, [{ id: "new-type", code: null, name: "Low loader" }]) });
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-VehicleType" }));
    });
    expect(client.getQueryData<LookupItem[]>(key)).toHaveLength(1);
  });

  it("leaves a lookup that was never loaded alone", async () => {
    grants = { "MasterData.VehicleTypes.Create": true };
    const { client, onCreated } = setup("VehicleType");
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-VehicleType" }));
    });
    expect(onCreated).toHaveBeenCalledTimes(1);
    expect(client.getQueryData(masterDataQueryKey(vehicleTypes.entity, "lookup"))).toBeUndefined();
  });

  it("resolves a localized name in the user's culture and falls back to the plain name", async () => {
    grants = { "MasterData.Countries.Create": true };
    savedDtos.Country = { id: "c-x", isoAlpha2: "XX", names: {}, name: "Plain name" };
    const { onCreated } = setup("Country");
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-Country" }));
    });
    expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: "c-x", code: "XX", name: "Plain name" }));
  });

  it("builds a driver's name from first and last name when the DTO has no full name", async () => {
    grants = { "MasterData.Drivers.Create": true };
    savedDtos.Driver = { id: "d-x", code: "D1", firstName: "Piet", lastName: "Jansen", fullName: null };
    const { onCreated } = setup("Driver");
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-Driver" }));
    });
    expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: "d-x", name: "Piet Jansen" }));
  });

  it("a user without name and surname shows the user name", async () => {
    grants = { "Platform.Administration.Users.Create": true };
    savedDtos.User = { id: "u-x", userName: "planner1", name: null, surname: null };
    const { onCreated } = setup("User");
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-User" }));
    });
    expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: "u-x", code: "planner1", name: "planner1" }));
  });

  it("a cancelled editor closes without handing anything back", () => {
    grants = { "MasterData.Currencies.Create": true };
    const { onCreated } = setup("Currency");
    fireEvent.click(addNew()!);
    fireEvent.click(screen.getByRole("button", { name: "stub-cancel-Currency" }));
    expect(stub()).toBeNull();
    expect(onCreated).not.toHaveBeenCalled();
  });

  it("a save without an id hands nothing back", async () => {
    grants = { "MasterData.Currencies.Create": true };
    savedDtos.Currency = undefined;
    const { onCreated } = setup("Currency");
    fireEvent.click(addNew()!);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "stub-save-Currency" }));
    });
    expect(onCreated).not.toHaveBeenCalled();
    expect(stub()).toBeNull();
  });

  it("a second Add new opens a fresh editor with the new term", () => {
    grants = { "MasterData.Currencies.Create": true };
    setup("Currency");
    fireEvent.click(addNew()!);
    const first = stub();
    fireEvent.click(addNew()!);
    expect(document.querySelectorAll("[data-stub-editor]")).toHaveLength(1);
    expect(stub()).not.toBe(first);
  });
});

describe("withId", () => {
  it("appends the id once and keeps the order", () => {
    expect(withId([], "a")).toEqual(["a"]);
    expect(withId(["a", "b"], "c")).toEqual(["a", "b", "c"]);
    expect(withId(["a", "b"], "a")).toEqual(["a", "b"]);
  });

  it("returns the same array when the id is already there (no state change)", () => {
    const ids = ["a", "b"];
    expect(withId(ids, "b")).toBe(ids);
  });
});
