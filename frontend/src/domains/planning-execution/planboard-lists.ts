import { useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import { isConcurrencyConflict, withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";
import { legKeys, type LegDto } from "@/domains/planning-execution/legs";
import { transportTripKeys, type TransportTripDto, type TransportTripRow } from "@/domains/planning-execution/trips";

/* The reads and commands of the Planboard's list panes (design 70 "Panes"; design 68 §6, §7 and "API contract — reads"):
 * the trips list on the board's range and organisation units (with `?ids=` for the live refetch), the unplanned legs
 * (plain, grouped by the D18 whitelist with counts, one group's legs, `?ids=`), and the narrow Planboard commands the
 * list panes run (header field, assign / unassign / move legs, add driver, complete with ATD and ATA, optimise stops) plus
 * the return-leg command on an unplanned leg. Every Planboard command carries the trip's concurrency
 * stamp: the trips list row's (`TransportTripRowDto.concurrencyStamp`) where the caller has the row, otherwise read fresh
 * from the trip (`readTripStamp`); a stale stamp is refused by the server (409). After a command the trip's detail, its legs and the leg lists are refetched; the panes patch their rows. */

type Schemas = components["schemas"];

export type PlanboardChangeDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardChangeDto"];
export type PlanboardTripField = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardTripField"];
export type PlanboardOptimizeStopsDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardOptimizeStopsDto"];
export type LegGroupDto = Schemas["OpenTms.PlanningExecution.Legs.LegGroupDto"];
export type PlanboardAssignOrderPreviewDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardAssignOrderPreviewDto"];
export type PlanboardAssignOrderResultDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardAssignOrderResultDto"];
export type PlanboardOrderLegDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardOrderLegDto"];
export type PlanboardPulledFromDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardPulledFromDto"];

/** Why a leg of a dragged order stays where it is (`PlanboardOrderLegDto.stuckReason`). */
export const PLANBOARD_STUCK_REASON = { TripFinal: 1, HasActuals: 2 } as const;

/** The header fields the trips pane edits inline (`PlanboardTripField`). */
export const PLANBOARD_TRIP_FIELD = { Name: 1, Remarks: 2, DriverInstructions: 3 } as const satisfies Record<string, PlanboardTripField>;

/** The groupings of Legs to schedule (design 68, PO decision D18), as the server names them. */
export const LEG_GROUPINGS = [
  { value: "origindate", key: "Planboard:Lists.GroupBy.OriginDate" },
  { value: "customer", key: "Planboard:Lists.GroupBy.Customer" },
  { value: "origincity", key: "Planboard:Lists.GroupBy.OriginCity" },
  { value: "destinationcity", key: "Planboard:Lists.GroupBy.DestinationCity" },
] as const;
export type LegGrouping = (typeof LEG_GROUPINGS)[number]["value"];

export function isLegGrouping(value: unknown): value is LegGrouping {
  return typeof value === "string" && LEG_GROUPINGS.some((g) => g.value === value);
}

/** The most ids one `?ids=` read takes (design 68: `PlanningExecutionConsts.MaxIdsPerQuery`). */
export const MAX_IDS_PER_QUERY = 100;

/** Ids in chunks of at most `size`. */
export function chunkIds(ids: readonly string[], size = MAX_IDS_PER_QUERY): string[][] {
  const unique = [...new Set(ids.filter(Boolean))];
  const chunks: string[][] = [];
  for (let i = 0; i < unique.length; i += size) chunks.push(unique.slice(i, i + size));
  return chunks;
}

/** The board filters a list pane applies (`YYYY-MM-DD` days, both inclusive; no units = every unit). */
export interface PlanboardListFilters {
  from: string;
  till: string;
  organizationUnitIds: readonly string[];
}

const localMidnight = (isoDate: string, addDays = 0) => {
  const [y, m, d] = isoDate.split("-").map(Number);
  return new Date(y, m - 1, d + addDays, 0, 0, 0, 0);
};

/** The range as the trip list's execution window: from the first day's midnight up to the last moment of the last day. */
export function executionWindow(filters: Pick<PlanboardListFilters, "from" | "till">): { executionFrom: string; executionTo: string } {
  return {
    executionFrom: localMidnight(filters.from).toISOString(),
    executionTo: new Date(localMidnight(filters.till, 1).getTime() - 1).toISOString(),
  };
}

const throwOnError = <T>(result: { data?: T; error?: unknown; response?: Response }): T => {
  /* With the status: a caller tells a stale stamp from a business refusal by it (`isConcurrencyConflict`). */
  if (result.error !== undefined) throw withStatus(result.error, result.response);
  return result.data as T;
};

const toPage = <T>(data: { rows?: T[] | null; totalCount?: number | string; filteredCount?: number | string } | undefined): GridResponse<T> => ({
  rows: data?.rows ?? [],
  totalCount: Number(data?.totalCount ?? 0),
  filteredCount: Number(data?.filteredCount ?? 0),
});

// ─── Trips ───────────────────────────────────────────────────────────────────

function tripQuery(filters: PlanboardListFilters) {
  const { executionFrom, executionTo } = executionWindow(filters);
  return {
    ExecutionFrom: executionFrom,
    ExecutionTo: executionTo,
    OrganizationUnitIds: filters.organizationUnitIds.length > 0 ? [...filters.organizationUnitIds] : undefined,
    IsArchived: false,
  };
}

/** A page of the trips pane: the non-archived trips whose planned execution overlaps the range, in the units. */
export async function fetchPlanboardTrips(request: GridRequest, filters: PlanboardListFilters): Promise<GridResponse<TransportTripRow>> {
  const sort = request.sortModels[0];
  const data = throwOnError(
    await apiClient.GET("/api/planning/transport-trip", {
      params: {
        query: {
          StartRow: request.startRow,
          EndRow: request.endRow,
          WildcardSearch: request.wildcardSearch ?? undefined,
          SortBy: sort?.colId,
          SortDir: sort?.sort,
          ...tripQuery(filters),
        },
      },
    }),
  );
  return toPage(data);
}

/** The rows of the given trips that still match the filters (live refetch; a trip that left the filters is absent). */
export async function fetchPlanboardTripsByIds(ids: readonly string[], filters: PlanboardListFilters): Promise<TransportTripRow[]> {
  const pages = await Promise.all(
    chunkIds(ids).map(async (chunk) =>
      throwOnError(await apiClient.GET("/api/planning/transport-trip", { params: { query: { StartRow: 0, EndRow: chunk.length, Ids: chunk, ...tripQuery(filters) } } })),
    ),
  );
  return pages.flatMap((page) => page?.rows ?? []);
}

// ─── Unplanned legs ──────────────────────────────────────────────────────────

export interface UnplannedLegFilters extends PlanboardListFilters {
  groupBy?: LegGrouping | null;
  /** One group's legs (with `groupBy`). */
  groupKey?: string | null;
}

function legQuery(filters: UnplannedLegFilters) {
  return {
    DateFrom: filters.from,
    DateTo: filters.till,
    OrganizationUnitIds: filters.organizationUnitIds.length > 0 ? [...filters.organizationUnitIds] : undefined,
    GroupBy: filters.groupBy && filters.groupKey != null ? filters.groupBy : undefined,
    GroupKey: filters.groupBy && filters.groupKey != null ? filters.groupKey : undefined,
  };
}

/** A page of the unplanned legs (the leg list without order or trip is the unplanned view), optionally of one group. */
export async function fetchUnplannedLegs(request: GridRequest, filters: UnplannedLegFilters): Promise<GridResponse<LegDto>> {
  const sort = request.sortModels[0];
  const data = throwOnError(
    await apiClient.GET("/api/planning/leg", {
      params: { query: { StartRow: request.startRow, EndRow: request.endRow, WildcardSearch: request.wildcardSearch ?? undefined, SortBy: sort?.colId, SortDir: sort?.sort, ...legQuery(filters) } },
    }),
  );
  return toPage(data);
}

/** The groups of the unplanned legs with their counts (D18), paged; `filteredCount` is the number of groups. */
export async function fetchUnplannedLegGroups(request: GridRequest, filters: PlanboardListFilters & { groupBy: LegGrouping }): Promise<GridResponse<LegGroupDto>> {
  const data = throwOnError(
    await apiClient.GET("/api/planning/leg/groups", {
      params: {
        query: {
          StartRow: request.startRow,
          EndRow: request.endRow,
          WildcardSearch: request.wildcardSearch ?? undefined,
          DateFrom: filters.from,
          DateTo: filters.till,
          OrganizationUnitIds: filters.organizationUnitIds.length > 0 ? [...filters.organizationUnitIds] : undefined,
          GroupBy: filters.groupBy,
        },
      },
    }),
  );
  return toPage(data);
}

/** The given legs that are still unplanned and match the filters (live refetch). */
export async function fetchUnplannedLegsByIds(ids: readonly string[], filters: PlanboardListFilters): Promise<LegDto[]> {
  const pages = await Promise.all(
    chunkIds(ids).map(async (chunk) =>
      throwOnError(
        await apiClient.GET("/api/planning/leg", {
          params: { query: { StartRow: 0, EndRow: chunk.length, Ids: chunk, DateFrom: filters.from, DateTo: filters.till, OrganizationUnitIds: filters.organizationUnitIds.length > 0 ? [...filters.organizationUnitIds] : undefined } },
        }),
      ),
    ),
  );
  return pages.flatMap((page) => page?.rows ?? []);
}

// ─── Commands ────────────────────────────────────────────────────────────────

/** The trip's current concurrency stamp (read fresh: the stamp a Planboard command must carry). */
export async function readTripStamp(queryClient: QueryClient, tripId: string): Promise<string> {
  const trip = await queryClient.fetchQuery({
    queryKey: transportTripKeys.detail(tripId),
    staleTime: 0,
    queryFn: async (): Promise<TransportTripDto> => throwOnError(await apiClient.GET("/api/planning/transport-trip/{id}", { params: { path: { id: tripId } } })),
  });
  return trip.concurrencyStamp ?? "";
}

/** The stamp a command carries: the caller's (the trips list row's) when it has one, otherwise read fresh. */
async function stampFor(queryClient: QueryClient, tripId: string, given: string | null | undefined): Promise<string> {
  return given ? given : readTripStamp(queryClient, tripId);
}

/**
 * A command that carries a trip's stamp, retried once with a fresh stamp when the stamp was stale (design 74 §10): the
 * calculator saves the trip after every change, so a stale stamp on an assign says nothing about the planner's intent, and an
 * assign overwrites nothing the stamp protects. Any other failure, and a stale stamp on the retry, goes to the caller.
 */
export async function retryOnStaleStamp<T>(queryClient: QueryClient, tripId: string, stamp: string | null | undefined, attempt: (stamp: string) => Promise<T>): Promise<T> {
  try {
    return await attempt(stamp ?? "");
  } catch (error) {
    if (!isConcurrencyConflict(error)) throw error;
    return attempt(await readTripStamp(queryClient, tripId));
  }
}

/** After a command on trips: their details, their legs and the leg lists are stale. */
function invalidateTrips(queryClient: QueryClient, tripIds: readonly (string | null | undefined)[]) {
  for (const id of tripIds) {
    if (!id) continue;
    void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(id) });
    void queryClient.invalidateQueries({ queryKey: transportTripKeys.summary(id) });
  }
  void queryClient.invalidateQueries({ queryKey: legKeys.all });
  void queryClient.invalidateQueries({ queryKey: transportTripKeys.list() });
}

