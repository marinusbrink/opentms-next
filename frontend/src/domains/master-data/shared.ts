import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback, useMemo } from "react";

import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The hooks every Master Data entity shares (design 41, API contract): one paged list with the
 * flat query contract, a detail read, a bounded lookup, create, update (with the concurrency
 * stamp), delete and bulk delete. Every call goes through the generated client; the entity
 * files below narrow the DTO types to the generated schema. */

export type Schemas = components["schemas"];

export type LookupItem = Schemas["OpenTms.MasterData.MasterDataLookupDto"];
export type BulkDeleteResult = Schemas["OpenTms.MasterData.MasterDataBulkDeleteResultDto"];
export type LocalizedNameMap = Record<string, string | null | undefined>;

export const MASTER_DATA_FEATURE = "MasterData.FirstRun";
export const LOOKUP_TAKE = 500;

/** The flat list query the services bind (startRow, endRow, wildcardSearch, sortBy, sortDir + entity filters). */
export type ListExtra = Record<string, string | number | boolean | null | undefined>;

interface LooseResult {
  data?: unknown;
  error?: unknown;
  response: Response;
}

interface LooseClient {
  GET: (path: string, init?: unknown) => Promise<LooseResult>;
  POST: (path: string, init?: unknown) => Promise<LooseResult>;
  PUT: (path: string, init?: unknown) => Promise<LooseResult>;
  DELETE: (path: string, init?: unknown) => Promise<LooseResult>;
}

/* The paths differ per entity only in their root; the entity files pin the DTO types, so the
 * generic factory calls the typed client through a path-agnostic view of it. */
const client = apiClient as unknown as LooseClient;

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
  | "/api/master-data/geographical-region";

export function masterDataQueryKey(entity: string, ...rest: unknown[]) {
  return ["masterData", entity, ...rest] as const;
}

export interface MasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra> {
  entity: string;
  path: MasterDataPath;
  useGridFetcher: (extra?: TExtra) => (request: GridRequest) => Promise<GridResponse<TRow>>;
  useDetail: (id: string | undefined) => { data: TDto | undefined; isLoading: boolean; error: unknown };
  useLookup: (enabled?: boolean) => { data: LookupItem[] | undefined; isLoading: boolean; error: unknown };
  useCreate: () => { mutateAsync: (dto: TCreate) => Promise<TDto>; isPending: boolean };
  useUpdate: () => { mutateAsync: (args: { id: string; dto: TUpdate }) => Promise<TDto>; isPending: boolean };
  useDelete: () => { mutateAsync: (id: string) => Promise<void>; isPending: boolean };
  useBulkDelete: () => {
    mutateAsync: (args: { selection: GridSelectionDto; extra?: TExtra }) => Promise<BulkDeleteResult>;
    isPending: boolean;
  };
}

export function createMasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra = ListExtra>(
  entity: string,
  path: MasterDataPath,
): MasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra> {
  return {
    entity,
    path,

    useGridFetcher(extra?: TExtra) {
      const extraKey = JSON.stringify(extra ?? {});
      return useCallback(
        async (request: GridRequest): Promise<GridResponse<TRow>> => {
          const sortModel = request.sortModels[0];
          const { data, error } = await client.GET(path, {
            params: {
              query: {
                startRow: request.startRow,
                endRow: request.endRow,
                wildcardSearch: request.wildcardSearch ?? undefined,
                sortBy: sortModel?.colId,
                sortDir: sortModel?.sort,
                ...(JSON.parse(extraKey) as TExtra),
              },
            },
          });
          if (error) throw error;
          const page = data as { rows?: TRow[]; totalCount?: number; filteredCount?: number } | undefined;
          return {
            rows: page?.rows ?? [],
            totalCount: Number(page?.totalCount ?? 0),
            filteredCount: Number(page?.filteredCount ?? 0),
          };
        },
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [extraKey],
      );
    },

    useDetail(id) {
      return useQuery({
        queryKey: masterDataQueryKey(entity, "detail", id),
        enabled: id != null,
        queryFn: async (): Promise<TDto> => {
          const { data, error } = await client.GET(`${path}/{id}`, { params: { path: { id } } });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useLookup(enabled = true) {
      return useQuery({
        queryKey: masterDataQueryKey(entity, "lookup"),
        enabled,
        staleTime: 60 * 1000,
        queryFn: async (): Promise<LookupItem[]> => {
          const { data, error } = await client.GET(`${path}/lookup`, { params: { query: { take: LOOKUP_TAKE } } });
          if (error) throw error;
          return (data as LookupItem[] | undefined) ?? [];
        },
      });
    },

    useCreate() {
      return useMutation({
        mutationFn: async (dto: TCreate): Promise<TDto> => {
          const { data, error } = await client.POST(path, { body: dto });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useUpdate() {
      return useMutation({
        mutationFn: async ({ id, dto }: { id: string; dto: TUpdate }): Promise<TDto> => {
          const { data, error } = await client.PUT(`${path}/{id}`, { params: { path: { id } }, body: dto });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useDelete() {
      return useMutation({
        mutationFn: async (id: string): Promise<void> => {
          const { error } = await client.DELETE(`${path}/{id}`, { params: { path: { id } } });
          if (error) throw error;
        },
      });
    },

    useBulkDelete() {
      return useMutation({
        mutationFn: async ({ selection, extra }: { selection: GridSelectionDto; extra?: TExtra }): Promise<BulkDeleteResult> => {
          const { data, error } = await client.POST(`${path}/bulk-delete`, {
            body: {
              selection: {
                mode: selection.mode,
                explicitIds: selection.explicitIds,
                excludedIds: selection.excludedIds,
                filterRequest: selection.filterRequest
                  ? {
                      startRow: selection.filterRequest.startRow,
                      endRow: selection.filterRequest.endRow,
                      wildcardSearch: selection.filterRequest.wildcardSearch,
                      sortModels: selection.filterRequest.sortModels,
                      columnFilters: selection.filterRequest.columnFilters,
                      rowGroupCols: selection.filterRequest.rowGroupCols,
                      groupKeys: selection.filterRequest.groupKeys,
                    }
                  : undefined,
              },
              ...(extra ?? {}),
            },
          });
          if (error) throw error;
          return (data as BulkDeleteResult | undefined) ?? { deletedCount: 0, skippedRows: [] };
        },
      });
    },
  };
}

/** Lookup items as picker options, sorted as the server returns them. */
export function useLookupOptions(lookup: { data: LookupItem[] | undefined }) {
  return useMemo(
    () => (lookup.data ?? []).map((item) => ({ id: item.id ?? "", value: item.id ?? "", label: item.name ?? "" })),
    [lookup.data],
  );
}
