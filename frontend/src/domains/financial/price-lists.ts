import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { fallbackFileName, fileNameFromContentDisposition, saveBlob } from "@/lib/browser";
import { createReferenceDataHooks, referenceDataQueryKey } from "@/lib/reference-data/crud-hooks";

/* The price lists of the Financial domain (design 57, Tariffs): the design-50 list contract for the header and its
 * columns, the rules paged and saved one at a time under the list (build note 3: a rule edit never bumps the header's
 * stamp), and the four commands of the editor's ribbon: copy, adjust (with preview), export and import. Row = grid,
 * Dto = editor header, Create/Update = what the editor sends; the schema names carry the backend namespaces. */

type Schemas = components["schemas"];

// ── enums (value → localization key) ──────────────────────────────────────────

export interface EnumOption<T extends number> {
  value: T;
  key: string;
}

const options = <T extends number>(prefix: string, names: readonly string[], first = 1): EnumOption<T>[] =>
  names.map((name, index) => ({ value: (index + first) as T, key: `Financial:${prefix}.${name}` }));

export const keyOf = <T extends number>(items: EnumOption<T>[], value: T | null | undefined) => items.find((i) => i.value === value)?.key ?? "";

export type RateSide = Schemas["OpenTms.Financial.Tariffs.RateSide"];
export const RATE_SIDES = options<RateSide>("RateSide", ["Revenue", "Cost"]);

export type QuantitySource = Schemas["OpenTms.Financial.Tariffs.QuantitySource"];
/** Manual = 0 ("typed": no grading); the rest in the backend's order. */
export const QUANTITY_SOURCES = options<QuantitySource>(
  "QuantitySource",
  ["Manual", "LineQuantity", "Positions", "Colli", "WeightKg", "VolumeM3", "LoadingMeters", "LoadFactor", "Epq", "DistanceKm", "TollDistanceKm", "DurationH", "VehicleType", "PerLine", "TripDistanceKm"],
  0,
);
/** The bases a list prices on: every source but "typed". */
export const PRICE_LIST_BASES = QUANTITY_SOURCES.filter((q) => q.value !== 0);

/** The bases an order is charged by, in the legacy order of the block "Price calculation" (design 61 "Order page
 *  review" 3; Financial's `ChargedBases.RevenueBases`), each with its row label ("Price by weight"). `Manual` and the
 *  cost-only `TripDistanceKm` are no revenue bases. */
export const REVENUE_BASES: EnumOption<QuantitySource>[] = (
  [
    [1, "LineQuantity"],
    [3, "Colli"],
    [8, "Epq"],
    [4, "WeightKg"],
    [9, "DistanceKm"],
    [2, "Positions"],
    [13, "PerLine"],
    [5, "VolumeM3"],
    [6, "LoadingMeters"],
    [7, "LoadFactor"],
    [11, "DurationH"],
    [12, "VehicleType"],
    [10, "TollDistanceKm"],
  ] as const
).map(([value, name]) => ({ value, key: `Financial:ChargedBasis.${name}` }));

/** Bases in the legacy order, each once, revenue bases only (what the server accepts and returns). */
export function orderedRevenueBases(bases: readonly QuantitySource[]): QuantitySource[] {
  return REVENUE_BASES.map((b) => b.value).filter((value) => bases.includes(value));
}

export type PriceMultiplier = Schemas["OpenTms.Financial.Tariffs.PriceMultiplier"];
export const PRICE_MULTIPLIERS = options<PriceMultiplier>("PriceMultiplier", ["PerUnit", "PerLine", "PerLineQuantity", "PerLastStop"]);

export type ColumnQuantitySource = Schemas["OpenTms.Financial.Tariffs.ColumnQuantitySource"];
export const COLUMN_QUANTITY_SOURCES = options<ColumnQuantitySource>("ColumnQuantitySource", [
  "LineBasis",
  "SumOfLaneLines",
  "SumByTransportUnit",
  "SumByProduct",
  "SumByTransportUnitAndProduct",
  "LineWeightKg",
  "LaneWeightKg",
  "LineVolumeM3",
  "LaneVolumeM3",
  "LineLoadingMeters",
  "LaneLoadingMeters",
  "LineDistanceKm",
  "LaneDistanceKm",
  "LineDurationH",
  "LaneDurationH",
]);

