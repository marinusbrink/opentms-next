import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The Maintenance view of the Administration app, read through the Platform module's maintenance
 * endpoints: caches, databases, outbox, health and background jobs. Everything but the outbox is
 * host-only; the backend enforces the side, these hooks only shape the calls. */

type Schemas = components["schemas"];

export type CacheStatus = Schemas["OpenTms.Platform.Administration.Maintenance.CacheStatusDto"];
export type CacheClearResult = Schemas["OpenTms.Platform.Administration.Maintenance.CacheClearResultDto"];
export type CacheName = Schemas["OpenTms.Platform.Administration.Maintenance.CacheNameDto"];
export type CacheEntryRowDto = Schemas["OpenTms.Platform.Administration.Maintenance.CacheEntryRowDto"];
/** Grid rows carry `id`; a cache entry's identity is its full key. */
export type CacheEntryRow = CacheEntryRowDto & { id: string };
export type DatabaseRowDto = Schemas["OpenTms.Platform.Administration.Maintenance.DatabaseRowDto"];
export type DatabaseDetail = Schemas["OpenTms.Platform.Administration.Maintenance.DatabaseDetailDto"];
export type DatabasesSummary = Schemas["OpenTms.Platform.Administration.Maintenance.DatabasesSummaryDto"];
export type BlobStorage = Schemas["OpenTms.Platform.Administration.Maintenance.BlobStorageDto"];
export type OutboxOverview = Schemas["OpenTms.Platform.Administration.Maintenance.OutboxOverviewDto"];
export type OutboxRowDto = Schemas["OpenTms.Platform.Administration.Maintenance.OutboxRowDto"];
export type OutboxDispatchResult = Schemas["OpenTms.Platform.Administration.Maintenance.OutboxDispatchResultDto"];
export type HealthReport = Schemas["OpenTms.Platform.Administration.Maintenance.HealthReportDto"];
export type HealthCheckEntry = Schemas["OpenTms.Platform.Administration.Maintenance.HealthCheckEntryDto"];
export type InstancesOverview = Schemas["OpenTms.Platform.Administration.Maintenance.InstancesOverviewDto"];
export type ApplicationInstanceRow = Schemas["OpenTms.Platform.Administration.Maintenance.ApplicationInstanceRowDto"];
export type ApplicationInstanceDetail = Schemas["OpenTms.Platform.Administration.Maintenance.ApplicationInstanceDetailDto"];
export type BackgroundJobsOverview = Schemas["OpenTms.Platform.Administration.Maintenance.BackgroundJobsOverviewDto"];
export type BackgroundJobRow = Schemas["OpenTms.Platform.Administration.Maintenance.BackgroundJobRowDto"];

/** Grid rows carry `id` (OpenTmsGrid's row identity); the API rows carry `key`. */
export type DatabaseRow = DatabaseRowDto & { id: string };
export type OutboxRow = OutboxRowDto & { id: string };

export type BackgroundJobState = "Enqueued" | "Scheduled" | "Processing" | "Succeeded" | "Failed" | "Deleted" | "Recurring";
export const BACKGROUND_JOB_STATES: readonly BackgroundJobState[] = [
  "Failed",
  "Enqueued",
  "Scheduled",
  "Processing",
  "Succeeded",
  "Deleted",
  "Recurring",
];
export const DEFAULT_BACKGROUND_JOB_STATE: BackgroundJobState = "Failed";

export function jobStateKey(state: string | null | undefined): string {
  return state ? `Maintenance:State.${state}` : "";
}

/** Localization key of an instance status (Running, Unresponsive, Stopped). */
export function instanceStatusKey(status: string | null | undefined): string {
  return status ? `Maintenance:Instance.Status.${status}` : "";
}

/** Localization key of a health status (Healthy, Degraded, Unhealthy); unknown values fall back to the raw text. */
export function healthStatusKey(status: string | null | undefined): string {
  switch (status) {
    case "Healthy":
    case "Degraded":
    case "Unhealthy":
      return `Maintenance:Status${status}`;
    default:
      return status ?? "";
  }
}

/** States from which Hangfire lets a job be put back in the queue. */
export function canRequeue(state: string | null | undefined): boolean {
  return state === "Failed" || state === "Deleted" || state === "Scheduled";
}

/** States in which deleting a job still changes something. */
export function canDeleteJob(state: string | null | undefined): boolean {
  return state === "Failed" || state === "Scheduled" || state === "Enqueued" || state === "Processing";
}

/** A .NET TimeSpan string from the API ("00:05:00", "1.02:00:00") as seconds. */
export function timeSpanToSeconds(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(value);
  if (!match) return null;
  const [, days, hours, minutes, seconds] = match;
  return Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds);
}

