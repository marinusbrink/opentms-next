import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";
import { legKeys } from "@/domains/planning-execution/legs";
import { TRIP_WARNING_CODES, transportTripKeys } from "@/domains/planning-execution/trips";

/* The Planboard's Scheduler reads and narrow commands (design 68 §6 and "API contract — reads"; design 70 "Scheduler"):
 * one page of resource rows of a mode with every bar (trip) using them in a window of at most 14 days, the bars of given
 * trips for the live-update refetch, and the commands a drag or drop issues. Every command takes the trip's concurrency
 * stamp and returns the new one (`PlanboardChangeDto`), so the pane reconciles its optimistic bar with it. */

type Schemas = components["schemas"];

export type PlanboardSchedulerDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardSchedulerDto"];
export type PlanboardBarDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardBarDto"];
export type PlanboardBlockDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardBlockDto"];
export type PlanboardResourceRowDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardResourceRowDto"];
export type PlanboardSchedulerKpisDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardSchedulerKpisDto"];
export type PlanboardChangeDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardChangeDto"];
export type PlanboardResourceModeValue = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardResourceMode"];
export type PlanboardCreateTripDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardCreateTripDto"];
export type PlanboardCreateTripResultDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardAssignOrderResultDto"];

/** The Scheduler's modes as the pane stores them, mapped onto the API's enum. */
export type PlanboardResourceMode = "tractor" | "trailer" | "driver";
export const PLANBOARD_RESOURCE_MODES: readonly PlanboardResourceMode[] = ["tractor", "trailer", "driver"];
export const RESOURCE_MODE_VALUE: Readonly<Record<PlanboardResourceMode, PlanboardResourceModeValue>> = { tractor: 1, trailer: 2, driver: 3 };
export const resourceModeKey = (mode: PlanboardResourceMode) => `PlanningExecution:PlanboardResourceMode.${mode === "tractor" ? "Tractor" : mode === "trailer" ? "Trailer" : "Driver"}`;

export function isPlanboardResourceMode(value: unknown): value is PlanboardResourceMode {
  return typeof value === "string" && (PLANBOARD_RESOURCE_MODES as readonly string[]).includes(value);
}

/** The longest Scheduler window (design 68: `MaxSchedulerWindowDays`). */
export const MAX_SCHEDULER_DAYS = 14;
/** The largest page of rows (`MaxPlanboardPageSize`). */
export const MAX_SCHEDULER_PAGE = 200;
/** The most ids one `?ids=` read takes (`MaxIdsPerQuery`). */
export const MAX_IDS_PER_QUERY = 100;

/** The warning codes the Planboard shows, including design 68's resource warnings (20–22). */
export const PLANBOARD_WARNING_CODES: Readonly<Record<number, string>> = {
  ...TRIP_WARNING_CODES,
  20: "ResourceDoubleBooked",
  21: "ResourceUnavailable",
  22: "OutsideWorkingTime",
};
export const planboardWarningKey = (code: number | null | undefined) => `PlanningExecution:TripWarningCode.${PLANBOARD_WARNING_CODES[code ?? 0] ?? "Unknown"}`;

/** A calendar range (`YYYY-MM-DD`, both ends inclusive) as the API's window: local midnight of the first day up to local
 * midnight after the last day. */
export function rangeWindow(range: { from: string; till: string }): { from: string; to: string } {
  const [fy, fm, fd] = range.from.split("-").map(Number);
  const [ty, tm, td] = range.till.split("-").map(Number);
  return { from: new Date(fy, fm - 1, fd).toISOString(), to: new Date(ty, tm - 1, td + 1).toISOString() };
}

export interface PlanboardSchedulerQuery {
  mode: PlanboardResourceMode;
  range: { from: string; till: string };
  organizationUnitIds: readonly string[];
  search: string;
  skip: number;
  take: number;
}

export const planboardSchedulerKeys = {
  all: ["planning", "planboard", "scheduler"] as const,
  page: (query: PlanboardSchedulerQuery) =>
    ["planning", "planboard", "scheduler", query.mode, query.range.from, query.range.till, [...query.organizationUnitIds].sort().join(","), query.search.trim(), query.skip, query.take] as const,
};

export async function fetchPlanboardScheduler(query: PlanboardSchedulerQuery): Promise<PlanboardSchedulerDto> {
  const window = rangeWindow(query.range);
  const { data, error } = await apiClient.GET("/api/planning/planboard/scheduler", {
    params: {
      query: {
        Mode: RESOURCE_MODE_VALUE[query.mode],
        From: window.from,
        To: window.to,
        OrganizationUnitIds: query.organizationUnitIds.length > 0 ? [...query.organizationUnitIds] : undefined,
        Search: query.search.trim() || undefined,
        Skip: query.skip,
        Take: Math.min(Math.max(1, query.take), MAX_SCHEDULER_PAGE),
      },
    },
  });
  if (error) throw error;
  return data ?? {};
}

/** The bars of given trips (live-update refetch), in chunks of at most 100 ids; a trip that is gone is absent. */
export async function fetchPlanboardBars(ids: readonly string[]): Promise<{ bars: PlanboardBarDto[]; names: Record<string, string> }> {
  const unique = [...new Set(ids)];
  const bars: PlanboardBarDto[] = [];
  const names: Record<string, string> = {};
  for (let i = 0; i < unique.length; i += MAX_IDS_PER_QUERY) {
    const { data, error } = await apiClient.GET("/api/planning/planboard/scheduler-bars", { params: { query: { Ids: unique.slice(i, i + MAX_IDS_PER_QUERY) } } });
    if (error) throw error;
    bars.push(...(data?.bars ?? []));
    Object.assign(names, data?.names ?? {});
  }
  return { bars, names };
}

