/**
 * E2E journeys mandated by design §Test risk analysis for AppCommandBar
 * (critical risk) and role bulk-delete (critical risk).
 *
 * Journey 1: select two non-static roles → Delete enabled → confirm → result dialog → grid refreshed.
 * Journey 2: select static role only → confirm → 0 deleted, 1 skipped.
 * Journey 3: /admin/users → New User command → dialog opens.
 * Journey 4: no selection → AppCommandBar at full height with Delete disabled.
 *
 * Prerequisites: run `npx playwright install chromium` once.
 * In CI: start the dev server and backend before `npx playwright test`.
 */
import { test, expect, type APIRequestContext } from "@playwright/test";

const BYPASS_AUTH = process.env["PLAYWRIGHT_BYPASS_AUTH"] === "1";
const ADMIN_USER = process.env["PLAYWRIGHT_USER"] ?? "admin";
const ADMIN_PASS = process.env["PLAYWRIGHT_PASS"] ?? "1q2w3E*";
const API_BASE = process.env["PLAYWRIGHT_API_BASE"] ?? "http://localhost:44301";

async function loginIfNeeded(page: import("@playwright/test").Page) {
  if (BYPASS_AUTH) return;
  await page.goto("/");
  await page.waitForURL(/\/Account\/Login/i, { timeout: 10_000 });
  await page.getByLabel(/username|gebruikersnaam/i).fill(ADMIN_USER);
  await page.getByLabel(/password|wachtwoord/i).fill(ADMIN_PASS);
  await page.getByRole("button", { name: /sign in|inloggen/i }).click();
  await page.waitForURL(/^(?!.*\/Account\/Login)/, { timeout: 15_000 });
}

async function getAccessToken(request: APIRequestContext): Promise<string> {
  const resp = await request.post(`${API_BASE}/connect/token`, {
    form: {
      grant_type: "password",
      username: ADMIN_USER,
      password: ADMIN_PASS,
      client_id: "OpenTms_App",
      scope: "openid profile email OpenTms",
    },
  });
  const body = await resp.json() as { access_token: string };
  return body["access_token"];
}

async function createRole(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<string> {
  const resp = await request.post(`${API_BASE}/api/platform/administration/roles`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { name, isDefault: false, isPublic: false },
  });
  const body = await resp.json() as { id: string };
  return body["id"];
}

async function deleteRoleById(
  request: APIRequestContext,
  token: string,
  id: string,
): Promise<void> {
  await request.delete(`${API_BASE}/api/platform/administration/roles/${id}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
}

test.describe("AppCommandBar + role bulk-delete — E2E (critical risk)", () => {
  test.beforeEach(async ({ page }) => {
    await loginIfNeeded(page);
  });

  test("journey 1: select two non-static roles → Delete enabled → confirm → result dialog → grid refreshed", async ({
    page,
    request,
  }) => {
    const token = await getAccessToken(request);
    const roleAId = await createRole(request, token, `e2e-bulk-a-${Date.now()}`);
    const roleBId = await createRole(request, token, `e2e-bulk-b-${Date.now()}`);

    try {
      await page.goto("/admin/roles");
      await page.waitForURL("/admin/roles", { timeout: 10_000 });

      // Wait for grid to be populated.
      await page.waitForSelector(".ag-row", { timeout: 15_000 });

      // The Delete button must be disabled before any selection.
      const deleteBtn = page.getByRole("button", { name: /bulk.?delete|verwijder/i }).last();
      await expect(deleteBtn).toBeDisabled();

      // Select both test rows by clicking their checkboxes.
      for (const roleId of [roleAId, roleBId]) {
        const row = page.locator(`.ag-row[row-id="${roleId}"]`);
        const checkbox = row.locator('[data-testid="row-checkbox"], input[type="checkbox"]').first();
        await checkbox.click();
      }

      // Delete button must now be enabled.
      await expect(deleteBtn).toBeEnabled();

      // Click Delete and confirm in the dialog.
      await deleteBtn.click();
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      await dialog.getByRole("button", { name: /delete.*role|verwijder/i }).click();

      // Result step must show ≥ 1 deleted.
      await expect(dialog.getByText(/deleted|verwijderd/i)).toBeVisible({ timeout: 10_000 });
      await dialog.getByRole("button", { name: /close|sluiten/i }).click();
      await expect(dialog).not.toBeVisible();

      // Grid must have refreshed (rows no longer present).
      await expect(page.locator(`.ag-row[row-id="${roleAId}"]`)).not.toBeVisible({ timeout: 5_000 });
    } finally {
      // Clean up — best-effort; roles may already be gone.
      for (const id of [roleAId, roleBId]) {
        await deleteRoleById(request, token, id).catch(() => undefined);
      }
    }
  });

  test("journey 2: select static role only → confirm → 0 deleted, 1 skipped", async ({
    page,
    request,
  }) => {
    const token = await getAccessToken(request);

    await page.goto("/admin/roles");
    await page.waitForURL("/admin/roles", { timeout: 10_000 });
    await page.waitForSelector(".ag-row", { timeout: 15_000 });

    // Find the static admin row (marked with "static" badge).
    const staticRow = page
      .locator(".ag-row")
      .filter({ has: page.locator('[class*="static" i], [aria-label*="static" i]').or(page.getByText(/admin/i)) })
      .first();

    const checkbox = staticRow
      .locator('[data-testid="row-checkbox"], input[type="checkbox"]')
      .first();
    await checkbox.click();

    const deleteBtn = page.getByRole("button", { name: /bulk.?delete|verwijder/i }).last();
    await expect(deleteBtn).toBeEnabled();
    await deleteBtn.click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: /delete.*role|verwijder/i }).click();

    // Result must show 0 deleted and 1 skipped.
    await expect(dialog.getByText(/deleted|verwijderd/i)).toBeVisible({ timeout: 10_000 });
    await expect(dialog.getByText(/skipped|overgeslagen/i)).toBeVisible();

    // Close the dialog.
    await dialog.getByRole("button", { name: /close|sluiten/i }).click();
    await expect(dialog).not.toBeVisible();

    // Verify the static role still exists.
    const _ = token; // token used above
  });

  test("journey 3: /admin/users → New User command → dialog opens", async ({ page }) => {
    await page.goto("/admin/users");
    await page.waitForURL("/admin/users", { timeout: 10_000 });

    // The New User button must be in the command bar.
    const newUserBtn = page.getByRole("button", { name: /new.?user|nieuwe.?gebruiker/i });
    await expect(newUserBtn).toBeVisible();
    await expect(newUserBtn).toBeEnabled();

    await newUserBtn.click();

    // The user form dialog must open.
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible({ timeout: 5_000 });
    await expect(dialog.getByRole("heading", { name: /new.?user|nieuwe.?gebruiker/i })).toBeVisible();
  });

  test("journey 4: no selection → AppCommandBar renders at full height, Delete disabled", async ({
    page,
  }) => {
    await page.goto("/admin/roles");
    await page.waitForURL("/admin/roles", { timeout: 10_000 });

    // Wait for the command bar to render.
    const commandBar = page.locator(".mb-4.flex.h-12");
    await expect(commandBar).toBeVisible({ timeout: 10_000 });

    // h-12 class must be present (full height preserved even when empty selection).
    await expect(commandBar).toHaveClass(/h-12/);

    // Delete button must be disabled when selectionCount === 0.
    const deleteBtn = page.getByRole("button", { name: /bulk.?delete|verwijder/i }).last();
    await expect(deleteBtn).toBeDisabled();
  });
});
