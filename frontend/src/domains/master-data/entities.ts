import { useMutation, useQuery } from "@tanstack/react-query";

import type {
  EntityLookupPage,
  EntityLookupQuery,
} from "@/components/ui/entity-lookup";
import { apiClient } from "@/lib/api/client";
import {
  LOOKUP_TAKE,
  createMasterDataHooks,
  masterDataQueryKey,
  type LookupItem,
  type Schemas,
} from "@/domains/master-data/shared";

/* One typed hook set per entity (design 41). Row = grid, Dto = editor, Create/Update = what the
 * editor sends. The schema names carry the backend namespaces. */

export type CurrencyRow =
  Schemas["OpenTms.MasterData.Reference.CurrencyRowDto"];
export type CurrencyDto = Schemas["OpenTms.MasterData.Reference.CurrencyDto"];
export type CurrencyCreateDto =
  Schemas["OpenTms.MasterData.Reference.CurrencyCreateDto"];
export type CurrencyUpdateDto =
  Schemas["OpenTms.MasterData.Reference.CurrencyUpdateDto"];
export const currencies = createMasterDataHooks<
  CurrencyRow,
  CurrencyDto,
  CurrencyCreateDto,
  CurrencyUpdateDto,
  { isActive?: boolean }
>("currencies", "/api/master-data/currency");

export function useSetDefaultCurrency() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/currency/{id}/set-default",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

/** The tenant's one default country (PO 2026-10-13): new addresses start with it. */
export function useSetDefaultCountry() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/country/{id}/set-default",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useDefaultCountry(enabled = true) {
  return useQuery({
    queryKey: ["masterData", "countries", "default"],
    enabled,
    queryFn: async (): Promise<CountryDto | null> => {
      const { data, error, response } = await apiClient.GET(
        "/api/master-data/country/default",
      );
      if (error) throw error;
      return response.status === 204
        ? null
        : ((data as CountryDto | undefined) ?? null);
    },
  });
}

export type CountryRow = Schemas["OpenTms.MasterData.Reference.CountryRowDto"];
export type CountryDto = Schemas["OpenTms.MasterData.Reference.CountryDto"];
export type CountryCreateDto =
  Schemas["OpenTms.MasterData.Reference.CountryCreateDto"];
export type CountryUpdateDto =
  Schemas["OpenTms.MasterData.Reference.CountryUpdateDto"];
export const countries = createMasterDataHooks<
  CountryRow,
  CountryDto,
  CountryCreateDto,
  CountryUpdateDto
>("countries", "/api/master-data/country");

export type AddressTypeKind =
  Schemas["OpenTms.MasterData.Reference.AddressTypeKind"];
export type AddressTypeRow =
  Schemas["OpenTms.MasterData.Reference.AddressTypeRowDto"];
export type AddressTypeDto =
  Schemas["OpenTms.MasterData.Reference.AddressTypeDto"];
export type AddressTypeCreateDto =
  Schemas["OpenTms.MasterData.Reference.AddressTypeCreateDto"];
export type AddressTypeUpdateDto =
  Schemas["OpenTms.MasterData.Reference.AddressTypeUpdateDto"];
export const addressTypes = createMasterDataHooks<
  AddressTypeRow,
  AddressTypeDto,
  AddressTypeCreateDto,
  AddressTypeUpdateDto
>("addressTypes", "/api/master-data/address-type");
export const ADDRESS_TYPE_KINDS: { value: AddressTypeKind; key: string }[] = [
  { value: 1, key: "MasterData:AddressTypeKind.Visiting" },
  { value: 2, key: "MasterData:AddressTypeKind.Postal" },
  { value: 0, key: "MasterData:AddressTypeKind.Other" },
];
export function addressTypeKindKey(kind: AddressTypeKind | undefined): string {
  return (
    ADDRESS_TYPE_KINDS.find((k) => k.value === kind)?.key ??
    "MasterData:AddressTypeKind.Other"
  );
}

export type RelationCategoryRow =
  Schemas["OpenTms.MasterData.Relations.RelationCategoryRowDto"];
