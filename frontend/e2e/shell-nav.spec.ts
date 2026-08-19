/**
 * E2E smoke tests for the AppNavPane + AppShell platform shell (critical risk).
 *
 * These tests run against a live Vite dev server connected to a real backend.
 * They exercise the TanStack Router navigation lifecycle in a real browser —
 * the gap that jsdom-based Vitest + RTL component tests cannot cover.
 *
 * Minimum journey mandated by the design (§Test risk analysis):
 *   login → /admin/users → click Roles nav entry → assert /admin/roles renders
 *
 * Prerequisite: run `npx playwright install chromium` once before first run.
 * In CI: start the dev server and backend before `npx playwright test`.
 *
 * When PLAYWRIGHT_BYPASS_AUTH=1 is set the tests skip OIDC login and assume
 * an already-authenticated session cookie is present (for local dev iteration).
 */
import { test, expect } from "@playwright/test";

const BYPASS_AUTH = process.env["PLAYWRIGHT_BYPASS_AUTH"] === "1";
const ADMIN_USER = process.env["PLAYWRIGHT_USER"] ?? "admin";
const ADMIN_PASS = process.env["PLAYWRIGHT_PASS"] ?? "1q2w3E*";

/**
 * Log in through the OpenIddict login page.
 * Skipped when PLAYWRIGHT_BYPASS_AUTH=1 (pre-authenticated session).
 */
async function loginIfNeeded(page: import("@playwright/test").Page) {
  if (BYPASS_AUTH) return;
  await page.goto("/");
  // The OIDC redirect lands on the OpenIddict login page at /Account/Login.
  await page.waitForURL(/\/Account\/Login/i, { timeout: 10_000 });
  await page.getByLabel(/username|gebruikersnaam/i).fill(ADMIN_USER);
  await page.getByLabel(/password|wachtwoord/i).fill(ADMIN_PASS);
  await page.getByRole("button", { name: /sign in|inloggen/i }).click();
  // After login the OIDC callback redirects back to the SPA.
  await page.waitForURL(/^(?!.*\/Account\/Login)/, { timeout: 15_000 });
}

test.describe("AppShell + AppNavPane — shell navigation (critical risk)", () => {
  test.beforeEach(async ({ page }) => {
    await loginIfNeeded(page);
  });

  test("login → /admin/users → click Roles nav entry → /admin/roles renders", async ({
    page,
  }) => {
    // Navigate to the Administration app (Users view).
    await page.goto("/admin/users");
    await page.waitForURL("/admin/users", { timeout: 10_000 });

    // The left nav pane must be visible.
    const nav = page.getByRole("navigation", { name: /administration navigation/i });
    await expect(nav).toBeVisible();

    // The "Roles" nav entry must be present in the pane.
    const rolesLink = nav.getByRole("link", { name: /roles|rollen/i });
    await expect(rolesLink).toBeVisible();

    // Clicking Roles navigates to /admin/roles.
    await rolesLink.click();
    await page.waitForURL("/admin/roles", { timeout: 5_000 });

    // The Roles link is now the active entry.
    await expect(rolesLink).toHaveAttribute("aria-current", "page");
  });

  test("nav pane collapses and expands via the toggle button", async ({ page }) => {
    await page.goto("/admin/users");
    await page.waitForURL("/admin/users", { timeout: 10_000 });

    const nav = page.getByRole("navigation", { name: /administration navigation/i });
    await expect(nav).toBeVisible();

    // Pane starts expanded (w-[250px]).
    await expect(nav).toHaveClass(/w-\[250px\]/);

    // Toggle collapses the pane.
    const toggle = page.getByRole("button", { name: /collapse navigation/i });
    await toggle.click();
    await expect(nav).toHaveClass(/w-14/);

    // Toggle expands it again.
    const expandToggle = page.getByRole("button", { name: /expand navigation/i });
    await expandToggle.click();
    await expect(nav).toHaveClass(/w-\[250px\]/);
  });

  test("apps without views render full-width layout (no left nav pane)", async ({ page }) => {
    await page.goto("/dashboard");
    await page.waitForURL("/dashboard", { timeout: 10_000 });

    // No AppNavPane for dashboard (no views configured).
    const nav = page.getByRole("navigation", { name: /dashboard navigation/i });
    await expect(nav).not.toBeVisible();
  });
});
