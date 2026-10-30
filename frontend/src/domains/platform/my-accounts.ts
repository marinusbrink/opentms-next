import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* The other accounts of the signed-in user's e-mail address and the switch to one (docs/designs/63-user-menu.md). The
 * switch returns the auth-server URL that changes the browser session, as "Log in as" does. */

export type MyAccount = components["schemas"]["OpenTms.Platform.Accounts.MyAccountDto"];

const myAccountsQueryKey = ["platform", "my-accounts"] as const;

/** Thrown by the switch when that account's password is needed; the menu then asks for it. */
export const ACCOUNT_PASSWORD_REQUIRED = "OpenTms.Platform:AccountPasswordRequired";

export function useMyAccounts(enabled = true) {
  return useQuery({
    queryKey: myAccountsQueryKey,
    enabled,
    queryFn: async (): Promise<MyAccount[]> => {
      const { data, error } = await apiClient.GET("/api/platform/my-accounts");
      if (error) throw error;
      return data ?? [];
    },
    staleTime: 60 * 1000,
  });
}

export function useSwitchAccount() {
  return useMutation({
    mutationFn: async (input: { tenantId: string | null; password?: string; rememberPassword?: boolean }) => {
      const { data, error } = await apiClient.POST("/api/platform/my-accounts/switch", {
        body: { tenantId: input.tenantId, password: input.password ?? null, rememberPassword: input.rememberPassword ?? false },
      });
      if (error) throw error;
      return data!.url!;
    },
  });
}

export function useForgetAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (tenantId: string | null) => {
      const { error } = await apiClient.DELETE("/api/platform/my-accounts/remembered", {
        params: { query: { tenantId: tenantId ?? undefined } },
      });
      if (error) throw error;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: myAccountsQueryKey }),
  });
}
