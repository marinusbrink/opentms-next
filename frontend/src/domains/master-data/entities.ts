import { useMutation, useQuery } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { createMasterDataHooks, masterDataQueryKey, type LookupItem, type Schemas } from "@/domains/master-data/shared";

/* One typed hook set per entity (design 41). Row = grid, Dto = editor, Create/Update = what the
 * editor sends. The schema names carry the backend namespaces. */

export type CurrencyRow = Schemas["OpenTms.MasterData.Reference.CurrencyRowDto"];
export type CurrencyDto = Schemas["OpenTms.MasterData.Reference.CurrencyDto"];
export type CurrencyCreateDto = Schemas["OpenTms.MasterData.Reference.CurrencyCreateDto"];
export type CurrencyUpdateDto = Schemas["OpenTms.MasterData.Reference.CurrencyUpdateDto"];
export const currencies = createMasterDataHooks<CurrencyRow, CurrencyDto, CurrencyCreateDto, CurrencyUpdateDto, { isActive?: boolean }>(
  "currencies",
  "/api/master-data/currency",
);

export function useSetDefaultCurrency() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/master-data/currency/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

export type CountryRow = Schemas["OpenTms.MasterData.Reference.CountryRowDto"];
export type CountryDto = Schemas["OpenTms.MasterData.Reference.CountryDto"];
export type CountryCreateDto = Schemas["OpenTms.MasterData.Reference.CountryCreateDto"];
export type CountryUpdateDto = Schemas["OpenTms.MasterData.Reference.CountryUpdateDto"];
export const countries = createMasterDataHooks<CountryRow, CountryDto, CountryCreateDto, CountryUpdateDto>(
  "countries",
  "/api/master-data/country",
);

export type AddressTypeKind = Schemas["OpenTms.MasterData.Reference.AddressTypeKind"];
export type AddressTypeRow = Schemas["OpenTms.MasterData.Reference.AddressTypeRowDto"];
export type AddressTypeDto = Schemas["OpenTms.MasterData.Reference.AddressTypeDto"];
export type AddressTypeCreateDto = Schemas["OpenTms.MasterData.Reference.AddressTypeCreateDto"];
export type AddressTypeUpdateDto = Schemas["OpenTms.MasterData.Reference.AddressTypeUpdateDto"];
export const addressTypes = createMasterDataHooks<AddressTypeRow, AddressTypeDto, AddressTypeCreateDto, AddressTypeUpdateDto>(
  "addressTypes",
  "/api/master-data/address-type",
);
export const ADDRESS_TYPE_KINDS: { value: AddressTypeKind; key: string }[] = [
  { value: 1, key: "MasterData:AddressTypeKind.Visiting" },
  { value: 2, key: "MasterData:AddressTypeKind.Postal" },
  { value: 0, key: "MasterData:AddressTypeKind.Other" },
];
export function addressTypeKindKey(kind: AddressTypeKind | undefined): string {
  return ADDRESS_TYPE_KINDS.find((k) => k.value === kind)?.key ?? "MasterData:AddressTypeKind.Other";
}

export type RelationCategoryRow = Schemas["OpenTms.MasterData.Relations.RelationCategoryRowDto"];
export type RelationCategoryDto = Schemas["OpenTms.MasterData.Relations.RelationCategoryDto"];
export type RelationCategoryCreateDto = Schemas["OpenTms.MasterData.Relations.RelationCategoryCreateDto"];
export type RelationCategoryUpdateDto = Schemas["OpenTms.MasterData.Relations.RelationCategoryUpdateDto"];
export const relationCategories = createMasterDataHooks<RelationCategoryRow, RelationCategoryDto, RelationCategoryCreateDto, RelationCategoryUpdateDto>(
  "relationCategories",
  "/api/master-data/relation-category",
);

export type TransportCharacteristicsRow = Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsRowDto"];
export type TransportCharacteristicsDto = Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsDto"];
export type TransportCharacteristicsCreateDto = Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsCreateDto"];
export type TransportCharacteristicsUpdateDto = Schemas["OpenTms.MasterData.Fleet.TransportCharacteristicsUpdateDto"];
export const transportCharacteristics = createMasterDataHooks<
  TransportCharacteristicsRow,
  TransportCharacteristicsDto,
  TransportCharacteristicsCreateDto,
  TransportCharacteristicsUpdateDto
>("transportCharacteristics", "/api/master-data/transport-characteristics");

