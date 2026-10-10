import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { GridRequest } from "@/components/ui/opentms-grid";

/* The domain hooks for organization units. Risk class: High — the tree request mapping
 * (expanded ancestors → parentId) and the 409 delete conflict decide what the user sees. */

vi.mock("@/lib/api/client", () => ({
  apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() },
}));

import { apiClient } from "@/lib/api/client";
import {
  parentIdFromRequest,
  toSelectionDto,
  useDeleteOrganizationUnit,
  useOrganizationUnitsGridFetcher,
} from "@/domains/platform/administration-organization-units";

const GET = vi.mocked(apiClient.GET);
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
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  GET.mockReset();
  DELETE.mockReset();
});

describe("parentIdFromRequest", () => {
  it("asks for the roots when nothing is expanded", () => {
    expect(parentIdFromRequest(request())).toBeUndefined();
  });

  it("asks for the children of the deepest expanded ancestor", () => {
    expect(parentIdFromRequest(request({ groupKeys: ["root-id", "child-id"] }))).toBe("child-id");
  });
});

describe("useOrganizationUnitsGridFetcher", () => {
  it("sends the page, the search, the sort and the parent to the API", async () => {
    GET.mockResolvedValue({ data: { rows: [{ id: "a" }], totalCount: 3, filteredCount: 1 }, error: undefined } as never);
    const { result } = renderHook(() => useOrganizationUnitsGridFetcher());

    const response = await result.current(
      request({
        startRow: 10,
        endRow: 60,
        wildcardSearch: "rott",
        sortModels: [{ colId: "displayName", sort: "desc" }],
        groupKeys: ["p1"],
      }),
    );

    expect(GET).toHaveBeenCalledWith("/api/platform/administration/organization-units", {
      params: {
        query: { startRow: 10, endRow: 60, wildcardSearch: "rott", sortBy: "displayName", sortDir: "desc", parentId: "p1" },
      },
    });
    expect(response).toEqual({ rows: [{ id: "a" }], totalCount: 3, filteredCount: 1 });
  });

  it("throws the API error so the grid shows its block error (UI-FB-005)", async () => {
    GET.mockResolvedValue({ data: undefined, error: { error: { message: "boom" } } } as never);
    const { result } = renderHook(() => useOrganizationUnitsGridFetcher());
    await expect(result.current(request())).rejects.toEqual({ error: { message: "boom" } });
  });
});

describe("useDeleteOrganizationUnit", () => {
  it("maps a 409 to a conflict with the counts the warning needs", async () => {
    DELETE.mockResolvedValue({
      response: { status: 409 },
      error: { displayName: "Planning", childCount: 2, memberCount: 5 },
    } as never);
    const { result } = renderHook(() => useDeleteOrganizationUnit(), { wrapper });

    const outcome = await result.current.mutateAsync({ id: "u1", force: false });

    expect(outcome).toEqual({ status: "conflict", displayName: "Planning", childCount: 2, memberCount: 5 });
    expect(DELETE).toHaveBeenCalledWith("/api/platform/administration/organization-units/{id}", {
      params: { path: { id: "u1" }, query: { force: false } },
    });
  });

  it("reports a plain delete as deleted and a failure as an error", async () => {
    DELETE.mockResolvedValueOnce({ response: { status: 204 }, error: undefined } as never);
    const { result } = renderHook(() => useDeleteOrganizationUnit(), { wrapper });
    await expect(result.current.mutateAsync({ id: "u1", force: true })).resolves.toEqual({ status: "deleted" });

    DELETE.mockResolvedValueOnce({ response: { status: 500 }, error: { error: { message: "down" } } } as never);
    await expect(result.current.mutateAsync({ id: "u1" })).rejects.toEqual({ error: { message: "down" } });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("toSelectionDto", () => {
  it("passes an explicit selection through and a filter-based one with its request", () => {
    expect(toSelectionDto({ mode: "Explicit", explicitIds: ["a", "b"], filterRequest: null, excludedIds: [] })).toEqual({
      mode: "Explicit",
      explicitIds: ["a", "b"],
      excludedIds: [],
      filterRequest: undefined,
    });

    const dto = toSelectionDto({
      mode: "FilterBased",
      explicitIds: [],
      excludedIds: ["x"],
      filterRequest: request({
        wildcardSearch: "jan",
        columnFilters: { email: { filterType: "text", type: "contains", filter: "@t.nl" } },
      }),
    });
    expect(dto.mode).toBe("FilterBased");
    expect(dto.excludedIds).toEqual(["x"]);
    expect(dto.filterRequest?.wildcardSearch).toBe("jan");
    expect(dto.filterRequest?.columnFilters).toEqual({
      email: { filterType: "text", type: "contains", filter: "@t.nl", filterTo: undefined },
    });
  });
});
