import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./generated/schema";

/* The ONLY way the frontend talks to the backend (coding conventions — data access):
 * a typed client generated from the committed OpenAPI contract. No hand-written fetch
 * calls, no hand-typed response shapes. Regenerate with `npm run generate:client`
 * after the backend contract changed (see /scripts/generate-openapi.sh).
 */

export const API_BASE_URL: string =
  (import.meta.env.VITE_API_URL as string | undefined) ?? "https://localhost:44301";

/** Set by the auth layer once the user is signed in. */
let accessTokenProvider: (() => string | undefined) | undefined;
/** Set by the tenant resolution layer; ABP resolves the tenant from the __tenant header. */
let tenantProvider: (() => string | undefined) | undefined;

export function setAccessTokenProvider(provider: () => string | undefined): void {
  accessTokenProvider = provider;
}

export function setTenantProvider(provider: () => string | undefined): void {
  tenantProvider = provider;
}

const authAndTenantMiddleware: Middleware = {
  onRequest({ request }) {
    const token = accessTokenProvider?.();
    if (token) {
      request.headers.set("Authorization", `Bearer ${token}`);
    }

    const tenant = tenantProvider?.();
    if (tenant) {
      request.headers.set("__tenant", tenant);
    }

    return request;
  },
};

export const apiClient = createClient<paths>({ baseUrl: API_BASE_URL });
apiClient.use(authAndTenantMiddleware);
