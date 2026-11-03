import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* Administration → Services (docs/designs/maintenance-view.md, "Services"): the host's settings of pdfgeneratorapi,
 * routing and (design 72) the order intake's AI vendor and mail providers, one configuration for every tenant, and a connection test per service. Host only; the backend enforces the
 * side. Keys and secrets are write-only: the API says whether one is stored, never what it is. */

type Schemas = components["schemas"];

export type ServiceSettings = Schemas["OpenTms.Integrations.ServiceSettings.ServiceSettingsDto"];
export type UpdateServiceSettings = Schemas["OpenTms.Integrations.ServiceSettings.UpdateServiceSettingsDto"];

/** The routing providers the page offers (ServiceSettingsConsts.RoutingProviders). */
export const ROUTING_PROVIDERS = ["Osrm", "RouteFunction"] as const;

/** The services the page tests (ServiceSettingNames): design 72 adds the Integrations tab (AI, Google, Microsoft 365). */
export type TestedService = "documents" | "routing" | "integrations";

/** The model the deployment uses when neither the tab nor the configuration names one (design 72, "Host settings"). */
export const DEFAULT_AI_MODEL = "claude-opus-5-5";

const KEY = ["integrations", "administration", "service-settings"] as const;

export function useServiceSettings() {
  return useQuery({
    queryKey: KEY,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/integrations/administration/service-settings");
      if (error) throw error;
      return data;
    },
  });
}

export function useUpdateServiceSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateServiceSettings) => {
      const { error } = await apiClient.PUT("/api/integrations/administration/service-settings", { body: input });
      if (error) throw error;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  });
}

export function useTestService() {
  return useMutation({
    mutationFn: async (service: TestedService) => {
      const { data, error } = await apiClient.POST("/api/integrations/administration/service-settings/test/{service}", {
        params: { path: { service } },
      });
      if (error) throw error;
      return data ?? [];
    },
  });
}
