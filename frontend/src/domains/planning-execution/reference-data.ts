import { useMutation, useQuery } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks, referenceDataQueryKey } from "@/lib/reference-data/crud-hooks";

/* The Planning & Execution reference data shown in the Master data app (design 53). Row = grid, Dto = editor,
 * Create/Update = what the editor sends; the schema names carry the backend namespaces. */

type Schemas = components["schemas"];

export type TripExecution = Schemas["OpenTms.PlanningExecution.TransportTrips.TripExecution"];
export const TRIP_EXECUTIONS: { value: TripExecution; key: string }[] = [
  { value: 1, key: "PlanningExecution:TripExecution.OwnFleet" },
  { value: 2, key: "PlanningExecution:TripExecution.Subcontracted" },
];

export type TransportTripTypeRow = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripTypeRowDto"];
export type TransportTripTypeDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripTypeDto"];
export type TransportTripTypeCreateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripTypeCreateDto"];
export type TransportTripTypeUpdateDto = Schemas["OpenTms.PlanningExecution.TransportTrips.TransportTripTypeUpdateDto"];

export const transportTripTypes = createReferenceDataHooks<
  TransportTripTypeRow,
  TransportTripTypeDto,
  TransportTripTypeCreateDto,
  TransportTripTypeUpdateDto,
  { includeArchived?: boolean }
>("transportTripTypes", "/api/planning/transport-trip-type");

/** The tenant's default trip type (design 53: at most one), or null when none is marked: the list sorted on the flag. */
export function useDefaultTransportTripTypeId(enabled = true) {
  return useQuery({
    queryKey: referenceDataQueryKey("transportTripTypes", "default"),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<string | null> => {
      const { data, error } = await apiClient.GET("/api/planning/transport-trip-type", { params: { query: { StartRow: 0, EndRow: 1, SortBy: "isDefault", SortDir: "desc" } } });
      if (error) throw error;
      const row = data?.rows?.[0];
      return row?.isDefault ? (row.id ?? null) : null;
    },
  });
}

/** Moves the tenant's one default trip type (design 53). */
export function useSetDefaultTransportTripType() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/planning/transport-trip-type/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}
