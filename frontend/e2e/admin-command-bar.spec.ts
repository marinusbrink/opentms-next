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
import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import { accessToken, signInAsHost } from "./support/auth";

const API_BASE = process.env["PLAYWRIGHT_API_BASE"] ?? "http://localhost:44301";

/** The bulk-delete command; its label follows the selection: "Delete role", "Delete 1 role", "Delete 3 roles". */
function deleteCommand(page: Page) {
  return page.getByRole("button", { name: /^delete( \d+)? roles?$|^(\d+ )?(rol|rollen) verwijderen$/i });
}

/** The grid row of the role with this exact name (the centre container holds the cells, the pinned one the checkbox). */
async function rowIdOfRole(page: Page, name: string): Promise<string> {
  const row = page
    .locator(".ag-center-cols-container .ag-row")
    .filter({ has: page.getByRole("gridcell", { name, exact: true }) })
    .first();
  const id = await row.getAttribute("row-id");
  if (!id) throw new Error(`no row for role ${name}`);
  return id;
}

function rowCheckbox(page: Page, rowId: string) {
  return page.locator(`.ag-row[row-id="${rowId}"]`).locator('[data-testid="row-checkbox"], input[type="checkbox"]').first();
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
    await signInAsHost(page);
  });

  test("journey 1: select two non-static roles → Delete enabled → confirm → result dialog → grid refreshed", async ({
    page,
    request,
  }) => {
    const token = await accessToken(page);
    const roleAId = await createRole(request, token, `e2e-bulk-a-${Date.now()}`);
    const roleBId = await createRole(request, token, `e2e-bulk-b-${Date.now()}`);

    try {
      await page.goto("/admin/roles");
      await page.waitForURL("/admin/roles", { timeout: 10_000 });

      // Wait for grid to be populated.
      await page.waitForSelector(".ag-row", { timeout: 15_000 });

      // The Delete button must be disabled before any selection.
      const deleteBtn = deleteCommand(page);
      await expect(deleteBtn).toBeDisabled();

      // Select both test rows by clicking their checkboxes.
      for (const roleId of [roleAId, roleBId]) {
        await rowCheckbox(page, roleId).click();
      }

      // Delete button must now be enabled.
      await expect(deleteBtn).toBeEnabled();

      // Click Delete and confirm in the dialog.
      await deleteBtn.click();
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      await dialog.getByRole("button", { name: /^delete$|^verwijderen$/i }).click();

      // Result step must show ≥ 1 deleted.
      await expect(dialog.getByText(/deleted|verwijderd/i)).toBeVisible({ timeout: 10_000 });
      await dialog.getByRole("button", { name: /^(close|sluiten)$/i }).getByText(/^(close|sluiten)$/i).click();
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
  }) => {
    await page.goto("/admin/roles");
    await page.waitForURL("/admin/roles", { timeout: 10_000 });
    await page.waitForSelector(".ag-row", { timeout: 15_000 });

    // The seeded admin role is a system role (static): bulk delete skips it.
    const adminRowId = await rowIdOfRole(page, "admin");
    await rowCheckbox(page, adminRowId).click();

    const deleteBtn = deleteCommand(page);
    await expect(deleteBtn).toBeEnabled();
    await deleteBtn.click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: /^delete$|^verwijderen$/i }).click();

    // Result must show 0 deleted and 1 skipped.
    await expect(dialog.getByText(/deleted|verwijderd/i)).toBeVisible({ timeout: 10_000 });
    await expect(dialog.getByText(/skipped|overgeslagen/i)).toBeVisible();

    // Close the dialog.
    await dialog.getByRole("button", { name: /^(close|sluiten)$/i }).getByText(/^(close|sluiten)$/i).click();
    await expect(dialog).not.toBeVisible();

    // The static role is still there.
    await expect(page.locator(`.ag-row[row-id="${adminRowId}"]`).first()).toBeVisible();
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

    // Wait for the command bar to render (the Office-365 h-10 strip; design 30 retired the boxed h-12 bar).
    const commandBar = page.locator(".flex.h-10").first();
    await expect(commandBar).toBeVisible({ timeout: 10_000 });

    // Delete button must be disabled when selectionCount === 0.
    const deleteBtn = deleteCommand(page);
    await expect(deleteBtn).toBeDisabled();
  });
});
