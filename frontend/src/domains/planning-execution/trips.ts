import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { useRecordUpdates } from "@/lib/realtime/use-record-updates";

/* Transport trips (design 59; design 61 "Trips list and editor"): the list, the record with its stops and their legs,
 * the header save, the commands of the lifecycle (start, complete, cancel, archive, unarchive, delete), the carrier/
 * vehicles/drivers commands, the leg commands on a trip (assign, unassign, move, split, return leg, carry out
 * separately, outsource origin) and the stop commands (add manual, move/update, remove, record actuals). The trip's
 * derived values (times, distances, load factor, warnings, CO₂, cost and revenue) arrive by push: the screens watch
 * `PlanningExecution.TransportTrip` (flow 2). */

type Schemas = components["schemas"];

export type TransportTripRow = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripRowDto"];
export type TransportTripDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripDto"];
export type TransportTripCreateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripCreateDto"];
export type TransportTripUpdateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripUpdateDto"];
export type TransportTripSummaryDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripSummaryDto"];
export type TransportTripStatus = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripStatus"];
export type TripStopDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripStopDto"];
export type TripStopLegDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripStopLegDto"];
export type TripWarningDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TripWarningDto"];
export type TripWarningCode = Schemas["OpenTms.PlanningExecution.TransportTrips.TripWarningCode"];
export type TripExecution = Schemas["OpenTms.PlanningExecution.TransportTrips.TripExecution"];
export type StopCompositionMode = Schemas["OpenTms.PlanningExecution.TransportTrips.StopCompositionMode"];
export type FirstLastStopRule = Schemas["OpenTms.PlanningExecution.TransportTrips.FirstLastStopRule"];
export type AddManualStopDto = Schemas["OpenTms.PlanningExecution.TransportTrips.AddManualStopDto"];
export type UpdateStopDto = Schemas["OpenTms.PlanningExecution.TransportTrips.UpdateStopDto"];
export type StopActualsDto = Schemas["OpenTms.PlanningExecution.TransportTrips.StopActualsDto"];
export type OutsourceOriginDto = Schemas["OpenTms.PlanningExecution.TransportTrips.OutsourceOriginDto"];
export type TripAttributeDefinitionRow = Schemas["OpenTms.PlanningExecution.TransportTrips.TripAttributeDefinitionRowDto"];

export const TRANSPORT_TRIP_TARGET_TYPE = "PlanningExecution.TransportTrip";

export const TRANSPORT_TRIP_STATUSES: { value: TransportTripStatus; name: string; key: string }[] = [
  { value: 1, name: "Planned", key: "PlanningExecution:TransportTripStatus.Planned" },
  { value: 2, name: "InProgress", key: "PlanningExecution:TransportTripStatus.InProgress" },
  { value: 3, name: "Completed", key: "PlanningExecution:TransportTripStatus.Completed" },
  { value: 4, name: "Cancelled", key: "PlanningExecution:TransportTripStatus.Cancelled" },
];

export function tripStatusName(status: TransportTripStatus | null | undefined): string | undefined {
  return TRANSPORT_TRIP_STATUSES.find((s) => s.value === status)?.name;
}

export const STOP_COMPOSITION_MODES: { value: StopCompositionMode; key: string }[] = [
  { value: 1, key: "PlanningExecution:StopCompositionMode.MergeByLocationAndActivity" },
  { value: 2, key: "PlanningExecution:StopCompositionMode.OneStopPairPerLeg" },
];

export const FIRST_LAST_STOP_RULES: { value: FirstLastStopRule; key: string }[] = [
  { value: 0, key: "PlanningExecution:FirstLastStopRule.None" },
  { value: 1, key: "PlanningExecution:FirstLastStopRule.DepartureIsFirstStop" },
  { value: 2, key: "PlanningExecution:FirstLastStopRule.ArrivalIsLastStop" },
  { value: 3, key: "PlanningExecution:FirstLastStopRule.Both" },
];

/** The warning codes of the TripCalculator (design 59), localized as `PlanningExecution:TripWarningCode.<Code>`. */
export const TRIP_WARNING_CODES: Record<number, string> = {
  1: "MissingTransportCharacteristic",
  2: "IncompatibleTemperatures",
  3: "IncompatibleProducts",
  4: "IncompatibleTransportUnits",
  5: "LegOutsideTripWindow",
  6: "EtaOutsideStopWindow",
  7: "LegChangedAfterPlanning",
  8: "AddressNotGeocoded",
  9: "NoCostPriceSource",
  10: "RoutingNotConfigured",
  11: "CapacityExceeded",
};

