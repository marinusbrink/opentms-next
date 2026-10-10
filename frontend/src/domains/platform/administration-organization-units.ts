import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";
import type { UserRow } from "@/domains/platform/administration-users";

/* Organization units: ABP Identity's tree of units with user memberships, exposed by the
 * Platform module. The list is fetched one level at a time (the grid's tree data sends the
 * expanded ancestors as `groupKeys`); a search returns a flat list over the whole tree. */

export type OrganizationUnitRow = {
  id?: string;
  displayName?: string | null;
  code?: string | null;
  parentId?: string | null;
  parentDisplayName?: string | null;
  path?: string | null;
  hasChildren?: boolean;
  memberCount?: number;
  creationTime?: string;
  lastModificationTime?: string | null;
};

export type OrganizationUnitCreateDto = {
  displayName: string;
  parentId?: string | null;
};

export type OrganizationUnitUpdateDto = {
  displayName: string;
  /** The parent after the update; null for a root unit. A change moves the unit with its subtree. */
  parentId?: string | null;
};

export type OrganizationUnitDeleteResult =
  | { status: "deleted" }
  | { status: "conflict"; displayName: string; childCount: number; memberCount: number };

export type OrganizationUnitMembersResult = { affectedCount: number };

type ListQuery = {
  startRow: number;
  endRow: number;
  wildcardSearch?: string;
  sortBy?: string;
  sortDir?: "asc" | "desc";
};

function toListQuery(request: GridRequest): ListQuery {
  const sortModel = request.sortModels[0];
  return {
    startRow: request.startRow,
    endRow: request.endRow,
    wildcardSearch: request.wildcardSearch ?? undefined,
    sortBy: sortModel?.colId,
    sortDir: sortModel?.sort,
  };
}

function toGridResponse<T>(
  data: { rows?: T[] | null; totalCount?: number; filteredCount?: number } | undefined,
): GridResponse<T> {
  return {
    rows: data?.rows ?? [],
    totalCount: Number(data?.totalCount ?? 0),
    filteredCount: Number(data?.filteredCount ?? 0),
  };
}

/** The selection as the API takes it (the same shape the users and roles bulk actions send). */
export function toSelectionDto(selection: GridSelectionDto) {
  return {
    mode: selection.mode,
    explicitIds: selection.explicitIds,
    excludedIds: selection.excludedIds,
    filterRequest: selection.filterRequest
      ? {
          startRow: selection.filterRequest.startRow,
          endRow: selection.filterRequest.endRow,
          wildcardSearch: selection.filterRequest.wildcardSearch,
          sortModels: selection.filterRequest.sortModels.map((sm) => ({ colId: sm.colId, sort: sm.sort })),
          columnFilters: Object.fromEntries(
            Object.entries(selection.filterRequest.columnFilters).map(([k, v]) => [
              k,
              { filterType: v.filterType, type: v.type, filter: v.filter, filterTo: v.filterTo },
            ]),
          ),
          rowGroupCols: selection.filterRequest.rowGroupCols,
          groupKeys: selection.filterRequest.groupKeys,
        }
      : undefined,
  };
}

/** The parent a tree request asks for: the deepest expanded ancestor, or none for the roots. */
export function parentIdFromRequest(request: GridRequest): string | undefined {
  return request.groupKeys.length > 0 ? request.groupKeys[request.groupKeys.length - 1] : undefined;
}

export function useOrganizationUnitsGridFetcher(): (request: GridRequest) => Promise<GridResponse<OrganizationUnitRow>> {
  return useCallback(async (request: GridRequest) => {
    const { data, error } = await apiClient.GET("/api/platform/administration/organization-units", {
      params: { query: { ...toListQuery(request), parentId: parentIdFromRequest(request) } },
    });
    if (error) throw error;
    return toGridResponse<OrganizationUnitRow>(data);
  }, []);
}

/** Every unit of the tenant in tree order: the source of the parent picker. */
export function useAllOrganizationUnits() {
  return useQuery({
    queryKey: ["administration", "organization-units", "all"],
    queryFn: async (): Promise<OrganizationUnitRow[]> => {
      const { data, error } = await apiClient.GET("/api/platform/administration/organization-units/all");
      if (error) throw error;
      return data ?? [];
    },
    staleTime: 60 * 1000,
  });
}

/** A page of 200 is the API's maximum; lists an editor stages in memory are fetched whole, bounded. */
export const PAGE_SIZE = 200;
export const MAX_STAGED_ROWS = 5000;

