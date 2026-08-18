import { useMutation } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";

export type UserRow = {
  id?: string;
  userName?: string | null;
  email?: string | null;
  name?: string | null;
  surname?: string | null;
  isActive?: boolean;
  roleNames?: string[] | null;
  creationTime?: string;
};

export type UserCreateDto = {
  userName: string;
  email: string;
  name?: string | null;
  surname?: string | null;
  password: string;
  roleNames?: string[] | null;
};

export type UserUpdateDto = {
  userName: string;
  email: string;
  name?: string | null;
  surname?: string | null;
  isActive?: boolean;
  roleNames?: string[] | null;
};

export type BulkDeleteResult = {
  deletedCount: number;
  skippedRows: { id: string; userName: string; reason: string }[];
};

export function useUsersGridFetcher(): (request: GridRequest) => Promise<GridResponse<UserRow>> {
  return useCallback(async (request: GridRequest): Promise<GridResponse<UserRow>> => {
    const sortModel = request.sortModels[0];
    const { data, error } = await apiClient.GET("/api/platform/administration/users", {
      params: {
        query: {
          startRow: request.startRow,
          endRow: request.endRow,
          wildcardSearch: request.wildcardSearch ?? undefined,
          sortBy: sortModel?.colId,
          sortDir: sortModel?.sort,
          filterUserName: request.columnFilters["userName"]?.filter ?? undefined,
          filterEmail: request.columnFilters["email"]?.filter ?? undefined,
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

export function useCreateUser() {
  return useMutation({
    mutationFn: async (dto: UserCreateDto) => {
      const { data, error } = await apiClient.POST("/api/platform/administration/users", {
        body: dto,
      });
      if (error) throw error;
      return data;
    },
  });
}

export function useUpdateUser() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: UserUpdateDto }) => {
      const { data, error } = await apiClient.PUT(
        "/api/platform/administration/users/{id}",
        { params: { path: { id } }, body: dto },
      );
      if (error) throw error;
      return data;
    },
  });
}

export function useDeleteUser() {
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.DELETE(
        "/api/platform/administration/users/{id}",
        { params: { path: { id } } },
      );
      if (error) throw error;
    },
  });
}

export function useBulkDeleteUsers() {
  return useMutation({
    mutationFn: async (selection: GridSelectionDto): Promise<BulkDeleteResult> => {
      const { data, error } = await apiClient.POST(
        "/api/platform/administration/users/bulk-delete",
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
          userName: r.userName ?? "",
          reason: r.reason ?? "",
        })),
      };
    },
  });
}

export function useResetPassword() {
  return useMutation({
    mutationFn: async ({ id, newPassword }: { id: string; newPassword: string }) => {
      const { error } = await apiClient.POST(
        "/api/platform/administration/users/{id}/reset-password",
        { params: { path: { id } }, body: { newPassword } },
      );
      if (error) throw error;
    },
  });
}
