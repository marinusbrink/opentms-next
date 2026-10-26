import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { useRecordUpdates } from "@/lib/realtime/use-record-updates";

/* Rate lines (design 57, AD-08; design 61 "Pricing" and "Cost and revenue"): the stored revenue lines of an order
 * and the cost lines of a trip with their totals and calculation state, the manual line, the adjustment of a
 * calculated line's unit price, the fixed price, and the lookups the order's financial terms need (price lists,
 * the customer's financial defaults). The calculated lines are written by Financial's calculators only; the screens
 * watch `Financial.OrderRateLines` and refetch (flow 2). */

type Schemas = components["schemas"];

export type RateLineDto = Schemas["OpenTms.Financial.Tariffs.RateLineDto"];
export type RateLineSetDto = Schemas["OpenTms.Financial.Tariffs.RateLineSetDto"];
export type RateLineSetTotalsDto = Schemas["OpenTms.Financial.Tariffs.RateLineSetTotalsDto"];
export type RateLineKind = Schemas["OpenTms.Financial.Tariffs.RateLineKind"];
export type RateSide = Schemas["OpenTms.Financial.Tariffs.RateSide"];
export type QuantitySource = Schemas["OpenTms.Financial.Tariffs.QuantitySource"];
export type MultipleMatches = Schemas["OpenTms.Financial.Tariffs.MultipleMatches"];
export type ManualRateLineCreateDto = Schemas["OpenTms.Financial.Tariffs.ManualRateLineCreateDto"];
export type RateLineUpdateDto = Schemas["OpenTms.Financial.Tariffs.RateLineUpdateDto"];
export type FixedPriceDto = Schemas["OpenTms.Financial.Tariffs.FixedPriceDto"];
export type RelationFinancialSettingsDto = Schemas["OpenTms.Financial.Invoicing.RelationFinancialSettingsDto"];
export type PriceListLookupItem = Schemas["OpenTms.Platform.ReferenceData.ReferenceDataLookupDto"];

export const ORDER_RATE_LINES_TARGET_TYPE = "Financial.OrderRateLines";

export const RATE_LINE_KINDS: { value: RateLineKind; key: string }[] = [
  { value: 1, key: "Financial:RateLineKind.PriceList" },
  { value: 2, key: "Financial:RateLineKind.Manual" },
  { value: 3, key: "Financial:RateLineKind.FixedPrice" },
  { value: 4, key: "Financial:RateLineKind.DiscountOrSurcharge" },
  { value: 5, key: "Financial:RateLineKind.AdditionalService" },
  { value: 6, key: "Financial:RateLineKind.AgreedPrice" },
  { value: 7, key: "Financial:RateLineKind.OwnFleetDistance" },
];

export const RATE_LINE_KIND = { PriceList: 1, Manual: 2, FixedPrice: 3, DiscountOrSurcharge: 4, AdditionalService: 5, AgreedPrice: 6, OwnFleetDistance: 7 } as const;
export const RATE_SIDE = { Revenue: 1, Cost: 2 } as const;

export const QUANTITY_SOURCES: { value: QuantitySource; key: string }[] = [
  { value: 0, key: "Financial:QuantitySource.Manual" },
  { value: 1, key: "Financial:QuantitySource.LineQuantity" },
  { value: 2, key: "Financial:QuantitySource.Positions" },
  { value: 3, key: "Financial:QuantitySource.Colli" },
  { value: 4, key: "Financial:QuantitySource.WeightKg" },
  { value: 5, key: "Financial:QuantitySource.VolumeM3" },
  { value: 6, key: "Financial:QuantitySource.LoadingMeters" },
  { value: 7, key: "Financial:QuantitySource.LoadFactor" },
  { value: 8, key: "Financial:QuantitySource.Epq" },
  { value: 9, key: "Financial:QuantitySource.DistanceKm" },
  { value: 10, key: "Financial:QuantitySource.TollDistanceKm" },
  { value: 11, key: "Financial:QuantitySource.DurationH" },
  { value: 12, key: "Financial:QuantitySource.VehicleType" },
  { value: 13, key: "Financial:QuantitySource.PerLine" },
  { value: 14, key: "Financial:QuantitySource.TripDistanceKm" },
];

export const MULTIPLE_MATCHES: { value: MultipleMatches; key: string }[] = [
  { value: 1, key: "Financial:MultipleMatches.All" },
  { value: 2, key: "Financial:MultipleMatches.HighestWins" },
  { value: 3, key: "Financial:MultipleMatches.LowestWins" },
];

