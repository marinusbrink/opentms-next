/* Tenant selection for the SPA.
 *
 * The selected tenant drives ABP's tenant resolution twice:
 *  - as the `__tenant` query parameter on the OpenIddict authorize request
 *    (login happens in the tenant's context), and
 *  - as the `__tenant` header on every API call (see lib/api/client.ts).
 *
 * We store the tenant ID (GUID) next to the display name and always send the ID:
 * parts of the auth server parse `__tenant` strictly as a GUID. The name→id
 * lookup happens once, at switch time, via /api/abp/multi-tenancy/tenants/by-name.
 *
 * No stored tenant = host context (host administration). Changing the tenant ends
 * the auth-server session and starts a fresh login in the new context.
 */

export interface StoredTenant {
  id: string;
  name: string;
}

const STORAGE_KEY = "opentms.tenant";

export function getStoredTenant(): StoredTenant | undefined {
  const raw = window.localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return undefined;
  }
  try {
    const parsed = JSON.parse(raw) as StoredTenant;
    return parsed.id && parsed.name ? parsed : undefined;
  } catch {
    return undefined;
  }
}

export function setStoredTenant(tenant: StoredTenant | undefined): void {
  if (tenant) {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(tenant));
  } else {
    window.localStorage.removeItem(STORAGE_KEY);
  }
}
