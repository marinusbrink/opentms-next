import { apiClient } from "@/lib/api/client";
import { dropLocalOidcUsers } from "@/auth/session-switch";
import { getStoredTenant, setStoredTenant } from "@/auth/tenant";

/* A stored tenant that no longer exists (deleted, or the local database was rebuilt) would otherwise lock the browser
 * out: it travels as __tenant on the authorize request and on every API call, and the server answers each of those
 * with "Tenant not found". So, once at start-up and before anything reads the stored tenant, the SPA asks the server
 * whether it still exists. Only a definite "not found" forgets it, together with the local tokens issued in it; sign-in
 * then takes the normal route (host, or the tenant found from the address, design 62). A failed check keeps both. */

/**
 * Must run before the API client gets its tenant provider (app/providers.tsx): the lookup itself is sent without a
 * __tenant header, which is what lets the server answer for an unknown tenant at all.
 */
export async function discardUnknownStoredTenant(): Promise<void> {
  const tenant = getStoredTenant();
  if (!tenant) return;
  try {
    const { data } = await apiClient.GET("/api/abp/multi-tenancy/tenants/by-id/{id}", {
      params: { path: { id: tenant.id } },
    });
    if (data?.success === false) {
      setStoredTenant(undefined);
      dropLocalOidcUsers();
    }
  } catch {
    // Server unreachable: keep the stored tenant; the normal error handling takes over.
  }
}