const tripPath = (id: string) => ({ params: { path: { id } } });

/** Inline edit of a trip's name, remarks or driver instructions (`POST planboard/{id}/set-field`). */
export function useSetTripField() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, field, value, concurrencyStamp: rowStamp }: { tripId: string; field: PlanboardTripField; value: string | null; concurrencyStamp?: string | null }): Promise<PlanboardChangeDto> => {
      const concurrencyStamp = await stampFor(queryClient, tripId, rowStamp);
      return throwOnError(await apiClient.POST("/api/planning/planboard/{id}/set-field", { ...tripPath(tripId), body: { field, value, concurrencyStamp } }));
    },
    onSuccess: (_change, { tripId }) => invalidateTrips(queryClient, [tripId]),
  });
}

/** Legs dropped on a trip (`POST planboard/{id}/assign-legs`); `allowOutsideWindow` after the user confirmed the warning. */
export function useAssignLegsToTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, legIds, allowOutsideWindow, concurrencyStamp: rowStamp }: { tripId: string; legIds: string[]; allowOutsideWindow?: boolean; concurrencyStamp?: string | null }): Promise<PlanboardChangeDto> => {
      const concurrencyStamp = await stampFor(queryClient, tripId, rowStamp);
      return throwOnError(await apiClient.POST("/api/planning/planboard/{id}/assign-legs", { ...tripPath(tripId), body: { legIds, concurrencyStamp, allowOutsideWindow } }));
    },
    onSuccess: (_change, { tripId }) => invalidateTrips(queryClient, [tripId]),
  });
}

