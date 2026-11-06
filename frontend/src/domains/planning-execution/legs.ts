import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";

/* Legs (design 59): the legs of an order (the order's Planning tab) or of a trip, and the editable leg fields of
 * OpenTMS's leg grid (references, remarks, planned dates within the stop's window). Legs are never posted by the
 * client; Planning derives them from the order's stops. */

type Schemas = components["schemas"];

export type LegDto = Schemas["OpenTms.PlanningExecution.Legs.LegDto"];
export type LegEndSnapshotDto = Schemas["OpenTms.PlanningExecution.Legs.LegEndSnapshotDto"];
export type LegUpdateDto = Schemas["OpenTms.PlanningExecution.Legs.LegUpdateDto"];
export type LegSource = Schemas["OpenTms.PlanningExecution.Legs.LegSource"];
export type LegSplitAtAddressDto = Schemas["OpenTms.PlanningExecution.Legs.LegSplitAtAddressDto"];
export type LegChainDto = Schemas["OpenTms.PlanningExecution.Legs.LegChainDto"];
export type LegChainRowDto = Schemas["OpenTms.PlanningExecution.Legs.LegChainRowDto"];
export type LegChainUpdateDto = Schemas["OpenTms.PlanningExecution.Legs.LegChainUpdateDto"];
export type LegChainRowInputDto = Schemas["OpenTms.PlanningExecution.Legs.LegChainRowInputDto"];
export type LegChainEndInputDto = Schemas["OpenTms.PlanningExecution.Legs.LegChainEndInputDto"];
export type LegSplitOrderLineResultDto = Schemas["OpenTms.PlanningExecution.Legs.LegSplitOrderLineResultDto"];

export const LEG_SOURCES: { value: LegSource; key: string }[] = [
  { value: 1, key: "PlanningExecution:LegSource.Generated" },
  { value: 2, key: "PlanningExecution:LegSource.ViaDepot" },
  { value: 3, key: "PlanningExecution:LegSource.Split" },
  { value: 4, key: "PlanningExecution:LegSource.Return" },
  { value: 5, key: "PlanningExecution:LegSource.Outsourced" },
  { value: 6, key: "PlanningExecution:LegSource.SplitAtAddress" },
];

export const LEG_SOURCE = { Generated: 1, ViaDepot: 2, Split: 3, Return: 4, Outsourced: 5, SplitAtAddress: 6 } as const;

export const legKeys = {
  all: ["planning", "legs"] as const,
  ofOrder: (orderId: string | undefined) => ["planning", "legs", "order", orderId?.toLowerCase()] as const,
  ofTrip: (tripId: string | undefined) => ["planning", "legs", "trip", tripId?.toLowerCase()] as const,
  chain: (lineId: string | undefined) => ["planning", "legs", "chain", lineId?.toLowerCase()] as const,
};

export interface LegListFilters {
  orderId?: string;
  tripId?: string;
  dateFrom?: string;
  dateTo?: string;
  customerRelationId?: string;
}

export async function fetchLegs(request: GridRequest, filters: LegListFilters): Promise<GridResponse<LegDto>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/planning/leg", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        OrderId: filters.orderId || undefined,
        TripId: filters.tripId || undefined,
        DateFrom: filters.dateFrom || undefined,
        DateTo: filters.dateTo || undefined,
        CustomerRelationId: filters.customerRelationId || undefined,
      },
    },
  });
  if (error) throw error;
  return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
}

const WHOLE_LIST: GridRequest = { startRow: 0, endRow: 200, sortModels: [], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] };

/** The legs of one order, in the server's order (position); an order has at most a few dozen. */
export function useLegsOfOrder(orderId: string | undefined) {
  return useQuery({
    queryKey: legKeys.ofOrder(orderId),
    enabled: orderId != null,
    queryFn: async () => (await fetchLegs(WHOLE_LIST, { orderId })).rows,
  });
}

/** The legs of one trip (at most 60 per trip, design 56). */
export function useLegsOfTrip(tripId: string | undefined) {
  return useQuery({
    queryKey: legKeys.ofTrip(tripId),
    enabled: tripId != null,
    queryFn: async () => (await fetchLegs(WHOLE_LIST, { tripId })).rows,
  });
}