export type RelationCategoryDto =
  Schemas["OpenTms.MasterData.Relations.RelationCategoryDto"];
export type RelationCategoryCreateDto =
  Schemas["OpenTms.MasterData.Relations.RelationCategoryCreateDto"];
export type RelationCategoryUpdateDto =
  Schemas["OpenTms.MasterData.Relations.RelationCategoryUpdateDto"];
export const relationCategories = createMasterDataHooks<
  RelationCategoryRow,
  RelationCategoryDto,
  RelationCategoryCreateDto,
  RelationCategoryUpdateDto
>("relationCategories", "/api/master-data/relation-category");

export type TransportCharacteristicsRow =
  Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsRowDto"];
export type TransportCharacteristicsDto =
  Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsDto"];
export type TransportCharacteristicsCreateDto =
  Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsCreateDto"];
export type TransportCharacteristicsUpdateDto =
  Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsUpdateDto"];
export const transportCharacteristics = createMasterDataHooks<
  TransportCharacteristicsRow,
  TransportCharacteristicsDto,
  TransportCharacteristicsCreateDto,
  TransportCharacteristicsUpdateDto
>("transportCharacteristics", "/api/master-data/transport-characteristics");

/** The tenant's default currency (one per tenant), for a new relation's currency (PO 2026-10-13). */
export function useDefaultCurrency(enabled = true) {
  return useQuery({
    queryKey: ["masterData", "currencies", "default"],
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<{ id: string; isoCode: string } | null> => {
      const { data, error } = await apiClient.GET("/api/master-data/currency", {
        params: {
          query: {
            StartRow: 0,
            EndRow: 1,
            SortBy: "isDefault",
            SortDir: "desc",
          },
        },
      });
      if (error) throw error;
      const row = (data?.rows ?? [])[0];
      return row?.isDefault && row.id
        ? { id: row.id, isoCode: row.isoCode ?? "" }
        : null;
    },
  });
}

/** Server-searched pages of transport characteristics for EntityLookup (UI-LOOK-002); rows carry code and description. */
export async function transportCharacteristicLookupPage({
  term,
  skip,
  take,
}: EntityLookupQuery): Promise<
  EntityLookupPage<TransportCharacteristicOption>
> {
  const { data, error } = await apiClient.GET(
    "/api/master-data/transport-characteristics/lookup-page",
    { params: { query: { term: term || undefined, skip, take } } },
  );
  if (error) throw error;
  return {
    items: (data?.items ?? []).map(toTransportCharacteristicOption),
    totalCount: data?.totalCount ?? 0,
  };
}

export interface TransportCharacteristicOption {
  id: string;
  code: string;
  name: string;
  description: string | null;
}
export function toTransportCharacteristicOption(
  dto: TransportCharacteristicsRow,
): TransportCharacteristicOption {
  return {
    id: dto.id ?? "",
    code: dto.code ?? "",
    name: dto.name ?? "",
    description: dto.description ?? null,
  };
}

export type VehicleTypeKind =
  Schemas["OpenTms.MasterData.Fleet.VehicleTypeKind"];
export type VehicleTypeRow =
  Schemas["OpenTms.MasterData.Fleet.VehicleTypeRowDto"];
export type VehicleTypeDto = Schemas["OpenTms.MasterData.Fleet.VehicleTypeDto"];
export type VehicleTypeCreateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleTypeCreateDto"];
export type VehicleTypeUpdateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleTypeUpdateDto"];
export const vehicleTypes = createMasterDataHooks<
  VehicleTypeRow,
  VehicleTypeDto,
  VehicleTypeCreateDto,
  VehicleTypeUpdateDto
>("vehicleTypes", "/api/master-data/vehicle-type");
export const VEHICLE_TYPE_KINDS: { value: VehicleTypeKind; key: string }[] = [
  { value: 1, key: "MasterData:VehicleTypeKind.Tractor" },
  { value: 2, key: "MasterData:VehicleTypeKind.Trailer" },
  { value: 3, key: "MasterData:VehicleTypeKind.Rigid" },
  { value: 4, key: "MasterData:VehicleTypeKind.Van" },
];
export function vehicleTypeKindKey(kind: VehicleTypeKind | undefined): string {
  return VEHICLE_TYPE_KINDS.find((k) => k.value === kind)?.key ?? "";
}

