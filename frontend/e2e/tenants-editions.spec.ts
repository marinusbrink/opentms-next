/**
 * E2E for the Tenants app (docs/designs/tenants-and-editions.md; review gap B4). Critical risk: it creates and drops
 * real databases and switches the browser session to another user.
 *
 * Runs against the live dev setup (CLAUDE.md "Running locally"): the SPA at http://localhost:5173, the API host and
 * auth server at http://localhost:44301 with Hangfire running the provisioning job, PostgreSQL on localhost:5432
 * (postgres/postgres; psql on the PATH). Host login admin / 1q2w3E* (PLAYWRIGHT_USER / PLAYWRIGHT_PASS override).
 *
 * Journeys, in order, on one browser session:
 *   1. editions: create an edition, switch the Master data "Countries" list feature on in its Features tab;
 *   2. tenants: create a tenant on that edition → status reaches Ready → its database exists;
 *   3. "Log in as": open the tenant's users, log in as its admin → banner with the names; the tenant's application
 *      configuration has the edition's feature on (Countries) and a default-off one off (Currencies), and the shell
 *      shows the Countries list but FeatureUnavailable for Currencies; "Back to host" → host session, no banner;
 *   4. delete the tenant with its typed name → gone from the list, its database dropped; delete the edition.
 *
 * Every tenant, edition and database this spec creates carries a unique "e2e-<stamp>" name and is removed in afterAll
 * whatever happened; the existing tenants demo, test, test2 and the edition Standaard are never touched.
 */
import { execFileSync } from "node:child_process";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { accessToken, signInAsHost } from "./support/auth";

const API = process.env["PLAYWRIGHT_API_URL"] ?? "http://localhost:44301";
const PROTECTED_TENANTS = ["demo", "test", "test2"];
const PROTECTED_EDITIONS = ["Standaard"];

