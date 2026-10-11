import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { GridRequest } from "@/components/ui/opentms-grid";

/* The domain hooks for the logbooks. Risk class: High — the period and filter mapping decide
 * which log entries an administrator sees; a wrong lower bound silently hides history. */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  DEFAULT_LOGBOOK_PERIOD,
  periodStart,
  readStoredPeriod,
  storePeriod,
  useFunctionalLogbookEntry,
  useFunctionalLogbookGridFetcher,
  useTechnicalLogbookGridFetcher,
} from "@/domains/platform/administration-logbook";

const GET = vi.mocked(apiClient.GET);

function request(overrides: Partial<GridRequest> = {}): GridRequest {
  return {
    startRow: 0,
    endRow: 50,
    sortModels: [],
    columnFilters: {},
    wildcardSearch: null,
    rowGroupCols: [],
    groupKeys: [],
    ...overrides,
  };
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  GET.mockReset();
  window.localStorage.clear();
});

describe("periodStart", () => {
  const now = new Date(Date.UTC(2026, 9, 10, 12, 0, 0));

  it("computes the lower bound of each preset from now", () => {
    expect(periodStart("lastHour", now)).toBe("2026-10-10T11:00:00.000Z");
    expect(periodStart("last24Hours", now)).toBe("2026-10-09T12:00:00.000Z");
    expect(periodStart("last7Days", now)).toBe("2026-10-03T12:00:00.000Z");
    expect(periodStart("last30Days", now)).toBe("2026-09-10T12:00:00.000Z");
    expect(new Date(periodStart("last6Months", now)!).getUTCMonth()).toBe(3);
  });

  it("has no lower bound for 'all'", () => {
    expect(periodStart("all", now)).toBeUndefined();
  });
});

describe("stored period (UI-GRID-005: the last choice is kept per grid)", () => {
  it("falls back to the default when nothing is stored or the value is unknown", () => {
    expect(readStoredPeriod("grid-a")).toBe(DEFAULT_LOGBOOK_PERIOD);
    window.localStorage.setItem("opentms.logbook.period.grid-a", "bogus");
    expect(readStoredPeriod("grid-a")).toBe(DEFAULT_LOGBOOK_PERIOD);
  });

  it("round-trips a choice per grid id", () => {
    storePeriod("grid-a", "last7Days");
    expect(readStoredPeriod("grid-a")).toBe("last7Days");
    expect(readStoredPeriod("grid-b")).toBe(DEFAULT_LOGBOOK_PERIOD);
  });
});

describe("useFunctionalLogbookGridFetcher", () => {
  it("sends the page, search, sort, period, change type and column filters", async () => {
    GET.mockResolvedValue({ data: { rows: [{ id: "c1" }], totalCount: 7, filteredCount: 1 }, error: undefined } as never);

    const { result } = renderHook(() => useFunctionalLogbookGridFetcher({ period: "all", changeType: "Deleted" }));
    const response = await result.current(
      request({
        startRow: 50,
        endRow: 100,
        wildcardSearch: "vehicle",
        sortModels: [{ colId: "userName", sort: "asc" }],
        columnFilters: {
          entityTypeName: { filterType: "text", type: "contains", filter: "Order" },
          entityId: { filterType: "text", type: "contains", filter: "o-1" },
          userName: { filterType: "text", type: "contains", filter: "bob" },
        },
      }),
    );

    expect(GET).toHaveBeenCalledWith("/api/platform/administration/logbook/functional", {
      params: {
        query: {
          startRow: 50,
          endRow: 100,
          wildcardSearch: "vehicle",
          sortBy: "userName",
          sortDir: "asc",
          from: undefined,
          changeType: "Deleted",
          entityTypeFullName: "Order",
          entityId: "o-1",
          userName: "bob",
        },
      },
    });
    expect(response).toEqual({ rows: [{ id: "c1" }], totalCount: 7, filteredCount: 1 });
  });

  it("sends the period's lower bound as an ISO instant", async () => {
    GET.mockResolvedValue({ data: { rows: [], totalCount: 0, filteredCount: 0 }, error: undefined } as never);
    const { result } = renderHook(() => useFunctionalLogbookGridFetcher({ period: "last7Days" }));
    await result.current(request());

    const query = (GET.mock.calls[0][1] as unknown as { params: { query: { from?: string } } }).params.query;
    expect(query.from).toMatch(/^\d{4}-\d{2}-\d{2}T/);
    expect(Date.now() - new Date(query.from!).getTime()).toBeGreaterThan(6.9 * 24 * 60 * 60 * 1000);
  });

  it("throws the API error so the grid shows its block error", async () => {
    GET.mockResolvedValue({ data: undefined, error: { error: { message: "nope" } } } as never);
    const { result } = renderHook(() => useFunctionalLogbookGridFetcher({ period: "all" }));
    await expect(result.current(request())).rejects.toEqual({ error: { message: "nope" } });
  });
});

describe("useTechnicalLogbookGridFetcher", () => {
  it("sends errors-only as hasException and maps the column filters", async () => {
    GET.mockResolvedValue({ data: { rows: [], totalCount: 0, filteredCount: 0 }, error: undefined } as never);

    const { result } = renderHook(() => useTechnicalLogbookGridFetcher({ period: "all", errorsOnly: true }));
    await result.current(
      request({
        columnFilters: {
          url: { filterType: "text", type: "contains", filter: "/api/orders" },
          httpMethod: { filterType: "text", type: "contains", filter: "POST" },
          correlationId: { filterType: "text", type: "contains", filter: "abc" },
        },
      }),
    );

    expect(GET).toHaveBeenCalledWith("/api/platform/administration/logbook/technical", {
      params: {
        query: expect.objectContaining({
          hasException: true,
          url: "/api/orders",
          httpMethod: "POST",
          correlationId: "abc",
          from: undefined,
        }),
      },
    });
  });

  it("omits hasException when all requests are wanted", async () => {
    GET.mockResolvedValue({ data: { rows: [], totalCount: 0, filteredCount: 0 }, error: undefined } as never);
    const { result } = renderHook(() => useTechnicalLogbookGridFetcher({ period: "all", errorsOnly: false }));
    await result.current(request());

    const query = (GET.mock.calls[0][1] as unknown as { params: { query: { hasException?: boolean } } }).params.query;
    expect(query.hasException).toBeUndefined();
  });
});

describe("useFunctionalLogbookEntry", () => {
  it("loads one entry by id and surfaces the API error", async () => {
    GET.mockResolvedValueOnce({ data: { id: "c1", propertyChanges: [] }, error: undefined } as never);
    const { result } = renderHook(() => useFunctionalLogbookEntry("c1"), { wrapper });
    await waitFor(() => expect(result.current.data).toEqual({ id: "c1", propertyChanges: [] }));
    expect(GET).toHaveBeenCalledWith("/api/platform/administration/logbook/functional/{id}", {
      params: { path: { id: "c1" } },
    });

    GET.mockResolvedValueOnce({ data: undefined, error: { error: { message: "gone" } } } as never);
    const failing = renderHook(() => useFunctionalLogbookEntry("c2"), { wrapper });
    await waitFor(() => expect(failing.result.current.isError).toBe(true));
  });

  it("does not fetch without an id", () => {
    renderHook(() => useFunctionalLogbookEntry(undefined), { wrapper });
    expect(GET).not.toHaveBeenCalled();
  });
});