export type ArchivableExtra = { includeArchived?: boolean };

export type TemperatureRow =
  Schemas["OpenTms.MasterData.Transport.TemperatureRowDto"];
export type TemperatureDto =
  Schemas["OpenTms.MasterData.Transport.TemperatureDto"];
export type TemperatureCreateDto =
  Schemas["OpenTms.MasterData.Transport.TemperatureCreateDto"];
export type TemperatureUpdateDto =
  Schemas["OpenTms.MasterData.Transport.TemperatureUpdateDto"];
export const temperatures = createMasterDataHooks<
  TemperatureRow,
  TemperatureDto,
  TemperatureCreateDto,
  TemperatureUpdateDto,
  ArchivableExtra
>("temperatures", "/api/master-data/temperature");

export type ProductRow = Schemas["OpenTms.MasterData.Transport.ProductRowDto"];
export type ProductDto = Schemas["OpenTms.MasterData.Transport.ProductDto"];
export type ProductCreateDto =
  Schemas["OpenTms.MasterData.Transport.ProductCreateDto"];
export type ProductUpdateDto =
  Schemas["OpenTms.MasterData.Transport.ProductUpdateDto"];
export const products = createMasterDataHooks<
  ProductRow,
  ProductDto,
  ProductCreateDto,
  ProductUpdateDto,
  ArchivableExtra
>("products", "/api/master-data/product");

export type TransportUnitRow =
  Schemas["OpenTms.MasterData.Transport.TransportUnitRowDto"];
export type TransportUnitDto =
  Schemas["OpenTms.MasterData.Transport.TransportUnitDto"];
export type TransportUnitCreateDto =
  Schemas["OpenTms.MasterData.Transport.TransportUnitCreateDto"];
export type TransportUnitUpdateDto =
  Schemas["OpenTms.MasterData.Transport.TransportUnitUpdateDto"];
export type TransportUnitLoadFactorDto =
  Schemas["OpenTms.MasterData.Transport.TransportUnitLoadFactorDto"];
export const transportUnits = createMasterDataHooks<
  TransportUnitRow,
  TransportUnitDto,
  TransportUnitCreateDto,
  TransportUnitUpdateDto,
  ArchivableExtra
>("transportUnits", "/api/master-data/transport-unit");

export type GeographicalRegionRow =
  Schemas["OpenTms.MasterData.Regions.GeographicalRegionRowDto"];
export type GeographicalRegionDto =
  Schemas["OpenTms.MasterData.Regions.GeographicalRegionDto"];
export type GeographicalRegionCreateDto =
  Schemas["OpenTms.MasterData.Regions.GeographicalRegionCreateDto"];
export type GeographicalRegionUpdateDto =
  Schemas["OpenTms.MasterData.Regions.GeographicalRegionUpdateDto"];
export type PostalCodeRangeDto =
  Schemas["OpenTms.MasterData.Regions.GeographicalRegionPostalCodeRangeDto"];
export const geographicalRegions = createMasterDataHooks<
  GeographicalRegionRow,
  GeographicalRegionDto,
  GeographicalRegionCreateDto,
  GeographicalRegionUpdateDto,
  { countryId?: string }
>("geographicalRegions", "/api/master-data/geographical-region");

/** The regions whose ranges match a postcode in a country (PostalCodeRangeMatcher on the server). */
export function useRegionMatches(
  countryId: string | undefined,
  postalCode: string,
) {
  const code = postalCode.trim();
  return useQuery({
    queryKey: masterDataQueryKey(
      "geographicalRegions",
      "matches",
      countryId,
      code,
    ),
    enabled: countryId != null && code.length > 0,
    queryFn: async (): Promise<LookupItem[]> => {
      const { data, error } = await apiClient.GET(
        "/api/master-data/geographical-region/matches/{countryId}",
        {
          params: {
            path: { countryId: countryId! },
            query: { postalCode: code },
          },
        },
      );
      if (error) throw error;
      return data ?? [];
    },
  });
}