export type VehicleTypeKind = Schemas["OpenTms.MasterData.Fleet.VehicleTypeKind"];
export type VehicleTypeRow = Schemas["OpenTms.MasterData.Fleet.VehicleTypeRowDto"];
export type VehicleTypeDto = Schemas["OpenTms.MasterData.Fleet.VehicleTypeDto"];
export type VehicleTypeCreateDto = Schemas["OpenTms.MasterData.Fleet.VehicleTypeCreateDto"];
export type VehicleTypeUpdateDto = Schemas["OpenTms.MasterData.Fleet.VehicleTypeUpdateDto"];
export const vehicleTypes = createMasterDataHooks<VehicleTypeRow, VehicleTypeDto, VehicleTypeCreateDto, VehicleTypeUpdateDto>(
  "vehicleTypes",
  "/api/master-data/vehicle-type",
);
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

export type TemperatureRow = Schemas["OpenTms.MasterData.Transport.TemperatureRowDto"];
export type TemperatureDto = Schemas["OpenTms.MasterData.Transport.TemperatureDto"];
export type TemperatureCreateDto = Schemas["OpenTms.MasterData.Transport.TemperatureCreateDto"];
export type TemperatureUpdateDto = Schemas["OpenTms.MasterData.Transport.TemperatureUpdateDto"];
export const temperatures = createMasterDataHooks<TemperatureRow, TemperatureDto, TemperatureCreateDto, TemperatureUpdateDto, ArchivableExtra>(
  "temperatures",
  "/api/master-data/temperature",
);

export type ProductRow = Schemas["OpenTms.MasterData.Transport.ProductRowDto"];
export type ProductDto = Schemas["OpenTms.MasterData.Transport.ProductDto"];
export type ProductCreateDto = Schemas["OpenTms.MasterData.Transport.ProductCreateDto"];
export type ProductUpdateDto = Schemas["OpenTms.MasterData.Transport.ProductUpdateDto"];
export const products = createMasterDataHooks<ProductRow, ProductDto, ProductCreateDto, ProductUpdateDto, ArchivableExtra>(
  "products",
  "/api/master-data/product",
);

export type TransportUnitRow = Schemas["OpenTms.MasterData.Transport.TransportUnitRowDto"];
export type TransportUnitDto = Schemas["OpenTms.MasterData.Transport.TransportUnitDto"];
export type TransportUnitCreateDto = Schemas["OpenTms.MasterData.Transport.TransportUnitCreateDto"];
export type TransportUnitUpdateDto = Schemas["OpenTms.MasterData.Transport.TransportUnitUpdateDto"];
export type TransportUnitLoadFactorDto = Schemas["OpenTms.MasterData.Transport.TransportUnitLoadFactorDto"];
export const transportUnits = createMasterDataHooks<TransportUnitRow, TransportUnitDto, TransportUnitCreateDto, TransportUnitUpdateDto, ArchivableExtra>(
  "transportUnits",
  "/api/master-data/transport-unit",
);

export type GeographicalRegionRow = Schemas["OpenTms.MasterData.Regions.GeographicalRegionRowDto"];
export type GeographicalRegionDto = Schemas["OpenTms.MasterData.Regions.GeographicalRegionDto"];
export type GeographicalRegionCreateDto = Schemas["OpenTms.MasterData.Regions.GeographicalRegionCreateDto"];
export type GeographicalRegionUpdateDto = Schemas["OpenTms.MasterData.Regions.GeographicalRegionUpdateDto"];
export type PostalCodeRangeDto = Schemas["OpenTms.MasterData.Regions.GeographicalRegionPostalCodeRangeDto"];
export const geographicalRegions = createMasterDataHooks<
  GeographicalRegionRow,
  GeographicalRegionDto,
  GeographicalRegionCreateDto,
  GeographicalRegionUpdateDto,
  { countryId?: string }
>("geographicalRegions", "/api/master-data/geographical-region");

/** The regions whose ranges match a postcode in a country (PostalCodeRangeMatcher on the server). */
export function useRegionMatches(countryId: string | undefined, postalCode: string) {
  const code = postalCode.trim();
  return useQuery({
    queryKey: masterDataQueryKey("geographicalRegions", "matches", countryId, code),
    enabled: countryId != null && code.length > 0,
    queryFn: async (): Promise<LookupItem[]> => {
      const { data, error } = await apiClient.GET("/api/master-data/geographical-region/matches/{countryId}", {
        params: { path: { countryId: countryId! }, query: { postalCode: code } },
      });
      if (error) throw error;
      return data ?? [];
    },
  });
}