export const rateLineKeys = {
  all: ["financial", "rateLines"] as const,
  ofOrder: (orderId: string | undefined) => ["financial", "rateLines", "order", orderId?.toLowerCase()] as const,
  ofTrip: (tripId: string | undefined) => ["financial", "rateLines", "trip", tripId?.toLowerCase()] as const,
  priceListLookup: (side: RateSide | undefined) => ["financial", "priceLists", "lookup", side] as const,
  relationDefaults: (relationId: string | undefined) => ["financial", "relationFinancialSettings", relationId?.toLowerCase()] as const,
};

async function fetchRateLines(query: { OrderId?: string; TripId?: string }): Promise<RateLineSetDto> {
  const { data, error } = await apiClient.GET("/api/financial/rate-line", { params: { query } });
  if (error) throw error;
  return data ?? {};
}

export function useOrderRateLines(orderId: string | undefined) {
  return useQuery({ queryKey: rateLineKeys.ofOrder(orderId), enabled: orderId != null, queryFn: () => fetchRateLines({ OrderId: orderId }) });
}

export function useTripRateLines(tripId: string | undefined) {
  return useQuery({ queryKey: rateLineKeys.ofTrip(tripId), enabled: tripId != null, queryFn: () => fetchRateLines({ TripId: tripId }) });
}

/** Watches an order's rate lines (flow 2): a recalculation refetches the set. */
export function useOrderRateLineUpdates(orderId: string | undefined) {
  useRecordUpdates(ORDER_RATE_LINES_TARGET_TYPE, [orderId], { queryKeysFor: (id) => [rateLineKeys.ofOrder(id)] });
}

const unwrap = <T,>(result: { data?: T; error?: unknown }): T => {
  if (result.error) throw result.error;
  return result.data as T;
};

function useRateLineMutation<TArgs, TResult>(run: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({ mutationFn: run, onSuccess: () => void queryClient.invalidateQueries({ queryKey: rateLineKeys.all }) });
}

export function useAddManualRateLine() {
  return useRateLineMutation(async (dto: ManualRateLineCreateDto) => unwrap(await apiClient.POST("/api/financial/rate-line/manual", { body: dto })));
}

/** Adjusts a line: the unit price of a calculated line, or quantity, price and description of a manual one. */
export function useUpdateRateLine() {
  return useRateLineMutation(async ({ id, dto }: { id: string; dto: RateLineUpdateDto }) => unwrap(await apiClient.PUT("/api/financial/rate-line/{id}", { params: { path: { id } }, body: dto })));
}

export function useRemoveRateLine() {
  return useRateLineMutation(async (id: string) => {
    const { error } = await apiClient.DELETE("/api/financial/rate-line/{id}", { params: { path: { id } } });
    if (error) throw error;
  });
}

export function useSetFixedPrice() {
  return useRateLineMutation(async (dto: FixedPriceDto) => unwrap(await apiClient.POST("/api/financial/rate-line/set-fixed-price", { body: dto })));
}

export function useRemoveFixedPrice() {
  return useRateLineMutation(async (orderId: string) => {
    const { error } = await apiClient.DELETE("/api/financial/rate-line/fixed-price/{orderId}", { params: { path: { orderId } } });
    if (error) throw error;
  });
}

/** The price lists of a side, for the order's financial terms (UI-LOOK-001: a bounded reference list). */
export function usePriceListLookup(side: RateSide | undefined, enabled = true) {
  return useQuery({
    queryKey: rateLineKeys.priceListLookup(side),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<PriceListLookupItem[]> => {
      const { data, error } = await apiClient.GET("/api/financial/price-list/lookup", { params: { query: { side, take: 500 } } });
      if (error) throw error;
      return data ?? [];
    },
  });
}

/** The customer's financial defaults (design 57); the order copies them on creation and asks on a customer change. */
export async function fetchRelationFinancialDefaults(relationId: string): Promise<RelationFinancialSettingsDto | null> {
  const { data, error, response } = await apiClient.GET("/api/financial/relation-financial-settings/by-relation/{relationId}", { params: { path: { relationId } } });
  if (response.status === 404) return null;
  if (error) throw error;
  return data ?? null;
}

export function useRelationFinancialDefaults(relationId: string | undefined) {
  return useQuery({
    queryKey: rateLineKeys.relationDefaults(relationId),
    enabled: relationId != null,
    staleTime: 60 * 1000,
    queryFn: () => fetchRelationFinancialDefaults(relationId!),
  });
}