/** What dropping an order on a trip would do (design 74 §8): the legs that would leave which trips, and those that stay. */
export async function fetchAssignOrderPreview(tripId: string, orderId: string): Promise<PlanboardAssignOrderPreviewDto> {
  return throwOnError(await apiClient.GET("/api/planning/planboard/{id}/assign-order-preview", { params: { path: { id: tripId }, query: { OrderId: orderId } } }));
}

/** An order dropped on a trip (`POST planboard/{id}/assign-order`): every leg except the stuck, leaving their old trips. */
export function useAssignOrderToTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, orderId, allowOutsideWindow, concurrencyStamp }: { tripId: string; orderId: string; allowOutsideWindow: boolean; concurrencyStamp: string }): Promise<PlanboardAssignOrderResultDto> =>
      throwOnError(await apiClient.POST("/api/planning/planboard/{id}/assign-order", { ...tripPath(tripId), body: { orderId, allowOutsideWindow, concurrencyStamp } })),
    onSuccess: (result, { tripId }) => invalidateTrips(queryClient, [tripId, ...(result.pulledFrom ?? []).map((trip) => trip.tripId)]),
  });
}

/** Legs taken off their trip (`POST planboard/{id}/unassign-legs`): they return to Legs to schedule. */
export function useUnassignLegsFromTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, legIds, concurrencyStamp: rowStamp }: { tripId: string; legIds: string[]; concurrencyStamp?: string | null }): Promise<PlanboardChangeDto> => {
      const concurrencyStamp = await stampFor(queryClient, tripId, rowStamp);
      return throwOnError(await apiClient.POST("/api/planning/planboard/{id}/unassign-legs", { ...tripPath(tripId), body: { legIds, concurrencyStamp } }));
    },
    onSuccess: (_change, { tripId }) => invalidateTrips(queryClient, [tripId]),
  });
}

