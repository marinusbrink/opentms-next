import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { planboardSchedulerKeys, rangeWindow } from "@/domains/planning-execution/planboard-scheduler";

/* Resource availability (design 68 §2, design 70 "Resources"): unavailability of vehicles and drivers (leave, sickness,
 * training, maintenance, inspection; optionally weekly repeating) and each resource's weekly working pattern (weekday
 * 0 = Monday … 6 = Sunday; no row = no constraint that day). The Resources pane's driver and vehicle lists carry each
 * resource's blocks and trip ids for the window (≤ 14 days). */

type Schemas = components["schemas"];

export type ResourceKind = Schemas["OpenTms.PlanningExecution.Availability.ResourceKind"];
export type UnavailabilityReason = Schemas["OpenTms.PlanningExecution.Availability.UnavailabilityReason"];
export type ResourceAvailabilityListDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceAvailabilityListDto"];
export type ResourceUnavailabilityDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceUnavailabilityDto"];
export type ResourceUnavailabilityCreateDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceUnavailabilityCreateDto"];
export type ResourceUnavailabilityUpdateDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceUnavailabilityUpdateDto"];
export type ResourceWorkingPatternDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceWorkingPatternDto"];
export type ResourceWorkingDayDto = Schemas["OpenTms.PlanningExecution.Availability.ResourceWorkingDayDto"];
export type PlanboardResourceDriverDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardResourceDriverDto"];
export type PlanboardResourceVehicleDto = Schemas["OpenTms.PlanningExecution.Planboard.PlanboardResourceVehicleDto"];

export const RESOURCE_KIND = { Vehicle: 1, Driver: 2 } as const satisfies Record<string, ResourceKind>;

export const RESOURCE_AVAILABILITY_PERMISSION = "PlanningExecution.ResourceAvailability";
export const RESOURCE_AVAILABILITY_MANAGE_PERMISSION = "PlanningExecution.ResourceAvailability.Manage";

export const UNAVAILABILITY_REASONS: { value: UnavailabilityReason; name: string; key: string }[] = [
  { value: 1, name: "Leave", key: "PlanningExecution:UnavailabilityReason.Leave" },
  { value: 2, name: "Sick", key: "PlanningExecution:UnavailabilityReason.Sick" },
  { value: 3, name: "Training", key: "PlanningExecution:UnavailabilityReason.Training" },
  { value: 4, name: "Maintenance", key: "PlanningExecution:UnavailabilityReason.Maintenance" },
  { value: 5, name: "Inspection", key: "PlanningExecution:UnavailabilityReason.Inspection" },
  { value: 99, name: "Other", key: "PlanningExecution:UnavailabilityReason.Other" },
];
/** The reasons offered per resource kind (design 70: vehicles get Maintenance and Inspection). */
export const DRIVER_REASONS: readonly UnavailabilityReason[] = [1, 2, 3, 99];
export const VEHICLE_REASONS: readonly UnavailabilityReason[] = [4, 5, 99];

export const unavailabilityReasonKey = (reason: number | null | undefined) =>
  UNAVAILABILITY_REASONS.find((r) => r.value === reason)?.key ?? "PlanningExecution:UnavailabilityReason.Other";

/** Weekdays: 0 = Monday … 6 = Sunday (working patterns); the recurrence flag of weekday i is `1 << i` (Mon = 1 … Sun = 64). */
export const WEEKDAYS = [0, 1, 2, 3, 4, 5, 6] as const;
export const weekdayFlag = (weekday: number) => 1 << weekday;
export const weekdaysFromFlags = (flags: number | null | undefined): number[] => WEEKDAYS.filter((d) => ((flags ?? 0) & weekdayFlag(d)) !== 0);
export const flagsFromWeekdays = (weekdays: readonly number[]): number => weekdays.reduce((sum, d) => sum | weekdayFlag(d), 0);

/** The longest availability read (design 68: `MaxAvailabilityWindowDays`) and the most ids per read. */
export const MAX_AVAILABILITY_WINDOW_DAYS = 62;
export const MAX_AVAILABILITY_RESOURCE_IDS = 100;
export const MAX_UNAVAILABILITY_REMARKS = 512;

