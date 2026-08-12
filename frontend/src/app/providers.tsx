import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useRef, type ReactNode } from "react";
import { AuthProvider, useAuth, type AuthProviderProps } from "react-oidc-context";
import { WebStorageStateStore } from "oidc-client-ts";
import { API_BASE_URL, setAccessTokenProvider, setTenantProvider } from "@/lib/api/client";
import { getStoredTenant } from "@/auth/tenant";
import { LocalizationProvider } from "@/lib/i18n/LocalizationProvider";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

/* OIDC against the ABP host's OpenIddict server: authorization code + PKCE,
 * public client (seeded as OpenTms_App by the DbMigrator). The selected tenant
 * travels as the __tenant query parameter so login happens in tenant context. */
const storedTenant = getStoredTenant();

const oidcConfig: AuthProviderProps = {
  authority: API_BASE_URL,
  client_id: "OpenTms_App",
  redirect_uri: `${window.location.origin}/auth/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: "code",
  scope: "openid profile email phone address roles offline_access OpenTms",
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: window.localStorage }),
  extraQueryParams: storedTenant ? { __tenant: storedTenant.id } : {},
  onSigninCallback: () => {
    // Drop code/state from the URL; the callback route navigates onward.
    window.history.replaceState({}, document.title, "/auth/callback");
  },
};

/** Bridges the OIDC session and tenant selection into the typed API client. */
function ApiClientBridge({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const authRef = useRef(auth);
  authRef.current = auth;

  useEffect(() => {
    setAccessTokenProvider(() => authRef.current.user?.access_token);
    setTenantProvider(() => getStoredTenant()?.id);
  }, []);

  return children;
}

export function Providers({ children }: { children: ReactNode }) {
  return (
    <AuthProvider {...oidcConfig}>
      <QueryClientProvider client={queryClient}>
        <ApiClientBridge>
          <LocalizationProvider>{children}</LocalizationProvider>
        </ApiClientBridge>
      </QueryClientProvider>
    </AuthProvider>
  );
}