/** A .NET TimeSpan string from the API ("00:05:00", "1.02:00:00") as minutes, for the duration formatter. */
export function timeSpanToMinutes(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(value);
  if (!match) return null;
  const [, days, hours, minutes, seconds] = match;
  return Number(days ?? 0) * 1440 + Number(hours) * 60 + Number(minutes) + Math.round(Number(seconds) / 60);
}

export const maintenanceKeys = {
  all: ["administration", "maintenance"] as const,
  caches: () => ["administration", "maintenance", "caches"] as const,
  cacheNames: () => ["administration", "maintenance", "caches", "names"] as const,
  databasesSummary: () => ["administration", "maintenance", "databases", "summary"] as const,
  database: (key: string) => ["administration", "maintenance", "databases", key] as const,
  outboxOverview: () => ["administration", "maintenance", "outbox", "overview"] as const,
  health: () => ["administration", "maintenance", "health"] as const,
  instancesOverview: () => ["administration", "maintenance", "instances", "overview"] as const,
  instance: (id: string) => ["administration", "maintenance", "instances", id] as const,
  jobsOverview: () => ["administration", "maintenance", "background-jobs", "overview"] as const,
};

function toGridResponse<T>(data: { rows?: T[] | null; totalCount?: number; filteredCount?: number } | undefined): GridResponse<T> {
  return {
    rows: data?.rows ?? [],
    totalCount: Number(data?.totalCount ?? 0),
    filteredCount: Number(data?.filteredCount ?? 0),
  };
}

// ── Caches ──────────────────────────────────────────────────────────────────────────

export function useCacheStatus() {
  return useQuery({
    queryKey: maintenanceKeys.caches(),
    queryFn: async (): Promise<CacheStatus> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/caches");
      if (error) throw error;
      return data!;
    },
  });
}

export function useCacheNames() {
  return useQuery({
    queryKey: maintenanceKeys.cacheNames(),
    queryFn: async (): Promise<CacheName[]> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/caches/names");
      if (error) throw error;
      return data ?? [];
    },
  });
}

export function useCacheEntriesGridFetcher(
  cacheName: string | undefined,
  refreshKey = 0,
): (request: GridRequest) => Promise<GridResponse<CacheEntryRow>> {
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<CacheEntryRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/caches/entries", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
            cacheName,
          },
        },
      });
      if (error) throw error;
      const response = toGridResponse<CacheEntryRowDto>(data);
      return { ...response, rows: response.rows.map((row) => ({ ...row, id: row.key ?? "" })) };
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [cacheName, refreshKey],
  );
}

export function useRemoveCacheEntry(options?: { onSuccess?: (key: string) => void; onError?: (error: unknown) => void }) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (key: string): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/platform/administration/maintenance/caches/entries", {
        params: { query: { key } },
      });
      if (error) throw error;
    },
    onSuccess: (_, key) => {
      void queryClient.invalidateQueries({ queryKey: maintenanceKeys.caches() });
      options?.onSuccess?.(key);
    },
    onError: options?.onError,
  });
}

export function useClearCaches(options?: { onSuccess?: (result: CacheClearResult) => void; onError?: (error: unknown) => void }) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (): Promise<CacheClearResult> => {
      const { data, error } = await apiClient.POST("/api/platform/administration/maintenance/caches/clear");
      if (error) throw error;
      return data!;
    },
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: maintenanceKeys.caches() });
      options?.onSuccess?.(result);
    },
    onError: options?.onError,
  });
}

// ── Databases ────────────────────────────────────────────────────────────────────────

/** `refreshKey` changes make a new fetcher, which the grid treats as a reason to reload. */
export function useDatabasesGridFetcher(refreshKey = 0): (request: GridRequest) => Promise<GridResponse<DatabaseRow>> {
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<DatabaseRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/databases", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
          },
        },
      });
      if (error) throw error;
      const response = toGridResponse<DatabaseRowDto>(data);
      return { ...response, rows: response.rows.map((row) => ({ ...row, id: row.key ?? "" })) };
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [refreshKey],
  );
}

export function useDatabasesSummary() {
  return useQuery({
    queryKey: maintenanceKeys.databasesSummary(),
    queryFn: async (): Promise<DatabasesSummary> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/databases/summary");
      if (error) throw error;
      return data!;
    },
  });
}

export function useDatabaseDetail(key: string | undefined) {
  return useQuery({
    queryKey: maintenanceKeys.database(key ?? ""),
    enabled: !!key,
    queryFn: async (): Promise<DatabaseDetail> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/databases/{key}", {
        params: { path: { key: key! } },
      });
      if (error) throw error;
      return data!;
    },
  });
}

