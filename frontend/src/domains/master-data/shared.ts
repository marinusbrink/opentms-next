import { useMemo } from "react";

import type { components } from "@/lib/api/generated/schema";
import {
  createReferenceDataHooks,
  referenceDataQueryKey,
  type ListExtra,
  type ReferenceDataHooks,
} from "@/lib/reference-data/crud-hooks";

export { LOOKUP_TAKE, type ListExtra } from "@/lib/reference-data/crud-hooks";

/* The hooks every Master Data entity shares (design 41, API contract): one paged list with the
 * flat query contract, a detail read, a bounded lookup, create, update (with the concurrency
 * stamp), delete and bulk delete. Every call goes through the generated client; the entity
 * files below narrow the DTO types to the generated schema. */

export type Schemas = components["schemas"];

export type LookupItem = Schemas["OpenTms.MasterData.MasterDataLookupDto"];
export type BulkDeleteResult = Schemas["OpenTms.MasterData.MasterDataBulkDeleteResultDto"];
export type LocalizedNameMap = Record<string, string | null | undefined>;

export type MasterDataPath =
  | "/api/master-data/currency"
  | "/api/master-data/country"
  | "/api/master-data/address-type"
  | "/api/master-data/relation-category"
  | "/api/master-data/transport-characteristics"
  | "/api/master-data/temperature"
  | "/api/master-data/product"
  | "/api/master-data/transport-unit"
  | "/api/master-data/vehicle-type"
  | "/api/master-data/geographical-region"
  | "/api/master-data/relation"
  | "/api/master-data/driver"
  | "/api/master-data/vehicle"
  | "/api/master-data/vehicle-combination"
  | "/api/master-data/product-relation-code";

export const masterDataQueryKey = referenceDataQueryKey;

export type MasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra> = ReferenceDataHooks<TRow, TDto, TCreate, TUpdate, TExtra, LookupItem>;

/** The generic factory of src/lib/reference-data, pinned to the Master Data paths. */
export function createMasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra = ListExtra>(
  entity: string,
  path: MasterDataPath,
): MasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra> {
  return createReferenceDataHooks<TRow, TDto, TCreate, TUpdate, TExtra, LookupItem>(entity, path);
}

/** Lookup items as picker options, sorted as the server returns them. */
export function useLookupOptions(lookup: { data: LookupItem[] | undefined }) {
  return useMemo(
    () => (lookup.data ?? []).map((item) => ({ id: item.id ?? "", value: item.id ?? "", label: item.name ?? "" })),
    [lookup.data],
  );
}