export const tripWarningKey = (code: TripWarningCode | null | undefined) => `PlanningExecution:TripWarningCode.${TRIP_WARNING_CODES[code ?? 0] ?? "Unknown"}`;

/** The URL filter values of the Trips list (apps.config.ts) mapped onto the API's `view` (design 59: a string). */
export const TRIP_VIEWS: Record<string, string> = {
  today: "Today",
  "this-week": "ThisWeek",
  open: "Open",
  all: "All",
};

export const transportTripKeys = {
  all: ["planning", "transportTrips"] as const,
  list: () => ["planning", "transportTrips", "list"] as const,
  detail: (id: string | undefined) => ["planning", "transportTrips", "detail", id?.toLowerCase()] as const,
  summary: (id: string | undefined) => ["planning", "transportTrips", "summary", id?.toLowerCase()] as const,
  attributeDefinitions: () => ["planning", "tripAttributeDefinitions"] as const,
};

export const transportTripUpdateKeys = (id: string) => [transportTripKeys.detail(id), transportTripKeys.summary(id)];

export interface TransportTripListFilters {
  view?: string;
  status?: TransportTripStatus[];
  dateFrom?: string;
  dateTo?: string;
  carrierRelationId?: string;
  organizationUnitId?: string;
  isArchived?: boolean;
  hideEmpty?: boolean;
}

export async function fetchTransportTrips(request: GridRequest, filters: TransportTripListFilters): Promise<GridResponse<TransportTripRow>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/planning/transport-trip", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        View: filters.view,
        Status: filters.status && filters.status.length > 0 ? filters.status : undefined,
        DateFrom: filters.dateFrom || undefined,
        DateTo: filters.dateTo || undefined,
        CarrierRelationId: filters.carrierRelationId || undefined,
        OrganizationUnitId: filters.organizationUnitId || undefined,
        IsArchived: filters.isArchived,
        HideEmpty: filters.hideEmpty,
      },
    },
  });
  if (error) throw error;
  return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
}

export function useTransportTripsGridFetcher(filters: TransportTripListFilters) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchTransportTrips(request, JSON.parse(key) as TransportTripListFilters), [key]);
}

export function useTransportTrip(id: string | undefined) {
  return useQuery({
    queryKey: transportTripKeys.detail(id),
    enabled: id != null,
    queryFn: async (): Promise<TransportTripDto> => {
      const { data, error, response } = await apiClient.GET("/api/planning/transport-trip/{id}", { params: { path: { id: id! } } });
      /* The ABP error body carries no status; the page tells "not found" from it (recordLoadState). */
      if (error) throw { ...(error as object), status: response.status };
      return data!;
    },
  });
}

export function useTransportTripSummary(id: string | undefined) {
  return useQuery({
    queryKey: transportTripKeys.summary(id),
    enabled: id != null,
    queryFn: async (): Promise<TransportTripSummaryDto> => {
      const { data, error } = await apiClient.GET("/api/planning/transport-trip/{id}/summary", { params: { path: { id: id! } } });
      if (error) throw error;
      return data!;
    },
  });
}

export function useTransportTripUpdates(ids: readonly (string | null | undefined)[], onUpdate?: () => void) {
  useRecordUpdates(TRANSPORT_TRIP_TARGET_TYPE, ids, { queryKeysFor: transportTripUpdateKeys, onUpdate });
}

export function useTripAttributeDefinitions(enabled = true) {
  return useQuery({
    queryKey: transportTripKeys.attributeDefinitions(),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<TripAttributeDefinitionRow[]> => {
      const { data, error } = await apiClient.GET("/api/planning/trip-attribute-definition", {
        params: { query: { StartRow: 0, EndRow: 200, SortBy: "position", SortDir: "asc" } },
      });
      if (error) throw error;
      return data?.rows ?? [];
    },
  });
}

const unwrap = <T,>(result: { data?: T; error?: unknown }): T => {
  if (result.error) throw result.error;
  return result.data as T;
};

function useTripMutation<TArgs>(run: (args: TArgs) => Promise<TransportTripDto>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (dto) => {
      if (dto?.id) queryClient.setQueryData(transportTripKeys.detail(dto.id), dto);
      void queryClient.invalidateQueries({ queryKey: transportTripKeys.list() });
      void queryClient.invalidateQueries({ queryKey: ["planning", "legs"] });
    },
  });
}

