import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { useQueryClient } from "@tanstack/react-query";
import { Archive, ArchiveRestore, Plus } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import {
  FORM_SECTION_CAPTION,
  FormSection,
} from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { MultiSelect } from "@/components/ui/multi-select";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  AddressFields,
  hasAddressInput,
  newAddressRow,
  validateAddressRow,
  type AddressRow,
} from "@/apps/master-data/components/AddressFields";
import { AddressMap } from "@/apps/master-data/components/AddressMap";
import { TransportCharacteristicsEditor } from "@/apps/master-data/editors/TransportCharacteristicsEditor";
import {
  ADDRESS_LOOKUP_FEATURE,
  ADDRESS_LOOKUP_PERMISSION,
  fetchAddressSuggestions,
  lookupAddress,
} from "@/domains/integrations/address-lookup";
import {
  useCanCreateInPlace,
  useCreateInPlace,
  withId,
} from "@/apps/master-data/components/create-in-place";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import {
  CheckboxField,
  TextAreaField,
  TextField,
  emailError,
  numberToText,
  parseNumber,
} from "@/apps/master-data/components/fields";
import {
  MasterDataEditor,
  type EditorTab,
  type FieldErrors,
  type FormContext,
} from "@/apps/master-data/components/MasterDataEditor";
import { timeZoneOptions } from "@/apps/master-data/components/time-zones";
import {
  ContactPersonDialog,
  contactFullName,
  type ContactRow,
} from "@/apps/master-data/editors/ContactPersonDialog";
import {
  BLOCK_REASONS,
  RELATION_ROLES,
  addressTypes,
  blockReasonKey,
  contactPersonRoleKey,
  countries,
  currencies,
  geographicalRegions,
  hasOwnCompanyRole,
  listToRoles,
  relationCategories,
  relationRoleKeys,
  relations,
  rolesToList,
  useArchiveRelation,
  useDefaultCountry,
  useDefaultCurrency,
  toTransportCharacteristicOption,
  transportCharacteristicLookupPage,
  useUnarchiveRelation,
  type RelationDto,
  type RelationRoles,
  type TransportCharacteristicOption,
  type TransportCharacteristicsDto,
} from "@/domains/master-data/entities";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsFeatureEnabled, useIsGranted } from "@/lib/auth/permissions";
import { useLanguages } from "@/lib/i18n/languages";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The relation editor (design 43 as revised by the PO on 2026-10-12/13 after the old OpenTMS screen):
 * `lg`. General: the Relation block (roles with the Archived and Blocked switches and the block reason
 * while blocked; categories and regions), then the Name and Contact information blocks stacked next to
 * the Addresses block. The Addresses block is the old one: "All addresses equal" with one inline
 * address (stored without an address type), or, when off, one tab per address type with the same
 * inline fields; below it the Map / Remarks section. Then Administration (the old Administratie tab:
 * company keys, currency, language, the n bank accounts, external ids, transport characteristics),
 * Contact persons and Record identification. Archive and Unarchive are ribbon commands, confirmed; the roles, Archived
 * and Blocked show as badges after the title. */

const DEFAULT_KEY = "default";

/** The roles MultiSelect holds the flags as strings; the own company is never archived (PO 2026-10-16). */
const isOwnCompany = (roles: string[]) => hasOwnCompanyRole(listToRoles(roles));

/* The in-block tab strips (address types, Map / Remarks) wear the editor strip's look: uppercase, brand
 * underline, compact (UI-TOK-001: the brand token, no raw colour). */
const SUB_TAB_LIST = "h-8 justify-start gap-0 rounded-none p-0";
const SUB_TAB =
  "h-8 flex-none rounded-none border-0 px-3 text-label font-semibold tracking-wide text-muted-foreground uppercase after:bottom-0 after:bg-brand hover:text-foreground data-active:text-foreground";
/** The address-type tabs read like the block's caption (the switch beside them): caption size and brand ink, the active one underlined. */
const CAPTION_TAB = `h-7 flex-none rounded-none border-0 px-3 ${FORM_SECTION_CAPTION} after:bottom-0 after:bg-brand hover:text-brand data-active:text-brand`;

interface BankRow {
  key: string;
  id: string | null;
  iban: string;
  bic: string;
  bankName: string;
  accountHolder: string;
  isDefault: boolean;
}

interface ExternalIdRow {
  key: string;
  id: string | null;
  source: string;
  identifier: string;
}

/** A characteristic the relation carries; picked with the EntityLookup above the grid, shown read-only. */
type CharacteristicRow = TransportCharacteristicOption;

interface Values {
  code: string;
  name: string;
  fullName: string;
  roles: string[];
  isArchived: boolean;
  isBlocked: boolean;
  blockReason: number;
  phone: string;
  mobile: string;
  email: string;
  website: string;
  preferredCulture: string | null;
  chamberOfCommerceNumber: string;
  vatNumber: string;
  debtorNumber: string;
  creditorNumber: string;
  carrierCode: string;
  currencyId: string | null;
  externalIdentifiers: ExternalIdRow[];
  transportCharacteristics: CharacteristicRow[];
  categoryIds: string[];
  geographicalRegionIds: string[];
  organizationUnitIds: string[];
  description: string;
  /** "All addresses equal": the default address serves every type without an address of its own. */
  allAddressesEqual: boolean;
  defaultAddress: AddressRow;
  /** The addresses with an address type (one tab each while "all addresses equal" is off). */
  addresses: AddressRow[];
  contactPersons: ContactRow[];
  bankAccounts: BankRow[];
}

let rowCounter = 0;

const toAddressRow = (
  a: NonNullable<RelationDto["addresses"]>[number],
): AddressRow => ({
  key: a.id ?? `new-${++rowCounter}`,
  id: a.id ?? null,
  addressTypeId: a.addressTypeId ?? null,
  countryId: a.countryId,
  street: a.street,
  houseNumber: a.houseNumber ?? "",
  houseNumberSuffix: a.houseNumberSuffix ?? "",
  addressLine2: a.addressLine2 ?? "",
  postalCode: a.postalCode,
  city: a.city,
  latitude: numberToText(a.latitude),
  longitude: numberToText(a.longitude),
  timeZoneId: a.timeZoneId ?? null,
});

