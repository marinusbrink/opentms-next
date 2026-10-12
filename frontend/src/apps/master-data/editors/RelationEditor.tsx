import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { useQueryClient } from "@tanstack/react-query";
import { Archive, ArchiveRestore, Plus } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
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
import { AddressMap } from "@/apps/master-data/components/AddressMap";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import {
  CheckboxField,
  NumberField,
  TextAreaField,
  TextField,
  emailError,
  numberError,
  numberToText,
  parseNumber,
} from "@/apps/master-data/components/fields";
import {
  MasterDataEditor,
  type EditorTab,
  type FieldErrors,
  type FormContext,
} from "@/apps/master-data/components/MasterDataEditor";
import {
  AddressDialog,
  type AddressRow,
} from "@/apps/master-data/editors/AddressDialog";
import { timeZoneOptions } from "@/apps/master-data/components/time-zones";
import {
  ContactPersonDialog,
  contactFullName,
  type ContactRow,
} from "@/apps/master-data/editors/ContactPersonDialog";
import {
  BLOCK_REASONS,
  RELATION_ROLES,
  blockReasonKey,
  contactPersonRoleKey,
  countries,
  geographicalRegions,
  listToRoles,
  relationCategories,
  relationRoleKeys,
  relations,
  rolesToList,
  useArchiveRelation,
  useUnarchiveRelation,
  type RelationDto,
  type RelationRoles,
} from "@/domains/master-data/entities";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useLanguages } from "@/lib/i18n/languages";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The relation editor (design 43, UI design as revised by the PO on 2026-10-12 after the old OpenTMS
 * screen): `lg`. General: one block with categories, the Archived and Blocked switches (with the
 * block reason), regions and roles; then the Name block next to the Addresses block ("All addresses
 * equal" with the default address inline, stored without an address type, and a Map / Remarks
 * section); then the Contact information block. Then
 * Administration (legal numbers, preferred language, organisation units), Financial (bank accounts,
 * exactly one default), Addresses (the per-type addresses, in their own dialog; they override the
 * default), Contact persons, and Record identification. Archive and Unarchive are ribbon commands,
 * confirmed; the roles, Archived and Blocked show as badges after the title. */

interface BankRow {
  key: string;
  id: string | null;
  iban: string;
  bic: string;
  bankName: string;
  accountHolder: string;
  isDefault: boolean;
}

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
  categoryIds: string[];
  geographicalRegionIds: string[];
  organizationUnitIds: string[];
  description: string;
  /** "All addresses equal": the default address below serves every type without an address of its own. */
  allAddressesEqual: boolean;
  defaultAddressId: string | null;
  addrCountryId: string | null;
  addrPostalCode: string;
  addrCity: string;
  addrTimeZoneId: string | null;
  addrStreet: string;
  addrHouseNumber: string;
  addrAddressLine2: string;
  addrLatitude: string;
  addrLongitude: string;
  /** The addresses with an address type (the Addresses tab). */
  addresses: AddressRow[];
  contactPersons: ContactRow[];
  bankAccounts: BankRow[];
}

const DEFAULT_ADDRESS_FIELDS: (keyof Values & string)[] = [
  "allAddressesEqual",
  "addrCountryId",
  "addrPostalCode",
  "addrCity",
  "addrTimeZoneId",
  "addrStreet",
  "addrHouseNumber",
  "addrAddressLine2",
  "addrLatitude",
  "addrLongitude",
];

const hasDefaultAddressInput = (v: Values) =>
  Boolean(
    v.addrCountryId ||
    v.addrStreet.trim() ||
    v.addrPostalCode.trim() ||
    v.addrCity.trim(),
  );

let rowCounter = 0;

