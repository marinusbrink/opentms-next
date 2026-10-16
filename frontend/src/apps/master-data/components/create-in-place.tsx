import { Fragment, useCallback, useRef, useState, type ReactNode } from "react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";

import { useAppFeatures } from "@/app/app-features";

import { OrganizationUnitFormDialog } from "@/domains/platform/components/OrganizationUnitFormDialog";
import { UserFormDialog } from "@/domains/platform/components/UserFormDialog";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CountryEditor } from "@/apps/master-data/editors/CountryEditor";
import { CurrencyEditor } from "@/apps/master-data/editors/CurrencyEditor";
import { DriverEditor } from "@/apps/master-data/editors/DriverEditor";
import { GeographicalRegionEditor } from "@/apps/master-data/editors/GeographicalRegionEditor";
import { ProductEditor } from "@/apps/master-data/editors/ProductEditor";
import { RelationCategoryEditor } from "@/apps/master-data/editors/RelationCategoryEditor";
import { RelationEditor } from "@/apps/master-data/editors/RelationEditor";
import { TemperatureEditor } from "@/apps/master-data/editors/TemperatureEditor";
import { TransportCharacteristicsEditor } from "@/apps/master-data/editors/TransportCharacteristicsEditor";
import { TransportUnitEditor } from "@/apps/master-data/editors/TransportUnitEditor";
import { VehicleEditor } from "@/apps/master-data/editors/VehicleEditor";
import { VehicleTypeEditor } from "@/apps/master-data/editors/VehicleTypeEditor";
import { resolveLocalizedName } from "@/domains/master-data/localized-name";
import { masterDataQueryKey, type LookupItem, type LocalizedNameMap } from "@/domains/master-data/shared";
import type { OrganizationUnitRow } from "@/domains/platform/administration-organization-units";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* "Add new …" in every record picker of a form (UI-LOOK-004, PO decision 2026-10-16): the user never leaves the
 * screen to add a missing record. One hook per editor holds which entity's editor is open on top, renders it in
 * create mode with the typed term as its name, and on save hands the new record to the picker, which selects it
 * (single) or adds it to the selection (multi). Lists that come from a lookup query show the new record at once:
 * it is put into the cached lookup before the picker selects it (the refetch the save started replaces it), and
 * the paged EntityLookup lists are refetched. No row in a read-only form, and none unless the user holds the
 * entity's Create permission AND the feature chain of the entity's navigation node is on (UI-PERM-006): the server
 * requires both on the create endpoint (e.g. CountryAppService: Apps.MasterData.Reference + …Reference.Countries;
 * the Administration controllers: RequiresTenantAppFeature). On the host side an app marked hostAlwaysEnabled
 * (Administration) counts as on, as in the shell. A picker that keeps its own create dialog (one it also opens for
 * editing) asks the same gate through `canCreate(entity, readOnly)`.
 *
 *   const inPlace = useCreateInPlace();
 *   <ReferenceCombobox … {...inPlace.offer("Currency", form.readOnly, (r) => form.set("currencyId", r.id))} />
 *   …
 *   {inPlace.dialog}
 */

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

