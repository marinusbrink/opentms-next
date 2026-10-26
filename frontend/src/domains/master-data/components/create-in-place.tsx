import { Fragment, useCallback, useRef, useState, type ReactNode } from "react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";

import { useAppFeatures } from "@/app/app-features";

import { OrganizationUnitFormDialog } from "@/domains/platform/components/OrganizationUnitFormDialog";
import { UserFormDialog } from "@/domains/platform/components/UserFormDialog";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { AdditionalServiceEditor } from "@/domains/financial/components/AdditionalServiceEditor";
import { rateLineKeys } from "@/domains/financial/rate-lines";
import { ConsignmentNoteFixedDescriptionEditor } from "@/domains/orders/components/ConsignmentNoteFixedDescriptionEditor";
import { ConsignmentNoteProfileEditor } from "@/domains/orders/components/ConsignmentNoteProfileEditor";
import { TimeSlotEditor } from "@/domains/orders/components/TimeSlotEditor";
import { TransportOrderTypeEditor } from "@/domains/orders/components/TransportOrderTypeEditor";
import { consignmentNoteKeys } from "@/domains/orders/consignment-notes";
import { transportOrderKeys } from "@/domains/orders/transport-orders";
import { TransportTripTypeEditor } from "@/domains/planning-execution/components/TransportTripTypeEditor";
import { CountryEditor } from "@/domains/master-data/components/CountryEditor";
import { CurrencyEditor } from "@/domains/master-data/components/CurrencyEditor";
import { DiscountOrSurchargeEditor } from "@/domains/financial/components/DiscountOrSurchargeEditor";
import { DriverEditor } from "@/domains/master-data/components/DriverEditor";
import { GeographicalRegionEditor } from "@/domains/master-data/components/GeographicalRegionEditor";
import { PriceListEditor } from "@/domains/financial/components/PriceListEditor";
import { ProductEditor } from "@/domains/master-data/components/ProductEditor";
import { ProductRelationCodeEditor } from "@/domains/master-data/components/ProductRelationCodeEditor";
import { RelationCategoryEditor } from "@/domains/master-data/components/RelationCategoryEditor";
import { RelationEditor, type RelationEditorProps } from "@/domains/master-data/components/RelationEditor";
import { ReportDefinitionEditor } from "@/domains/platform/components/ReportDefinitionEditor";
import { TemperatureEditor } from "@/domains/master-data/components/TemperatureEditor";
import { TransportCharacteristicsEditor } from "@/domains/master-data/components/TransportCharacteristicsEditor";
import { TransportUnitEditor } from "@/domains/master-data/components/TransportUnitEditor";
import { VehicleEditor } from "@/domains/master-data/components/VehicleEditor";
import { VatPercentageEditor } from "@/domains/financial/components/VatPercentageEditor";
import { VehicleCombinationEditor } from "@/domains/master-data/components/VehicleCombinationEditor";
import { VehicleTypeEditor } from "@/domains/master-data/components/VehicleTypeEditor";
import { WaypointTypeEditor } from "@/domains/orders/components/WaypointTypeEditor";
import { resolveLocalizedName } from "@/domains/master-data/localized-name";
import { masterDataQueryKey, type LookupItem, type LocalizedNameMap } from "@/domains/master-data/shared";
import type { OrganizationUnitRow } from "@/domains/platform/administration-organization-units";
import { platformMasterDataQueryKey, reportDefinitions } from "@/domains/platform/master-data";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* "Add new …" in every record picker of a form (UI-LOOK-004, PO decision 2026-10-16; no exception for an editor that
 * another app shows): the user never leaves the
 * screen to add a missing record. One hook per editor holds which entity's editor is open on top, renders it in
 * create mode with the typed term as its name, and on save hands the new record to the picker, which selects it
 * (single) or adds it to the selection (multi). Lists that come from a lookup query show the new record at once:
 * it is put into the cached lookup before the picker selects it (the refetch the save started replaces it), and
 * the paged EntityLookup lists are refetched. No row in a read-only form, and none unless the user holds the
 * entity's Create permission AND the feature chain of the entity's navigation node is on (UI-PERM-006): the server
 * requires both on the create endpoint (e.g. CountryAppService: Apps.MasterData.General + …General.Reference + …Reference.Countries;
 * the Administration controllers: RequiresTenantAppFeature). On the host side an app marked hostAlwaysEnabled
 * (Administration) counts as on, as in the shell. A picker that keeps its own create dialog (one it also opens for
 * editing) asks the same gate through `canCreate(entity, readOnly)`.
 *
 *   const inPlace = useCreateInPlace();
 *   <ReferenceCombobox … {...inPlace.offer("Currency", form.readOnly, (r) => form.set("currencyId", r.id))} />
 *   …
 *   {inPlace.dialog}
 *
 * A record that belongs to another one (a relation's address or contact person: the relation aggregate has no
 * endpoint of its own for them) is created in that record's editor: `offer("RelationAddress", readOnly, onCreated,
 * { parentId })` opens the relation editor on the picked relation (the contact person's dialog already open with the
 * typed name) and hands back the saved relation, whose new address or contact person the picker selects. Without a
 * parent there is no row. `roles` gives a new relation the roles of the picker that created it (a customer picker
 * creates a customer, so the role-narrowed lookup finds it again).
 *
 * It lives in the Master Data domain folder, beside the editors it opens, because two apps use it (the Master data
 * app's editors and the Transport app's forms, design 61) and an app never imports another app (CLAUDE.md, frontend
 * structure). Its table is the one place a domain folder reaches into the others' editors (Orders, Financial,
 * Planning & Execution, Platform): the editors live in their owning domain's components/ folder, and the table only
 * looks them up when a dialog renders (the getters below), so the cycle editor → hook → editor never resolves a
 * binding during module load. */

/** What the picker gets back: the id, the code and name as the entity's lookup shows them, and the saved DTO. */
export interface CreatedRecord<TDto = unknown> {
  id: string;
  code: string | null;
  name: string;
  dto: TDto;
}

/** Spread onto EntityLookup / ReferenceCombobox / MultiSelect / TreeSelect. */
export interface CreateInPlaceProps {
  onCreateNew?: (term: string) => void;
  createLabel: string;
}

interface Saved {
  id?: string;
  code?: string | null;
  name?: string | null;
  names?: LocalizedNameMap | null;
  displayName?: string | null;
  fullName?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  isoAlpha2?: string | null;
  isoCode?: string | null;
  userName?: string | null;
  surname?: string | null;
}

/** What a picker passes besides the term (see the header). */
export interface CreateInPlaceOptions {
  /** The record the new one belongs to; required by the entities that set `parent`. */
  parentId?: string | null;
  /** A new relation's roles (a [Flags] value). */
  roles?: number;
}

interface EntityEntry {
  /** The Create permission (Update of the parent for a child record); without it the picker has no "Add new …" row. */
  permission: string;
  /** A child of another record: the row needs `options.parentId`, and the parent's editor opens on it. */
  parent?: true;
  /** The feature of the entity's navigation node (apps.config.ts); it and every ancestor must be on. */
  feature: string;
  labelKey: string;
  /** Renders the editor in create mode. The editor component is looked up only when this renders (the getters
   *  in ENTITIES), never at module load: the editors import this hook, so a binding read during the import cycle
   *  could still be uninitialised (a `const X = memo(…)` editor would throw). */
  render: (props: EntityEditorProps, options: CreateInPlaceOptions) => ReactNode;
  /** The code and name the entity's lookup shows. */
  toRecord: (dto: Saved, culture: string) => { code: string | null; name: string };
  /** Puts the record into the cached option list(s) the pickers read. */
  insert: (queryClient: QueryClient, record: CreatedRecord<Saved>) => void;
}

type EditorComponent = (props: EntityEditorProps) => ReactNode;

/** Looks the editor up through `getEditor` when the dialog renders (see EntityEntry.render). */
const masterDataEditor = (getEditor: () => EditorComponent) => (props: EntityEditorProps) => {
  const Editor = getEditor();
  return <Editor {...props} />;
};

/** The relation editor with the extras a picker may pass (a new relation's roles; a child record's tab). */
const relationEditor = (extra: (props: EntityEditorProps, options: CreateInPlaceOptions) => Partial<RelationEditorProps>) => (props: EntityEditorProps, options: CreateInPlaceOptions) => {
  const Editor = RelationEditor;
  return <Editor {...props} {...extra(props, options)} />;
};

/** Adds the record to the entity's cached lookup (masterDataQueryKey(entity, "lookup")) when it is loaded. */
const intoLookup =
  (entity: string) =>
  (queryClient: QueryClient, record: CreatedRecord<Saved>) =>
    queryClient.setQueryData<LookupItem[]>(masterDataQueryKey(entity, "lookup"), (items) =>
      items && !items.some((i) => i.id === record.id) ? [...items, { id: record.id, code: record.code, name: record.name }] : items,
    );

const named = (dto: Saved) => ({ code: dto.code ?? null, name: dto.name ?? "" });
const localized = (code: (dto: Saved) => string | null | undefined) => (dto: Saved, culture: string) => ({
  code: code(dto) ?? null,
  name: resolveLocalizedName(dto.names, culture) || dto.name || "",
});

/* The entities a form picks. AddressType is no picker's target. */
const ENTITIES = {
  Country: {
    permission: "MasterData.Countries.Create",
    feature: "Apps.MasterData.General.Reference.Countries",
    labelKey: "MasterData:AddNewCountry",
    render: masterDataEditor(() => CountryEditor),
    toRecord: localized((d) => d.isoAlpha2),
    insert: intoLookup("countries"),
  },
  Currency: {
    permission: "MasterData.Currencies.Create",
    feature: "Apps.MasterData.General.Reference.Currencies",
    labelKey: "MasterData:AddNewCurrency",
    render: masterDataEditor(() => CurrencyEditor),
    toRecord: localized((d) => d.isoCode),
    insert: intoLookup("currencies"),
  },
  Driver: {
    permission: "MasterData.Drivers.Create",
    feature: "Apps.MasterData.General.Fleet.Drivers",
    labelKey: "MasterData:AddNewDriver",
    render: masterDataEditor(() => DriverEditor),
    toRecord: (d) => ({ code: d.code ?? null, name: d.fullName || [d.firstName, d.lastName].filter(Boolean).join(" ") }),
    insert: intoLookup("drivers"),
  },
  GeographicalRegion: {
    permission: "MasterData.GeographicalRegions.Create",
    feature: "Apps.MasterData.General.Reference.GeographicalRegions",
    labelKey: "MasterData:AddNewGeographicalRegion",
    render: masterDataEditor(() => GeographicalRegionEditor),
    toRecord: named,
    insert: intoLookup("geographicalRegions"),
  },
  Product: {
    permission: "MasterData.Products.Create",
    feature: "Apps.MasterData.General.Transport.Products",
    labelKey: "MasterData:AddNewProduct",
    render: masterDataEditor(() => ProductEditor),
    toRecord: named,
    insert: intoLookup("products"),
  },
  Relation: {
    permission: "MasterData.Relations.Create",
    feature: "Apps.MasterData.General.Relations.Relations",
    labelKey: "MasterData:AddNewRelation",
    render: relationEditor((_props, options) => ({ initialRoles: options.roles })),
    toRecord: named,
    insert: intoLookup("relations"),
  },
  /* A relation's address and contact person (design 43: parts of the relation aggregate, saved with it). The relation
   * editor opens in edit mode on the picked relation; the relation's queries are refetched by its save. */
  RelationAddress: {
    permission: "MasterData.Relations.Update",
    parent: true,
    feature: "Apps.MasterData.General.Relations.Relations",
    labelKey: "MasterData:AddNewAddress",
    render: relationEditor((_props, options) => ({ mode: "edit", id: options.parentId ?? undefined, initialName: undefined, initialTab: "general" })),
    toRecord: named,
    insert: () => undefined,
  },
  RelationContactPerson: {
    permission: "MasterData.Relations.Update",
    parent: true,
    feature: "Apps.MasterData.General.Relations.Relations",
    labelKey: "MasterData:AddNewContactPerson",
    render: relationEditor((props, options) => ({ mode: "edit", id: options.parentId ?? undefined, initialName: undefined, initialTab: "contactPersons", newContactPersonName: props.initialName ?? "" })),
    toRecord: named,
    insert: () => undefined,
  },
  RelationCategory: {
    permission: "MasterData.RelationCategories.Create",
    feature: "Apps.MasterData.General.Relations.RelationCategories",
    labelKey: "MasterData:AddNewRelationCategory",
    render: masterDataEditor(() => RelationCategoryEditor),
    toRecord: named,
    insert: intoLookup("relationCategories"),
  },
  Temperature: {
    permission: "MasterData.Temperatures.Create",
    feature: "Apps.MasterData.General.Transport.Temperatures",
    labelKey: "MasterData:AddNewTemperature",
    render: masterDataEditor(() => TemperatureEditor),
    toRecord: named,
    insert: intoLookup("temperatures"),
  },
  TransportCharacteristics: {
    permission: "MasterData.TransportCharacteristics.Create",
    feature: "Apps.MasterData.General.Fleet.TransportCharacteristics",
    labelKey: "MasterData:AddNewTransportCharacteristics",
    render: masterDataEditor(() => TransportCharacteristicsEditor),
    toRecord: named,
    insert: intoLookup("transportCharacteristics"),
  },
  TransportUnit: {
    permission: "MasterData.TransportUnits.Create",
    feature: "Apps.MasterData.General.Transport.TransportUnits",
    labelKey: "MasterData:AddNewTransportUnit",
    render: masterDataEditor(() => TransportUnitEditor),
    toRecord: named,
    insert: intoLookup("transportUnits"),
  },
  Vehicle: {
    permission: "MasterData.Vehicles.Create",
    feature: "Apps.MasterData.General.Fleet.Vehicles",
    labelKey: "MasterData:AddNewVehicle",
    render: masterDataEditor(() => VehicleEditor),
    toRecord: named,
    insert: intoLookup("vehicles"),
  },
  VehicleType: {
    permission: "MasterData.VehicleTypes.Create",
    feature: "Apps.MasterData.General.Fleet.VehicleTypes",
    labelKey: "MasterData:AddNewVehicleType",
    render: masterDataEditor(() => VehicleTypeEditor),
    toRecord: named,
    insert: intoLookup("vehicleTypes"),
  },
  /* The Transport run (design 61): the pickers of the order and trip editors. */
  VehicleCombination: {
    permission: "MasterData.VehicleCombinations.Create",
    feature: "Apps.MasterData.General.Fleet.VehicleCombinations",
    labelKey: "MasterData:AddNewVehicleCombination",
    render: masterDataEditor(() => VehicleCombinationEditor),
    toRecord: named,
    insert: intoLookup("vehicleCombinations"),
  },
  /* A product relation code is its code (it has no name). */
  ProductRelationCode: {
    permission: "MasterData.ProductRelationCodes.Create",
    feature: "Apps.MasterData.General.Transport.ProductRelationCodes",
    labelKey: "MasterData:AddNewProductRelationCode",
    render: masterDataEditor(() => ProductRelationCodeEditor),
    toRecord: (d) => ({ code: d.code ?? null, name: d.code ?? "" }),
    insert: intoLookup("productRelationCodes"),
  },
  /* Orders (design 51): the next type of a waypoint type and the default stops of a transport order type. */
  WaypointType: {
    permission: "Orders.WaypointTypes.Create",
    feature: "Apps.MasterData.Orders.TransportOrders.WaypointTypes",
    labelKey: "Orders:AddNewWaypointType",
    render: masterDataEditor(() => WaypointTypeEditor),
    toRecord: localized((d) => d.code),
    insert: intoLookup("waypointTypes"),
  },
  /* Orders (design 61): an order's type, a stop's time slot, the profile a CMR prints with. */
  TransportOrderType: {
    permission: "Orders.TransportOrderTypes.Create",
    feature: "Apps.MasterData.Orders.TransportOrders.TransportOrderTypes",
    labelKey: "Orders:AddNewTransportOrderType",
    render: masterDataEditor(() => TransportOrderTypeEditor),
    toRecord: localized((d) => d.code),
    /* The New order / New template dialogs read the types' rows (with the default flag), not the lookup. */
    insert: (queryClient, record) => {
      intoLookup("transportOrderTypes")(queryClient, record);
      void queryClient.invalidateQueries({ queryKey: transportOrderKeys.orderTypes() });
    },
  },
  TimeSlot: {
    permission: "Orders.TimeSlots.Create",
    feature: "Apps.MasterData.Orders.TransportOrders.TimeSlots",
    labelKey: "Orders:AddNewTimeSlot",
    render: masterDataEditor(() => TimeSlotEditor),
    toRecord: localized((d) => d.code),
    insert: intoLookup("timeSlots"),
  },
  ConsignmentNoteProfile: {
    permission: "Orders.ConsignmentNoteProfiles.Create",
    feature: "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles",
    labelKey: "Orders:AddNewConsignmentNoteProfile",
    render: masterDataEditor(() => ConsignmentNoteProfileEditor),
    toRecord: named,
    /* The Print wizard reads the profiles' rows (with the default flag), not the lookup. */
    insert: (queryClient, record) => {
      intoLookup("consignmentNoteProfiles")(queryClient, record);
      void queryClient.invalidateQueries({ queryKey: consignmentNoteKeys.profiles() });
    },
  },
  /* Planning & Execution (design 53): the trip type of an order's "Tevens trip". */
  TransportTripType: {
    permission: "PlanningExecution.TransportTripTypes.Create",
    feature: "Apps.MasterData.PlanningExecution.Trips.TransportTripTypes",
    labelKey: "PlanningExecution:AddNewTransportTripType",
    render: masterDataEditor(() => TransportTripTypeEditor),
    toRecord: localized((d) => d.code),
    insert: intoLookup("transportTripTypes"),
  },
  /* Financial (design 52): the VAT category of an additional service or a discount or surcharge. */
  VatPercentage: {
    permission: "Financial.VatPercentages.Create",
    feature: "Apps.MasterData.Financial.Invoicing.VatPercentages",
    labelKey: "Financial:AddNewVatPercentage",
    render: masterDataEditor(() => VatPercentageEditor),
    toRecord: localized((d) => d.code),
    insert: intoLookup("vatPercentages"),
  },
  /* Financial (design 57): a relation's price lists and default discounts or surcharges. The price-list lookups are
   * per side ("lookup", side): the save's invalidation of the entity refetches the Master data editors' ones; the
   * order's terms read Financial's lookup per side (rate-lines.ts), refetched here. */
  PriceList: {
    permission: "Financial.PriceLists.Create",
    feature: "Apps.MasterData.Financial.Pricing.PriceLists",
    labelKey: "Financial:AddNewPriceList",
    render: masterDataEditor(() => PriceListEditor),
    toRecord: named,
    insert: (queryClient) => void queryClient.invalidateQueries({ queryKey: rateLineKeys.priceListLookup(undefined).slice(0, 3) }),
  },
  /* Financial (design 61): the service of an order's "Add additional service" line. */
  AdditionalService: {
    permission: "Financial.AdditionalServices.Create",
    feature: "Apps.MasterData.Financial.Pricing.AdditionalServices",
    labelKey: "Financial:AddNewAdditionalService",
    render: masterDataEditor(() => AdditionalServiceEditor),
    toRecord: localized((d) => d.code),
    insert: intoLookup("additionalServices"),
  },
  DiscountOrSurcharge: {
    permission: "Financial.DiscountsOrSurcharges.Create",
    feature: "Apps.MasterData.Financial.Pricing.DiscountsOrSurcharges",
    labelKey: "Financial:AddNewDiscountOrSurcharge",
    render: masterDataEditor(() => DiscountOrSurchargeEditor),
    toRecord: named,
    insert: intoLookup("discountsOrSurcharges"),
  },
  /* Orders (design 58): the fixed texts of a CMR profile. */
  ConsignmentNoteFixedDescription: {
    permission: "Orders.ConsignmentNoteFixedDescriptions.Create",
    feature: "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions",
    labelKey: "Orders:AddNewConsignmentNoteFixedDescription",
    render: masterDataEditor(() => ConsignmentNoteFixedDescriptionEditor),
    toRecord: named,
    insert: intoLookup("consignmentNoteFixedDescriptions"),
  },
  /* Platform (design 55): the report definition a CMR profile renders with; its options are read per purpose. */
  ReportDefinition: {
    permission: "Platform.ReportDefinitions.Create",
    feature: "Apps.MasterData.Platform.ReportDefinitions",
    labelKey: "Platform:AddNewReportDefinition",
    render: masterDataEditor(() => ReportDefinitionEditor),
    toRecord: named,
    insert: (queryClient) => void queryClient.invalidateQueries({ queryKey: platformMasterDataQueryKey(reportDefinitions.entity) }),
  },
  /* Platform records picked in master-data forms: the Administration app's own editors open on top. */
  OrganizationUnit: {
    permission: "Platform.Administration.OrganizationUnits.Create",
    feature: "Apps.Administration.OrganizationUnits",
    labelKey: "Administration:AddNewOrganizationUnit",
    render: ({ onClose, onSaved, initialName }) => (
      <OrganizationUnitFormDialog mode="create" open initialName={initialName} onClose={onClose} onSuccess={(saved) => onSaved(saved)} />
    ),
    toRecord: (d) => ({ code: d.code ?? null, name: d.displayName ?? "" }),
    insert: (queryClient, record) =>
      queryClient.setQueryData<OrganizationUnitRow[]>(["administration", "organization-units", "all"], (rows) =>
        rows && !rows.some((r) => r.id === record.id) ? [...rows, record.dto as OrganizationUnitRow] : rows,
      ),
  },
  User: {
    permission: "Platform.Administration.Users.Create",
    feature: "Apps.Administration.Users",
    labelKey: "Administration:AddNewUser",
    render: ({ onClose, onSaved, initialName }) => (
      <UserFormDialog mode="create" open initialName={initialName} onClose={onClose} onSuccess={(saved) => onSaved(saved)} />
    ),
    toRecord: (d) => ({ code: d.userName ?? null, name: [d.name, d.surname].filter(Boolean).join(" ") || d.userName || "" }),
    /* The user lookup is a paged EntityLookup: the refetch of "entity-lookup" covers it. */
    insert: () => undefined,
  },
} satisfies Record<string, EntityEntry>;

export type CreatableEntity = keyof typeof ENTITIES;

/** A multi-value picker's ids with the created record added (once). */
export function withId(ids: string[], id: string): string[] {
  return ids.includes(id) ? ids : [...ids, id];
}

interface Pending {
  seq: number;
  entity: CreatableEntity;
  term: string;
  options: CreateInPlaceOptions;
  onCreated: (record: CreatedRecord<never>) => void;
}

/** The one "may this picker offer Add new …" gate: not read-only, the entity's Create permission, and its node's
 *  whole feature chain on (UI-PERM-006). For pickers that keep their own create dialog; `offer` uses it too. */
export function useCanCreateInPlace(): (entity: CreatableEntity, readOnly: boolean, options?: CreateInPlaceOptions) => boolean {
  const isGranted = useIsGrantedChecker();
  const features = useAppFeatures();
  return (entity, readOnly, options) => {
    const entry: EntityEntry = ENTITIES[entity];
    if (entry.parent && !options?.parentId) return false;
    return !readOnly && isGranted(entry.permission) && features.isFeatureEnabled(entry.feature);
  };
}

export function useCreateInPlace() {
  const { t, culture } = useL();
  const queryClient = useQueryClient();
  const canCreate = useCanCreateInPlace();
  const [pending, setPending] = useState<Pending | null>(null);
  const seq = useRef(0);
  /* `offer` stays the same function across renders (grid column definitions depend on it); it reads the latest
   * gate and texts when it is called, which is during render. */
  const latest = useRef({ canCreate, t });
  latest.current = { canCreate, t };

  const offer = useCallback(
    <TDto = unknown,>(entity: CreatableEntity, readOnly: boolean, onCreated: (record: CreatedRecord<TDto>) => void, options: CreateInPlaceOptions = {}): CreateInPlaceProps => {
      const entry: EntityEntry = ENTITIES[entity];
      return {
        createLabel: latest.current.t(entry.labelKey),
        onCreateNew: latest.current.canCreate(entity, readOnly, options)
          ? (term: string) => setPending({ seq: ++seq.current, entity, term, options, onCreated: onCreated as (record: CreatedRecord<never>) => void })
          : undefined,
      };
    },
    [],
  );

  let dialog: ReactNode = null;
  if (pending) {
    const entry: EntityEntry = ENTITIES[pending.entity];
    dialog = (
      <Fragment key={pending.seq}>
        {entry.render({
          mode: "create",
          readOnly: false,
          open: true,
          initialName: pending.term,
          onClose: () => setPending(null),
          onSaved: (saved) => {
            const dto = saved as Saved | undefined;
            if (!dto?.id) return;
            const record: CreatedRecord<Saved> = { id: dto.id, ...entry.toRecord(dto, culture), dto };
            entry.insert(queryClient, record);
            void queryClient.invalidateQueries({ queryKey: ["entity-lookup"] });
            pending.onCreated(record as CreatedRecord<never>);
          },
        }, pending.options)}
      </Fragment>
    );
  }

  return { offer, canCreate, dialog };
}