const initialValuesFor =
  (initialName: string | undefined) =>
  (dto: RelationDto | undefined): Values => {
    const defaultAddress = dto?.addresses?.find((a) => !a.addressTypeId);
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
      categoryIds: dto?.categoryIds ?? [],
      geographicalRegionIds: dto?.geographicalRegionIds ?? [],
      organizationUnitIds: dto?.organizationUnitIds ?? [],
      description: dto?.description ?? "",
      /* A new relation starts with one address for all types, like the old screen; a loaded one follows its data. */
      allAddressesEqual: dto ? defaultAddress != null : true,
      defaultAddressId: defaultAddress?.id ?? null,
      addrCountryId: defaultAddress?.countryId ?? null,
      addrPostalCode: defaultAddress?.postalCode ?? "",
      addrCity: defaultAddress?.city ?? "",
      addrTimeZoneId: defaultAddress?.timeZoneId ?? null,
      addrStreet: defaultAddress?.street ?? "",
      addrHouseNumber: defaultAddress?.houseNumber ?? "",
      addrAddressLine2: defaultAddress?.addressLine2 ?? "",
      addrLatitude: numberToText(defaultAddress?.latitude),
      addrLongitude: numberToText(defaultAddress?.longitude),
      addresses: (dto?.addresses ?? [])
        .filter((a) => Boolean(a.addressTypeId))
        .map((a) => ({
          key: a.id ?? `new-${++rowCounter}`,
          id: a.id ?? null,
          addressTypeId: a.addressTypeId ?? null,
          addressTypeName: a.addressTypeName ?? "",
          countryId: a.countryId,
          countryCode: a.countryCode ?? "",
          street: a.street,
          houseNumber: a.houseNumber ?? "",
          addressLine2: a.addressLine2 ?? "",
          postalCode: a.postalCode,
          city: a.city,
          latitude: numberToText(a.latitude),
          longitude: numberToText(a.longitude),
          timeZoneId: a.timeZoneId ?? null,
        })),
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

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.code.trim()) errors.code = "MasterData:CodeRequired";
  if (!v.name.trim()) errors.name = "MasterData:NameRequired";
  if (v.roles.length === 0) errors.roles = "MasterData:RolesRequired";
  if (v.isBlocked && v.blockReason === 0)
    errors.blockReason = "MasterData:BlockReasonRequired";
  const email = emailError(v.email);
  if (email) errors.email = email;
  if (v.allAddressesEqual && hasDefaultAddressInput(v)) {
    if (!v.addrCountryId) errors.addrCountryId = "MasterData:CountryRequired";
    if (!v.addrStreet.trim()) errors.addrStreet = "MasterData:StreetRequired";
    if (!v.addrPostalCode.trim())
      errors.addrPostalCode = "MasterData:PostalCodeRequired";
    if (!v.addrCity.trim()) errors.addrCity = "MasterData:CityRequired";
    const latitude = numberError(v.addrLatitude, { min: -90, max: 90 });
    if (latitude) errors.addrLatitude = latitude;
    const longitude = numberError(v.addrLongitude, { min: -180, max: 180 });
    if (longitude) errors.addrLongitude = longitude;
  }
  if (v.bankAccounts.some((b) => !b.iban.trim()))
    errors.bankAccounts = "MasterData:IbanRequired";
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
  ...DEFAULT_ADDRESS_FIELDS,
];