/** One Scheduler page; the previous page stays on screen while the next loads. */
export function usePlanboardScheduler(query: PlanboardSchedulerQuery, enabled = true) {
  return useQuery({
    queryKey: planboardSchedulerKeys.page(query),
    enabled,
    placeholderData: keepPreviousData,
    queryFn: () => fetchPlanboardScheduler(query),
  });
}

const unwrap = <T,>(result: { data?: T; error?: unknown; response?: Response }): T => {
  /* With the status: the Scheduler tells a stale stamp from a business refusal by it (`isConcurrencyConflict`). */
  if (result.error) throw withStatus(result.error, result.response);
  return result.data as T;
};

/** A narrow command: the trip's own record and the lists that show it are refreshed; the Scheduler reconciles itself. */
function usePlanboardCommand<TArgs, TResult extends PlanboardChangeDto = PlanboardChangeDto>(run: (args: TArgs) => Promise<TResult>, touchesLegs = false) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (change) => {
      if (change?.id) void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(change.id) });
      void queryClient.invalidateQueries({ queryKey: transportTripKeys.list() });
      if (touchesLegs) void queryClient.invalidateQueries({ queryKey: legKeys.all });
    },
  });
}

export interface RescheduleArgs {
  tripId: string;
  plannedDeparture: string;
  plannedArrival?: string | null;
  concurrencyStamp: string;
}

/** Moves a trip in time (Planned / InProgress only); the stops move by the same delta. */
export function useRescheduleTrip() {
  return usePlanboardCommand(async ({ tripId, ...body }: RescheduleArgs) =>
    unwrap(await apiClient.POST("/api/planning/planboard/{id}/reschedule", { params: { path: { id: tripId } }, body })),
  );
}

export interface ChangeResourceArgs {
  tripId: string;
  mode: PlanboardResourceMode;
  fromResourceId?: string | null;
  toResourceId: string;
  concurrencyStamp: string;
}

/** Moves a trip to another row: replaces the tractor, a trailer or a driver (D11: a tractor fills only empty slots). */
export function useChangeTripResource() {
  return usePlanboardCommand(async ({ tripId, mode, ...rest }: ChangeResourceArgs) =>
    unwrap(await apiClient.POST("/api/planning/planboard/{id}/change-resource", { params: { path: { id: tripId } }, body: { ...rest, mode: RESOURCE_MODE_VALUE[mode] } })),
  );
}

/** Adds a driver in the next position (max 3; a duplicate is a no-op): a driver dropped on a bar or trip row. */
export function useAddTripDriver() {
  return usePlanboardCommand(async ({ tripId, driverId, concurrencyStamp }: { tripId: string; driverId: string; concurrencyStamp: string }) =>
    unwrap(await apiClient.POST("/api/planning/planboard/{id}/driver", { params: { path: { id: tripId } }, body: { driverId, concurrencyStamp } })),
  );
}

export interface CreatePlanboardTripArgs {
  transportTripTypeId?: string | null;
  mode?: PlanboardResourceMode;
  resourceId?: string | null;
  plannedDeparture: string;
  legIds?: string[];
  /** An order dropped on an empty cell (design 74 §8): its legs go on the new trip; not together with `legIds`. */
  orderId?: string | null;
}

/** A trip from an empty Scheduler cell or a drop of legs or an order on a row: the type's defaults, the resource in its slot, the legs. */
export function useCreatePlanboardTrip() {
  return usePlanboardCommand(
    async ({ mode, legIds, ...rest }: CreatePlanboardTripArgs): Promise<PlanboardCreateTripResultDto> =>
      unwrap<PlanboardCreateTripResultDto>(
        await apiClient.POST("/api/planning/planboard/trip", {
          body: { ...rest, mode: mode ? RESOURCE_MODE_VALUE[mode] : undefined, legIds: legIds && legIds.length > 0 ? legIds : undefined },
        }),
      ),
    true,
  );
}

/** Assigns unplanned legs to a trip (a leg drop on a bar, or "Add to trip" on the map); `allowOutsideWindow` after the
 * user confirmed the `LegOutsideTripWindow` warning. */
export function useAssignLegsToTrip() {
  return usePlanboardCommand(
    async ({ tripId, legIds, concurrencyStamp, allowOutsideWindow }: { tripId: string; legIds: string[]; concurrencyStamp: string; allowOutsideWindow?: boolean }) =>
      unwrap(await apiClient.POST("/api/planning/planboard/{id}/assign-legs", { params: { path: { id: tripId } }, body: { legIds, concurrencyStamp, allowOutsideWindow } })),
    true,
  );
}

/** The bars of given trips as a query (the Resources pane's trip dots); empty without ids. */
export function usePlanboardBarsByIds(ids: readonly string[], enabled = true) {
  const sorted = [...new Set(ids.map((id) => id.toLowerCase()))].sort();
  return useQuery({
    queryKey: ["planning", "planboard", "bars", sorted.join(",")] as const,
    enabled: enabled && sorted.length > 0,
    placeholderData: keepPreviousData,
    queryFn: () => fetchPlanboardBars(sorted),
  });
}
