# Design: Maintenance view in the Administration app

**PBI:** none; built on the PO's direct instruction in a working session on 2026-10-11
("build Caches, Databases (host), Outbox, Health and Background jobs"). The PO's wording is
the design; this note records what was decided along the way so gate 2 can review against it.
**Status:** in review
**Date:** 2026-10-11

> **Platform change — risk class critical by default** (design §3.1): the view reads host-wide
> stores and acts on the process. Design and implementation travel in one PR because the PO
> asked for the build directly; gate 1 and gate 2 are the same review.
>
> **DEVIATION(constitution-4): the host-only tabs (Caches, Databases, Health, Background jobs)
> are gated by host-side permissions, not by a per-tenant feature flag, approved in this
> document (PO instruction 2026-10-11).** A feature value belongs to a tenant; the host has none
> to carry it, so a default-off flag would hide the host tabs forever. The same reasoning
> approved the Administration app shell in design #8. The tenant-side part of the view (the
> Outbox tab) **is** behind the per-tenant feature `Platform.Maintenance`, default off.

## Domain impact

**Module:** Platform (horizontal). No domain module changes; no domain entity; no migration.

The view is five tabs, each read through its own application service and one controller
(`MaintenanceAdministrationController`, route root `api/platform/administration/maintenance`):

| Tab | Side | Reads | Acts |
|---|---|---|---|
| Caches | host | the registered `IDistributedCache` through `ICacheMaintenance`: status, the named caches and every entry (key, size, stored, expiry) | clear all entries; remove one entry |
| Databases | host | the tenant store plus PostgreSQL's catalog (`pg_database`, `pg_stat_database`; per database on open: schemas, `pg_stat_user_tables`, the `AbpBlobs` table) through `IDatabaseStatisticsProvider` | none (clone and download are a later phase, per the PO) |
| Outbox | both | the outbox table of each listed database through `IOutboxInspector`, plus the in-memory `IOutboxDispatchRegistry` the dispatcher fills | run the dispatcher for one database now |
| Health | host | ASP.NET Core's `HealthCheckService` through `IHealthReportProvider`, plus process facts | none |
| Background jobs | host | Hangfire's monitoring API through `IBackgroundJobMonitor` | requeue and delete one job |

