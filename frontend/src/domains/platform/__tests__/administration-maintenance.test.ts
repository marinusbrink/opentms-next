import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { GridRequest } from "@/components/ui/opentms-grid";

/* The domain hooks of the Maintenance view. Risk class: High — the query mapping decides which
 * page and state the grids show; the mutations are the actions (clear, dispatch, requeue, delete). */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), DELETE: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  canDeleteJob,
  canRequeue,
  healthStatusKey,
  instanceStatusKey,
  jobStateKey,
  timeSpanToMinutes,
  timeSpanToSeconds,
  useBackgroundJobsGridFetcher,
  useCacheEntriesGridFetcher,
  useClearCaches,
  useDatabaseDetail,
  useDatabasesGridFetcher,
  useDeleteBackgroundJob,
  useDispatchOutbox,
  useInstanceDetail,
  useInstancesGridFetcher,
  useOutboxGridFetcher,
  useRemoveCacheEntry,
  useRequeueBackgroundJob,
} from "@/domains/platform/administration-maintenance";

const GET = vi.mocked(apiClient.GET);
const POST = vi.mocked(apiClient.POST);
const DELETE = vi.mocked(apiClient.DELETE);

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
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  GET.mockReset();
  POST.mockReset();
  DELETE.mockReset();
});

describe("helpers", () => {
  it("maps TimeSpan strings to minutes", () => {
    expect(timeSpanToMinutes("00:05:00")).toBe(5);
    expect(timeSpanToMinutes("01:30:00")).toBe(90);
    expect(timeSpanToMinutes("1.02:00:00")).toBe(1560);
    expect(timeSpanToMinutes("00:00:30")).toBe(1);
    expect(timeSpanToMinutes(null)).toBeNull();
    expect(timeSpanToMinutes("soon")).toBeNull();
    expect(timeSpanToSeconds("00:00:30")).toBe(30);
    expect(timeSpanToSeconds("00:01:30")).toBe(90);
    expect(timeSpanToSeconds("1.00:00:05")).toBe(86405);
    expect(timeSpanToSeconds(undefined)).toBeNull();
  });

  it("builds localization keys for states and statuses", () => {
    expect(jobStateKey("Failed")).toBe("Maintenance:State.Failed");
    expect(jobStateKey(null)).toBe("");
    expect(healthStatusKey("Healthy")).toBe("Maintenance:StatusHealthy");
    expect(healthStatusKey("Weird")).toBe("Weird");
    expect(instanceStatusKey("Unresponsive")).toBe("Maintenance:Instance.Status.Unresponsive");
  });

  it("knows which states can be requeued or deleted", () => {
    expect(canRequeue("Failed")).toBe(true);
    expect(canRequeue("Succeeded")).toBe(false);
    expect(canDeleteJob("Enqueued")).toBe(true);
    expect(canDeleteJob("Recurring")).toBe(false);
  });
});

describe("useDatabasesGridFetcher", () => {
  it("maps the grid request to the query string and gives every row its key as id", async () => {
    GET.mockResolvedValue({
      data: { rows: [{ key: "host", databaseName: "OpenTms" }], totalCount: 1, filteredCount: 1 },
      error: undefined,
    } as never);

    const { result } = renderHook(() => useDatabasesGridFetcher(0));
    const response = await result.current(request({ startRow: 10, endRow: 60, wildcardSearch: "demo", sortModels: [{ colId: "sizeBytes", sort: "desc" }] }));

    expect(GET).toHaveBeenCalledWith("/api/platform/administration/maintenance/databases", {
      params: { query: { startRow: 10, endRow: 60, wildcardSearch: "demo", sortBy: "sizeBytes", sortDir: "desc" } },
    });
    expect(response.rows[0]).toMatchObject({ id: "host", key: "host" });
    expect(response.totalCount).toBe(1);
  });

  it("throws the API error", async () => {
    GET.mockResolvedValue({ data: undefined, error: { error: { message: "down" } } } as never);
    const { result } = renderHook(() => useDatabasesGridFetcher(0));
    await expect(result.current(request())).rejects.toEqual({ error: { message: "down" } });
  });
});

describe("useOutboxGridFetcher", () => {
  it("calls the outbox endpoint and gives every row its key as id", async () => {
    GET.mockResolvedValue({ data: { rows: [{ key: "t-1" }], totalCount: 2, filteredCount: 1 }, error: undefined } as never);
    const { result } = renderHook(() => useOutboxGridFetcher(0));
    const response = await result.current(request({ sortModels: [{ colId: "tenantName", sort: "asc" }] }));
    expect(GET.mock.calls[0][0]).toBe("/api/platform/administration/maintenance/outbox");
    expect(response.rows[0].id).toBe("t-1");
    expect(response.filteredCount).toBe(1);
  });
});

describe("useBackgroundJobsGridFetcher", () => {
  it("passes the state and the paging", async () => {
    GET.mockResolvedValue({ data: { rows: [], totalCount: 0, filteredCount: 0 }, error: undefined } as never);
    const { result } = renderHook(() => useBackgroundJobsGridFetcher("Succeeded", 0));
    await result.current(request({ startRow: 50, endRow: 100 }));
    expect(GET).toHaveBeenCalledWith("/api/platform/administration/maintenance/background-jobs", {
      params: { query: { state: "Succeeded", startRow: 50, endRow: 100 } },
    });
  });
});

