import { useMutation, useQuery } from "@tanstack/react-query";

import type {
  EntityLookupPage,
  EntityLookupQuery,
} from "@/components/ui/entity-lookup";
import { apiClient } from "@/lib/api/client";
import {
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

export function useRelationAddresses(id: string | undefined) {
  return useQuery({
    queryKey: masterDataQueryKey("relations", "addresses", id),
    enabled: id != null,
    queryFn: async (): Promise<RelationAddressDto[]> => {
      const { data, error } = await apiClient.GET(
        "/api/master-data/relation/{id}/addresses",
        { params: { path: { id: id! } } },
      );
      if (error) throw error;
      return data ?? [];
    },
  });
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
