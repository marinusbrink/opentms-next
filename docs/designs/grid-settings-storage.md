# Design: Per-user grid settings in their own Platform table

**PBI:** none — defect found while building the Administration app (design #8): the fifth grid of
one user breaks grid-settings persistence for that user. No issue exists for it; Marinus approves
the direction in chat (CLAUDE.local.md: the task as stated is the design doc).
**Status:** implemented with this change (gate 1 and gate 2 are the same review, see below)
**Date:** 2026-10-10, amended 2026-10-11 (implementation details, frontend failure path)
**Amends:** [6-platform-grid-component.md](6-platform-grid-component.md) §Settings storage

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not. -->

> **Platform change — risk class critical by default.** The design and the implementation travel
> in one PR, as the outbox did (`outbox-per-tenant.md`): gate 1 and gate 2 are the same review.

## The defect

Design #6 (amended 2026-08-18) stores **every grid of a user in one ABP setting value**,
`OpenTms.Platform.Grid.Settings`, as a JSON dictionary keyed `{tenantId}:{gridId}`. ABP's
`AbpSettings.Value` column is `character varying(2048)` (`SettingConsts.MaxValueLength`). The
Administration app alone has five grids (`users`, `roles`, `organizationUnits`,
`organizationUnitMembers`, `organizationUnitUnaddedUsers`). A tenant user who has personalised
all five (25 column states) produces a blob of **2 167 characters** (measured; the 32-character
tenant prefix on every key counts five times — a host user stays at 2 027, five grids of four
columns each at 1 879). From that point `PUT /api/platform/grid-settings/{gridId}` fails with
HTTP 500 (Npgsql 22001, *value too long for type character varying(2048)*), the grid logs
`GridSettingsSaveFailed`, and because the whole blob is rewritten on every save, **no grid of that
user persists any more** — the smaller ones included. A single grid at the validated maximum of
50 columns (~3 KB) would overflow the value on its own.

## Options weighed

| | Option | Verdict |
|---|---|---|
| **(a)** | A Platform entity `GridUserSetting` — one row per (tenant, user, grid) in `platform."GridUserSettings"`, additive migration via the DbMigrator. | **Chosen.** Removes the size limit for good, makes tenant isolation the ABP data filter's job instead of a string prefix, and ends the read-modify-write on a shared blob. |
| (b) | One ABP setting per `gridId` through dynamic setting definitions (`IDynamicSettingDefinitionStore`) or a custom `ISettingDefinitionManager`. | Rejected. Every value is still capped at 2048 characters, so a 50-column grid still fails. Definitions are global, so each new `gridId` would be written to the definition store at runtime. ABP user-level settings are keyed by user only, so the manual `{tenantId}:` prefix (or a value per tenant) stays. |
| (c) | Raise the length of `AbpSettings.Value`. | Not possible as a module change: the table is ABP's, mapped by `ConfigureSettingManagement()` in the host context; its length is a framework constant that the setting store, cache and DTOs validate against. |

## Domain impact

**Module:** Platform (horizontal). No domain module changes. The frontend changes in one place:
the grid's save and reset calls get the visible failure path of UI-FB-005 (§UI design).

**New entity** `OpenTms.Platform.Grid.GridUserSetting` (project `OpenTms.Platform.Domain`),
`AuditedAggregateRoot<Guid>, IMultiTenant`:

| Property | Type | Notes |
|---|---|---|
| `TenantId` | `Guid?` | ABP data filter (constitution rule 1). Null for host users. |
| `UserId` | `Guid` | The owner. Every query filters on `CurrentUser.Id`. |
| `GridId` | `string(100)` | Validated `^[a-zA-Z0-9_.-]{1,100}$` as today. |
| `SettingsJson` | `text` | The `GridSettingsDto` serialised with `System.Text.Json` web defaults; no length limit. |
| audit columns | | `CreationTime`, `CreatorId`, `LastModificationTime`, `LastModifierId`, `ConcurrencyStamp`, `ExtraProperties` from the base class. |

Table `platform."GridUserSettings"`, unique index `(TenantId, UserId, GridId)` declared
`NULLS NOT DISTINCT` (PostgreSQL 15+; the project runs 17) so host users, whose `TenantId` is null,
get the same one-row guarantee as tenant users. Data access through ABP's default repository
`IRepository<GridUserSetting, Guid>` (already registered with `includeAllEntities: true`); no custom
repository. The entity carries `[DisableAuditing]`: a column resize is machine traffic, which the
functional logbook deliberately leaves out (`FunctionalLogbookEntitySelector`), like the ABP setting
rows it replaces.

**`GridSettingsAppService`** keeps its class name, `IGridSettingsAppService` and the three methods;
the body changes:

- `GetAsync` — one indexed lookup `(UserId, GridId)` under the tenant filter; null when absent.
- `SetAsync` — upsert of that row. The per-user distributed lock of design #6 stays, **narrowed to
  `OpenTms.Platform.GridSettings:{tenantId}:{userId}:{gridId}`**: it serialises two tabs saving the
  same grid so the upsert cannot race into the unique index; different grids no longer wait on each
  other. The unique index is the hard guarantee if the lock is ever unavailable.
- `ResetAsync` — deletes the row; idempotent.
- The legacy migration below takes the per-user lock `OpenTms.Platform.GridSettings:{tenantId}:{userId}`.
  `SetAsync` and `ResetAsync` hold the grid lock while they migrate, so the locks nest grid → user,
  always in that order; `GetAsync` takes the user lock only. Both are `LocalAbpDistributedLock`
  today (Assumption 5).

**Tenant isolation.** The `{tenantId}:` key prefix of design #6 disappears: `IMultiTenant` plus the
ABP data filter scope every query, the row is written with the current tenant, and for a
database-per-tenant tenant it lands in that tenant's own database. Nothing disables the filter.

**The old blob is migrated lazily, per user, per tenant.** On the first `GetAsync`/`SetAsync`/
`ResetAsync` for which no row exists, the service reads the legacy setting value once; entries whose
prefix is the **current** tenant (or `host` in host context) are inserted as rows and removed from
the blob, and the blob is written back (set to null when empty). This runs under the per-user lock
of design #6. Entries of *other* tenants that share the same host database are left in the blob
for their own tenant context to migrate: the migration never writes a row for a tenant other than
the current one. The setting definition `OpenTms.Platform.Grid.Settings` stays registered in this
release (reading an undefined setting throws); removing it and deleting leftover `AbpSettings` rows
is the contract step of a later release (constitution rule 2). *Alternative, if preferred: discard
the blob — same code minus the migration path; users re-personalise once. No production tenant
exists yet, so either is safe; migrating is chosen because it costs ~30 lines and one test.*

Architecture decisions touched: none (AD register unchanged). Flow (`02-flows.md`): a plain
command on the TMS core; no event is published — the setting is private to one user and nothing
reacts to it.

## API contract

Unchanged: `GET`/`PUT`/`DELETE /api/platform/grid-settings/{gridId}`, `GridSettingsDto`,
`ColumnStateDto`, `SortModel`, the validation rules (gridId pattern, colId pattern, ≤ 50 column
states) and the status codes (200/404, 204, 204). `openapi/opentms-next.json` and the generated
client do not change.

## Migration strategy

One additive migration in the Platform context, `AddGridUserSettings`: creates
`platform."GridUserSettings"` and the unique index `IX_GridUserSettings_TenantId_UserId_GridId`.
Applied by `OpenTms.DbMigrator` to the host database and to every tenant database (the module
schema set is identical everywhere). Expand only; nothing is dropped or renamed. The old code keeps
working against the migrated schema (it does not know the new table). The contract step (remove the
setting definition, delete rows `Name = 'OpenTms.Platform.Grid.Settings'`) is a separate later PR.

## UI design

No screen changes: column widths, visibility and sorts persist again for users with five or more
grids. One behaviour is added, because the defect stayed invisible for months: **a failed save or
reset of the layout shows a toast** (UI-FB-005, *no silent failures*). Design #6 §Column
personalization said "failed save is logged (structured, not a toast)"; that line is superseded.

- `OpenTmsGrid` delegates the debounced `PUT` and the reset's `DELETE` to
  `createGridSettingsPersister(gridId, t)` in `src/components/ui/opentms-grid-settings.ts`, a plain
  module so the failure path is testable without AG Grid.
- openapi-fetch does not reject on an HTTP error: it resolves with `error` set and `response.ok`
  false. The old `void apiClient.PUT(...).catch(...)` therefore never saw the 500 — the persister
  checks `error` and `response.ok` and catches network failures.
- Failure: `toast.error(t("Grid:SettingsSaveFailed"), { id: "grid-settings:{gridId}",
  description: getApiErrorMessage(error, t) })` (reset: `Grid:SettingsResetFailed`). The stable
  sonner id per grid means a repeated failure updates the one toast instead of stacking: no spam
  while a user keeps resizing against a failing server. The structured console event stays. The
  local column state is kept: the user's current view is right, only persistence failed.
- New localization keys (nl, en, Platform resource): `Grid:SettingsSaveFailed`,
  `Grid:SettingsResetFailed`. Manual pages that describe **Kolommen** mention the message.

## Test risk analysis

Risk class **critical** (Platform; tenant isolation; the data filter now carries the isolation that
a string prefix used to carry).

| Test | Where | Why |
|---|---|---|
| **Reproduction:** one user saves five grids of four columns each and reads all five back | `PostgreSql/Grid/GridUserSettingsPostgreSqlTests` (real schema from the DbMigrator) | Fails on the old code with 22001; the regression guard for this defect. |
| Written under tenant A (own database) is invisible for the shared-tier tenant and in host context, and the reverse | same | Negative tenant-isolation test against real PostgreSQL, database-per-tenant included. |
| Saving the same grid twice leaves one row | same | The unique index and the upsert. |
| Roundtrip, several grids, reset, reset idempotent, reset leaves other grids, tenant A/B isolation, host/tenant isolation, all validation cases | `GridSettingsAppServiceIntegrationTests` (SQLite; existing suite, kept) | Contract of the app service unchanged. |
| Five grids × four columns (SQLite) | same, new | Runs in the default suite without a PostgreSQL server. |
| Legacy blob with entries for tenant A and tenant B: `GetAsync` in A migrates A's entries into rows, leaves B's in the blob; a second read no longer touches the setting; `ResetAsync` after migration does not resurrect the value | same, new | The migration path and its isolation. |
| Written by one user is invisible to another user of the same tenant, and the other user's save or reset does not touch it | PostgreSQL and SQLite | User scoping on every query. |
| Legacy blob in the host database with a host entry and a shared-tier tenant entry: each context migrates its own, the value is cleared when empty, no row reaches tenant A's database | `GridUserSettingsPostgreSqlTests` | The migration against the real setting store and database-per-tenant. |
| Corrupt legacy value is discarded once, not re-parsed on every call | SQLite | Robustness of the migration path. |
| Controller tests | `GridSettingsControllerTests` (existing) | Unchanged. |
| Frontend: PUT/DELETE success is quiet; an HTTP 500 (resolved, not rejected) and a network failure each show one toast with the API message, same toast id per grid | `opentms-grid-settings.test.ts` (vitest, mocked client and sonner) | UI-FB-005 failure path; risk class high (design #6: column personalization). |
| Frontend: the two localization keys exist in nl and en | `localization-keys.test.ts` | Low risk, texts. |
| Migration test | `PostgreSqlTestDatabases` runs the real DbMigrator on host + tenant database before the PostgreSQL tests | The obligation of the test-engineer matrix, already in place. |

**Performance at volume ×5.** Expected volume (design #6: 100 tenants × 50 users; now ~10 grids per
user) is 50 000 rows; ×5 is 250 000 rows. Every read and write is a single-row lookup on the unique
index, well inside the interactive-read (p95 < 300 ms) and mutation (p95 < 500 ms) budgets. No
dedicated load test is added: the endpoint has no list query and no paging surface. The SQLite
test module must create the Platform context's tables (it creates only `OpenTmsDbContext` tables
today) and switch that context to SQLite — a test-infrastructure change in the same PR.

## Flag & rollout plan

> **DEVIATION(constitution-4): defect fix in a storage mechanism; the only user-visible change is
> the failure toast that UI-FB-005 already requires of every mutation, and a flag-off path would
> keep the broken 2048-character blob and the silent failure alive, approved in
> docs/designs/grid-settings-storage.md** — Marinus confirms or overrules this in the PR.

Rollout is the DbMigrator run (additive migration) followed by the deploy. Rollback is a revision
revert: the old code ignores the new table and reads whatever is left in the blob.

## Cost & SLO impact

One row per user per grid: at expected volume ≈ 50 000 rows, a few MB spread over the tenant
databases. One indexed read per grid mount, one indexed upsert per debounced save (≥ 1 s apart per
grid) — fewer bytes than today's 2 KB blob rewrite. No new process, connection, job or external
call; Cloud Run and Cloud SQL sizing unchanged; per-tenant margin unaffected.

## Assumptions

1. ABP's default repositories are registered for the Platform context with `includeAllEntities:
   true` (verified in `PlatformEntityFrameworkCoreModule`), so `IRepository<GridUserSetting, Guid>`
   needs no registration.
2. The `OpenTmsEntityFrameworkCoreTestModule` (SQLite) can create the `PlatformDbContext` tables with
   `AbpUnitTestSqliteDatabase.CreateTables` and override the context's Npgsql configuration with
   `UseSqlite` — the Platform context has had no entity under test before.
3. No production tenant exists yet (deploy workflows arrive at department onboarding), so the lazy
   migration serves developer and health-tenant data; its correctness is still tested as critical.
4. `gridId` keeps its 100-character maximum and the DTO keeps its 50-column maximum; `SettingsJson`
   is `text`, so neither bounds the storage.
5. The distributed lock stays `LocalAbpDistributedLock` (in-process) until a multi-instance
   deployment replaces it, as design #6 states. It is not a database lock, so a connection pooler in
   transaction mode (`04-data-and-tenancy.md`) does not affect it.
6. openapi-fetch (the generated client's runtime) resolves with `error` on an HTTP error status and
   rejects only on a network failure; the persister handles both.

## Security quickscan

- Authentication unchanged: `[Authorize]` on the controller and the service; no permission is
  needed to manage one's own layout.
- Authorisation: every query filters on `CurrentUser.Id` and runs under the tenant filter; a user
  cannot read or write another user's rows, and a tenant cannot see another's.
- Input: `gridId` and `colId` validated by the existing patterns before use; the JSON is produced by
  the service from the typed DTO and deserialised back into it — a corrupt row is logged (ids only)
  and treated as absent, as the blob was.
- Privacy (rule 5): the entity holds a user id and layout preferences, no personal data. Logs carry
  tenant and user ids only. GDPR art. 15: the rows are part of a user's data on request; art. 17:
  erasing a user leaves preference rows with no identifying content — deleting them on user deletion
  (an `EntityDeletedEventData<IdentityUser>` handler) is a follow-up, as it is today for the ABP user
  setting rows.
