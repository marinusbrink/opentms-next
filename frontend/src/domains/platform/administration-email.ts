import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The host's e-mail settings in the Administration app (docs/designs/64-email-delivery-postmark.md, "Host e-mail
 * settings"): one configuration of the mail service for every tenant, and a test mail. Host only; the backend enforces
 * the side. The token and the webhook password are write-only: the API says whether one is stored, never what it is. */

type Schemas = components["schemas"];

export type EmailSettings = Schemas["OpenTms.Platform.Emailing.EmailSettingsDto"];
export type UpdateEmailSettings = Schemas["OpenTms.Platform.Emailing.UpdateEmailSettingsDto"];
export type TestEmailResult = Schemas["OpenTms.Platform.Emailing.TestEmailResultDto"];

/** Where the server token comes from (EmailSettingSource). */
export const EMAIL_SETTING_SOURCE = { None: 0, Settings: 1, Configuration: 2 } as const;

/** Postmark's link tracking modes (EmailSettingsConsts.TrackLinksValues). */
export const TRACK_LINKS_VALUES = ["None", "HtmlAndText", "HtmlOnly", "TextOnly"] as const;

const KEY = ["platform", "administration", "email-settings"] as const;

export function useEmailSettings() {
  return useQuery({
    queryKey: KEY,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/platform/administration/email-settings");
      if (error) throw error;
      return data;
    },
  });
}

export function useUpdateEmailSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateEmailSettings) => {
      const { error } = await apiClient.PUT("/api/platform/administration/email-settings", { body: input });
      if (error) throw error;
    },
    /* The save returns nothing: the settings are read again (the effective values may change too). */
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  });
}

export function useSendTestEmail() {
  return useMutation({
    mutationFn: async (to: string) => {
      const { data, error } = await apiClient.POST("/api/platform/administration/email-settings/test", { body: { to } });
      if (error) throw error;
      return data;
    },
  });
}