/* ── Run 2 (design 43): Relation and Driver ─────────────────────────────────────────────────────── */

export type RelationRoles =
  Schemas["OpenTms.MasterData.Relations.RelationRoles"];
export type ContactPersonRole =
  Schemas["OpenTms.MasterData.Relations.ContactPersonRole"];
export type RelationRow =
  Schemas["OpenTms.MasterData.Relations.RelationRowDto"];
export type RelationDto = Schemas["OpenTms.MasterData.Relations.RelationDto"];
export type RelationCreateDto =
  Schemas["OpenTms.MasterData.Relations.RelationCreateDto"];
export type RelationUpdateDto =
  Schemas["OpenTms.MasterData.Relations.RelationUpdateDto"];
export type RelationAddressDto =
  Schemas["OpenTms.MasterData.Relations.RelationAddressDto"];
export type RelationContactPersonDto =
  Schemas["OpenTms.MasterData.Relations.RelationContactPersonDto"];
export type RelationBankAccountDto =
  Schemas["OpenTms.MasterData.Relations.RelationBankAccountDto"];
export type RelationLookupDto =
  Schemas["OpenTms.MasterData.Relations.RelationLookupDto"];
export type RelationMatchRequestDto =
  Schemas["OpenTms.MasterData.Relations.RelationMatchRequestDto"];
export type RelationMatchResultDto =
  Schemas["OpenTms.MasterData.Relations.RelationMatchResultDto"];
/** The relation list filters (design 43 API contract); `roles` is the flags value. */
export type RelationListExtra = {
  includeArchived?: boolean;
  roles?: number;
  categoryId?: string;
  organizationUnitId?: string;
};

export const relations = createMasterDataHooks<
  RelationRow,
  RelationDto,
  RelationCreateDto,
  RelationUpdateDto,
  RelationListExtra
>("relations", "/api/master-data/relation");

/** The flags of `RelationRoles` as the UI lists them; a relation carries one or more. */
export const RELATION_ROLES: { value: number; key: string }[] = [
  { value: 1, key: "MasterData:Role.Customer" },
  { value: 2, key: "MasterData:Role.Carrier" },
  { value: 4, key: "MasterData:Role.Creditor" },
  { value: 8, key: "MasterData:Role.Depot" },
  { value: 16, key: "MasterData:Role.OwnCompany" },
];
/** Carrier | OwnCompany: the roles a driver's employer must carry (design 43). */
export const EMPLOYER_ROLES = 2 | 16;
/** The OwnCompany flag: one relation per tenant, never archived (PO 2026-10-16). */
export const OWN_COMPANY_ROLE = 16;
export function hasOwnCompanyRole(flags: number | null | undefined): boolean {
  return ((flags ?? 0) & OWN_COMPANY_ROLE) === OWN_COMPANY_ROLE;
}

/** The generated enum names the single flags; a combination is the same flags enum to the server. */
export function asRoles(flags: number | undefined): RelationRoles | undefined {
  return flags == null || flags === 0 ? undefined : (flags as RelationRoles);
}
export function rolesToList(flags: number | undefined): string[] {
  return RELATION_ROLES.filter((r) => ((flags ?? 0) & r.value) === r.value).map(
    (r) => String(r.value),
  );
}
export function listToRoles(list: string[]): number {
  return list.reduce((sum, v) => sum | Number(v), 0);
}
export function relationRoleKeys(flags: number | undefined): string[] {
  return RELATION_ROLES.filter((r) => ((flags ?? 0) & r.value) === r.value).map(
    (r) => r.key,
  );
}

export const CONTACT_PERSON_ROLES: { value: ContactPersonRole; key: string }[] =
  [
    { value: 0, key: "MasterData:ContactPersonRole.Other" },
    { value: 1, key: "MasterData:ContactPersonRole.Planning" },
    { value: 2, key: "MasterData:ContactPersonRole.Invoicing" },
    { value: 3, key: "MasterData:ContactPersonRole.Sales" },
    { value: 4, key: "MasterData:ContactPersonRole.Management" },
  ];