const stamp = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`;
const TENANT_NAME = `e2e-${stamp}`;
const EDITION_NAME = `E2E edition ${stamp}`;

test.describe.configure({ mode: "serial" });
test.setTimeout(240_000);

/* ── helpers ─────────────────────────────────────────────────────────────────────────────── */

async function api(request: APIRequestContext, token: string, method: "GET" | "DELETE" | "PUT" | "POST", path: string, data?: unknown, tenantId?: string) {
  const response = await request.fetch(`${API}${path}`, {
    method,
    data,
    headers: { Authorization: `Bearer ${token}`, ...(tenantId ? { __tenant: tenantId } : {}) },
  });
  return response;
}

function psql(sql: string): string {
  return execFileSync("psql", ["-h", "localhost", "-U", "postgres", "-d", "postgres", "-At", "-c", sql], {
    env: { ...process.env, PGPASSWORD: "postgres" },
  })
    .toString()
    .trim();
}

function databaseExists(name: string): boolean {
  if (!/^t_[a-z0-9_]+$/.test(name)) throw new Error(`refusing to look up ${name}`);
  return psql(`SELECT count(*) FROM pg_database WHERE datname = '${name}'`) === "1";
}

type TenantRow = { id: string; name: string; provisioningState?: string | null; databaseName?: string | null; editionId?: string | null };

async function findTenant(request: APIRequestContext, token: string, name: string): Promise<TenantRow | undefined> {
  const response = await api(request, token, "GET", `/api/platform/tenants?startRow=0&endRow=50&wildcardSearch=${encodeURIComponent(name)}`);
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as { rows: TenantRow[] };
  return body.rows.find((r) => r.name === name);
}

async function findEdition(request: APIRequestContext, token: string, name: string): Promise<{ id: string; displayName: string; tenantCount: number } | undefined> {
  const response = await api(request, token, "GET", `/api/platform/editions?startRow=0&endRow=200`);
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as { rows: { id: string; displayName: string; tenantCount: number }[] };
  return body.rows.find((r) => r.displayName === name);
}

const gridRow = (page: Page, text: string) => page.getByRole("row").filter({ hasText: text });

async function rowAction(page: Page, rowText: string, action: RegExp) {
  await gridRow(page, rowText).first().click({ button: "right" });
  await page.getByRole("menuitem", { name: action }).click();
}

/* ── shared state across the serial tests ────────────────────────────────────────────────── */

let hostToken = "";
/* The protected tenants this machine has (a fresh environment may have none); each must still be there at the end. */
let presentProtectedTenants: string[] = [];
let tenantId = "";
let databaseName = "";
let editionId = "";

test.describe("Tenants app – end to end (critical)", () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    expect(PROTECTED_TENANTS).not.toContain(TENANT_NAME);
    page = await browser.newPage();
    await signInAsHost(page);
    hostToken = await accessToken(page);
    for (const name of PROTECTED_TENANTS) {
      if (await findTenant(page.request, hostToken, name)) presentProtectedTenants.push(name);
    }
  });

  test.afterAll(async ({ request }) => {
    /* Clean up whatever is left, through the API (host token), then the database itself if the drop never ran. */
    try {
      const token = hostToken;
      const tenant = token ? await findTenant(request, token, TENANT_NAME) : undefined;
      if (tenant && !PROTECTED_TENANTS.includes(tenant.name)) {
        const deadline = Date.now() + 120_000;
        let current: TenantRow | undefined = tenant;
        while (current && ["Queued", "Running"].includes(current.provisioningState ?? "") && Date.now() < deadline) {
          await new Promise((r) => setTimeout(r, 2_000));
          current = await findTenant(request, token, TENANT_NAME);
        }
        if (current) await api(request, token, "DELETE", `/api/platform/tenants/${current.id}`);
      }

      const edition = token ? await findEdition(request, token, EDITION_NAME) : undefined;
      if (edition && !PROTECTED_EDITIONS.includes(edition.displayName)) {
        await api(request, token, "DELETE", `/api/platform/editions/${edition.id}`);
      }
    } finally {
      if (databaseName && databaseName.startsWith("t_e2e_")) {
        /* The drop job normally does this; wait for it briefly, then drop what is left. */
        for (let i = 0; i < 30 && databaseExists(databaseName); i++) await new Promise((r) => setTimeout(r, 1_000));
        if (databaseExists(databaseName)) {
          psql(`DROP DATABASE IF EXISTS "${databaseName}" WITH (FORCE)`);
          psql(`DROP ROLE IF EXISTS "${databaseName}"`);
        }
      }
      await page?.close();
    }
  });

  test("an edition is created and its Countries list feature is switched on", async () => {
    await page.goto("/tenants/editions");
    await page.getByRole("button", { name: "New edition" }).click();
    await page.locator("#ef-name").fill(EDITION_NAME);
    await page.getByRole("button", { name: /^save$/i }).click();
    await expect(gridRow(page, EDITION_NAME)).toBeVisible({ timeout: 15_000 });

    const edition = await findEdition(page.request, hostToken, EDITION_NAME);
    expect(edition).toBeDefined();
    editionId = edition!.id;

    /* Open the edition (double-click), Features tab, switch Countries on, save. */
    await gridRow(page, EDITION_NAME).first().dblclick();
    await page.getByRole("tab", { name: "Features" }).click();
    /* The visible checkbox (Base UI also renders a hidden input, which carries the id). */
    const countries = page.getByRole("dialog").getByRole("checkbox", { name: /^Countries Shows the Countries list/ });
    await expect(countries).toBeVisible({ timeout: 15_000 });
    await expect(countries).not.toBeChecked();
    await countries.click();
    await expect(countries).toBeChecked();
    await page.getByRole("button", { name: /^save$/i }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0, { timeout: 15_000 });

    /* Stored on the edition (provider "E"). */
    const features = await api(page.request, hostToken, "GET", `/api/feature-management/features?providerName=E&providerKey=${editionId}`);
    expect(features.ok()).toBeTruthy();
    const groups = (await features.json()) as { groups: { features: { name: string; value: string }[] }[] };
    const values = Object.fromEntries(groups.groups.flatMap((g) => g.features).map((f) => [f.name, f.value]));
    expect(values["Apps.MasterData.Reference.Countries"]).toBe("true");
    expect(values["Apps.MasterData.Reference.Currencies"]).toBe("false");
  });

  test("a tenant created on that edition is provisioned (Ready) and has its own database", async () => {
    await page.goto("/tenants/list");
    await page.getByRole("button", { name: "New tenant" }).click();
    await page.locator("#tf-name").fill(TENANT_NAME);
    await page.locator("#tf-edition").click();
    await page.locator("#tf-edition").fill(EDITION_NAME);
    await page.getByRole("option", { name: EDITION_NAME }).click();
    await page.locator("#tf-adminEmailAddress").fill(`admin@${TENANT_NAME}.example.test`);
    await page.getByRole("button", { name: /^save$/i }).click();
    await expect(gridRow(page, TENANT_NAME)).toBeVisible({ timeout: 15_000 });

    const created = await findTenant(page.request, hostToken, TENANT_NAME);
    expect(created).toBeDefined();
    tenantId = created!.id;
    databaseName = created!.databaseName ?? "";
    expect(created!.editionId).toBe(editionId);
    expect(databaseName).toMatch(/^t_e2e_[a-z0-9_]+$/);

    /* The list refreshes itself while a tenant is pending; the status reaches Ready. */
    await expect(gridRow(page, TENANT_NAME)).toContainText("Ready", { timeout: 180_000 });
    expect((await findTenant(page.request, hostToken, TENANT_NAME))!.provisioningState).toBe("Succeeded");
    expect(databaseExists(databaseName)).toBe(true);
  });

  test("Log in as the tenant's admin shows the banner and the edition's features, and Back to host returns", async () => {
    await page.goto("/tenants/list");
    await rowAction(page, TENANT_NAME, /log in as/i);

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    const adminRow = dialog.getByRole("row").filter({ hasText: /^admin/ }).first();
    await expect(adminRow).toBeVisible({ timeout: 15_000 });
    await expect(dialog).not.toContainText(`@${TENANT_NAME}`); // no email addresses in the picker
    await adminRow.dblclick();

    /* The auth server switches the session and the SPA signs in again in the tenant. */
    const banner = page.getByRole("status").filter({ hasText: "You are logged in as" });
    await expect(banner).toBeVisible({ timeout: 45_000 });
    await expect(banner).toContainText(`You are logged in as admin in ${TENANT_NAME}, on behalf of admin.`);
    await expect(page.getByRole("button", { name: "Back to host" })).toBeVisible();

    /* The tenant's own configuration: its edition switched Countries on; Currencies keeps its default (off). */
    const tenantToken = await accessToken(page);
    const configuration = await api(page.request, tenantToken, "GET", "/api/abp/application-configuration", undefined, tenantId);
    expect(configuration.ok()).toBeTruthy();
    const config = (await configuration.json()) as {
      currentTenant: { id: string };
      currentUser: { userName: string; impersonatorUserId?: string };
      features: { values: Record<string, string> };
    };
    expect(config.currentTenant.id).toBe(tenantId);
    expect(config.currentUser.userName).toBe("admin");
    expect(config.currentUser.impersonatorUserId).toBeTruthy();
    expect(config.features.values["Apps.MasterData.Reference.Countries"]).toBe("true");
    expect(config.features.values["Apps.MasterData.Reference.Currencies"]).toBe("false");

    /* The shell agrees: the Countries list is a screen, the Currencies list is not part of the package. */
    await page.goto("/master-data/countries");
    await expect(page.getByText("This screen is not part of your package")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "New country" })).toBeVisible({ timeout: 15_000 });
    /* The nav pane holds only the group with an enabled list, and in it only Countries. */
    const nav = page.getByRole("navigation", { name: "Master data navigation" });
    await expect(nav.getByRole("listitem")).toHaveCount(1);
    await nav.getByRole("button", { name: "Regions and lists" }).click();
    await expect(nav.getByRole("link", { name: "Countries" })).toBeVisible();
    await expect(nav.getByRole("link", { name: "Currencies" })).toHaveCount(0);
    await page.goto("/master-data/currencies");
    await expect(page.getByText("This screen is not part of your package")).toBeVisible({ timeout: 15_000 });

    /* The host-only API refuses the impersonated tenant session. */
    const refused = await api(page.request, tenantToken, "GET", "/api/platform/tenants?startRow=0&endRow=10", undefined, tenantId);
    expect(refused.ok()).toBeFalsy();

    /* Back to host. */
    await page.getByRole("button", { name: "Back to host" }).click();
    await page.waitForURL(/^http:\/\/localhost:5173\/admin/, { timeout: 45_000 });
    await expect(page.getByRole("button", { name: /Host/ })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole("status").filter({ hasText: "You are logged in as" })).toHaveCount(0);
    hostToken = await accessToken(page);
    const hostConfig = await api(page.request, hostToken, "GET", "/api/abp/application-configuration");
    const host = (await hostConfig.json()) as { currentTenant: { id: string | null }; currentUser: { impersonatorUserId?: string | null } };
    expect(host.currentTenant.id ?? null).toBeNull();
    expect(host.currentUser.impersonatorUserId ?? null).toBeNull();
  });

  test("deleting the tenant with its typed name removes it and drops its database; the edition is deleted", async () => {
    await page.goto("/tenants/list");
    await rowAction(page, TENANT_NAME, /^delete$/i);

    const confirm = page.getByRole("alertdialog");
    await expect(confirm).toBeVisible();
    const deleteButton = confirm.getByRole("button", { name: /^delete$/i });
    await expect(deleteButton).toBeDisabled();
    await confirm.locator("#dt-confirm-name").fill(TENANT_NAME.slice(0, -1));
    await expect(deleteButton).toBeDisabled();
    await confirm.locator("#dt-confirm-name").fill(TENANT_NAME);
    await expect(deleteButton).toBeEnabled();
    await deleteButton.click();

    await expect(gridRow(page, TENANT_NAME)).toHaveCount(0, { timeout: 15_000 });
    await expect.poll(async () => (await findTenant(page.request, hostToken, TENANT_NAME))?.id ?? "gone", { timeout: 15_000 }).toBe("gone");
    await expect.poll(() => databaseExists(databaseName), { timeout: 120_000, intervals: [2_000] }).toBe(false);
    expect(psql(`SELECT count(*) FROM pg_roles WHERE rolname = '${databaseName}'`)).toBe("0");

    /* The edition has no tenants left and goes too. */
    await page.goto("/tenants/editions");
    await rowAction(page, EDITION_NAME, /^delete$/i);
    await page.getByRole("alertdialog").getByRole("button", { name: /^delete$/i }).click();
    await expect(gridRow(page, EDITION_NAME)).toHaveCount(0, { timeout: 15_000 });
    await expect.poll(async () => (await findEdition(page.request, hostToken, EDITION_NAME))?.id ?? "gone", { timeout: 15_000 }).toBe("gone");

    /* The protected fixtures are still there. */
    for (const name of presentProtectedTenants) {
      expect(await findTenant(page.request, hostToken, name)).toBeDefined();
    }
  });
});
