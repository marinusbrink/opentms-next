import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { getStoredTenant, setStoredTenant } from "@/auth/tenant";

/* Start-up check of the stored tenant: a tenant the server no longer knows is forgotten together with the local tokens
 * issued in it, so the browser is not locked out by "Tenant not found"; an existing tenant, or a check that failed,
 * changes nothing. Risk class: Critical — authentication and tenant resolution (forgetting a live tenant would sign the
 * user in as the wrong party; keeping a dead one locks them out). */

const fetchMock = vi.fn();
let discardUnknownStoredTenant: typeof import("@/auth/stale-tenant").discardUnknownStoredTenant;

const ACME = { id: "11111111-1111-1111-1111-111111111111", name: "acme" };
const OIDC_USER = "oidc.user:http://localhost:44301:OpenTms_App";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const notFound = { success: false, tenantId: null, name: null, normalizedName: null, isActive: false };
const found = { success: true, tenantId: ACME.id, name: ACME.name, normalizedName: "ACME", isActive: true };

const lastRequest = (): Request => fetchMock.mock.calls.at(-1)![0] as Request;

beforeAll(async () => {
  vi.stubGlobal("fetch", fetchMock);
  ({ discardUnknownStoredTenant } = await import("@/auth/stale-tenant"));
});

beforeEach(() => {
  fetchMock.mockReset();
  window.localStorage.clear();
});

describe("discardUnknownStoredTenant", () => {
  it("asks nothing when no tenant is stored (host context)", async () => {
    await discardUnknownStoredTenant();

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("looks the stored tenant up by id, without a __tenant header", async () => {
    setStoredTenant(ACME);
    fetchMock.mockResolvedValue(json(found));

    await discardUnknownStoredTenant();

    expect(new URL(lastRequest().url).pathname).toBe(`/api/abp/multi-tenancy/tenants/by-id/${ACME.id}`);
    expect(lastRequest().headers.get("__tenant")).toBeNull();
  });

  it("forgets a tenant the server does not know, with only the OIDC user entries", async () => {
    setStoredTenant(ACME);
    window.localStorage.setItem(OIDC_USER, "{\"access_token\":\"old\"}");
    window.localStorage.setItem("oidc.state", "keep");
    window.localStorage.setItem("opentms.grid.settings", "keep");
    fetchMock.mockResolvedValue(json(notFound));

    await discardUnknownStoredTenant();

    expect(getStoredTenant()).toBeUndefined();
    expect(window.localStorage.getItem(OIDC_USER)).toBeNull();
    expect(window.localStorage.getItem("oidc.state")).toBe("keep");
    expect(window.localStorage.getItem("opentms.grid.settings")).toBe("keep");
  });

  it("keeps a tenant that exists, and its tokens", async () => {
    setStoredTenant(ACME);
    window.localStorage.setItem(OIDC_USER, "{\"access_token\":\"valid\"}");
    fetchMock.mockResolvedValue(json(found));

    await discardUnknownStoredTenant();

    expect(getStoredTenant()).toEqual(ACME);
    expect(window.localStorage.getItem(OIDC_USER)).not.toBeNull();
  });

  it("keeps the tenant when the server answers with an error", async () => {
    setStoredTenant(ACME);
    fetchMock.mockResolvedValue(json({ error: { message: "boom" } }, 500));

    await discardUnknownStoredTenant();

    expect(getStoredTenant()).toEqual(ACME);
  });

  it("keeps the tenant when the server cannot be reached", async () => {
    setStoredTenant(ACME);
    window.localStorage.setItem(OIDC_USER, "{\"access_token\":\"valid\"}");
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));

    await expect(discardUnknownStoredTenant()).resolves.toBeUndefined();

    expect(getStoredTenant()).toEqual(ACME);
    expect(window.localStorage.getItem(OIDC_USER)).not.toBeNull();
  });
});