// ── Outbox ───────────────────────────────────────────────────────────────────────────

export function useOutboxOverview() {
  return useQuery({
    queryKey: maintenanceKeys.outboxOverview(),
    queryFn: async (): Promise<OutboxOverview> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/outbox/overview");
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useOutboxGridFetcher(refreshKey = 0): (request: GridRequest) => Promise<GridResponse<OutboxRow>> {
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<OutboxRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/outbox", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
          },
        },
      });
      if (error) throw error;
      const response = toGridResponse<OutboxRowDto>(data);
      return { ...response, rows: response.rows.map((row) => ({ ...row, id: row.key ?? "" })) };
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [refreshKey],
  );
}

export function useDispatchOutbox(options?: { onSuccess?: (result: OutboxDispatchResult) => void; onError?: (error: unknown) => void }) {
  return useMutation({
    mutationFn: async (tenantId: string | null | undefined): Promise<OutboxDispatchResult> => {
      const { data, error } = await apiClient.POST("/api/platform/administration/maintenance/outbox/dispatch", {
        params: { query: { tenantId: tenantId ?? undefined } },
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: options?.onSuccess,
    onError: options?.onError,
  });
}

// ── Health ───────────────────────────────────────────────────────────────────────────

export function useHealthReport() {
  return useQuery({
    queryKey: maintenanceKeys.health(),
    queryFn: async (): Promise<HealthReport> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/health");
      if (error) throw error;
      return data!;
    },
    staleTime: 0,
  });
}

// ── Instances (the registry on the Health tab) ───────────────────────────────────────

export function useInstancesOverview() {
  return useQuery({
    queryKey: maintenanceKeys.instancesOverview(),
    queryFn: async (): Promise<InstancesOverview> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/instances/overview");
      if (error) throw error;
      return data!;
    },
    staleTime: 0,
  });
}

export function useInstancesGridFetcher(refreshKey = 0): (request: GridRequest) => Promise<GridResponse<ApplicationInstanceRow>> {
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<ApplicationInstanceRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/instances", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
          },
        },
      });
      if (error) throw error;
      return toGridResponse<ApplicationInstanceRow>(data);
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [refreshKey],
  );
}

export function useInstanceDetail(id: string | undefined) {
  return useQuery({
    queryKey: maintenanceKeys.instance(id ?? ""),
    enabled: !!id,
    queryFn: async (): Promise<ApplicationInstanceDetail> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/instances/{id}", {
        params: { path: { id: id! } },
      });
      if (error) throw error;
      return data!;
    },
    staleTime: 0,
  });
}

// ── Background jobs ──────────────────────────────────────────────────────────────────

export function useBackgroundJobsOverview() {
  return useQuery({
    queryKey: maintenanceKeys.jobsOverview(),
    queryFn: async (): Promise<BackgroundJobsOverview> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/background-jobs/overview");
      if (error) throw error;
      return data!;
    },
    staleTime: 0,
  });
}

export function useBackgroundJobsGridFetcher(
  state: BackgroundJobState,
  refreshKey = 0,
): (request: GridRequest) => Promise<GridResponse<BackgroundJobRow>> {
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<BackgroundJobRow>> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/maintenance/background-jobs", {
        params: {
          query: {
            state,
            startRow: request.startRow,
            endRow: request.endRow,
          },
        },
      });
      if (error) throw error;
      return toGridResponse<BackgroundJobRow>(data);
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state, refreshKey],
  );
}

function useJobMutation(
  action: (jobId: string) => Promise<void>,
  options?: { onSuccess?: (jobId: string) => void; onError?: (error: unknown) => void },
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: action,
    onSuccess: (_, jobId) => {
      void queryClient.invalidateQueries({ queryKey: maintenanceKeys.jobsOverview() });
      options?.onSuccess?.(jobId);
    },
    onError: options?.onError,
  });
}

export function useRequeueBackgroundJob(options?: { onSuccess?: (jobId: string) => void; onError?: (error: unknown) => void }) {
  return useJobMutation(async (jobId) => {
    const { error } = await apiClient.POST("/api/platform/administration/maintenance/background-jobs/{jobId}/requeue", {
      params: { path: { jobId } },
    });
    if (error) throw error;
  }, options);
}

export function useDeleteBackgroundJob(options?: { onSuccess?: (jobId: string) => void; onError?: (error: unknown) => void }) {
  return useJobMutation(async (jobId) => {
    const { error } = await apiClient.DELETE("/api/platform/administration/maintenance/background-jobs/{jobId}", {
      params: { path: { jobId } },
    });
    if (error) throw error;
  }, options);
}
