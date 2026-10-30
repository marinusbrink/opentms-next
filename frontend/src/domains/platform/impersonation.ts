import { useMutation } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";

/* "Log in as" (docs/designs/tenants-and-editions.md): the host picks a user of a tenant; the API returns the
 * auth-server URL that switches the browser session; the way back works the same. */

export type TenantUserRow = components["schemas"]["OpenTms.Platform.Impersonation.TenantUserRowDto"];
export type ImpersonationTenantRow = components["schemas"]["OpenTms.Platform.Impersonation.ImpersonationTenantRowDto"];

/** The tenants a host user with the Impersonation permission may log in to (the user menu, design 63). */
export async function fetchImpersonationTenants(request: GridRequest): Promise<GridResponse<ImpersonationTenantRow>> {
  const sortModel = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/platform/impersonation/tenants", {
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
}

export async function fetchTenantUsers(tenantId: string, request: GridRequest): Promise<GridResponse<TenantUserRow>> {
  const sortModel = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/platform/impersonation/tenants/{id}/users", {
    params: {
      path: { id: tenantId },
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
}

export function useStartImpersonation() {
  return useMutation({
    mutationFn: async ({ tenantId, userId }: { tenantId: string; userId: string }) => {
      const { data, error } = await apiClient.POST("/api/platform/impersonation/tenants/{id}/users/{userId}", {
        params: { path: { id: tenantId, userId } },
      });
      if (error) throw error;
      return data!.url!;
    },
  });
}

export function useReturnFromImpersonation() {
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await apiClient.POST("/api/platform/impersonation/return");
      if (error) throw error;
      return data!.url!;
    },
  });
}