export async function fetchAllPages<T>(
  fetchPage: (startRow: number, endRow: number) => Promise<GridResponse<T>>,
): Promise<T[]> {
  const rows: T[] = [];
  for (let start = 0; start < MAX_STAGED_ROWS; start += PAGE_SIZE) {
    const page = await fetchPage(start, start + PAGE_SIZE);
    rows.push(...page.rows);
    if (page.rows.length < PAGE_SIZE || rows.length >= page.filteredCount) break;
  }
  return rows;
}

export const membersQueryKey = (unitId: string) => ["administration", "organization-units", unitId, "members"] as const;

/** All members of a unit, for the editor's Members tab (changes are staged and applied on Save). */
export function useOrganizationUnitMembers(unitId: string) {
  return useQuery({
    queryKey: membersQueryKey(unitId),
    queryFn: () =>
      fetchAllPages<UserRow>(async (startRow, endRow) => {
        const { data, error } = await apiClient.GET("/api/platform/administration/organization-units/{id}/members", {
          params: { path: { id: unitId }, query: { startRow, endRow } },
        });
        if (error) throw error;
        return toGridResponse<UserRow>(data);
      }),
  });
}

export function useCreateOrganizationUnit() {
  return useMutation({
    mutationFn: async (dto: OrganizationUnitCreateDto) => {
      const { data, error } = await apiClient.POST("/api/platform/administration/organization-units", { body: dto });
      if (error) throw error;
      return data;
    },
  });
}

export function useUpdateOrganizationUnit() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: OrganizationUnitUpdateDto }) => {
      const { data, error } = await apiClient.PUT("/api/platform/administration/organization-units/{id}", {
        params: { path: { id } },
        body: dto,
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useDeleteOrganizationUnit() {
  return useMutation({
    mutationFn: async ({ id, force }: { id: string; force?: boolean }): Promise<OrganizationUnitDeleteResult> => {
      const result = await apiClient.DELETE("/api/platform/administration/organization-units/{id}", {
        params: { path: { id }, query: { force } },
      });
      if (result.response.status === 409) {
        const conflict = result.error as
          | { displayName?: string | null; childCount?: number; memberCount?: number }
          | undefined;
        return {
          status: "conflict",
          displayName: conflict?.displayName ?? "",
          childCount: conflict?.childCount ?? 0,
          memberCount: conflict?.memberCount ?? 0,
        };
      }
      if (result.error) throw result.error;
      return { status: "deleted" };
    },
  });
}

export function useOrganizationUnitMembersGridFetcher(unitId: string): (request: GridRequest) => Promise<GridResponse<UserRow>> {
  return useCallback(
    async (request: GridRequest) => {
      const { data, error } = await apiClient.GET("/api/platform/administration/organization-units/{id}/members", {
        params: { path: { id: unitId }, query: toListQuery(request) },
      });
      if (error) throw error;
      return toGridResponse<UserRow>(data);
    },
    [unitId],
  );
}

export async function fetchUnaddedUsers(unitId: string, request: GridRequest): Promise<GridResponse<UserRow>> {
  const { data, error } = await apiClient.GET("/api/platform/administration/organization-units/{id}/unadded-users", {
    params: { path: { id: unitId }, query: toListQuery(request) },
  });
  if (error) throw error;
  return toGridResponse<UserRow>(data);
}

export function useUnaddedUsersGridFetcher(unitId: string): (request: GridRequest) => Promise<GridResponse<UserRow>> {
  return useCallback((request: GridRequest) => fetchUnaddedUsers(unitId, request), [unitId]);
}

export function useAddOrganizationUnitMembers() {
  return useMutation({
    mutationFn: async ({ id, selection }: { id: string; selection: GridSelectionDto }): Promise<OrganizationUnitMembersResult> => {
      const { data, error } = await apiClient.POST("/api/platform/administration/organization-units/{id}/members", {
        params: { path: { id } },
        body: { selection: toSelectionDto(selection) },
      });
      if (error) throw error;
      return { affectedCount: data?.affectedCount ?? 0 };
    },
  });
}

export function useRemoveOrganizationUnitMembers() {
  return useMutation({
    mutationFn: async ({ id, selection }: { id: string; selection: GridSelectionDto }): Promise<OrganizationUnitMembersResult> => {
      const { data, error } = await apiClient.POST("/api/platform/administration/organization-units/{id}/members/remove", {
        params: { path: { id } },
        body: { selection: toSelectionDto(selection) },
      });
      if (error) throw error;
      return { affectedCount: data?.affectedCount ?? 0 };
    },
  });
}