/** Why a relation is blocked (PO 2026-10-12); 0 = none, only valid while not blocked. */
export const BLOCK_REASONS: { value: number; key: string }[] = [
  { value: 1, key: "MasterData:BlockReason.PaymentArrears" },
  { value: 2, key: "MasterData:BlockReason.CreditLimitExceeded" },
  { value: 3, key: "MasterData:BlockReason.Insolvency" },
  { value: 4, key: "MasterData:BlockReason.Dispute" },
  { value: 5, key: "MasterData:BlockReason.Other" },
];

export function blockReasonKey(reason: number | undefined): string | undefined {
  return BLOCK_REASONS.find((r) => r.value === reason)?.key;
}

export function contactPersonRoleKey(
  role: ContactPersonRole | undefined,
): string {
  return (
    CONTACT_PERSON_ROLES.find((r) => r.value === role)?.key ??
    "MasterData:ContactPersonRole.Other"
  );
}

/** The option an EntityLookup over relations holds (UI-LOOK-003: the name closed; code and place in the list). */
export interface RelationOption {
  id: string;
  code: string;
  name: string;
  city: string | null;
  countryCode: string | null;
  roles: number;
}
export function toRelationOption(dto: RelationLookupDto): RelationOption {
  return {
    id: dto.id ?? "",
    code: dto.code ?? "",
    name: dto.name ?? "",
    city: dto.city ?? null,
    countryCode: dto.countryCode ?? null,
    roles: dto.roles ?? 0,
  };
}
/** Server-searched pages for EntityLookup (UI-LOOK-002), optionally narrowed to relations carrying one of `roles`. */
export function relationLookupPage(
  roles?: number,
): (q: EntityLookupQuery) => Promise<EntityLookupPage<RelationOption>> {
  return async ({ term, skip, take }) => {
    const { data, error } = await apiClient.GET(
      "/api/master-data/relation/lookup-page",
      {
        params: {
          query: { term: term || undefined, skip, take, roles: asRoles(roles) },
        },
      },
    );
    if (error) throw error;
    return {
      items: (data?.items ?? []).map(toRelationOption),
      totalCount: data?.totalCount ?? 0,
    };
  };
}

/** The addresses of one relation as a query (the pickers' list, and the default address the order page's fast entry
 *  takes without a picker: `queryClient.fetchQuery(relationAddressesQuery(id))`). */
export function relationAddressesQuery(id: string | undefined) {
  return {
    queryKey: masterDataQueryKey("relations", "addresses", id),
    queryFn: async (): Promise<RelationAddressDto[]> => {
      const { data, error } = await apiClient.GET(
        "/api/master-data/relation/{id}/addresses",
        { params: { path: { id: id! } } },
      );
      if (error) throw error;
      return data ?? [];
    },
  };
}

export function useRelationAddresses(id: string | undefined) {
  return useQuery({ ...relationAddressesQuery(id), enabled: id != null });
}

/** The matcher (design 43 §3.15): finds existing relations for a party; it never creates one. */
export function useMatchRelations() {
  return useMutation({
    mutationFn: async (
      request: RelationMatchRequestDto,
    ): Promise<RelationMatchResultDto> => {
      const { data, error } = await apiClient.POST(
        "/api/master-data/relation/match",
        { body: request },
      );
      if (error) throw error;
      return data as RelationMatchResultDto;
    },
  });
}

export function useArchiveRelation() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/relation/{id}/archive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useUnarchiveRelation() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/relation/{id}/unarchive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export type DriverQualificationKind =
  Schemas["OpenTms.MasterData.Drivers.DriverQualificationKind"];
export type DriverRow = Schemas["OpenTms.MasterData.Drivers.DriverRowDto"];
export type DriverDto = Schemas["OpenTms.MasterData.Drivers.DriverDto"];
export type DriverCreateDto =
  Schemas["OpenTms.MasterData.Drivers.DriverCreateDto"];
export type DriverUpdateDto =
  Schemas["OpenTms.MasterData.Drivers.DriverUpdateDto"];