**Ports and adapters.** The module defines the four ports in `Platform.Domain/Maintenance`.
The PostgreSQL and EF Core adapters live in `Platform.EntityFrameworkCore`; the Hangfire and
health-check adapters live in the host (`OpenTms.HttpApi.Host/Maintenance`), so the module
stays free of the Hangfire and health-check packages (AD-13: Hangfire is the host's choice).

**The cache.** The module replaces the framework's in-memory `IDistributedCache` with
`OpenTmsMemoryDistributedCache`, the same thing with a count, a `Clear()` and a record per entry
(key, size, stored at, expiry), kept on `Set` and dropped by the eviction callback, because
`MemoryCache` exposes no per-entry metadata. ABP's key shape `c:{CacheName},k:{ItemKey}` is parsed
for the list; a key in another shape is listed under its whole key. It is per instance; the UI
says so. A distributed cache (proposal AP-13) registers its own
`IDistributedCache` after this module and brings its own `ICacheMaintenance`.

**The outbox.** `IOutboxInspector` is the one reader of the outbox table besides the
dispatcher; the Platform module owns the table (`03-events-and-outbox.md`), modules still never
touch it. The dispatcher now records each visit's outcome in `IOutboxDispatchRegistry`, so a
persistently failing event (open point AO-10) is visible to an administrator by name and id
instead of only in the log. This does not decide AO-10: nothing is dead-lettered.

**Why Background jobs is host-only.** Hangfire's storage is the `hangfire` schema of the host
database and carries no tenant filter. A tenant view would have to filter on serialized job
arguments, which is not the ABP data filter (constitution rule 1), so there is none. Jobs
that run for a tenant show that tenant's id.

**Why the Databases tab costs one connection per server, not per database.** The list reads
`pg_database` and `pg_stat_database` once per distinct server for every database on it
(`04-data-and-tenancy.md`: connections are the constraint; never loop over tenants inside a
request). The per-database figures (schemas, largest tables, blob store) need a connection to
that database and are read on demand for one database. The Outbox tab's pending counts also
need a connection per database; a page of the host list measures only the rows on that page,
so the cost is bounded by the page size.

## API contract

See `openapi/opentms-next.json` (regenerated). Endpoints:

- `GET maintenance/caches`, `GET maintenance/caches/names`, `GET maintenance/caches/entries` (grid plus
  `cacheName`), `POST maintenance/caches/clear`, `DELETE maintenance/caches/entries?key=` (the full key
  travels in the query string because it contains colons and commas)
- `GET maintenance/databases` (grid: startRow, endRow, wildcardSearch, sortBy, sortDir),
  `GET maintenance/databases/summary`, `GET maintenance/databases/{key}` (`host` or a tenant id)
- `GET maintenance/outbox/overview`, `GET maintenance/outbox` (grid), `POST maintenance/outbox/dispatch?tenantId=`
- `GET maintenance/health`
- `GET maintenance/background-jobs/overview`, `GET maintenance/background-jobs?state=&startRow=&endRow=`,
  `POST maintenance/background-jobs/{jobId}/requeue`, `DELETE maintenance/background-jobs/{jobId}`

Connection strings never leave the backend: a database row carries the database name and
`host:port` only.

**Permissions** (`PlatformPermissions.Administration.Maintenance`): `Default` (both sides),
`Caches` and `Caches.Clear` (host), `Databases` (host), `Outbox` and `Outbox.Dispatch` (both),
`Health` (host), `BackgroundJobs` and `BackgroundJobs.Manage` (host). Seeded to the `admin` role;
the host-only ones for the host admin only (ABP rejects a host-only permission inside a tenant).

**Feature:** `Platform.Maintenance`, default `false`, tenant side only.

## Migration strategy

Not applicable: no schema change. Hangfire's schema and the outbox table already exist.

## UI design

`/admin/maintenance`, nav entry **Maintenance** (wrench) in the Administration app. The view is
a tab strip with one page per tab (UI-PAGE-006): each page has its own `AppCommandBar` and
content; grids are `OpenTmsGrid` (`platform.maintenance.databases`, `platform.maintenance.outbox`,
`platform.maintenance.backgroundJobs`). A tab without its permission is not rendered
(UI-PERM-002); without any tab, or on the tenant side without the feature, the
`PermissionDenied` panel shows (UI-PAGE-005).

- **Caches:** cards (provider, entries, caches, total size, key prefix, instance), a warning that
  clearing is per instance, the entry grid (`platform.maintenance.cacheEntries`) with a named-cache
  filter in the toolbar, **Remove entry** as a row action (reversible: the item is read again on
  its next use, so no confirmation), the command **Clear caches** behind a `ConfirmDialog`.
- **Databases:** summary cards (databases, tenant databases, shared tenants, total size, host
  blob storage, unreachable), the grid, and `DatabaseDetailDialog` (editor dialog, read-only;
  tabs General, Schemas, Largest tables; the two tables are shadcn `Table`, small and fixed).
- **Outbox:** overview cards (dispatcher on/off, sweep period, batch size, instance), the
  registry notice, the grid; **Dispatch now** as a row action.
- **Health:** status cards, the checks table, the process card; **Refresh** re-runs the checks.
- **Background jobs:** cards per state (click to switch), servers and queues, a state filter in
  the grid toolbar, the grid; **Requeue** and **Delete** as row actions behind `ConfirmDialog`s.

Library additions: `BytesCell` and `PercentCell` (grid cells), `Formatter.bytes()`, `Table`,
`StatCards` (admin), `useIsHostSide()`.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Side and permission on every endpoint and service | Critical | controller policy tests; service tests throw `AbpAuthorizationException` inside a tenant; PostgreSQL tests for the outbox (tenant A sees and dispatches only its own; a shared-tier tenant neither) |
| Databases against real PostgreSQL | Critical | `DatabaseMaintenanceAppServiceTests` (host, tenant A, shared tenant, detail, not-found) |
| Outbox counts and dispatch | Critical | `OutboxMaintenanceAppServiceTests` (count before, dispatch, zero after, registry filled) |
| Cache replacement | High | `OpenTmsMemoryDistributedCacheTests`, `CacheMaintenanceAppServiceTests` |
| Health and jobs mapping | High | SQLite service tests over fakes; the Hangfire adapter is exercised by the running host only (no Hangfire storage in tests) |
| Frontend view, tabs, actions, hooks | Medium | `maintenance-view.test.tsx`, `administration-maintenance.test.ts`, format and config tests |

Idempotency: "dispatch now" is the dispatcher itself (at least once, handlers idempotent);
"clear caches", "requeue" and "delete" are idempotent by nature. Performance at volume: the list
endpoints page in memory over the tenant store (1,000 rows at most) and run one catalog query per
server; no volume figure was given, so no ×5 test was written.

## Flag & rollout plan

`Platform.Maintenance` (tenant side, default off) is flipped per tenant by the release manager.
Host tabs are live for host admins with the permissions on merge (deviation above).

## Cost & SLO impact

No new service, no new min instances, no egress. Each Databases page costs one catalog query per
PostgreSQL server; each Outbox page one short count per listed database; each detail open one
connection to that database. Health runs the registered checks (one database round trip).
Per-tenant margin unaffected.

## Assumptions

1. PostgreSQL 17 catalog columns (`pg_stat_database`, `pg_stat_user_tables`) as read by the
   provider; `AbpBlobs` lives in the `public` schema of every database (the application context
   has no default schema).
2. AP-13 (Redis) will replace the cache; until then the per-instance note stands.
3. Tenants without a connection string are shared tier (CLAUDE.md, multi-tenancy model).

## Security quickscan

No PII; connection strings and passwords never reach the API (the server column is host and
port). Every action is behind an ABP permission; host-only ones are host-side by definition and
guarded again in the service. Exception messages shown in the Outbox tab's last error and the
Background jobs tab come from the process's own exceptions, visible to host admins only.
