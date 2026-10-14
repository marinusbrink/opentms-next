import { getStoredTenant, setStoredTenant, type StoredTenant } from "@/auth/tenant";

/* Switching the browser session to another user ("Log in as" and back; docs/designs/tenants-and-editions.md).
 * The auth server's Impersonate page changes the auth cookie and sends the browser back to the SPA, which then signs
 * in again through the normal code flow in the new tenant's context. So, before leaving: remember the new tenant
 * (it drives __tenant on the authorize request and on every API call) and drop the local tokens of the old session.
 * The tokens are removed from storage directly, not through the OIDC context: its removeUser() fires an event, the
 * shell reacts with a sign-in redirect, and that redirect cancels the navigation to the auth server.
 * Unlike a tenant switch, the auth-server session must NOT be signed out: the page checks that the ticket belongs
 * to it. If the page refuses, it comes back with ?impersonation=refused and the previous tenant is restored. */

const PREVIOUS_TENANT_KEY = "opentms.sessionSwitch.previousTenant";
const REFUSED_KEY = "opentms.sessionSwitch.refused";

/** The OIDC user store is localStorage (app/providers.tsx); its entries are keyed "oidc.user:<authority>:<client>". */
const OIDC_USER_PREFIX = "oidc.user:";

export function switchSession(redirectUrl: string, tenant: StoredTenant | undefined): void {
  window.sessionStorage.setItem(PREVIOUS_TENANT_KEY, JSON.stringify(getStoredTenant() ?? null));
  setStoredTenant(tenant);
  for (const key of Object.keys(window.localStorage)) {
    if (key.startsWith(OIDC_USER_PREFIX)) window.localStorage.removeItem(key);
  }
  window.location.assign(redirectUrl);
}

/**
 * Runs once at start-up, before the OIDC configuration reads the stored tenant. After a refused switch it puts the
 * previous tenant back and remembers to tell the user; after a successful one it forgets the previous tenant.
 */
export function settleSessionSwitch(): void {
  const params = new URLSearchParams(window.location.search);
  const previous = window.sessionStorage.getItem(PREVIOUS_TENANT_KEY);
  if (params.get("impersonation") === "refused") {
    if (previous != null) {
      setStoredTenant((JSON.parse(previous) as StoredTenant | null) ?? undefined);
    }
    window.sessionStorage.setItem(REFUSED_KEY, "1");
    params.delete("impersonation");
    const query = params.toString();
    window.history.replaceState({}, document.title, window.location.pathname + (query ? `?${query}` : ""));
  }
  if (previous != null && !window.location.pathname.startsWith("/auth/callback")) {
    window.sessionStorage.removeItem(PREVIOUS_TENANT_KEY);
  }
}

/** True once after a refused switch (the shell shows a message). */
export function takeRefusedSwitch(): boolean {
  const refused = window.sessionStorage.getItem(REFUSED_KEY) === "1";
  window.sessionStorage.removeItem(REFUSED_KEY);
  return refused;
}