export type StopMatching = Schemas["OpenTms.Financial.Tariffs.StopMatching"];
export const STOP_MATCHINGS = options<StopMatching>("StopMatching", ["First", "Last", "Both"]);

export type MultipleMatches = Schemas["OpenTms.Financial.Tariffs.MultipleMatches"];
export const MULTIPLE_MATCHES = options<MultipleMatches>("MultipleMatches", ["All", "HighestWins", "LowestWins"]);

export type PriceRounding = Schemas["OpenTms.Financial.Tariffs.PriceRounding"];
export const PRICE_ROUNDINGS = options<PriceRounding>("PriceRounding", ["None", "Cent", "FiveCents", "Quarter", "Half", "Whole"], 0);

// ── the list header and its columns ───────────────────────────────────────────

export type PriceListRow = Schemas["OpenTms.Financial.Tariffs.PriceListRowDto"];
export type PriceListDto = Schemas["OpenTms.Financial.Tariffs.PriceListDto"];
export type PriceListCreateDto = Schemas["OpenTms.Financial.Tariffs.PriceListCreateDto"];
export type PriceListUpdateDto = Schemas["OpenTms.Financial.Tariffs.PriceListUpdateDto"];
export type PriceListColumnDto = Schemas["OpenTms.Financial.Tariffs.PriceListColumnDto"];

export type PriceListListExtra = { side?: RateSide; includeArchived?: boolean };

export const priceLists = createReferenceDataHooks<PriceListRow, PriceListDto, PriceListCreateDto, PriceListUpdateDto, PriceListListExtra>(
  "priceLists",
  "/api/financial/price-list",
);

/** FinancialConsts: at most 50 columns and 30 brackets; a list holds at most 10,000 rules. */
export const MAX_PRICE_LIST_COLUMNS = 50;
export const MAX_PRICE_LIST_BRACKETS = 30;

/** The lookup of one side's lists (a relation's revenue or cost lists pick from their own side). */
export function usePriceListLookup(side: RateSide | undefined, enabled = true) {
  return useQuery({
    queryKey: referenceDataQueryKey(priceLists.entity, "lookup", side ?? "all"),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/financial/price-list/lookup", { params: { query: { side, take: 500 } } });
      if (error) throw error;
      return data ?? [];
    },
  });
}

/* The value of the per-basis rows (PriceListsPerBasis): the chosen lists in their order and the bases switched on. */
export interface PriceListsPerBasisValue {
  priceListIds: string[];
  chargedBases: QuantitySource[];
}

/** A list the record holds, with its basis and label: places a list the lookups do not return (archived) or not yet
 *  (one just created in place) in its row. */
export interface KnownPriceList {
  id: string;
  basis: QuantitySource;
  label: string;
}

/** The value with a list added (once) and its basis switched on: a list created in place. */
export function withCreatedList(value: PriceListsPerBasisValue, id: string, basis: QuantitySource | null | undefined): PriceListsPerBasisValue {
  return {
    priceListIds: value.priceListIds.includes(id) ? value.priceListIds : [...value.priceListIds, id],
    chargedBases: basis == null ? value.chargedBases : orderedRevenueBases([...value.chargedBases, basis]),
  };
}

/** `known` with a record added or replaced by id. */
export function withKnownList(known: KnownPriceList[], list: KnownPriceList): KnownPriceList[] {
  return [...known.filter((k) => k.id !== list.id), list];
}

/** The lookup of one side's lists per revenue basis (design 61 "Order page review" 3: one row per basis), in the order
 *  of REVENUE_BASES. Under the entity's key, so a list saved in its editor (also one created in place) refetches them.
 *  `enabled` per basis: PriceListsPerBasis loads only the bases it shows. */
export function usePriceListLookupsByBasis(side: RateSide, enabled: boolean | ((basis: QuantitySource) => boolean) = true) {
  return useQueries({
    queries: REVENUE_BASES.map((basis) => ({
      queryKey: referenceDataQueryKey(priceLists.entity, "lookup", side, "basis", basis.value),
      enabled: typeof enabled === "function" ? enabled(basis.value) : enabled,
      staleTime: 60 * 1000,
      queryFn: async () => {
        const { data, error } = await apiClient.GET("/api/financial/price-list/lookup", { params: { query: { side, basis: basis.value, take: 500 } } });
        if (error) throw error;
        return data ?? [];
      },
    })),
  });
}