/** Legs dragged from one trip onto another (the existing `move-legs` of design 59). */
export function useMoveLegsToTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ fromTripId, toTripId, legIds, allowOutsideWindow }: { fromTripId: string; toTripId: string; legIds: string[]; allowOutsideWindow?: boolean }): Promise<TransportTripDto> =>
      throwOnError(await apiClient.POST("/api/planning/transport-trip/{id}/move-legs", { ...tripPath(fromTripId), body: { legIds, toTripId, allowOutsideWindow } })),
    onSuccess: (_dto, { fromTripId, toTripId }) => invalidateTrips(queryClient, [fromTripId, toTripId]),
  });
}

/** A driver dropped on a trip (`POST planboard/{id}/driver`): the next position, at most three; a duplicate is a no-op. */
export function useAddTripDriver() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, driverId, concurrencyStamp: rowStamp }: { tripId: string; driverId: string; concurrencyStamp?: string | null }): Promise<PlanboardChangeDto> => {
      const concurrencyStamp = await stampFor(queryClient, tripId, rowStamp);
      return throwOnError(await apiClient.POST("/api/planning/planboard/{id}/driver", { ...tripPath(tripId), body: { driverId, concurrencyStamp } }));
    },
    onSuccess: (_change, { tripId }) => invalidateTrips(queryClient, [tripId]),
  });
}

/** Complete with the actual departure and arrival (`POST planboard/{id}/complete`, P:Q17). */
export function usePlanboardCompleteTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tripId, actualDeparture, actualArrival, concurrencyStamp: rowStamp }: { tripId: string; actualDeparture: string; actualArrival: string; concurrencyStamp?: string | null }): Promise<PlanboardChangeDto> => {
      const concurrencyStamp = await stampFor(queryClient, tripId, rowStamp);
      return throwOnError(await apiClient.POST("/api/planning/planboard/{id}/complete", { ...tripPath(tripId), body: { actualDeparture, actualArrival, concurrencyStamp } }));
    },
    onSuccess: (_change, { tripId }) => invalidateTrips(queryClient, [tripId]),
  });
}

/** Optimise the stop order (`POST planboard/{id}/optimize-stops`, 202): the job runs on the server; the trip's live
 * update shows the new order. */
export function useOptimizeTripStops() {
  return useMutation({
    mutationFn: async (tripId: string): Promise<PlanboardOptimizeStopsDto | undefined> =>
      throwOnError(await apiClient.POST("/api/planning/planboard/{id}/optimize-stops", tripPath(tripId))),
  });
}

/** A return leg for an unplanned leg (`POST leg/{id}/return-leg`, design 68 §7). Returns the new leg. */
export function useUnplannedReturnLeg() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (legId: string): Promise<LegDto> => throwOnError(await apiClient.POST("/api/planning/leg/{id}/return-leg", tripPath(legId))),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: legKeys.all }),
  });
}