describe("useCacheEntriesGridFetcher", () => {
  it("passes paging, search, sort and the named cache, and gives every row its key as id", async () => {
    GET.mockResolvedValue({ data: { rows: [{ key: "c:X,k:1", cacheName: "X" }], totalCount: 3, filteredCount: 1 }, error: undefined } as never);
    const { result } = renderHook(() => useCacheEntriesGridFetcher("X", 0));
    const response = await result.current(request({ startRow: 0, endRow: 25, wildcardSearch: "1", sortModels: [{ colId: "sizeBytes", sort: "desc" }] }));
    expect(GET).toHaveBeenCalledWith("/api/platform/administration/maintenance/caches/entries", {
      params: { query: { startRow: 0, endRow: 25, wildcardSearch: "1", sortBy: "sizeBytes", sortDir: "desc", cacheName: "X" } },
    });
    expect(response.rows[0].id).toBe("c:X,k:1");
    expect(response.totalCount).toBe(3);
  });
});

describe("instances", () => {
  it("lists instances with paging, search and sort, and reads one by id", async () => {
    GET.mockResolvedValue({ data: { rows: [{ id: "i-1", role: "Api" }], totalCount: 1, filteredCount: 1 }, error: undefined } as never);
    const { result } = renderHook(() => useInstancesGridFetcher(0));
    const response = await result.current(request({ wildcardSearch: "api", sortModels: [{ colId: "lastHeartbeatAt", sort: "desc" }] }));
    expect(GET).toHaveBeenCalledWith("/api/platform/administration/maintenance/instances", {
      params: { query: { startRow: 0, endRow: 50, wildcardSearch: "api", sortBy: "lastHeartbeatAt", sortDir: "desc" } },
    });
    expect(response.rows[0].id).toBe("i-1");

    GET.mockResolvedValue({ data: { id: "i-1", role: "Api", healthEntries: [] }, error: undefined } as never);
    const detail = renderHook(() => useInstanceDetail("i-1"), { wrapper });
    await waitFor(() => expect(detail.result.current.data?.role).toBe("Api"));
    expect(GET).toHaveBeenLastCalledWith("/api/platform/administration/maintenance/instances/{id}", { params: { path: { id: "i-1" } } });
  });
});

describe("useDatabaseDetail", () => {
  it("reads one database by key and stays idle without a key", async () => {
    GET.mockResolvedValue({ data: { key: "host", databaseName: "OpenTms" }, error: undefined } as never);
    const { result } = renderHook(() => useDatabaseDetail("host"), { wrapper });
    await waitFor(() => expect(result.current.data?.databaseName).toBe("OpenTms"));
    expect(GET).toHaveBeenCalledWith("/api/platform/administration/maintenance/databases/{key}", { params: { path: { key: "host" } } });

    const idle = renderHook(() => useDatabaseDetail(undefined), { wrapper });
    expect(idle.result.current.fetchStatus).toBe("idle");
  });
});

describe("mutations", () => {
  it("clears the caches and reports the result", async () => {
    POST.mockResolvedValue({ data: { clearedEntryCount: 4, instanceName: "box" }, error: undefined } as never);
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useClearCaches({ onSuccess }), { wrapper });
    await result.current.mutateAsync();
    expect(POST).toHaveBeenCalledWith("/api/platform/administration/maintenance/caches/clear");
    expect(onSuccess).toHaveBeenCalledWith({ clearedEntryCount: 4, instanceName: "box" });
  });

  it("removes one cache entry by its full key in the query string", async () => {
    DELETE.mockResolvedValue({ data: undefined, error: undefined } as never);
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useRemoveCacheEntry({ onSuccess }), { wrapper });
    await result.current.mutateAsync("c:X,k:pn:R,pk:admin");
    expect(DELETE).toHaveBeenCalledWith("/api/platform/administration/maintenance/caches/entries", { params: { query: { key: "c:X,k:pn:R,pk:admin" } } });
    await waitFor(() => expect(onSuccess).toHaveBeenCalledWith("c:X,k:pn:R,pk:admin"));
  });

  it("dispatches one outbox by tenant, or the host outbox without one", async () => {
    POST.mockResolvedValue({ data: { tenantId: "t-1", publishedCount: 2 }, error: undefined } as never);
    const { result } = renderHook(() => useDispatchOutbox(), { wrapper });
    await result.current.mutateAsync("t-1");
    expect(POST).toHaveBeenCalledWith("/api/platform/administration/maintenance/outbox/dispatch", { params: { query: { tenantId: "t-1" } } });

    await result.current.mutateAsync(null);
    expect(POST).toHaveBeenLastCalledWith("/api/platform/administration/maintenance/outbox/dispatch", { params: { query: { tenantId: undefined } } });
  });

  it("requeues and deletes a job by id and surfaces failures", async () => {
    POST.mockResolvedValue({ data: undefined, error: undefined } as never);
    DELETE.mockResolvedValue({ data: undefined, error: { error: { message: "gone" } } } as never);
    const onError = vi.fn();

    const requeue = renderHook(() => useRequeueBackgroundJob(), { wrapper });
    await requeue.result.current.mutateAsync("job-1");
    expect(POST).toHaveBeenCalledWith("/api/platform/administration/maintenance/background-jobs/{jobId}/requeue", { params: { path: { jobId: "job-1" } } });

    const remove = renderHook(() => useDeleteBackgroundJob({ onError }), { wrapper });
    await expect(remove.result.current.mutateAsync("job-2")).rejects.toEqual({ error: { message: "gone" } });
    expect(DELETE).toHaveBeenCalledWith("/api/platform/administration/maintenance/background-jobs/{jobId}", { params: { path: { jobId: "job-2" } } });
    await waitFor(() => expect(onError).toHaveBeenCalled());
  });
});