export const resourceAvailabilityKeys = {
  all: ["planning", "resourceAvailability"] as const,
  list: (kind: ResourceKind, ids: readonly string[], range: { from: string; till: string }) =>
    ["planning", "resourceAvailability", "list", kind, [...ids].map((id) => id.toLowerCase()).sort().join(","), range.from, range.till] as const,
  drivers: (range: { from: string; till: string }, search: string, skip: number, take: number) =>
    ["planning", "resourceAvailability", "drivers", range.from, range.till, search.trim(), skip, take] as const,
  vehicles: (range: { from: string; till: string }, units: readonly string[], search: string, skip: number, take: number) =>
    ["planning", "resourceAvailability", "vehicles", range.from, range.till, [...units].sort().join(","), search.trim(), skip, take] as const,
};

/** The availability of given resources in a window: the expanded blocks, the unavailabilities behind them and the patterns. */
export function useResourceAvailability(kind: ResourceKind, resourceIds: readonly string[], range: { from: string; till: string }, enabled = true) {
  return useQuery({
    queryKey: resourceAvailabilityKeys.list(kind, resourceIds, range),
    enabled: enabled && resourceIds.length > 0,
    queryFn: async (): Promise<ResourceAvailabilityListDto> => {
      const window = rangeWindow(range);
      const { data, error } = await apiClient.GET("/api/planning/resource-availability", {
        params: { query: { ResourceKind: kind, ResourceIds: resourceIds.slice(0, MAX_AVAILABILITY_RESOURCE_IDS), From: window.from, To: window.to } },
      });
      if (error) throw error;
      return data ?? {};
    },
  });
}

/** A page of drivers with their blocks and trip ids in the window (Resources pane). */
export function usePlanboardResourceDrivers(range: { from: string; till: string }, search: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: resourceAvailabilityKeys.drivers(range, search, skip, take),
    enabled,
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const window = rangeWindow(range);
      const { data, error } = await apiClient.GET("/api/planning/planboard/resource-drivers", {
        params: { query: { From: window.from, To: window.to, Search: search.trim() || undefined, Skip: skip, Take: take } },
      });
      if (error) throw error;
      return { items: data?.items ?? [], totalCount: Number(data?.totalCount ?? 0) };
    },
  });
}

/** A page of motor vehicles with their blocks and trip ids in the window (Resources pane, Vehicles tab). */
export function usePlanboardResourceVehicles(range: { from: string; till: string }, organizationUnitIds: readonly string[], search: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: resourceAvailabilityKeys.vehicles(range, organizationUnitIds, search, skip, take),
    enabled,
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const window = rangeWindow(range);
      const { data, error } = await apiClient.GET("/api/planning/planboard/resource-vehicles", {
        params: {
          query: {
            From: window.from,
            To: window.to,
            OrganizationUnitIds: organizationUnitIds.length > 0 ? [...organizationUnitIds] : undefined,
            Search: search.trim() || undefined,
            Skip: skip,
            Take: take,
          },
        },
      });
      if (error) throw error;
      return { items: data?.items ?? [], totalCount: Number(data?.totalCount ?? 0) };
    },
  });
}

const unwrap = <T,>(result: { data?: T; error?: unknown }): T => {
  if (result.error) throw result.error;
  return result.data as T;
};

/** After an availability change: the availability reads, the resource lists and the Scheduler blocks are refreshed. */
function useAvailabilityMutation<TArgs, TResult>(run: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: resourceAvailabilityKeys.all });
      void queryClient.invalidateQueries({ queryKey: planboardSchedulerKeys.all });
    },
  });
}

export function useCreateUnavailability() {
  return useAvailabilityMutation(async (dto: ResourceUnavailabilityCreateDto) => unwrap(await apiClient.POST("/api/planning/resource-availability", { body: dto })));
}

export function useUpdateUnavailability() {
  return useAvailabilityMutation(async ({ id, dto }: { id: string; dto: ResourceUnavailabilityUpdateDto }) =>
    unwrap(await apiClient.PUT("/api/planning/resource-availability/{id}", { params: { path: { id } }, body: dto })),
  );
}

export function useDeleteUnavailability() {
  return useAvailabilityMutation(async (id: string) => {
    const { error } = await apiClient.DELETE("/api/planning/resource-availability/{id}", { params: { path: { id } } });
    if (error) throw error;
  });
}

/** Replaces a resource's weekly working pattern (the weekdays named are set, the others removed). */
export function useSaveWorkingPatterns() {
  return useAvailabilityMutation(async ({ kind, resourceId, days }: { kind: ResourceKind; resourceId: string; days: ResourceWorkingDayDto[] }) =>
    unwrap(await apiClient.PUT("/api/planning/resource-availability/patterns", { body: { resourceKind: kind, resourceId, days } })),
  );
}
