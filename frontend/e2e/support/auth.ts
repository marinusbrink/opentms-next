/**
 * Signing in for the E2E specs, against the live dev setup (CLAUDE.md "Running locally"): the SPA sends the browser
 * straight to the auth server's sign-in page (/Account/Login), which asks the e-mail address, then the password, and
 * the organisation only when the credentials are valid in several (docs/designs/62-sign-in-tenant-detection.md).
 * Host login admin@abp.io / 1q2w3E* unless PLAYWRIGHT_EMAIL / PLAYWRIGHT_PASS say otherwise; where the address is valid
 * in several places too, the host is picked.
 * PLAYWRIGHT_BYPASS_AUTH=1 skips it for a session that is already signed in.
 */
import { type Page } from "@playwright/test";

export const ADMIN_EMAIL = process.env["PLAYWRIGHT_EMAIL"] ?? "admin@abp.io";
export const ADMIN_PASS = process.env["PLAYWRIGHT_PASS"] ?? "1q2w3E*";
const BYPASS_AUTH = process.env["PLAYWRIGHT_BYPASS_AUTH"] === "1";

export async function signInAsHost(page: Page) {
  if (BYPASS_AUTH) return;
  await page.goto("/");
  await page.waitForURL(/\/Account\/Login/i, { timeout: 20_000 });
  await page.waitForLoadState("networkidle");

  /* A remembered address starts at the password; otherwise step 1 asks it. */
  const email = page.locator("#Email");
  if (await email.isVisible()) {
    await email.fill(ADMIN_EMAIL);
    await page.getByRole("button", { name: /^next$|^volgende$/i }).click();
  }

  await page.locator("#Password").fill(ADMIN_PASS);
  await page.getByRole("button", { name: /^sign in$|^inloggen$/i }).click();

  /* Valid in several places: the organisation step; the host is typed by its full name. */
  const tenant = page.locator("#TenantQuery");
  const onTenantStep = await tenant
    .waitFor({ state: "visible", timeout: 5_000 })
    .then(() => true)
    .catch(() => false);
  if (onTenantStep) {
    await tenant.fill("Host");
    await page.getByRole("button", { name: /^continue$|^doorgaan$/i }).click();
  }

  await page.waitForURL(/^http:\/\/localhost:5173\/(?!auth\/callback)/, { timeout: 30_000 });
}

/** The SPA's own access token (oidc-client-ts stores the user in localStorage), for API calls next to the UI. */
export async function accessToken(page: Page): Promise<string> {
  return page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((k) => k.startsWith("oidc.user:"));
    if (!key) throw new Error("not signed in");
    return (JSON.parse(window.localStorage.getItem(key)!) as { access_token: string }).access_token;
  });
}
