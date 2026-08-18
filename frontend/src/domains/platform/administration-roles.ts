import { useMutation } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";

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