export type DriverQualificationDto =
  Schemas["OpenTms.MasterData.Drivers.DriverQualificationDto"];
export type DriverUserLookupDto =
  Schemas["OpenTms.MasterData.Drivers.DriverUserLookupDto"];
export type DriverListExtra = {
  includeArchived?: boolean;
  employerRelationId?: string;
};

export const drivers = createMasterDataHooks<
  DriverRow,
  DriverDto,
  DriverCreateDto,
  DriverUpdateDto,
  DriverListExtra
>("drivers", "/api/master-data/driver");

export const QUALIFICATION_KINDS: {
  value: DriverQualificationKind;
  key: string;
}[] = [
  { value: 1, key: "MasterData:QualificationKind.DrivingLicenceB" },
  { value: 2, key: "MasterData:QualificationKind.DrivingLicenceC" },
  { value: 3, key: "MasterData:QualificationKind.DrivingLicenceCE" },
  { value: 4, key: "MasterData:QualificationKind.Code95" },
  { value: 5, key: "MasterData:QualificationKind.AdrCertificate" },
  { value: 6, key: "MasterData:QualificationKind.TachographCard" },
  { value: 7, key: "MasterData:QualificationKind.ForkliftLicence" },
  { value: 0, key: "MasterData:QualificationKind.Other" },
];
export function qualificationKindKey(
  kind: DriverQualificationKind | undefined,
): string {
  return (
    QUALIFICATION_KINDS.find((k) => k.value === kind)?.key ??
    "MasterData:QualificationKind.Other"
  );
}

/** The option the user-account lookup holds: an ABP identity user of the tenant (Platform published surface). */
export interface UserOption {
  id: string;
  name: string;
  userName: string;
  email: string | null;
}
export function toUserOption(dto: DriverUserLookupDto): UserOption {
  return {
    id: dto.id ?? "",
    name: dto.name ?? "",
    userName: dto.userName ?? "",
    email: dto.email ?? null,
  };
}
export async function userLookupPage({
  term,
  skip,
  take,
}: EntityLookupQuery): Promise<EntityLookupPage<UserOption>> {
  const { data, error } = await apiClient.GET(
    "/api/master-data/driver/user-lookup-page",
    {
      params: { query: { term: term || undefined, skip, take } },
    },
  );
  if (error) throw error;
  return {
    items: (data?.items ?? []).map(toUserOption),
    totalCount: data?.totalCount ?? 0,
  };
}

export function useArchiveDriver() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/driver/{id}/archive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useUnarchiveDriver() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/driver/{id}/unarchive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

/** GDPR art. 17 (design 43): replaces the person fields, clears contact details, user link and qualifications; idempotent. */
export function useAnonymiseDriver() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/driver/{id}/anonymise",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

/* ── Run 3 (design 48): Vehicle, vehicle combination, product relation code ─────────────────────── */

export type FuelType = Schemas["OpenTms.MasterData.Fleet.FuelType"];
export type EuroClass = Schemas["OpenTms.MasterData.Fleet.EuroClass"];
export type VehicleCharacteristicOrigin =
  Schemas["OpenTms.MasterData.Fleet.VehicleCharacteristicOrigin"];
export type VehicleRow = Schemas["OpenTms.MasterData.Fleet.VehicleRowDto"];
export type VehicleDto = Schemas["OpenTms.MasterData.Fleet.VehicleDto"];
export type VehicleCreateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleCreateDto"];
export type VehicleUpdateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleUpdateDto"];
export type VehicleCharacteristicDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleCharacteristicDto"];
export type VehicleTypeDefaultsDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleTypeDefaultsDto"];
export type VehicleLookupDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleLookupDto"];
/** The vehicle list filters (design 48 API contract); `kinds` repeats in the query string. */
export type VehicleListExtra = {
  includeArchived?: boolean;
  kinds?: number;
  ownerRelationId?: string;
};

export const vehicles = createMasterDataHooks<
  VehicleRow,
  VehicleDto,
  VehicleCreateDto,
  VehicleUpdateDto,
  VehicleListExtra
