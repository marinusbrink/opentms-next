import { useMutation } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";

export type RoleRow = {
  id?: string;
  name?: string | null;
  isDefault?: boolean;
  isPublic?: boolean;
  isStatic?: boolean;
  userCount?: number;
};

export type RoleCreateUpdateDto = {
  name: string;
  isDefault?: boolean;
  isPublic?: boolean;
};

export type RoleDeleteResult =
  | { status: "deleted" }
  | { status: "conflict"; roleName: string; userCount: number };

export function useRolesGridFetcher(): (request: GridRequest) => Promise<GridResponse<RoleRow>> {
  return useCallback(async (request: GridRequest): Promise<GridResponse<RoleRow>> => {
    const sortModel = request.sortModels[0];
    const { data, error } = await apiClient.GET("/api/platform/administration/roles", {
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
    return {
      rows: data?.rows ?? [],
      totalCount: Number(data?.totalCount ?? 0),
      filteredCount: Number(data?.filteredCount ?? 0),
    };
  }, []);
}

export function useCreateRole() {
  return useMutation({
    mutationFn: async (dto: RoleCreateUpdateDto) => {
      const { data, error } = await apiClient.POST("/api/platform/administration/roles", {
        body: dto,
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useUpdateRole() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: RoleCreateUpdateDto }) => {
      const { data, error } = await apiClient.PUT(
        "/api/platform/administration/roles/{id}",
        { params: { path: { id } }, body: dto },
      );
      if (error) throw error;
      return data;
    },
  });
}

export function useDeleteRole() {
  return useMutation({
    mutationFn: async ({ id, force }: { id: string; force?: boolean }): Promise<RoleDeleteResult> => {
      const result = await apiClient.DELETE("/api/platform/administration/roles/{id}", {
        params: { path: { id }, query: { force } },
      });
      if (result.response.status === 409) {
        const conflict = result.error as { roleName?: string | null; userCount?: number } | undefined;
        return {
          status: "conflict",
          roleName: conflict?.roleName ?? "",
          userCount: conflict?.userCount ?? 0,
        };
      }
      if (result.error) throw result.error;
      return { status: "deleted" };
    },
  });
}

export type BulkDeleteRolesResult = {
  deletedCount: number;
  skippedRows: { id: string; name: string; reason: string }[];
};

export function useBulkDeleteRoles() {
  return useMutation({
    mutationFn: async (selection: GridSelectionDto): Promise<BulkDeleteRolesResult> => {
      const { data, error } = await apiClient.POST(
        "/api/platform/administration/roles/bulk-delete",
        {
          body: {
            selection: {
              mode: selection.mode,
              explicitIds: selection.explicitIds,
              excludedIds: selection.excludedIds,
              filterRequest: selection.filterRequest
                ? {
                    startRow: selection.filterRequest.startRow,
                    endRow: selection.filterRequest.endRow,
                    wildcardSearch: selection.filterRequest.wildcardSearch,
                    sortModels: selection.filterRequest.sortModels.map((sm) => ({
                      colId: sm.colId,
                      sort: sm.sort,
                    })),
                    columnFilters: Object.fromEntries(
                      Object.entries(selection.filterRequest.columnFilters).map(
                        ([k, v]) => [k, { filterType: v.filterType, type: v.type, filter: v.filter, filterTo: v.filterTo }],
                      ),
                    ),
                    rowGroupCols: selection.filterRequest.rowGroupCols,
                    groupKeys: selection.filterRequest.groupKeys,
                  }
                : undefined,
            },
          },
        },
      );
      if (error) throw error;
      return {
        deletedCount: data?.deletedCount ?? 0,
        skippedRows: (data?.skippedRows ?? []).map((r) => ({
          id: r.id ?? "",
          name: r.name ?? "",
          reason: r.reason ?? "",
        })),
      };
    },
  });
}