const tripPath = (id: string) => ({ params: { path: { id } } });

export function useUpdateTransportTrip() {
  return useTripMutation(async ({ id, dto }: { id: string; dto: TransportTripUpdateDto }) =>
    unwrap(await apiClient.PUT("/api/planning/transport-trip/{id}", { ...tripPath(id), body: dto })),
  );
}

export function useSetTripCarrier() {
  return useTripMutation(async ({ id, carrierRelationId, carrierContactPersonId }: { id: string; carrierRelationId: string | null; carrierContactPersonId: string | null }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/set-carrier", { ...tripPath(id), body: { carrierRelationId, carrierContactPersonId } })),
  );
}

export function useSetTripVehicles() {
  return useTripMutation(async ({ id, vehicleCombinationId, vehicleIds }: { id: string; vehicleCombinationId: string | null; vehicleIds: string[] }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/set-vehicles", { ...tripPath(id), body: { vehicleCombinationId, vehicleIds } })),
  );
}

export function useSetTripDrivers() {
  return useTripMutation(async ({ id, driverIds }: { id: string; driverIds: string[] }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/set-drivers", { ...tripPath(id), body: { driverIds } })),
  );
}

export function useStartTrip() {
  return useTripMutation(async ({ id, at }: { id: string; at?: string | null }) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/start", { ...tripPath(id), body: { at } })));
}

export function useCompleteTrip() {
  return useTripMutation(async ({ id, at }: { id: string; at?: string | null }) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/complete", { ...tripPath(id), body: { at } })));
}

export function useCancelTrip() {
  return useTripMutation(async (id: string) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/cancel", tripPath(id))));
}

export function useArchiveTrip() {
  return useTripMutation(async (id: string) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/archive", tripPath(id))));
}

export function useUnarchiveTrip() {
  return useTripMutation(async (id: string) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/unarchive", tripPath(id))));
}

/** The Archive wizard runs this per trip, so it reports per item (UI-WIZ-004). */
export async function archiveTrip(id: string): Promise<TransportTripDto> {
  return unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/archive", tripPath(id)));
}

export function useDeleteTrip() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/planning/transport-trip/{id}", tripPath(id));
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: transportTripKeys.all }),
  });
}

export function useUnassignLegs() {
  return useTripMutation(async ({ id, legIds }: { id: string; legIds: string[] }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/unassign-legs", { ...tripPath(id), body: { legIds } })),
  );
}

export function useCarryOutSeparately() {
  return useTripMutation(async ({ id, legId }: { id: string; legId: string }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/carry-out-separately", { ...tripPath(id), body: { legId } })),
  );
}

/** Return leg and outsource origin return the new leg; the trip is refetched by invalidation. */
function useTripLegMutation<TArgs>(run: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: transportTripKeys.all });
      void queryClient.invalidateQueries({ queryKey: ["planning", "legs"] });
    },
  });
}

export function useCreateReturnLeg() {
  return useTripLegMutation(async ({ id, legId }: { id: string; legId: string }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/return-leg", { ...tripPath(id), body: { legId } })),
  );
}

export function useOutsourceOrigin() {
  return useTripLegMutation(async ({ id, dto }: { id: string; dto: OutsourceOriginDto }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/outsource-origin", { ...tripPath(id), body: dto })),
  );
}

export function useAddManualStop() {
  return useTripMutation(async ({ id, dto }: { id: string; dto: AddManualStopDto }) => unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/stop", { ...tripPath(id), body: dto })));
}

export function useUpdateStop() {
  return useTripMutation(async ({ id, stopId, dto }: { id: string; stopId: string; dto: UpdateStopDto }) =>
    unwrap(await apiClient.PUT("/api/planning/transport-trip/{id}/stop/{stopId}", { params: { path: { id, stopId } }, body: dto })),
  );
}

export function useRemoveStop() {
  return useTripMutation(async ({ id, stopId }: { id: string; stopId: string }) =>
    unwrap(await apiClient.DELETE("/api/planning/transport-trip/{id}/stop/{stopId}", { params: { path: { id, stopId } } })),
  );
}

export function useRecordStopActuals() {
  return useTripMutation(async ({ id, stopId, dto }: { id: string; stopId: string; dto: StopActualsDto }) =>
    unwrap(await apiClient.POST("/api/planning/transport-trip/{id}/record-stop-actuals/{stopId}", { params: { path: { id, stopId } }, body: dto })),
  );
}
