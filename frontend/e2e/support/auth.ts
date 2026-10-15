/**
 * Signing in for the E2E specs, against the live dev setup (CLAUDE.md "Running locally"): the SPA's start page
 * offers "Sign in", which goes to the auth server's login page (LeptonX-lite, /Account/Login), and back.
 * Host login admin / 1q2w3E* unless PLAYWRIGHT_USER / PLAYWRIGHT_PASS say otherwise.
 * PLAYWRIGHT_BYPASS_AUTH=1 skips it for a session that is already signed in.
 */
import { expect, type Page } from "@playwright/test";

export const ADMIN_USER = process.env["PLAYWRIGHT_USER"] ?? "admin";
export const ADMIN_PASS = process.env["PLAYWRIGHT_PASS"] ?? "1q2w3E*";
const BYPASS_AUTH = process.env["PLAYWRIGHT_BYPASS_AUTH"] === "1";

export async function signInAsHost(page: Page) {
  if (BYPASS_AUTH) return;
  await page.goto("/");
  const signIn = page.getByRole("button", { name: /^sign in$|^inloggen$/i });
  /* The SPA's start page offers "Sign in"; an expired session may go to the login page directly. */
  const onStartPage = await signIn
    .waitFor({ state: "visible", timeout: 15_000 })
    .then(() => true)
    .catch(() => false);
  if (onStartPage) {
    await signIn.click();
  }
  await page.waitForURL(/\/Account\/Login/i, { timeout: 20_000 });
  /* The development login page pre-fills the user name after load; fill once it has settled. */
  await page.waitForLoadState("networkidle");
  const user = page.locator("#LoginInput_UserNameOrEmailAddress");
  await user.fill(ADMIN_USER);
  await expect(user).toHaveValue(ADMIN_USER);
  await page.locator("#LoginInput_Password").fill(ADMIN_PASS);
  await page.getByRole("button", { name: /^login$|^inloggen$/i }).click();
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
