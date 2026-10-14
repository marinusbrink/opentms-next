import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";

/* The Tenants app (host only): docs/designs/tenants-and-editions.md. */

export type TenantRow = components["schemas"]["OpenTms.Platform.Tenants.TenantRowDto"];
export type TenantDetail = components["schemas"]["OpenTms.Platform.Tenants.TenantDto"];
export type TenantCreateDto = components["schemas"]["OpenTms.Platform.Tenants.TenantCreateDto"];
export type TenantUpdateDto = components["schemas"]["OpenTms.Platform.Tenants.TenantUpdateDto"];
export type EditionLookup = components["schemas"]["OpenTms.Platform.Editions.EditionLookupDto"];

/** One of the provisioning states the backend writes (TenantProvisioningState). */
export const TenantProvisioningStates = {
  Queued: "Queued",
  Running: "Running",
  Succeeded: "Succeeded",
  Failed: "Failed",
} as const;

export function useTenantsGridFetcher(): (request: GridRequest) => Promise<GridResponse<TenantRow>> {
  return useCallback(async (request: GridRequest): Promise<GridResponse<TenantRow>> => {
    const sortModel = request.sortModels[0];
    const { data, error } = await apiClient.GET("/api/platform/tenants", {
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

export function useTenant(id: string | undefined) {
  return useQuery({
    queryKey: ["platform", "tenants", id],
    enabled: id != null,
    queryFn: async (): Promise<TenantDetail> => {
      const { data, error } = await apiClient.GET("/api/platform/tenants/{id}", {
        params: { path: { id: id! } },
      });
      if (error) throw error;
      return data!;
    },
  });
}

export function useEditionLookup() {
  return useQuery({
    queryKey: ["platform", "tenants", "edition-lookup"],
    queryFn: async (): Promise<EditionLookup[]> => {
      const { data, error } = await apiClient.GET("/api/platform/tenants/edition-lookup");
      if (error) throw error;
      return data ?? [];
    },
    staleTime: 60 * 1000,
  });
}

/** The connection string a new tenant of this name would get (password masked, id part as x's). */
export function useTenantConnectionPreview(name: string, enabled: boolean) {
  return useQuery({
    queryKey: ["platform", "tenants", "connection-preview", name],
    enabled,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/platform/tenants/connection-preview", {
        params: { query: { name } },
      });
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useCreateTenant() {
  return useMutation({
    mutationFn: async (dto: TenantCreateDto) => {
      const { data, error } = await apiClient.POST("/api/platform/tenants", { body: dto });
      if (error) throw error;
      return data!;
    },
  });
}

export function useUpdateTenant() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: TenantUpdateDto }) => {
      const { data, error } = await apiClient.PUT("/api/platform/tenants/{id}", {
        params: { path: { id } },
        body: dto,
      });
      if (error) throw error;
      return data!;
    },
  });
}

export function useDeleteTenant() {
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.DELETE("/api/platform/tenants/{id}", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

export function useRetryTenantProvisioning() {
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.POST("/api/platform/tenants/{id}/retry-provisioning", {
        params: { path: { id } },
      });
      if (error) throw error;
    },
  });
}

export function useResendTenantInvitation() {
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.POST("/api/platform/tenants/{id}/resend-invitation", {
        params: { path: { id } },
      });
      if (error) throw error;
    },
  });
}