/** A new relation's default address starts with the tenant's default country and its zone (PO 2026-10-13), as part of the initial values so an untouched record stays clean. */
const initialValuesFor =
  (
    initialName: string | undefined,
    defaultCountry: { id: string; timeZoneId: string | null } | null,
    defaultCurrencyId: string | null,
  ) =>
  (dto: RelationDto | undefined): Values => {
    const stored = dto?.addresses ?? [];
    const defaultAddress = stored.find((a) => !a.addressTypeId);
    return {
      code: dto?.code ?? "",
      name: dto?.name ?? initialName ?? "",
      fullName: dto?.fullName ?? "",
      roles: rolesToList(dto?.roles),
      isArchived: dto?.isArchived ?? false,
      isBlocked: dto?.isBlocked ?? false,
      blockReason: dto?.blockReason ?? 0,
      phone: dto?.phone ?? "",
      mobile: dto?.mobile ?? "",
      email: dto?.email ?? "",
      website: dto?.website ?? "",
      preferredCulture: dto?.preferredCulture ?? null,
      chamberOfCommerceNumber: dto?.chamberOfCommerceNumber ?? "",
      vatNumber: dto?.vatNumber ?? "",
      debtorNumber: dto?.debtorNumber ?? "",
      creditorNumber: dto?.creditorNumber ?? "",
      carrierCode: dto?.carrierCode ?? "",
      /* A new relation starts with the tenant's default currency, like the old screen's required "Valuta". */
      currencyId: dto ? (dto.currencyId ?? null) : defaultCurrencyId,
      externalIdentifiers: (dto?.externalIdentifiers ?? []).map((e) => ({
        key: e.id ?? `new-${++rowCounter}`,
        id: e.id ?? null,
        source: e.source,
        identifier: e.identifier,
      })),
      transportCharacteristics: (dto?.transportCharacteristics ?? []).map(
        toTransportCharacteristicOption,
      ),
      categoryIds: dto?.categoryIds ?? [],
      geographicalRegionIds: dto?.geographicalRegionIds ?? [],
      organizationUnitIds: dto?.organizationUnitIds ?? [],
      description: dto?.description ?? "",
      /* A new relation starts with one address for all types, like the old screen; a loaded one follows its data. */
      allAddressesEqual: dto ? defaultAddress != null : true,
      defaultAddress: defaultAddress
        ? { ...toAddressRow(defaultAddress), key: DEFAULT_KEY }
        : {
            ...newAddressRow(),
            key: DEFAULT_KEY,
            countryId: dto ? null : (defaultCountry?.id ?? null),
            timeZoneId: dto ? null : (defaultCountry?.timeZoneId ?? null),
          },
      addresses: stored
        .filter((a) => Boolean(a.addressTypeId))
        .map(toAddressRow),
      contactPersons: (dto?.contactPersons ?? []).map((c) => ({
        key: c.id ?? `new-${++rowCounter}`,
        id: c.id ?? null,
        firstName: c.firstName,
        middleName: c.middleName ?? "",
        lastName: c.lastName,
        role: c.role ?? 0,
        jobTitle: c.jobTitle ?? "",
        department: c.department ?? "",
        email: c.email ?? "",
        phone: c.phone ?? "",
        mobile: c.mobile ?? "",
      })),
      bankAccounts: (dto?.bankAccounts ?? []).map((b) => ({
        key: b.id ?? `new-${++rowCounter}`,
        id: b.id ?? null,
        iban: b.iban,
        bic: b.bic ?? "",
        bankName: b.bankName ?? "",
        accountHolder: b.accountHolder ?? "",
        isDefault: b.isDefault ?? false,
      })),
    };
  };

const addressIncomplete = (row: AddressRow) =>
  hasAddressInput(row) && Object.keys(validateAddressRow(row)).length > 0;

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:CodeRequired";
  if (!v.name.trim()) errors.name = "MasterData:NameRequired";
  if (v.roles.length === 0) errors.roles = "MasterData:RolesRequired";
  if (v.isBlocked && v.blockReason === 0)
    errors.blockReason = "MasterData:BlockReasonRequired";
  const email = emailError(v.email);
  if (email) errors.email = email;
  if (v.allAddressesEqual) {
    if (addressIncomplete(v.defaultAddress))
      errors.defaultAddress = "MasterData:AddressIncomplete";
  } else if (v.addresses.some(addressIncomplete)) {
    errors.addresses = "MasterData:AddressIncomplete";
  }
  if (v.bankAccounts.some((b) => !b.iban.trim()))
    errors.bankAccounts = "MasterData:IbanRequired";
  if (
    v.externalIdentifiers.some((e) => !e.source.trim() || !e.identifier.trim())
  )
    errors.externalIdentifiers = "MasterData:ExternalIdentifierIncomplete";
  return errors;
};

const GENERAL_FIELDS: (keyof Values & string)[] = [
  "code",
  "name",
  "fullName",
  "roles",
  "isArchived",
  "isBlocked",
  "blockReason",
  "phone",
  "mobile",
  "email",
  "website",
  "categoryIds",
  "geographicalRegionIds",
  "description",
  "allAddressesEqual",
  "defaultAddress",
  "addresses",
];

const ADMINISTRATION_FIELDS: (keyof Values & string)[] = [
  "debtorNumber",
  "creditorNumber",
  "chamberOfCommerceNumber",
  "vatNumber",
  "carrierCode",
  "organizationUnitIds",
  "currencyId",
  "preferredCulture",
  "bankAccounts",
  "externalIdentifiers",
  "transportCharacteristics",
];