// ── rules ──────────────────────────────────────────────────────────────────────

export type PriceListRuleRow = Schemas["OpenTms.Financial.Tariffs.PriceListRuleRowDto"];
export type PriceListRuleDto = Schemas["OpenTms.Financial.Tariffs.PriceListRuleDto"];
export type PriceListRuleInputDto = Schemas["OpenTms.Financial.Tariffs.PriceListRuleInputDto"];
export type PriceListBracketDto = Schemas["OpenTms.Financial.Tariffs.PriceListBracketDto"];
export type PriceListBracketInputDto = Schemas["OpenTms.Financial.Tariffs.PriceListBracketInputDto"];

export const rulesQueryKey = (priceListId: string, ...rest: unknown[]) => referenceDataQueryKey(priceLists.entity, "rules", priceListId, ...rest);

/** The tier of a rule: how many of its three dimensions are set (design 57: the specificity the lookup ranks by). */
export function tierOf(rule: Pick<PriceListRuleRow, "transportUnitId" | "productId" | "vehicleTypeId">): number {
  return [rule.transportUnitId, rule.productId, rule.vehicleTypeId].filter((id) => id != null).length;
}

/**
 * The server-side page fetcher of a list's rules for OpenTmsGrid; `tiers` narrows to those specificities (the
 * editor's "Overrides" = 1, 2, 3, "Defaults" = 0; design 57 build note 20; undefined = all) and `refreshKey` gives the
 * grid a new fetcher after a rule command (the grid refetches when the fetcher changes).
 */
export function usePriceListRulesFetcher(priceListId: string | undefined, tiers: number[] | undefined, refreshKey = 0) {
  const tiersKey = tiers && tiers.length > 0 ? tiers.join(",") : "";
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<PriceListRuleRow>> => {
      if (!priceListId) return { rows: [], totalCount: 0, filteredCount: 0 };
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/financial/price-list/{id}/rules", {
        params: {
          path: { id: priceListId },
          query: {
            Tiers: tiersKey ? tiersKey.split(",").map(Number) : undefined,
            StartRow: request.startRow,
            EndRow: request.endRow,
            WildcardSearch: request.wildcardSearch ?? undefined,
            SortBy: sortModel?.colId,
            SortDir: sortModel?.sort,
          },
        },
      });
      if (error) throw error;
      return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [priceListId, tiersKey, refreshKey],
  );
}

export function usePriceListRule(priceListId: string | undefined, ruleId: string | undefined) {
  return useQuery({
    queryKey: rulesQueryKey(priceListId ?? "", "detail", ruleId),
    enabled: priceListId != null && ruleId != null,
    queryFn: async (): Promise<PriceListRuleDto> => {
      const { data, error } = await apiClient.GET("/api/financial/price-list/{id}/rule/{ruleId}", { params: { path: { id: priceListId!, ruleId: ruleId! } } });
      if (error) throw error;
      return data as PriceListRuleDto;
    },
  });
}

export function useCreatePriceListRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ priceListId, dto }: { priceListId: string; dto: PriceListRuleInputDto }): Promise<PriceListRuleDto> => {
      const { data, error } = await apiClient.POST("/api/financial/price-list/{id}/rule", { params: { path: { id: priceListId } }, body: dto });
      if (error) throw error;
      return data as PriceListRuleDto;
    },
    onSuccess: (_, { priceListId }) => queryClient.invalidateQueries({ queryKey: rulesQueryKey(priceListId) }),
  });
}

export function useUpdatePriceListRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ priceListId, ruleId, dto }: { priceListId: string; ruleId: string; dto: PriceListRuleInputDto }): Promise<PriceListRuleDto> => {
      const { data, error } = await apiClient.PUT("/api/financial/price-list/{id}/rule/{ruleId}", { params: { path: { id: priceListId, ruleId } }, body: dto });
      if (error) throw error;
      return data as PriceListRuleDto;
    },
    onSuccess: (_, { priceListId }) => queryClient.invalidateQueries({ queryKey: rulesQueryKey(priceListId) }),
  });
}

