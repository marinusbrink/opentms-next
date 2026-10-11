import { useQuery } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The two logbooks ABP keeps per tenant, read through the Platform module's logbook endpoints:
 * the functional logbook (entity changes) and the technical logbook (requests). Read-only. */

type Schemas = components["schemas"];

export type FunctionalLogbookRow = Schemas["OpenTms.Platform.Administration.Logbook.FunctionalLogbookRowDto"];
export type FunctionalLogbookDetail = Schemas["OpenTms.Platform.Administration.Logbook.FunctionalLogbookDetailDto"];
export type LogbookPropertyChange = Schemas["OpenTms.Platform.Administration.Logbook.LogbookPropertyChangeDto"];
export type TechnicalLogbookRow = Schemas["OpenTms.Platform.Administration.Logbook.TechnicalLogbookRowDto"];
export type TechnicalLogbookDetail = Schemas["OpenTms.Platform.Administration.Logbook.TechnicalLogbookDetailDto"];
export type TechnicalLogbookAction = Schemas["OpenTms.Platform.Administration.Logbook.TechnicalLogbookActionDto"];

export type LogbookChangeType = "Created" | "Updated" | "Deleted";
export const LOGBOOK_CHANGE_TYPES: readonly LogbookChangeType[] = ["Created", "Updated", "Deleted"];

/** Localization key of a change type; unknown values fall back to the raw text. */
export function changeTypeKey(changeType: string | null | undefined): string {
  return changeType ? `Logbook:ChangeType.${changeType}` : "";
}

// ── Period (UI-GRID-005: a date range with a default, persisted per grid) ────────────

export type LogbookPeriod = "lastHour" | "last24Hours" | "last7Days" | "last30Days" | "last6Months" | "all";

export const LOGBOOK_PERIODS: readonly LogbookPeriod[] = [
  "lastHour",
  "last24Hours",
  "last7Days",
  "last30Days",
  "last6Months",
  "all",
];

export const DEFAULT_LOGBOOK_PERIOD: LogbookPeriod = "last6Months";

export function periodKey(period: LogbookPeriod): string {
  const suffix: Record<LogbookPeriod, string> = {
    lastHour: "LastHour",
    last24Hours: "Last24Hours",
    last7Days: "Last7Days",
    last30Days: "Last30Days",
    last6Months: "Last6Months",
    all: "All",
  };
  return `Logbook:Period.${suffix[period]}`;
}

/** The lower bound of the period as an ISO instant, or undefined for "all". */
export function periodStart(period: LogbookPeriod, now: Date = new Date()): string | undefined {
  const hour = 60 * 60 * 1000;
  const day = 24 * hour;
  switch (period) {
    case "lastHour":
      return new Date(now.getTime() - hour).toISOString();
    case "last24Hours":
      return new Date(now.getTime() - day).toISOString();
    case "last7Days":
      return new Date(now.getTime() - 7 * day).toISOString();
    case "last30Days":
      return new Date(now.getTime() - 30 * day).toISOString();
    case "last6Months": {
      const start = new Date(now);
      start.setMonth(start.getMonth() - 6);
      return start.toISOString();
    }
    case "all":
      return undefined;
  }
}

function isLogbookPeriod(value: unknown): value is LogbookPeriod {
  return typeof value === "string" && (LOGBOOK_PERIODS as readonly string[]).includes(value);
}

/** The user's last period choice for a grid; the default when nothing is stored or storage is unavailable. */
export function readStoredPeriod(gridId: string): LogbookPeriod {
  try {
    const stored = window.localStorage.getItem(`opentms.logbook.period.${gridId}`);
    return isLogbookPeriod(stored) ? stored : DEFAULT_LOGBOOK_PERIOD;
  } catch {
    return DEFAULT_LOGBOOK_PERIOD;
  }
}

export function storePeriod(gridId: string, period: LogbookPeriod): void {
  try {
    window.localStorage.setItem(`opentms.logbook.period.${gridId}`, period);
  } catch {
    /* Storage can be blocked (private window); the choice then lasts for the session only. */
  }
}

// ── Functional logbook ────────────────────────────────────────────────────────────────

export interface FunctionalLogbookFilter {
  period: LogbookPeriod;
  changeType?: LogbookChangeType;
}

export function useFunctionalLogbookGridFetcher(
  filter: FunctionalLogbookFilter,
): (request: GridRequest) => Promise<GridResponse<FunctionalLogbookRow>> {
  const { period, changeType } = filter;
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<FunctionalLogbookRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/logbook/functional", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
            from: periodStart(period),
            changeType,
            entityTypeFullName:
              request.columnFilters["entityTypeName"]?.filter ??
              request.columnFilters["entityTypeFullName"]?.filter ??
              undefined,
            entityId: request.columnFilters["entityId"]?.filter ?? undefined,
            userName: request.columnFilters["userName"]?.filter ?? undefined,
          },
        },
      });
      if (error) throw error;
      return {
        rows: data?.rows ?? [],
        totalCount: Number(data?.totalCount ?? 0),
        filteredCount: Number(data?.filteredCount ?? 0),
      };
    },
    [period, changeType],
  );
}

export const functionalLogbookEntryQueryKey = (id: string) =>
  ["administration", "logbook", "functional", id] as const;

export function useFunctionalLogbookEntry(id: string | undefined) {
  return useQuery({
    queryKey: functionalLogbookEntryQueryKey(id ?? ""),
    enabled: !!id,
    queryFn: async (): Promise<FunctionalLogbookDetail> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/logbook/functional/{id}", {
        params: { path: { id: id! } },
      });
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}

// ── Technical logbook ─────────────────────────────────────────────────────────────────

export interface TechnicalLogbookFilter {
  period: LogbookPeriod;
  errorsOnly: boolean;
}

export function useTechnicalLogbookGridFetcher(
  filter: TechnicalLogbookFilter,
): (request: GridRequest) => Promise<GridResponse<TechnicalLogbookRow>> {
  const { period, errorsOnly } = filter;
  return useCallback(
    async (request: GridRequest): Promise<GridResponse<TechnicalLogbookRow>> => {
      const sortModel = request.sortModels[0];
      const { data, error } = await apiClient.GET("/api/platform/administration/logbook/technical", {
        params: {
          query: {
            startRow: request.startRow,
            endRow: request.endRow,
            wildcardSearch: request.wildcardSearch ?? undefined,
            sortBy: sortModel?.colId,
            sortDir: sortModel?.sort,
            from: periodStart(period),
            hasException: errorsOnly ? true : undefined,
            userName: request.columnFilters["userName"]?.filter ?? undefined,
            url: request.columnFilters["url"]?.filter ?? undefined,
            httpMethod: request.columnFilters["httpMethod"]?.filter ?? undefined,
            correlationId: request.columnFilters["correlationId"]?.filter ?? undefined,
          },
        },
      });
      if (error) throw error;
      return {
        rows: data?.rows ?? [],
        totalCount: Number(data?.totalCount ?? 0),
        filteredCount: Number(data?.filteredCount ?? 0),
      };
    },
    [period, errorsOnly],
  );
}

export const technicalLogbookEntryQueryKey = (id: string) =>
  ["administration", "logbook", "technical", id] as const;

export function useTechnicalLogbookEntry(id: string | undefined) {
  return useQuery({
    queryKey: technicalLogbookEntryQueryKey(id ?? ""),
    enabled: !!id,
    queryFn: async (): Promise<TechnicalLogbookDetail> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/logbook/technical/{id}", {
        params: { path: { id: id! } },
      });
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}
