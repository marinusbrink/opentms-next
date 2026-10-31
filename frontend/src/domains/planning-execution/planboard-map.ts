import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { rangeWindow } from "@/domains/planning-execution/planboard-scheduler";

/* The Planboard map's reads (design 68 "API contract — reads", design 70 "Map"): the focused trip's detail (stops with
 * window, ETA, actuals and lateness; the stored route segments with toll; distance, duration, CO₂), the range's trips
 * as first→last lines (≤ 500, window ≤ 14 days) and the unplanned legs near a trip (≤ 200, radius ≤ 100 km, ≤ 31 days).
 * Nothing is calculated on read (AD-07): the map draws what the TripCalculator stored. */

type Schemas = components["schemas"];

export type PlanboardTripMapDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardTripMapDto"];
export type PlanboardMapStopDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardMapStopDto"];
export type PlanboardRouteSegmentDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardRouteSegmentDto"];
export type PlanboardMapTripDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardMapTripDto"];
export type PlanboardMapTripsDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardMapTripsDto"];
export type PlanboardNearLegDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardNearLegDto"];
export type PlanboardUnplannedNearDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardUnplannedNearDto"];

/** The map's windows (design 68): all trips ≤ 14 days, unplanned nearby ≤ 31 days and ≤ 100 km. */
export const MAX_MAP_TRIPS_DAYS = 14;
export const MAX_UNPLANNED_NEAR_DAYS = 31;
export const MAX_UNPLANNED_NEAR_RADIUS_KM = 100;
export const DEFAULT_UNPLANNED_NEAR_RADIUS_KM = 25;

export const planboardMapKeys = {
  all: ["planning", "planboard", "map"] as const,
  trip: (id: string | null | undefined) => ["planning", "planboard", "map", "trip", id?.toLowerCase()] as const,
  trips: (range: { from: string; till: string }, units: readonly string[]) => ["planning", "planboard", "map", "trips", range.from, range.till, [...units].sort().join(",")] as const,
  near: (tripId: string | null | undefined, radiusKm: number, range: { from: string; till: string }) =>
    ["planning", "planboard", "map", "near", tripId?.toLowerCase(), radiusKm, range.from, range.till] as const,
};

/** The focused trip's detail layer. */
export function usePlanboardTripMap(tripId: string | null | undefined) {
  return useQuery({
    queryKey: planboardMapKeys.trip(tripId),
    enabled: !!tripId,
    queryFn: async (): Promise<PlanboardTripMapDto> => {
      const { data, error } = await apiClient.GET("/api/planning/planboard/{id}/map", { params: { path: { id: tripId! } } });
      if (error) throw error;
      return data!;
    },
  });
}

/** The "all trips" layer of the range (at most 14 days; the caller clamps the board range). */
export function usePlanboardMapTrips(range: { from: string; till: string }, organizationUnitIds: readonly string[], enabled = true) {
  return useQuery({
    queryKey: planboardMapKeys.trips(range, organizationUnitIds),
    enabled,
    placeholderData: keepPreviousData,
    queryFn: async (): Promise<PlanboardMapTripsDto> => {
      const window = rangeWindow(range);
      const { data, error } = await apiClient.GET("/api/planning/planboard/map-trips", {
        params: { query: { From: window.from, To: window.to, OrganizationUnitIds: organizationUnitIds.length > 0 ? [...organizationUnitIds] : undefined } },
      });
      if (error) throw error;
      return data ?? {};
    },
  });
}

/** The unplanned legs near the trip's stops (the "unplanned nearby" toggle). */
export function usePlanboardUnplannedNear(tripId: string | null | undefined, radiusKm: number, range: { from: string; till: string }, enabled = true) {
  return useQuery({
    queryKey: planboardMapKeys.near(tripId, radiusKm, range),
    enabled: enabled && !!tripId,
    queryFn: async (): Promise<PlanboardUnplannedNearDto> => {
      const { data, error } = await apiClient.GET("/api/planning/planboard/unplanned-near", {
        params: { query: { TripId: tripId!, RadiusKm: radiusKm, From: range.from, To: range.till } },
      });
      if (error) throw error;
      return data ?? {};
    },
  });
}