/** One leg as it is now (an inline edit's refetch after a stale stamp). */
export async function fetchLeg(id: string): Promise<LegDto> {
  const { data, error, response } = await apiClient.GET("/api/planning/leg/{id}", { params: { path: { id } } });
  if (error) throw withStatus(error, response);
  return data!;
}

/**
 * The body of `PUT leg/{id}` for a leg as it is: the server replaces both references and the remarks on every call, so they
 * always travel; a date or a window left out stays as it is, so an edit sends only the one it changes (`{ ...legUpdateOf(leg), originPlannedDate }`).
 */
export function legUpdateOf(leg: LegDto): LegUpdateDto {
  return { concurrencyStamp: leg.concurrencyStamp ?? "", originReference: leg.origin?.reference ?? null, destinationReference: leg.destination?.reference ?? null, remarks: leg.remarks ?? null };
}

/** The editable leg fields: references, remarks, planned dates, windows (design 59 `PUT /api/planning/leg/{id}`, design 74 §6). */
export function useUpdateLeg() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: LegUpdateDto }): Promise<LegDto> => {
      const { data, error, response } = await apiClient.PUT("/api/planning/leg/{id}", { params: { path: { id } }, body: dto });
      if (error) throw withStatus(error, response);
      return data!;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}

/** The legs of one order line as a route (design 74 §4); `isChain` false: parallel legs, which cannot be managed as one. */
export function useLegChain(lineId: string | undefined) {
  return useQuery({
    queryKey: legKeys.chain(lineId),
    enabled: lineId != null,
    /* A dialog works on what the line is now, and its stamps must be current. */
    staleTime: 0,
    gcTime: 0,
    queryFn: async (): Promise<LegChainDto> => {
      const { data, error, response } = await apiClient.GET("/api/planning/leg/line/{lineId}", { params: { path: { lineId: lineId! } } });
      if (error) throw withStatus(error, response);
      return data!;
    },
  });
}

/** Saves the line's legs in one go (`PUT leg/line/{lineId}`); the server validates the chain. */
export function useUpdateLegChain() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ lineId, dto }: { lineId: string; dto: LegChainUpdateDto }): Promise<LegChainDto> => {
      const { data, error, response } = await apiClient.PUT("/api/planning/leg/line/{lineId}", { params: { path: { lineId } }, body: dto });
      if (error) throw withStatus(error, response);
      return data!;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}

/** Splits the leg's order line (design 74 §7, `POST leg/{id}/split-order-line`); `quantity` is the new line's amount. */
export function useSplitOrderLine() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ legId, quantity }: { legId: string; quantity: number }): Promise<LegSplitOrderLineResultDto> => {
      const { data, error, response } = await apiClient.POST("/api/planning/leg/{id}/split-order-line", { params: { path: { id: legId } }, body: { quantity } });
      if (error) throw withStatus(error, response);
      return data!;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}

/** Split a leg at an address (design 61 "Order page review" 5): A→B becomes A→X and X→B (`LegSource.SplitAtAddress`);
 *  refused while the leg is on a trip (`LegOnTrip`). Returns the two legs. */
export function useSplitLegAtAddress() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: LegSplitAtAddressDto }): Promise<LegDto[]> => {
      const { data, error } = await apiClient.POST("/api/planning/leg/{id}/split-at-address", { params: { path: { id } }, body: dto });
      if (error) throw error;
      return data ?? [];
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}

/** Remove a split at an address, from either half: the two legs become the split leg again. Returns that leg. */
export function useRemoveLegSplit() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string): Promise<LegDto> => {
      const { data, error } = await apiClient.POST("/api/planning/leg/{id}/remove-split", { params: { path: { id } } });
      if (error) throw error;
      return data!;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}

/** "Name, City" of a leg end, the way the grids show origin and destination. */
export function legEndLabel(end: LegEndSnapshotDto | undefined): string {
  return [end?.name, end?.city].filter(Boolean).join(", ");
}