export function RelationEditor({
  mode,
  id,
  readOnly,
  open,
  onClose,
  onSaved,
  initialName,
}: EntityEditorProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const detail = relations.useDetail(mode === "edit" ? id : undefined);
  const defaultCountry = useDefaultCountry(open);
  const defaultCountryId = defaultCountry.data?.id ?? null;
  const defaultCountryZone = defaultCountry.data?.primaryTimeZoneId ?? null;
  const defaultCurrency = useDefaultCurrency(open);
  const defaultCurrencyId = defaultCurrency.data?.id ?? null;
  const initialValues = useMemo(
    () =>
      initialValuesFor(
        initialName,
        defaultCountryId
          ? { id: defaultCountryId, timeZoneId: defaultCountryZone }
          : null,
        defaultCurrencyId,
      ),
    [initialName, defaultCountryId, defaultCountryZone, defaultCurrencyId],
  );
  /* A new relation waits for the default country (cached after the first open) so its initial values hold it. */
  const ready =
    mode !== "create" ||
    (defaultCountry.isFetched && defaultCurrency.isFetched);
  const create = relations.useCreate();
  const update = relations.useUpdate();
  const remove = relations.useDelete();
  const archive = useArchiveRelation();
  const unarchive = useUnarchiveRelation();
  const canDelete = useIsGranted("MasterData.Relations.Delete");
  /* Design 44: the postal-code lookup, while the tenant has it on and the user may ask it. */
  const lookupFeature = useIsFeatureEnabled(ADDRESS_LOOKUP_FEATURE);
  const lookupGranted = useIsGranted(ADDRESS_LOOKUP_PERMISSION);
  const lookupOn = lookupFeature && lookupGranted;

  const categoryLookup = relationCategories.useLookup(open);
  const regionLookup = geographicalRegions.useLookup(open);
  const countryLookup = countries.useLookup(open);
  const typeLookup = addressTypes.useLookup(open);
  const currencyLookup = currencies.useLookup(open);
  const currencyOptions = useMemo(
    () =>
      (currencyLookup.data ?? []).map((c) => ({
        value: c.id ?? "",
        label: `${c.code ?? ""} ${c.name ?? ""}`.trim(),
      })),
    [currencyLookup.data],
  );
  /* The one "Add new …" gate (permission + node feature chain + not read-only, UI-LOOK-004 / UI-PERM-006). */
  const canCreate = useCanCreateInPlace();
  const inPlace = useCreateInPlace();
  /* DEVIATION(UI-LOOK-001): organisation units are a MultiSelect, not a TreeSelect: a relation belongs to
   * several units and TreeSelect is single-valued, approved in docs/designs/43-master-data-relations-and-drivers.md. */
  const units = useAllOrganizationUnits();
  const languages = useLanguages();
  const categoryOptions = useMemo(
    () =>
      (categoryLookup.data ?? []).map((i) => ({
        id: i.id ?? "",
        label: i.name ?? "",
      })),
    [categoryLookup.data],
  );
  const regionOptions = useMemo(
    () =>
      (regionLookup.data ?? []).map((i) => ({
        id: i.id ?? "",
        label: i.name ?? "",
      })),
    [regionLookup.data],
  );
  /* The two-letter code only, like the old screen; the combobox searches it. */
  const countryOptions = useMemo(
    () =>
      (countryLookup.data ?? []).map((c) => ({
        value: c.id ?? "",
        label: c.code ?? "",
      })),
    [countryLookup.data],
  );
  const onLookup = useMemo(
    () =>
      lookupOn
        ? async (row: AddressRow) => {
            const code = countryLookup.data?.find(
              (c) => c.id === row.countryId,
            )?.code;
            if (!code) return null;
            const found = await lookupAddress(
              code,
              row.postalCode,
              row.houseNumber.trim() || null,
            );
            return found
              ? {
                  street: found.street,
                  houseNumber: found.houseNumber,
                  city: found.city,
                  latitude: found.latitude,
                  longitude: found.longitude,
                }
              : null;
          }
        : undefined,
    [lookupOn, countryLookup.data],
  );
  const onSuggest = useMemo(
    () =>
      lookupOn
        ? async (row: AddressRow, term: string) => {
            const code = countryLookup.data?.find(
              (c) => c.id === row.countryId,
            )?.code;
            if (!code) return [];
            return (await fetchAddressSuggestions(code, term)).map((line) => ({
              ...line,
              label: line.label ?? line.postalCode ?? "",
              postalCode: line.postalCode ?? "",
            }));
          }
        : undefined,
    [lookupOn, countryLookup.data],
  );
  const addressTypeTabs = useMemo(
    () =>
      (typeLookup.data ?? []).map((item) => ({
        id: item.id ?? "",
        name: item.name ?? "",
      })),
    [typeLookup.data],
  );
  const zones = useMemo(timeZoneOptions, []);
  const unitOptions = useMemo(
    () =>
      (units.data ?? []).map((u) => ({
        id: u.id ?? "",
        label: u.displayName ?? "",
      })),
    [units.data],
  );
  const languageOptions = useMemo(
    () =>
      languages.map((l) => ({ value: l.cultureName, label: l.displayName })),
    [languages],
  );
  const roleOptions = useMemo(
    () => RELATION_ROLES.map((r) => ({ id: String(r.value), label: t(r.key) })),
    [t],
  );
  const blockReasonOptions = useMemo(
    () =>
      BLOCK_REASONS.map((r) => ({ value: String(r.value), label: t(r.key) })),
    [t],
  );

  /* The nested dialog and the time-zone follow live outside the tabs, so their result reaches the form through applyValues. */
  const [applyValues, setApplyValues] = useState<
    { key: number; update: (values: Values) => Values } | undefined
  >();
  const apply = useCallback(
    (update: (values: Values) => Values) =>
      setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update })),
    [],
  );
  const [contactDialog, setContactDialog] = useState<{
    row: ContactRow | null;
  } | null>(null);
  const [activeTypeId, setActiveTypeId] = useState<string | null>(null);

  /* An address' time zone follows the country while it is empty or still a zone a previous country
   * filled in; a zone the user chose stays. The source names the address (default or an address type). */
  const [zoneSource, setZoneSource] = useState<{
    addressKey: string;
    countryId: string;
  } | null>(null);
  const autoZone = useRef<Map<string, string>>(new Map());
  /* A new address starts with the default country and its zone; that zone counts as filled in by a
   * country, so picking another country still lets the zone follow. */
  const prefilled = (row: AddressRow): AddressRow => {
    if (row.countryId || !defaultCountryId) return row;
    const key = row.addressTypeId ?? DEFAULT_KEY;
    if (defaultCountryZone && !autoZone.current.has(key))
      autoZone.current.set(key, defaultCountryZone);
    return {
      ...row,
      countryId: defaultCountryId,
      timeZoneId: row.timeZoneId ?? defaultCountryZone,
    };
  };
  useEffect(() => {
    if (
      mode === "create" &&
      defaultCountryZone &&
      !autoZone.current.has(DEFAULT_KEY)
    )
      autoZone.current.set(DEFAULT_KEY, defaultCountryZone);
  }, [mode, defaultCountryZone]);
  const zoneCountry = countries.useDetail(zoneSource?.countryId);
  useEffect(() => {
    const dto = zoneCountry.data;
    if (zoneSource && dto?.id === zoneSource.countryId) {
      const { addressKey } = zoneSource;
      setZoneSource(null);
      const zone = dto.primaryTimeZoneId ?? null;
      if (!zone) return;
      const previous = autoZone.current.get(addressKey) ?? null;
      autoZone.current.set(addressKey, zone);
      const follow = (row: AddressRow) =>
        !row.timeZoneId || row.timeZoneId === previous
          ? { ...row, timeZoneId: zone }
          : row;
      apply((values) =>
        addressKey === DEFAULT_KEY
          ? { ...values, defaultAddress: follow(values.defaultAddress) }
          : {
              ...values,
              addresses: values.addresses.map((a) =>
                a.addressTypeId === addressKey ? follow(a) : a,
              ),
            },
      );
    }
  }, [zoneCountry.data, zoneSource, apply]);

  const [confirmArchive, setConfirmArchive] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const archived = detail.data?.isArchived === true;

  const toggleArchive = async () => {
    if (!id) return;
    try {
      await (archived ? unarchive : archive).mutateAsync(id);
      void queryClient.invalidateQueries({
        queryKey: masterDataQueryKey(relations.entity),
      });
      setConfirmArchive(false);
      toast.success(
        t(
          archived
            ? "MasterData:RelationUnarchived"
            : "MasterData:RelationArchived",
        ),
      );
      onSaved();
      onClose();
    } catch (err) {
      setConfirmArchive(false);
      setActionError(getApiErrorMessage(err, t));
    }
  };

  const save = useCallback(
    async (v: Values, dto: RelationDto | undefined) => {
      const text = (value: string) => value.trim() || null;
      const toDto = (a: AddressRow) => ({
        id: a.id,
        addressTypeId: a.addressTypeId,
        countryId: a.countryId!,
        street: a.street.trim(),
        houseNumber: text(a.houseNumber),
        houseNumberSuffix: text(a.houseNumberSuffix),
        addressLine2: text(a.addressLine2),
        postalCode: a.postalCode.trim(),
        city: a.city.trim(),
        latitude: parseNumber(a.latitude),
        longitude: parseNumber(a.longitude),
        timeZoneId: a.timeZoneId,
      });
      /* The default address while "all addresses equal" is on, else the per-type addresses; an untouched address is none. */
      const addresses = (v.allAddressesEqual ? [v.defaultAddress] : v.addresses)
        .filter(hasAddressInput)
        .map(toDto);
      const body = {
        code: v.code.trim().toUpperCase(),
        name: v.name.trim(),
        fullName: text(v.fullName),
        roles: listToRoles(v.roles) as RelationRoles,
        isArchived: v.isArchived,
        isBlocked: v.isBlocked,
        blockReason: (v.isBlocked
          ? v.blockReason
          : 0) as RelationDto["blockReason"],
        phone: text(v.phone),
        mobile: text(v.mobile),
        email: text(v.email),
        website: text(v.website),
        preferredCulture: v.preferredCulture,
        chamberOfCommerceNumber: text(v.chamberOfCommerceNumber),
        vatNumber: text(v.vatNumber),
        debtorNumber: text(v.debtorNumber),
        creditorNumber: text(v.creditorNumber),
        carrierCode: text(v.carrierCode),
        currencyId: v.currencyId,
        externalIdentifiers: v.externalIdentifiers.map((e) => ({
          id: e.id,
          source: e.source.trim(),
          identifier: e.identifier.trim(),
        })),
        transportCharacteristicIds: v.transportCharacteristics.map((c) => c.id),
        description: text(v.description),
        categoryIds: v.categoryIds,
        geographicalRegionIds: v.geographicalRegionIds,
        organizationUnitIds: v.organizationUnitIds,
        addresses,
        contactPersons: v.contactPersons.map((c) => ({
          id: c.id,
          firstName: c.firstName.trim(),
          middleName: text(c.middleName),
          lastName: c.lastName.trim(),
          role: c.role,
          jobTitle: text(c.jobTitle),
          department: text(c.department),
          email: text(c.email),
          phone: text(c.phone),
          mobile: text(c.mobile),
        })),
        bankAccounts: v.bankAccounts.map((b) => ({
          id: b.id,
          iban: b.iban.replace(/\s+/g, "").toUpperCase(),
          bic: text(b.bic),
          bankName: text(b.bankName),
          accountHolder: text(b.accountHolder),
          isDefault: b.isDefault,
        })),
      };
      if (mode === "create") {
        return create.mutateAsync(body);
      }
      return update.mutateAsync({
        id: id!,
        dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" },
      });
    },
    [create, update, mode, id],
  );

  const contactColumns = useMemo<ColDef<ContactRow>[]>(
    () => [
      {
        colId: "name",
        headerName: t("MasterData:FullName"),
        flex: 2,
        minWidth: 160,
        valueGetter: (p) => (p.data ? contactFullName(p.data) : ""),
      },
      {
        field: "role",
        headerName: t("MasterData:Role"),
        flex: 1,
        minWidth: 120,
        valueFormatter: (p) =>
          t(contactPersonRoleKey(p.value as ContactRow["role"])),
      },
      {
        field: "email",
        headerName: t("MasterData:Email"),
        flex: 2,
        minWidth: 160,
      },
      {
        field: "phone",
        headerName: t("MasterData:Phone"),
        flex: 1,
        minWidth: 120,
      },
    ],
    [t],
  );

  const bankColumns = useMemo<ColDef<BankRow>[]>(
    () => [
      {
        field: "iban",
        headerName: t("MasterData:Iban"),
        flex: 2,
        minWidth: 200,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 34 },
      },
      {
        field: "bic",
        headerName: t("MasterData:Bic"),
        flex: 1,
        minWidth: 110,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 11 },
      },
      {
        field: "bankName",
        headerName: t("MasterData:BankName"),
        flex: 2,
        minWidth: 140,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 64 },
      },
      {
        field: "accountHolder",
        headerName: t("MasterData:AccountHolder"),
        flex: 2,
        minWidth: 160,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 128 },
      },
      {
        field: "isDefault",
        headerName: t("MasterData:IsDefault"),
        flex: 1,
        minWidth: 100,
        editable: !readOnly,
        cellRenderer: "agCheckboxCellRenderer",
        cellEditor: "agCheckboxCellEditor",
      },
    ],
    [readOnly, t],
  );

  const externalIdColumns = useMemo<ColDef<ExternalIdRow>[]>(
    () => [
      {
        field: "identifier",
        headerName: t("MasterData:ExternalIdentifier"),
        flex: 1,
        minWidth: 140,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 128 },
      },
      {
        field: "source",
        headerName: t("MasterData:ExternalSource"),
        flex: 1,
        minWidth: 120,
        editable: !readOnly,
        cellEditor: "agTextCellEditor",
        cellEditorParams: { maxLength: 64 },
      },
    ],
    [readOnly, t],
  );

  /* Read-only lines: a characteristic is added with the EntityLookup in the toolbar and removed with the row action. */
  const characteristicColumns = useMemo<ColDef<CharacteristicRow>[]>(
    () => [
      {
        field: "name",
        headerName: t("MasterData:Name"),
        flex: 2,
        minWidth: 160,
      },
      {
        field: "code",
        headerName: t("MasterData:Code"),
        flex: 1,
        minWidth: 100,
      },
      {
        field: "description",
        headerName: t("MasterData:Description"),
        flex: 2,
        minWidth: 160,
      },
    ],
    [t],
  );
  const addCharacteristic = useCallback(
    (option: TransportCharacteristicOption) =>
      apply((values) =>
        values.transportCharacteristics.some((c) => c.id === option.id)
          ? values
          : {
              ...values,
              transportCharacteristics: [
                ...values.transportCharacteristics,
                option,
              ],
            },
      ),
    [apply],
  );
  const [newCharacteristic, setNewCharacteristic] = useState<{
    term: string;
  } | null>(null);

  /* Exactly one default account: marking one unmarks the others (the server marks the first when none is). */
  const setBankRows = (form: FormContext<Values>, rows: BankRow[]) => {
    const previous = form.values.bankAccounts;
    const newlyDefault = rows.find(
      (r) => r.isDefault && !previous.find((p) => p.key === r.key)?.isDefault,
    );
    form.set(
      "bankAccounts",
      newlyDefault
        ? rows.map((r) => ({ ...r, isDefault: r.key === newlyDefault.key }))
        : rows,
    );
  };

  /* The typed address of an address type, or an empty one that only enters the form once edited. */
  const typedAddress = (form: FormContext<Values>, typeId: string) =>
    form.values.addresses.find((a) => a.addressTypeId === typeId) ??
    prefilled(newAddressRow(typeId));
  const setTypedAddress = (form: FormContext<Values>, row: AddressRow) => {
    const exists = form.values.addresses.some(
      (a) => a.addressTypeId === row.addressTypeId,
    );
    form.set(
      "addresses",
      exists
        ? form.values.addresses.map((a) =>
            a.addressTypeId === row.addressTypeId ? row : a,
          )
        : [...form.values.addresses, row],
    );
  };

  /* "Add new country…" in an address' country picker (UI-LOOK-004): the saved country goes on that address
   * (the default one or an address type's) and its time zone follows, as when a country is picked. */
  const countryCreate = (form: FormContext<Values>, addressKey: string) => {
    const { onCreateNew, createLabel } = inPlace.offer(
      "Country",
      form.readOnly,
      (r) => {
        if (addressKey === DEFAULT_KEY) {
          form.update((v) => ({
            ...v,
            defaultAddress: { ...v.defaultAddress, countryId: r.id },
          }));
        } else {
          form.update((v) => ({
            ...v,
            addresses: v.addresses.some((a) => a.addressTypeId === addressKey)
              ? v.addresses.map((a) =>
                  a.addressTypeId === addressKey
                    ? { ...a, countryId: r.id }
                    : a,
                )
              : [
                  ...v.addresses,
                  { ...prefilled(newAddressRow(addressKey)), countryId: r.id },
                ],
          }));
        }
        setZoneSource({ addressKey, countryId: r.id });
      },
    );
    return { onCreateCountry: onCreateNew, createCountryLabel: createLabel };
  };

  const renderAddressesBlock = (form: FormContext<Values>) => {
    const allEqual = form.values.allAddressesEqual;
    const currentTypeId =
      activeTypeId && addressTypeTabs.some((x) => x.id === activeTypeId)
        ? activeTypeId
        : (addressTypeTabs[0]?.id ?? null);
    const activeRow: AddressRow | null = allEqual
      ? form.values.defaultAddress
      : currentTypeId
        ? typedAddress(form, currentTypeId)
        : null;
    const showErrors = Boolean(
      form.error("defaultAddress") || form.error("addresses"),
    );
    return (
      <div className="flex min-h-0 flex-1 flex-col gap-3">
        <FormSection
          aria-label={t("MasterData:Addresses")}
          className="flex flex-col"
        >
          {/* The old header is the caption: the switch, and while it is off one tab per address type beside it. */}
          <div className="mb-3 flex min-h-7 items-center gap-4">
            <CheckboxField
              form={form}
              field="allAddressesEqual"
              labelKey="MasterData:AllAddressesEqual"
              className={`h-auto shrink-0 ${FORM_SECTION_CAPTION}`}
            />
            {!allEqual && currentTypeId && (
              <Tabs
                value={currentTypeId}
                onValueChange={(value) => setActiveTypeId(String(value))}
                className="min-w-0 flex-1"
              >
                <TabsList
                  variant="line"
                  className="h-7 justify-start gap-0 rounded-none p-0"
                  aria-label={t("MasterData:AddressType")}
                >
                  {addressTypeTabs.map((type) => (
                    <TabsTrigger
                      key={type.id}
                      value={type.id}
                      className={CAPTION_TAB}
                    >
                      {type.name}
                    </TabsTrigger>
                  ))}
                </TabsList>
              </Tabs>
            )}
          </div>
          {(form.error("defaultAddress") || form.error("addresses")) && (
            <p className="mt-2 text-label text-destructive">
              {form.error("defaultAddress") ?? form.error("addresses")}
            </p>
          )}
          <div className="mt-2">
            {allEqual ? (
              <AddressFields
                idPrefix={form.id("defaultAddress")}
                row={form.values.defaultAddress}
                onChange={(row) => form.set("defaultAddress", row)}
                readOnly={form.readOnly}
                showErrors={showErrors}
                countryOptions={countryOptions}
                zoneOptions={zones}
                onCountryPicked={(countryId) =>
                  setZoneSource({ addressKey: DEFAULT_KEY, countryId })
                }
                {...countryCreate(form, DEFAULT_KEY)}
                onLookup={onLookup}
                onSuggest={onSuggest}
              />
            ) : currentTypeId ? (
              <AddressFields
                key={currentTypeId}
                idPrefix={form.id(`address-${currentTypeId}`)}
                row={typedAddress(form, currentTypeId)}
                onChange={(row) => setTypedAddress(form, row)}
                readOnly={form.readOnly}
                showErrors={showErrors}
                countryOptions={countryOptions}
                zoneOptions={zones}
                onCountryPicked={(countryId) =>
                  setZoneSource({ addressKey: currentTypeId, countryId })
                }
                {...countryCreate(form, currentTypeId)}
                onLookup={onLookup}
                onSuggest={onSuggest}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                {t("MasterData:AddressesPerTypeHint")}
              </p>
            )}
          </div>
        </FormSection>
        <FormSection
          aria-label={t("MasterData:Map")}
          className="flex min-h-0 flex-1 flex-col"
        >
          <Tabs
            defaultValue="map"
            className="flex min-h-40 flex-1 flex-col gap-2"
          >
            <TabsList variant="line" className={SUB_TAB_LIST}>
              <TabsTrigger value="map" className={SUB_TAB}>
                {t("MasterData:Map")}
              </TabsTrigger>
              <TabsTrigger value="remarks" className={SUB_TAB}>
                {t("MasterData:Remarks")}
              </TabsTrigger>
            </TabsList>
            <TabsContent value="map" className="flex min-h-0 flex-1 flex-col">
              <AddressMap
                latitude={activeRow ? parseNumber(activeRow.latitude) : null}
                longitude={activeRow ? parseNumber(activeRow.longitude) : null}
              />
            </TabsContent>
            <TabsContent
              value="remarks"
              className="flex min-h-0 flex-1 flex-col"
            >
              <TextAreaField
                form={form}
                field="description"
                labelKey="MasterData:Remarks"
                maxLength={2500}
                className="flex-1"
              />
            </TabsContent>
          </Tabs>
        </FormSection>
      </div>
    );
  };

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: GENERAL_FIELDS,
      render: (form) => (
        <div className="flex flex-col gap-3">
          <FormSection aria-label={t("MasterData:Relation")}>
            <div className="flex flex-col gap-2">
              {/* Row 1: the roles grow into the space the switches and the block reason (while blocked) leave. */}
              <div className="flex items-end gap-4">
                <FormField
                  label={t("MasterData:Roles")}
                  htmlFor={form.id("roles")}
                  required
                  error={form.error("roles")}
                  floating="always"
                  className="min-w-0 flex-1"
                >
                  <MultiSelect
                    id={form.id("roles")}
                    value={form.values.roles}
                    onChange={(next) => {
                      form.set("roles", next);
                      form.blur("roles");
                    }}
                    options={roleOptions}
                    disabled={form.readOnly}
                  />
                </FormField>
                <div className="flex shrink-0 items-end gap-4">
                  {/* The own company is never archived (PO 2026-10-16): the switch is off limits while the role is
                   * picked; a switch that is already on stays enabled so it can be cleared before saving. */}
                  <CheckboxField
                    form={form}
                    field="isArchived"
                    labelKey="MasterData:IsArchived"
                    disabled={
                      isOwnCompany(form.values.roles) && !form.values.isArchived
                    }
                    describedBy={
                      isOwnCompany(form.values.roles)
                        ? form.id("isArchived") + "-hint"
                        : undefined
                    }
                  />
                  <CheckboxField
                    form={form}
                    field="isBlocked"
                    labelKey="MasterData:IsBlocked"
                  />
                  {form.values.isBlocked && (
                    <FormField
                      label={t("MasterData:BlockReason")}
                      htmlFor={form.id("blockReason")}
                      required
                      error={form.error("blockReason")}
                      floating="always"
                      className="w-56"
                    >
                      <Select
                        items={blockReasonOptions}
                        value={
                          form.values.blockReason
                            ? String(form.values.blockReason)
                            : null
                        }
                        onValueChange={(next) => {
                          form.set("blockReason", next ? Number(next) : 0);
                          form.blur("blockReason");
                        }}
                        disabled={form.readOnly}
                      >
                        <SelectTrigger
                          id={form.id("blockReason")}
                          className="h-10 w-full bg-background"
                          aria-invalid={
                            form.error("blockReason") ? true : undefined
                          }
                        >
                          <SelectValue
                            placeholder={t("MasterData:BlockReason")}
                          />
                        </SelectTrigger>
                        <SelectContent>
                          {blockReasonOptions.map((item) => (
                            <SelectItem key={item.value} value={item.value}>
                              {item.label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </FormField>
                  )}
                </div>
              </div>
              {isOwnCompany(form.values.roles) && (
                <p
                  id={form.id("isArchived") + "-hint"}
                  className="text-label text-muted-foreground"
                >
                  {t("MasterData:OwnCompanyCannotBeArchivedHint")}
                </p>
              )}
              {/* Row 2: classification. */}
              <div className="grid grid-cols-2 gap-x-4">
                <FormField
                  label={t("MasterData:Categories")}
                  htmlFor={form.id("categoryIds")}
                  floating="always"
                  className="min-w-0"
                >
                  <MultiSelect
                    id={form.id("categoryIds")}
                    value={form.values.categoryIds}
                    onChange={(next) => form.set("categoryIds", next)}
                    options={categoryOptions}
                    disabled={form.readOnly}
                    {...inPlace.offer("RelationCategory", form.readOnly, (r) =>
                      form.update((v) => ({
                        ...v,
                        categoryIds: withId(v.categoryIds, r.id),
                      })),
                    )}
                  />
                </FormField>
                <FormField
                  label={t("MasterData:GeographicalRegions")}
                  htmlFor={form.id("geographicalRegionIds")}
                  floating="always"
                  className="min-w-0"
                >
                  <MultiSelect
                    id={form.id("geographicalRegionIds")}
                    value={form.values.geographicalRegionIds}
                    onChange={(next) => form.set("geographicalRegionIds", next)}
                    options={regionOptions}
                    disabled={form.readOnly}
                    {...inPlace.offer(
                      "GeographicalRegion",
                      form.readOnly,
                      (r) =>
                        form.update((v) => ({
                          ...v,
                          geographicalRegionIds: withId(
                            v.geographicalRegionIds,
                            r.id,
                          ),
                        })),
                    )}
                  />
                </FormField>
              </div>
            </div>
          </FormSection>
          {/* The Name and Contact information blocks stacked on the left, the Addresses block on the right. */}
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-3">
              <FormSection title={t("MasterData:Name")}>
                <div className="grid grid-cols-3 gap-x-4 gap-y-3">
                  <TextField
                    form={form}
                    field="code"
                    labelKey="MasterData:Code"
                    required
                    maxLength={32}
                    upper
                  />
                  <TextField
                    form={form}
                    field="name"
                    labelKey="MasterData:Name"
                    required
                    maxLength={128}
                    className="col-span-2"
                  />
                  <TextField
                    form={form}
                    field="fullName"
                    labelKey="MasterData:RelationFullName"
                    maxLength={128}
                    className="col-span-3"
                  />
                </div>
              </FormSection>
              <FormSection title={t("MasterData:ContactInformation")}>
                <div className="grid grid-cols-1 gap-y-3">
                  <TextField
                    form={form}
                    field="phone"
                    labelKey="MasterData:Phone"
                    maxLength={32}
                  />
                  <TextField
                    form={form}
                    field="mobile"
                    labelKey="MasterData:Mobile"
                    maxLength={32}
                  />
                  <TextField
                    form={form}
                    field="email"
                    labelKey="MasterData:Email"
                    maxLength={256}
                  />
                  <TextField
                    form={form}
                    field="website"
                    labelKey="MasterData:Website"
                    maxLength={256}
                  />
                </div>
              </FormSection>
            </div>
            {renderAddressesBlock(form)}
          </div>
        </div>
      ),
    },
    {
      value: "administration",
      labelKey: "MasterData:Administration",
      fields: ADMINISTRATION_FIELDS,
      render: (form) => (
        /* The tab fills the dialog: the grids take the height that is left, so the tab never scrolls. */
        <div className="grid min-h-0 flex-1 grid-cols-2 gap-3">
          {/* Left: the company's administrative keys and the bank accounts (the old Administratie tab; n accounts, PO 2026-10-13). */}
          <div className="flex min-h-0 flex-col gap-3">
            <FormSection title={t("MasterData:Company")}>
              <div className="grid grid-cols-1 gap-y-3">
                <TextField
                  form={form}
                  field="debtorNumber"
                  labelKey="MasterData:DebtorNumber"
                  maxLength={32}
                  upper
                />
                <TextField
                  form={form}
                  field="creditorNumber"
                  labelKey="MasterData:CreditorNumber"
                  maxLength={32}
                  upper
                />
                <TextField
                  form={form}
                  field="chamberOfCommerceNumber"
                  labelKey="MasterData:ChamberOfCommerceNumber"
                  maxLength={32}
                />
                <TextField
                  form={form}
                  field="vatNumber"
                  labelKey="MasterData:VatNumber"
                  maxLength={32}
                  upper
                />
                <TextField
                  form={form}
                  field="carrierCode"
                  labelKey="MasterData:CarrierCode"
                  maxLength={32}
                />
                <FormField
                  label={t("MasterData:Department")}
                  htmlFor={form.id("organizationUnitIds")}
                  floating="always"
                >
                  <MultiSelect
                    id={form.id("organizationUnitIds")}
                    value={form.values.organizationUnitIds}
                    onChange={(next) => form.set("organizationUnitIds", next)}
                    options={unitOptions}
                    disabled={form.readOnly}
                    {...inPlace.offer("OrganizationUnit", form.readOnly, (r) =>
                      form.update((v) => ({
                        ...v,
                        organizationUnitIds: withId(
                          v.organizationUnitIds,
                          r.id,
                        ),
                      })),
                    )}
                  />
                </FormField>
                <FormField
                  label={t("MasterData:Currency")}
                  htmlFor={form.id("currencyId")}
                  floating="always"
                >
                  <ReferenceCombobox
                    id={form.id("currencyId")}
                    value={form.values.currencyId}
                    onChange={(next) => form.set("currencyId", next)}
                    options={currencyOptions}
                    disabled={form.readOnly}
                    {...inPlace.offer("Currency", form.readOnly, (r) =>
                      form.set("currencyId", r.id),
                    )}
                  />
                </FormField>
                <FormField
                  label={t("MasterData:PreferredCulture")}
                  htmlFor={form.id("preferredCulture")}
                  floating="always"
                >
                  <ReferenceCombobox
                    id={form.id("preferredCulture")}
                    value={form.values.preferredCulture}
                    onChange={(next) => form.set("preferredCulture", next)}
                    options={languageOptions}
                    disabled={form.readOnly}
                  />
                </FormField>
              </div>
            </FormSection>
            <FormSection
              title={t("MasterData:Bank")}
              className="flex min-h-0 flex-1 flex-col"
            >
              <div className="flex min-h-32 flex-1 flex-col gap-2">
                {form.error("bankAccounts") && (
                  <p className="text-label text-destructive">
                    {form.error("bankAccounts")}
                  </p>
                )}
                <LazyLinesGrid<BankRow>
                  rows={form.values.bankAccounts}
                  onRowsChange={(rows) => setBankRows(form, rows)}
                  getRowId={(row) => row.key}
                  columnDefs={bankColumns}
                  readOnly={form.readOnly}
                  toolbar={
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        form.set("bankAccounts", [
                          ...form.values.bankAccounts,
                          {
                            key: `new-${++rowCounter}`,
                            id: null,
                            iban: "",
                            bic: "",
                            bankName: "",
                            accountHolder: "",
                            isDefault: form.values.bankAccounts.length === 0,
                          },
                        ])
                      }
                    >
                      <Plus /> {t("MasterData:AddBankAccount")}
                    </Button>
                  }
                />
              </div>
            </FormSection>
          </div>
          {/* Right: the relation's ids in other systems and its transport characteristics. */}
          <div className="flex min-h-0 flex-col gap-3">
            <FormSection
              title={t("MasterData:ExternalIdentifiers")}
              className="flex min-h-0 flex-1 flex-col"
            >
              <div className="flex min-h-32 flex-1 flex-col gap-2">
                {form.error("externalIdentifiers") && (
                  <p className="text-label text-destructive">
                    {form.error("externalIdentifiers")}
                  </p>
                )}
                <LazyLinesGrid<ExternalIdRow>
                  rows={form.values.externalIdentifiers}
                  onRowsChange={(rows) => form.set("externalIdentifiers", rows)}
                  getRowId={(row) => row.key}
                  columnDefs={externalIdColumns}
                  readOnly={form.readOnly}
                  toolbar={
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        form.set("externalIdentifiers", [
                          ...form.values.externalIdentifiers,
                          {
                            key: `new-${++rowCounter}`,
                            id: null,
                            source: "",
                            identifier: "",
                          },
                        ])
                      }
                    >
                      <Plus /> {t("MasterData:AddExternalIdentifier")}
                    </Button>
                  }
                />
              </div>
            </FormSection>
            <FormSection
              title={t("MasterData:TransportCharacteristics")}
              className="flex min-h-0 flex-1 flex-col"
            >
              <div className="flex min-h-32 flex-1 flex-col gap-2">
                <LazyLinesGrid<CharacteristicRow>
                  rows={form.values.transportCharacteristics}
                  onRowsChange={(rows) =>
                    form.set("transportCharacteristics", rows)
                  }
                  getRowId={(row) => row.id}
                  columnDefs={characteristicColumns}
                  readOnly={form.readOnly}
                  toolbar={
                    <>
                      <label
                        htmlFor={form.id("transportCharacteristics")}
                        className="sr-only"
                      >
                        {t("MasterData:AddTransportCharacteristic")}
                      </label>
                      <EntityLookup<TransportCharacteristicOption>
                        id={form.id("transportCharacteristics")}
                        value={null}
                        onChange={(next) => {
                          if (next) addCharacteristic(next);
                        }}
                        fetchPage={transportCharacteristicLookupPage}
                        getLabel={(c) => c.name}
                        renderOption={(c) => (
                          <>
                            <div className="font-medium">{c.name}</div>
                            <div className="text-label text-muted-foreground">
                              {[c.code, c.description]
                                .filter(Boolean)
                                .join(", ")}
                            </div>
                          </>
                        )}
                        onCreateNew={
                          canCreate("TransportCharacteristics", form.readOnly)
                            ? (term) => setNewCharacteristic({ term })
                            : undefined
                        }
                        createLabel={t(
                          "MasterData:AddNewTransportCharacteristics",
                        )}
                        placeholder={t("MasterData:AddTransportCharacteristic")}
                        className="w-72"
                      />
                    </>
                  }
                />
              </div>
            </FormSection>
          </div>
        </div>
      ),
    },
    {
      value: "contactPersons",
      labelKey: "MasterData:ContactPersons",
      fields: ["contactPersons"],
      render: (form) => (
        <div className="flex h-80 flex-col gap-2">
          <LazyLinesGrid<ContactRow>
            rows={form.values.contactPersons}
            onRowsChange={(rows) => form.set("contactPersons", rows)}
            getRowId={(row) => row.key}
            columnDefs={contactColumns}
            readOnly={form.readOnly}
            onOpen={(row) => setContactDialog({ row })}
            toolbar={
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => setContactDialog({ row: null })}
              >
                <Plus /> {t("MasterData:NewContactPerson")}
              </Button>
            }
          />
        </div>
      ),
    },
  ];

  const badges =
    mode === "edit" && detail.data ? (
      <>
        {relationRoleKeys(detail.data.roles).map((key) => (
          <Badge key={key} variant="secondary">
            {t(key)}
          </Badge>
        ))}
        {archived && (
          <Badge variant="outline">{t("MasterData:Archived")}</Badge>
        )}
        {detail.data.isBlocked && (
          <Badge variant="destructive">
            {t("MasterData:IsBlocked")}
            {blockReasonKey(detail.data.blockReason)
              ? `: ${t(blockReasonKey(detail.data.blockReason)!)}`
              : ""}
          </Badge>
        )}
      </>
    ) : undefined;

  /* The editor seeds its form state when it mounts: a new relation is not mounted before its default country is known. */
  if (!ready) return null;

  return (
    <>
      <MasterDataEditor<Values, RelationDto>
        entityKey="Relation"
        queryEntity={relations.entity}
        size="full"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.name ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        extraCommands={
          mode === "edit" && !readOnly
            ? [
                {
                  id: "archive",
                  labelKey: archived
                    ? "MasterData:Unarchive"
                    : "MasterData:Archive",
                  icon: archived ? ArchiveRestore : Archive,
                  groupKey: "MasterData:Relation",
                  disabled: !detail.data,
                  onClick: () => setConfirmArchive(true),
                },
              ]
            : undefined
        }
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        notice={
          actionError ? (
            <Alert variant="destructive">
              <AlertDescription>{actionError}</AlertDescription>
            </Alert>
          ) : undefined
        }
        badges={badges}
        applyValues={applyValues}
      />
      {newCharacteristic && (
        <TransportCharacteristicsEditor
          mode="create"
          initialName={newCharacteristic.term}
          readOnly={false}
          open
          onClose={() => setNewCharacteristic(null)}
          onSaved={(saved) => {
            const created = saved as TransportCharacteristicsDto | undefined;
            if (!created?.id) return;
            addCharacteristic({
              id: created.id,
              code: created.code ?? "",
              name: created.name ?? "",
              description: created.description ?? null,
            });
          }}
        />
      )}
      {contactDialog && (
        <ContactPersonDialog
          open
          row={contactDialog.row}
          readOnly={readOnly}
          onClose={() => setContactDialog(null)}
          onSave={(row) =>
            apply((values) => ({
              ...values,
              contactPersons: values.contactPersons.some(
                (c) => c.key === row.key,
              )
                ? values.contactPersons.map((c) =>
                    c.key === row.key ? row : c,
                  )
                : [...values.contactPersons, row],
            }))
          }
        />
      )}
      <ConfirmDialog
        open={confirmArchive}
        onOpenChange={(next) => {
          if (!next) setConfirmArchive(false);
        }}
        title={t(
          archived
            ? "MasterData:UnarchiveRelation"
            : "MasterData:ArchiveRelation",
        )}
        description={t(
          archived
            ? "MasterData:UnarchiveRelationQuestion"
            : "MasterData:ArchiveRelationQuestion",
        ).replace("{0}", detail.data?.name ?? "")}
        confirmLabel={t(
          archived ? "MasterData:Unarchive" : "MasterData:Archive",
        )}
        onConfirm={toggleArchive}
      />
      {inPlace.dialog}
    </>
  );
}
