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
 * Sign-in: ./support/auth.ts (PLAYWRIGHT_BYPASS_AUTH=1 skips it for a session
 * that is already signed in).
 */
import { test, expect } from "@playwright/test";
import { signInAsHost } from "./support/auth";


test.describe("AppShell + AppNavPane — shell navigation (critical risk)", () => {
  test.beforeEach(async ({ page }) => {
    await signInAsHost(page);
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
    /* Dashboard has no views. Its app feature is off by default (docs/designs/tenants-and-editions.md, "App
     * features") and the host has no edition to switch it on, so the shell renders FeatureUnavailable full-width:
     * assert that screen, so the test cannot pass on a page that never rendered. */
    await page.goto("/dashboard");
    await page.waitForURL("/dashboard", { timeout: 10_000 });

    await expect(
      page.getByRole("status").filter({ hasText: /not part of your package|hoort niet bij uw pakket/i }),
    ).toBeVisible();

    // No AppNavPane for dashboard (no views configured).
    await expect(page.getByRole("navigation", { name: /dashboard navigation/i })).toHaveCount(0);
  });
});