>("vehicles", "/api/master-data/vehicle");

/** The kinds that may lead a combination (design 48): tractor, rigid, van. */
export const MOTORISED_KINDS: VehicleTypeKind[] = [1, 3, 4];
export const TRAILER_KINDS: VehicleTypeKind[] = [2];

export const FUEL_TYPES: { value: FuelType; key: string }[] = [
  { value: 1, key: "MasterData:FuelType.Diesel" },
  { value: 2, key: "MasterData:FuelType.Petrol" },
  { value: 3, key: "MasterData:FuelType.Lng" },
  { value: 4, key: "MasterData:FuelType.Cng" },
  { value: 5, key: "MasterData:FuelType.Lpg" },
  { value: 6, key: "MasterData:FuelType.Hvo" },
  { value: 7, key: "MasterData:FuelType.Electric" },
  { value: 8, key: "MasterData:FuelType.Hydrogen" },
  { value: 9, key: "MasterData:FuelType.DieselElectricHybrid" },
  { value: 10, key: "MasterData:FuelType.PetrolElectricHybrid" },
  { value: 11, key: "MasterData:FuelType.Other" },
  { value: 12, key: "MasterData:FuelType.NotApplicable" },
];
export function fuelTypeKey(value: FuelType | null | undefined): string | undefined {
  return FUEL_TYPES.find((f) => f.value === value)?.key;
}

export const EURO_CLASSES: { value: EuroClass; key: string }[] = [
  { value: 1, key: "MasterData:EuroClass.Euro0" },
  { value: 2, key: "MasterData:EuroClass.Euro1" },
  { value: 3, key: "MasterData:EuroClass.Euro2" },
  { value: 4, key: "MasterData:EuroClass.Euro3" },
  { value: 5, key: "MasterData:EuroClass.Euro4" },
  { value: 6, key: "MasterData:EuroClass.Euro5" },
  { value: 7, key: "MasterData:EuroClass.Euro6" },
  { value: 8, key: "MasterData:EuroClass.Eev" },
  { value: 9, key: "MasterData:EuroClass.ZeroEmission" },
];
export function euroClassKey(value: EuroClass | null | undefined): string | undefined {
  return EURO_CLASSES.find((e) => e.value === value)?.key;
}

/** The option an EntityLookup over vehicles holds (UI-LOOK-003: the name closed; plate and type in the list). */
export interface VehicleOption {
  id: string;
  name: string;
  licencePlate: string | null;
  kind: VehicleTypeKind;
  vehicleTypeName: string | null;
}
export function toVehicleOption(dto: VehicleLookupDto): VehicleOption {
  return {
    id: dto.id ?? "",
    name: dto.name ?? "",
    licencePlate: dto.licencePlate ?? null,
    kind: dto.kind ?? 1,
    vehicleTypeName: dto.vehicleTypeName ?? null,
  };
}
/** Server-searched pages of non-archived vehicles (UI-LOOK-002), optionally narrowed to kinds or an owner. */
export function vehicleLookupPage(
  kinds?: VehicleTypeKind[],
  ownerRelationId?: string,
): (q: EntityLookupQuery) => Promise<EntityLookupPage<VehicleOption>> {
  return async ({ term, skip, take }) => {
    const { data, error } = await apiClient.GET(
      "/api/master-data/vehicle/lookup-page",
      {
        params: {
          query: { term: term || undefined, skip, take, kinds, ownerRelationId },
        },
      },
    );
    if (error) throw error;
    return {
      items: (data?.items ?? []).map(toVehicleOption),
      totalCount: data?.totalCount ?? 0,
    };
  };
}

export function masterDataVehicleTypeDefaultsKey(vehicleTypeId: string | null | undefined) {
  return masterDataQueryKey("vehicles", "typeDefaults", vehicleTypeId);
}

/** What a new vehicle of the type takes (design 48): dimensions, kind and the type's characteristics. */
export async function fetchVehicleTypeDefaults(vehicleTypeId: string): Promise<VehicleTypeDefaultsDto> {
  const { data, error } = await apiClient.GET(
    "/api/master-data/vehicle/type-defaults/{vehicleTypeId}",
    { params: { path: { vehicleTypeId } } },
  );
  if (error) throw error;
  return data as VehicleTypeDefaultsDto;
}

