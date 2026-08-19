import { defineConfig, devices } from "@playwright/test";

/**
 * E2E config for the OpenTMS frontend shell.
 * Tests run against the Vite dev server (port 5173) — the same origin
 * that OIDC and CORS are bound to in the local dev setup (see CLAUDE.md).
 *
 * In CI: start the dev server before `npx playwright test`.
 * Against a real backend: set PLAYWRIGHT_BASE_URL and PLAYWRIGHT_BYPASS_AUTH.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env["CI"],
  retries: process.env["CI"] ? 2 : 0,
  workers: process.env["CI"] ? 1 : undefined,
  reporter: "html",
  use: {
    baseURL: process.env["PLAYWRIGHT_BASE_URL"] ?? "http://localhost:5173",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
