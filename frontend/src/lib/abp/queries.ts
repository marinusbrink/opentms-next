import { useQuery } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { apiClient } from "@/lib/api/client";
import { getStoredTenant } from "@/auth/tenant";

/* The standard query layer over ABP's framework endpoints. All data access goes
 * through the generated typed client + TanStack Query — components never fetch
 * on their own (coding conventions — data access). */

export function useApplicationConfiguration() {
  const auth = useAuth();

  return useQuery({
    queryKey: [
      "abp",
      "application-configuration",
      getStoredTenant()?.id ?? "host",
      auth.user?.profile.sub ?? "anonymous",
    ],
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/abp/application-configuration", {
        params: { query: { IncludeLocalizationResources: false } },
      });
      if (error) throw error;
      return data;
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useLocalizationData(cultureName: string) {
  return useQuery({
    queryKey: ["abp", "application-localization", cultureName],
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/abp/application-localization", {
        params: { query: { CultureName: cultureName, OnlyDynamics: false } },
      });
      if (error) throw error;
      return data;
    },
    staleTime: Infinity,
  });
}

export async function findTenantByName(name: string) {
  const { data, error } = await apiClient.GET("/api/abp/multi-tenancy/tenants/by-name/{name}", {
    params: { path: { name } },
  });
  if (error) throw error;
  return data;
}
