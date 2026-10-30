import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { settleSessionSwitch, switchSession, takeRefusedSwitch, takeSwitchReturnTo } from "@/auth/session-switch";
import { getStoredTenant, setStoredTenant } from "@/auth/tenant";

/* The SPA side of "Log in as" (docs/designs/tenants-and-editions.md, D14 step 3): before leaving for the auth server it
 * stores the target tenant, remembers the previous one, drops only the OIDC user entries (directly, not through the
 * OIDC context) and navigates; on the way back a refused switch restores the previous tenant and strips the marker.
 * Risk class: Critical — authentication (a wrong tenant or a leftover token would sign the browser in as the wrong
 * party). */

const PREVIOUS = "opentms.sessionSwitch.previousTenant";
const ACME = { id: "11111111-1111-1111-1111-111111111111", name: "acme" };
const HOME = { id: "22222222-2222-2222-2222-222222222222", name: "home" };

beforeEach(() => {
  window.localStorage.clear();
  window.sessionStorage.clear();
  window.history.replaceState({}, "", "/");
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("switchSession", () => {
  function stubLocation() {
    const assign = vi.fn();
    vi.stubGlobal("location", { ...window.location, assign });
    return assign;
  }

  it("stores the target tenant, remembers the previous one, removes only the OIDC user entries and navigates", () => {
    const assign = stubLocation();
    setStoredTenant(HOME);
    window.localStorage.setItem("oidc.user:http://localhost:44301:OpenTms_App", "{\"access_token\":\"old\"}");
    window.localStorage.setItem("oidc.user:other:client", "x");
    window.localStorage.setItem("oidc.state", "keep");
    window.localStorage.setItem("opentms.grid.settings", "keep");

    switchSession("https://auth.example.test/Account/Impersonate?ticket=abc", ACME);

    expect(getStoredTenant()).toEqual(ACME);
    expect(JSON.parse(window.sessionStorage.getItem(PREVIOUS)!)).toEqual(HOME);
    expect(Object.keys(window.localStorage).filter((k) => k.startsWith("oidc.user:"))).toEqual([]);
    expect(window.localStorage.getItem("oidc.state")).toBe("keep");
    expect(window.localStorage.getItem("opentms.grid.settings")).toBe("keep");
    expect(assign).toHaveBeenCalledExactlyOnceWith("https://auth.example.test/Account/Impersonate?ticket=abc");
  });

  it("switching to the host clears the stored tenant and remembers the previous tenant", () => {
    stubLocation();
    setStoredTenant(ACME);

    switchSession("https://auth.example.test/Account/Impersonate?ticket=back", undefined);

    expect(getStoredTenant()).toBeUndefined();
    expect(JSON.parse(window.sessionStorage.getItem(PREVIOUS)!)).toEqual(ACME);
  });

  it("remembers 'no tenant' (the host) as null", () => {
    stubLocation();
    switchSession("https://auth.example.test/x", ACME);
    expect(window.sessionStorage.getItem(PREVIOUS)).toBe("null");
  });
});

describe("settleSessionSwitch", () => {
  it("after a refused switch restores the previous tenant, strips only the marker and remembers to tell the user once", () => {
    window.sessionStorage.setItem(PREVIOUS, JSON.stringify(HOME));
    setStoredTenant(ACME);
    window.history.replaceState({}, "", "/admin?impersonation=refused&tab=users");

    settleSessionSwitch();

    expect(getStoredTenant()).toEqual(HOME);
    expect(window.location.pathname).toBe("/admin");
    expect(window.location.search).toBe("?tab=users");
    expect(window.sessionStorage.getItem(PREVIOUS)).toBeNull();
    expect(takeRefusedSwitch()).toBe(true);
    expect(takeRefusedSwitch()).toBe(false);
  });

  it("after a refused switch from the host restores the host (no stored tenant) and leaves a clean URL", () => {
    window.sessionStorage.setItem(PREVIOUS, "null");
    setStoredTenant(ACME);
    window.history.replaceState({}, "", "/admin?impersonation=refused");

    settleSessionSwitch();

    expect(getStoredTenant()).toBeUndefined();
    expect(window.location.search).toBe("");
    expect(window.location.pathname).toBe("/admin");
  });

  it("after a successful switch keeps the new tenant and forgets the previous one", () => {
    window.sessionStorage.setItem(PREVIOUS, JSON.stringify(HOME));
    setStoredTenant(ACME);
    window.history.replaceState({}, "", "/admin");

    settleSessionSwitch();

    expect(getStoredTenant()).toEqual(ACME);
    expect(window.sessionStorage.getItem(PREVIOUS)).toBeNull();
    expect(takeRefusedSwitch()).toBe(false);
  });

  it("keeps the marker during the OIDC callback, which runs before the landing page", () => {
    window.sessionStorage.setItem(PREVIOUS, JSON.stringify(HOME));
    window.history.replaceState({}, "", "/auth/callback?code=x&state=y");

    settleSessionSwitch();

    expect(window.sessionStorage.getItem(PREVIOUS)).not.toBeNull();
    expect(window.location.search).toBe("?code=x&state=y");
  });

  it("does nothing on an ordinary start", () => {
    setStoredTenant(ACME);
    window.history.replaceState({}, "", "/master-data?impersonation=other");

    settleSessionSwitch();

    expect(getStoredTenant()).toEqual(ACME);
    expect(window.location.search).toBe("?impersonation=other");
    expect(takeRefusedSwitch()).toBe(false);
  });

  it("remembers the path an account switch returns to, once, and only a local one", () => {
    switchSessionWithoutLeaving("/planboard");
    expect(takeSwitchReturnTo()).toBe("/planboard");
    expect(takeSwitchReturnTo()).toBeUndefined();

    switchSessionWithoutLeaving("//evil.example.test/x");
    expect(takeSwitchReturnTo()).toBeUndefined();
    switchSessionWithoutLeaving("https://evil.example.test/");
    expect(takeSwitchReturnTo()).toBeUndefined();
  });

  it("a switch without a return path forgets an earlier one", () => {
    switchSessionWithoutLeaving("/planboard");
    switchSessionWithoutLeaving(undefined);
    expect(takeSwitchReturnTo()).toBeUndefined();
  });
});

/** switchSession navigates away; jsdom cannot, so location.assign is stubbed for the call. */
function switchSessionWithoutLeaving(returnTo: string | undefined) {
  vi.stubGlobal("location", { ...window.location, assign: vi.fn() });
  try {
    switchSession("https://auth.example.test/x", undefined, returnTo);
  } finally {
    vi.unstubAllGlobals();
  }
}