interface EntityEntry {
  /** The Create permission; without it the picker has no "Add new …" row. */
  permission: string;
  /** The feature of the entity's navigation node (apps.config.ts); it and every ancestor must be on. */
  feature: string;
  labelKey: string;
  /** Renders the editor in create mode. The editor component is looked up only when this renders (the getters
   *  in ENTITIES), never at module load: the editors import this hook, so a binding read during the import cycle
   *  could still be uninitialised (a `const X = memo(…)` editor would throw). */
  render: (props: EntityEditorProps) => ReactNode;
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

/* The entities a form picks. AddressType, ProductRelationCode and VehicleCombination are no picker's target. */
const ENTITIES = {
  Country: {
    permission: "MasterData.Countries.Create",
    feature: "Apps.MasterData.Reference.Countries",
    labelKey: "MasterData:AddNewCountry",
    render: masterDataEditor(() => CountryEditor),
    toRecord: localized((d) => d.isoAlpha2),
    insert: intoLookup("countries"),
  },
  Currency: {
    permission: "MasterData.Currencies.Create",
    feature: "Apps.MasterData.Reference.Currencies",
    labelKey: "MasterData:AddNewCurrency",
    render: masterDataEditor(() => CurrencyEditor),
    toRecord: localized((d) => d.isoCode),
    insert: intoLookup("currencies"),
  },
  Driver: {
    permission: "MasterData.Drivers.Create",
    feature: "Apps.MasterData.Fleet.Drivers",
    labelKey: "MasterData:AddNewDriver",
    render: masterDataEditor(() => DriverEditor),
    toRecord: (d) => ({ code: d.code ?? null, name: d.fullName || [d.firstName, d.lastName].filter(Boolean).join(" ") }),
    insert: intoLookup("drivers"),
  },
  GeographicalRegion: {
    permission: "MasterData.GeographicalRegions.Create",
    feature: "Apps.MasterData.Reference.GeographicalRegions",
    labelKey: "MasterData:AddNewGeographicalRegion",
    render: masterDataEditor(() => GeographicalRegionEditor),
    toRecord: named,
    insert: intoLookup("geographicalRegions"),
  },
  Product: {
    permission: "MasterData.Products.Create",
    feature: "Apps.MasterData.Transport.Products",
    labelKey: "MasterData:AddNewProduct",
    render: masterDataEditor(() => ProductEditor),
    toRecord: named,
    insert: intoLookup("products"),
  },
  Relation: {
    permission: "MasterData.Relations.Create",
    feature: "Apps.MasterData.Relations.Relations",
    labelKey: "MasterData:AddNewRelation",
    render: masterDataEditor(() => RelationEditor),
    toRecord: named,
    insert: intoLookup("relations"),
  },
  RelationCategory: {
    permission: "MasterData.RelationCategories.Create",
    feature: "Apps.MasterData.Relations.RelationCategories",
    labelKey: "MasterData:AddNewRelationCategory",
    render: masterDataEditor(() => RelationCategoryEditor),
    toRecord: named,
    insert: intoLookup("relationCategories"),
  },
  Temperature: {
    permission: "MasterData.Temperatures.Create",
    feature: "Apps.MasterData.Transport.Temperatures",
    labelKey: "MasterData:AddNewTemperature",
    render: masterDataEditor(() => TemperatureEditor),
    toRecord: named,
    insert: intoLookup("temperatures"),
  },
  TransportCharacteristics: {
    permission: "MasterData.TransportCharacteristics.Create",
    feature: "Apps.MasterData.Fleet.TransportCharacteristics",
    labelKey: "MasterData:AddNewTransportCharacteristics",
    render: masterDataEditor(() => TransportCharacteristicsEditor),
    toRecord: named,
    insert: intoLookup("transportCharacteristics"),
  },
  TransportUnit: {
    permission: "MasterData.TransportUnits.Create",
    feature: "Apps.MasterData.Transport.TransportUnits",
    labelKey: "MasterData:AddNewTransportUnit",
    render: masterDataEditor(() => TransportUnitEditor),
    toRecord: named,
    insert: intoLookup("transportUnits"),
  },
  Vehicle: {
    permission: "MasterData.Vehicles.Create",
    feature: "Apps.MasterData.Fleet.Vehicles",
    labelKey: "MasterData:AddNewVehicle",
    render: masterDataEditor(() => VehicleEditor),
    toRecord: named,
    insert: intoLookup("vehicles"),
  },
  VehicleType: {
    permission: "MasterData.VehicleTypes.Create",
    feature: "Apps.MasterData.Fleet.VehicleTypes",
    labelKey: "MasterData:AddNewVehicleType",
    render: masterDataEditor(() => VehicleTypeEditor),
    toRecord: named,
    insert: intoLookup("vehicleTypes"),
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
  onCreated: (record: CreatedRecord<never>) => void;
}

/** The one "may this picker offer Add new …" gate: not read-only, the entity's Create permission, and its node's
 *  whole feature chain on (UI-PERM-006). For pickers that keep their own create dialog; `offer` uses it too. */
export function useCanCreateInPlace(): (entity: CreatableEntity, readOnly: boolean) => boolean {
  const isGranted = useIsGrantedChecker();
  const features = useAppFeatures();
  return (entity, readOnly) => {
    const entry: EntityEntry = ENTITIES[entity];
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
    <TDto = unknown,>(entity: CreatableEntity, readOnly: boolean, onCreated: (record: CreatedRecord<TDto>) => void): CreateInPlaceProps => {
      const entry: EntityEntry = ENTITIES[entity];
      return {
        createLabel: latest.current.t(entry.labelKey),
        onCreateNew: latest.current.canCreate(entity, readOnly)
          ? (term: string) => setPending({ seq: ++seq.current, entity, term, onCreated: onCreated as (record: CreatedRecord<never>) => void })
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
        })}
      </Fragment>
    );
  }

  return { offer, canCreate, dialog };
}