export function useDeletePriceListRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ priceListId, ruleId }: { priceListId: string; ruleId: string }): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/financial/price-list/{id}/rule/{ruleId}", { params: { path: { id: priceListId, ruleId } } });
      if (error) throw error;
    },
    onSuccess: (_, { priceListId }) => queryClient.invalidateQueries({ queryKey: rulesQueryKey(priceListId) }),
  });
}

// ── commands ───────────────────────────────────────────────────────────────────

export type PriceListCopyDto = Schemas["OpenTms.Financial.Tariffs.PriceListCopyDto"];
export type PriceListAdjustDto = Schemas["OpenTms.Financial.Tariffs.PriceListAdjustDto"];
export type PriceListAdjustResultDto = Schemas["OpenTms.Financial.Tariffs.PriceListAdjustResultDto"];
export type PriceListSimulateDto = Schemas["OpenTms.Financial.Tariffs.PriceListSimulateDto"];
export type PriceListSimulateResultDto = Schemas["OpenTms.Financial.Tariffs.PriceListSimulateResultDto"];
export type PriceListRuleCandidateDto = Schemas["OpenTms.Financial.Tariffs.PriceListRuleCandidateDto"];
export type PriceListImportResultDto = Schemas["OpenTms.Financial.Tariffs.PriceListImportResultDto"];

/** Copies the header, columns and every rule into a new list (the new code and name, its own validity). */
export function useCopyPriceList() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: PriceListCopyDto }): Promise<PriceListDto> => {
      const { data, error } = await apiClient.POST("/api/financial/price-list/{id}/copy", { params: { path: { id } }, body: dto });
      if (error) throw error;
      return data as PriceListDto;
    },
  });
}

/** Adjusts every cell by a percentage or an amount, rounded; `preview` only reports what would change. */
export function useAdjustPriceList() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: PriceListAdjustDto }): Promise<PriceListAdjustResultDto> => {
      const { data, error } = await apiClient.POST("/api/financial/price-list/{id}/adjust", { params: { path: { id } }, body: dto });
      if (error) throw error;
      return data as PriceListAdjustResultDto;
    },
    onSuccess: (_, { id, dto }) => (dto.preview ? undefined : queryClient.invalidateQueries({ queryKey: rulesQueryKey(id) })),
  });
}

export function useSimulatePriceList() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: PriceListSimulateDto }): Promise<PriceListSimulateResultDto> => {
      const { data, error } = await apiClient.POST("/api/financial/price-list/{id}/simulate", { params: { path: { id } }, body: dto });
      if (error) throw error;
      return data as PriceListSimulateResultDto;
    },
  });
}

/** Downloads the rules, brackets and cells as xlsx under the server's file name (else `<code>.xlsx`); ABP's conventional routing makes the stream command a POST. */
export function useExportPriceList() {
  return useMutation({
    mutationFn: async ({ id, fallbackName }: { id: string; fallbackName: string }): Promise<void> => {
      const { data, error, response } = await apiClient.POST("/api/financial/price-list/{id}/export", { params: { path: { id } }, parseAs: "blob" });
      if (error) throw error;
      const blob = data as unknown as Blob | undefined;
      if (!blob) throw new Error("The server returned no file.");
      const contentType = response.headers.get("Content-Type") ?? blob.type;
      saveBlob(blob, fileNameFromContentDisposition(response.headers.get("Content-Disposition")) ?? fallbackFileName(fallbackName, contentType, ".xlsx"));
    },
  });
}

/** Replaces the whole rule set of the list with the xlsx file's (design 57 build note 14; files above 5 MB are refused). */
export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;

export function useImportPriceList() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }): Promise<PriceListImportResultDto> => {
      const body = new FormData();
      body.append("file", file, file.name);
      const { data, error } = await apiClient.POST("/api/financial/price-list/{id}/import", {
        params: { path: { id } },
        body: body as unknown as { file?: string },
        bodySerializer: (b) => b as unknown as FormData,
      });
      if (error) throw error;
      return data as PriceListImportResultDto;
    },
    onSuccess: (_, { id }) => Promise.all([queryClient.invalidateQueries({ queryKey: rulesQueryKey(id) }), queryClient.invalidateQueries({ queryKey: referenceDataQueryKey(priceLists.entity, "detail", id) })]),
  });
}