const ADMINISTRATION_FIELDS: (keyof Values & string)[] = [
  "chamberOfCommerceNumber",
  "vatNumber",
  "preferredCulture",
  "organizationUnitIds",
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
  const initialValues = useMemo(
    () => initialValuesFor(initialName),
    [initialName],
  );
  const create = relations.useCreate();
  const update = relations.useUpdate();
  const remove = relations.useDelete();
  const archive = useArchiveRelation();
  const unarchive = useUnarchiveRelation();
  const canDelete = useIsGranted("MasterData.Relations.Delete");

  const categoryLookup = relationCategories.useLookup(open);
  const regionLookup = geographicalRegions.useLookup(open);
  const countryLookup = countries.useLookup(open);
  const countryOptions = useMemo(
    () =>
      (countryLookup.data ?? []).map((c) => ({
        value: c.id ?? "",
        label: `${c.code ?? ""} ${c.name ?? ""}`.trim(),
      })),
    [countryLookup.data],
  );
  const zones = useMemo(timeZoneOptions, []);
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

  /* The nested dialogs live outside the tabs, so their result reaches the form through applyValues. */
  const [applyValues, setApplyValues] = useState<
    { key: number; update: (values: Values) => Values } | undefined
  >();
  const apply = useCallback(
    (update: (values: Values) => Values) =>
      setApplyValues((previous) => ({ key: (previous?.key ?? 0) + 1, update })),
    [],
  );
  const [addressDialog, setAddressDialog] = useState<{
    row: AddressRow | null;
    usedTypeIds: string[];
  } | null>(null);
  const [contactDialog, setContactDialog] = useState<{
    row: ContactRow | null;
  } | null>(null);

  /* The default address' time zone follows the country while it is empty or still a zone a previous
   * country filled in (the same rule as the address dialog). */
  const [zoneSource, setZoneSource] = useState<string | null>(null);
  const autoZone = useRef<string | null>(null);
  const zoneCountry = countries.useDetail(zoneSource ?? undefined);
  useEffect(() => {
    const dto = zoneCountry.data;
    if (zoneSource && dto?.id === zoneSource) {
      setZoneSource(null);
      const zone = dto.primaryTimeZoneId ?? null;
      if (zone) {
        const previous = autoZone.current;
        autoZone.current = zone;
        apply((values) =>
          !values.addrTimeZoneId || values.addrTimeZoneId === previous
            ? { ...values, addrTimeZoneId: zone }
            : values,
        );
      }
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
        description: text(v.description),
        categoryIds: v.categoryIds,
        geographicalRegionIds: v.geographicalRegionIds,
        organizationUnitIds: v.organizationUnitIds,
        addresses: [
          /* The default address (no type) only while "all addresses equal" is on and something was entered. */
          ...(v.allAddressesEqual && hasDefaultAddressInput(v)
            ? [
                {
                  id: v.defaultAddressId,
                  addressTypeId: null,
                  countryId: v.addrCountryId!,
                  street: v.addrStreet.trim(),
                  houseNumber: text(v.addrHouseNumber),
                  addressLine2: text(v.addrAddressLine2),
                  postalCode: v.addrPostalCode.trim(),
                  city: v.addrCity.trim(),
                  latitude: parseNumber(v.addrLatitude),
                  longitude: parseNumber(v.addrLongitude),
                  timeZoneId: v.addrTimeZoneId,
                },
              ]
            : []),
          ...v.addresses.map((a) => ({
            id: a.id,
            addressTypeId: a.addressTypeId!,
            countryId: a.countryId!,
            street: a.street.trim(),
            houseNumber: text(a.houseNumber),
            addressLine2: text(a.addressLine2),
            postalCode: a.postalCode.trim(),
            city: a.city.trim(),
            latitude: parseNumber(a.latitude),
            longitude: parseNumber(a.longitude),
            timeZoneId: a.timeZoneId,
          })),
        ],
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

  const addressColumns = useMemo<ColDef<AddressRow>[]>(
    () => [
      {
        field: "addressTypeName",
        headerName: t("MasterData:AddressType"),
        flex: 1,
        minWidth: 120,
      },
      {
        field: "street",
        headerName: t("MasterData:Street"),
        flex: 2,
        minWidth: 160,
      },
      {
        field: "houseNumber",
        headerName: t("MasterData:HouseNumber"),
        flex: 1,
        minWidth: 90,
      },
      {
        field: "postalCode",
        headerName: t("MasterData:PostalCode"),
        flex: 1,
        minWidth: 100,
      },
      {
        field: "city",
        headerName: t("MasterData:City"),
        flex: 2,
        minWidth: 140,
      },
      {
        field: "countryCode",
        headerName: t("MasterData:Country"),
        flex: 1,
        minWidth: 80,
      },
    ],
    [t],
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

  const addressesTab: EditorTab<Values> = {
    value: "addresses",
    labelKey: "MasterData:Addresses",
    fields: ["addresses"],
    render: (form) => (
      <div className="flex h-80 flex-col gap-2">
        <LazyLinesGrid<AddressRow>
          rows={form.values.addresses}
          onRowsChange={(rows) => form.set("addresses", rows)}
          getRowId={(row) => row.key}
          columnDefs={addressColumns}
          readOnly={form.readOnly}
          onOpen={(row) =>
            setAddressDialog({
              row,
              usedTypeIds: form.values.addresses
                .filter((a) => a.key !== row.key)
                .map((a) => a.addressTypeId ?? "")
                .filter(Boolean),
            })
          }
          toolbar={
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() =>
                setAddressDialog({
                  row: null,
                  usedTypeIds: form.values.addresses
                    .map((a) => a.addressTypeId ?? "")
                    .filter(Boolean),
                })
              }
            >
              <Plus /> {t("MasterData:NewAddress")}
            </Button>
          }
        />
      </div>
    ),
  };

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "General",
      fields: GENERAL_FIELDS,
      render: (form) => (
        <div className="flex flex-col gap-4">
          {/* Rows 1-2 (PO 2026-10-12): roles with the Archived and Blocked switches (block reason while blocked); categories and regions. */}
          <FormSection title={t("MasterData:Relation")}>
            <div className="flex flex-col gap-3">
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
                  <CheckboxField
                    form={form}
                    field="isArchived"
                    labelKey="MasterData:IsArchived"
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
                  />
                </FormField>
              </div>
            </div>
          </FormSection>
          {/* Row 4 (PO 2026-10-12): the Name and Contact information blocks stacked on the left, the
           * Addresses block (address section plus the Map / Remarks section) on the right. */}
          <div className="grid grid-cols-2 gap-4">
            <div className="flex flex-col gap-4">
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
            <FormSection
              title={t("MasterData:Addresses")}
              className="flex flex-col"
            >
              <CheckboxField
                form={form}
                field="allAddressesEqual"
                labelKey="MasterData:AllAddressesEqual"
                className="font-medium uppercase tracking-wide text-label"
              />
              {form.values.allAddressesEqual ? (
                <div className="grid grid-cols-6 gap-x-4 gap-y-3">
                  <FormField
                    label={t("MasterData:Country")}
                    htmlFor={form.id("addrCountryId")}
                    required={hasDefaultAddressInput(form.values)}
                    error={form.error("addrCountryId")}
                    floating="always"
                    className="col-span-2"
                  >
                    <ReferenceCombobox
                      id={form.id("addrCountryId")}
                      value={form.values.addrCountryId}
                      onChange={(next) => {
                        form.set("addrCountryId", next);
                        form.blur("addrCountryId");
                        if (next) setZoneSource(next);
                      }}
                      options={countryOptions}
                      disabled={form.readOnly}
                    />
                  </FormField>
                  <TextField
                    form={form}
                    field="addrPostalCode"
                    labelKey="MasterData:PostalCode"
                    maxLength={16}
                    className="col-span-2"
                  />
                  <TextField
                    form={form}
                    field="addrCity"
                    labelKey="MasterData:City"
                    maxLength={128}
                    className="col-span-2"
                  />
                  <TextField
                    form={form}
                    field="addrStreet"
                    labelKey="MasterData:Street"
                    maxLength={128}
                    className="col-span-4"
                  />
                  <TextField
                    form={form}
                    field="addrHouseNumber"
                    labelKey="MasterData:HouseNumber"
                    maxLength={16}
                    className="col-span-2"
                  />
                  <TextField
                    form={form}
                    field="addrAddressLine2"
                    labelKey="MasterData:AddressLine2"
                    maxLength={128}
                    className="col-span-6"
                  />
                  <FormField
                    label={t("MasterData:TimeZone")}
                    htmlFor={form.id("addrTimeZoneId")}
                    floating="always"
                    className="col-span-2"
                  >
                    <ReferenceCombobox
                      id={form.id("addrTimeZoneId")}
                      value={form.values.addrTimeZoneId}
                      onChange={(next) => form.set("addrTimeZoneId", next)}
                      options={zones}
                      disabled={form.readOnly}
                    />
                  </FormField>
                  <NumberField
                    form={form}
                    field="addrLongitude"
                    labelKey="MasterData:Longitude"
                    className="col-span-2"
                  />
                  <NumberField
                    form={form}
                    field="addrLatitude"
                    labelKey="MasterData:Latitude"
                    className="col-span-2"
                  />
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">
                  {t("MasterData:AddressesPerTypeHint")}
                </p>
              )}
              <Tabs
                defaultValue="map"
                className="mt-3 flex min-h-72 flex-1 flex-col gap-2"
              >
                <TabsList
                  variant="line"
                  className="h-auto justify-start gap-0 rounded-none p-0"
                >
                  <TabsTrigger value="map">{t("MasterData:Map")}</TabsTrigger>
                  <TabsTrigger value="remarks">
                    {t("MasterData:Remarks")}
                  </TabsTrigger>
                </TabsList>
                <TabsContent
                  value="map"
                  className="flex min-h-0 flex-1 flex-col"
                >
                  <AddressMap
                    latitude={
                      form.values.allAddressesEqual
                        ? parseNumber(form.values.addrLatitude)
                        : null
                    }
                    longitude={
                      form.values.allAddressesEqual
                        ? parseNumber(form.values.addrLongitude)
                        : null
                    }
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
        </div>
      ),
    },
    {
      value: "administration",
      labelKey: "MasterData:Administration",
      fields: ADMINISTRATION_FIELDS,
      render: (form) => (
        <div className="grid grid-cols-2 gap-4">
          <FormSection title={t("MasterData:Legal")}>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
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
            </div>
          </FormSection>
          <FormSection title={t("MasterData:Administration")}>
            <div className="grid grid-cols-1 gap-3">
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
              <FormField
                label={t("MasterData:OrganizationUnits")}
                htmlFor={form.id("organizationUnitIds")}
                floating="always"
              >
                <MultiSelect
                  id={form.id("organizationUnitIds")}
                  value={form.values.organizationUnitIds}
                  onChange={(next) => form.set("organizationUnitIds", next)}
                  options={unitOptions}
                  disabled={form.readOnly}
                />
              </FormField>
            </div>
          </FormSection>
        </div>
      ),
    },
    {
      value: "financial",
      labelKey: "MasterData:Financial",
      fields: ["bankAccounts"],
      render: (form) => (
        <FormSection title={t("MasterData:BankAccounts")}>
          <div className="flex h-72 flex-col gap-2">
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
      ),
    },
    addressesTab,
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

  return (
    <>
      <MasterDataEditor<Values, RelationDto>
        entityKey="Relation"
        queryEntity={relations.entity}
        size="lg"
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
      {addressDialog && (
        <AddressDialog
          open
          row={addressDialog.row}
          usedAddressTypeIds={addressDialog.usedTypeIds}
          readOnly={readOnly}
          onClose={() => setAddressDialog(null)}
          onSave={(row) =>
            apply((values) => ({
              ...values,
              addresses: values.addresses.some((a) => a.key === row.key)
                ? values.addresses.map((a) => (a.key === row.key ? row : a))
                : [...values.addresses, row],
            }))
          }
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
    </>
  );
}