export function useVehicleTypeDefaults(vehicleTypeId: string | null | undefined) {
  return useQuery({
    queryKey: masterDataVehicleTypeDefaultsKey(vehicleTypeId),
    enabled: vehicleTypeId != null,
    queryFn: () => fetchVehicleTypeDefaults(vehicleTypeId!),
  });
}

/** The tenant's own company (the one relation with the OwnCompany role): a new vehicle's owner (design 48). */
export function useOwnCompany(enabled = true) {
  return useQuery({
    queryKey: masterDataQueryKey("relations", "ownCompany"),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<RelationOption | null> => {
      const page = await relationLookupPage(16)({ term: "", skip: 0, take: 1 });
      return page.items[0] ?? null;
    },
  });
}

export function useArchiveVehicle() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/vehicle/{id}/archive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useUnarchiveVehicle() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/vehicle/{id}/unarchive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

/** The option a default-driver lookup holds. */
export interface DriverOption {
  id: string;
  name: string;
  code: string;
}
/**
 * Pages for the default-driver EntityLookup over the bounded driver lookup (design 43 has no driver lookup page):
 * asks the server for skip + take rows of the searched list and returns the requested slice.
 */
export async function driverLookupPage({
  term,
  skip,
  take,
}: EntityLookupQuery): Promise<EntityLookupPage<DriverOption>> {
  const wanted = Math.min(LOOKUP_TAKE, skip + take + 1);
  const { data, error } = await apiClient.GET("/api/master-data/driver/lookup", {
    params: { query: { term: term || undefined, take: wanted } },
  });
  if (error) throw error;
  const all = (data ?? []).map((d) => ({ id: d.id ?? "", name: d.name ?? "", code: d.code ?? "" }));
  const more = all.length >= wanted && wanted < LOOKUP_TAKE;
  return {
    items: all.slice(skip, skip + take),
    totalCount: more ? skip + take + 1 : all.length,
  };
}

export type VehicleCombinationRow =
  Schemas["OpenTms.MasterData.Fleet.VehicleCombinationRowDto"];
export type VehicleCombinationDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleCombinationDto"];
export type VehicleCombinationCreateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleCombinationCreateDto"];
export type VehicleCombinationUpdateDto =
  Schemas["OpenTms.MasterData.Fleet.VehicleCombinationUpdateDto"];
export const vehicleCombinations = createMasterDataHooks<
  VehicleCombinationRow,
  VehicleCombinationDto,
  VehicleCombinationCreateDto,
  VehicleCombinationUpdateDto,
  ArchivableExtra
>("vehicleCombinations", "/api/master-data/vehicle-combination");

export function useArchiveVehicleCombination() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/vehicle-combination/{id}/archive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useUnarchiveVehicleCombination() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/vehicle-combination/{id}/unarchive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export type ProductRelationCodeRow =
  Schemas["OpenTms.MasterData.Transport.ProductRelationCodeRowDto"];
export type ProductRelationCodeDto =
  Schemas["OpenTms.MasterData.Transport.ProductRelationCodeDto"];
export type ProductRelationCodeCreateDto =
  Schemas["OpenTms.MasterData.Transport.ProductRelationCodeCreateDto"];
export type ProductRelationCodeUpdateDto =
  Schemas["OpenTms.MasterData.Transport.ProductRelationCodeUpdateDto"];
export const productRelationCodes = createMasterDataHooks<
  ProductRelationCodeRow,
  ProductRelationCodeDto,
  ProductRelationCodeCreateDto,
  ProductRelationCodeUpdateDto,
  ArchivableExtra
>("productRelationCodes", "/api/master-data/product-relation-code");

export function useArchiveProductRelationCode() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/product-relation-code/{id}/archive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useUnarchiveProductRelationCode() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST(
        "/api/master-data/product-relation-code/{id}/unarchive",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}
