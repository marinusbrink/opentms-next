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
| Health | host | the **instance registry** (`platform."ApplicationInstances"` in the host database) plus this instance's live `HealthCheckService` result through `IHealthReportProvider` | none |
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

**The instance registry (PO decision 2026-10-11, in the working session: "instances should also
be able to inform that they are born or die").** Every process registers itself at start
(`ApplicationInstance`: role, machine, process id, version, environment, runtime, start time),
heartbeats every `InstanceRegistry:HeartbeatPeriod` (30 s) with its health result and memory, and
marks itself stopped on a graceful shutdown (`InstanceRegistryWorker`, an ABP periodic worker the
host starts; Cloud Run sends SIGTERM before it removes an autoscaled instance, which runs the
stop). A killed instance never says goodbye: its heartbeat goes silent and the view shows it as
**Unresponsive** after `InstanceRegistry:UnresponsiveAfter` (90 s, three heartbeats). Rows are
written in the **host database** whatever tenant is current: the entity is not `IMultiTenant`,
and ABP's `EfCoreRepository` routes such entities to the host connection string from any tenant
context (the registry also switches to the host context explicitly, so the intent is readable).
It is host-owned operations data, not a tenant entity, so no tenant-filter bypass is involved
(`04-data-and-tenancy.md`, host-owned rows); the same rule is why its view is host-only. The table exists in every database because the
Platform schema is shared; tenant databases keep it empty. Heartbeats delete rows past
`InstanceRegistry:Retention` (7 days). The entity is excluded from the functional logbook (a
heartbeat per instance per period is machine traffic). The role comes from configuration
(`InstanceRegistry:Role`, default Api) until the role split selects it (AD-11, AP-12). The health
entries of the last heartbeat are kept as `jsonb` (AD-06). This registry is the cross-instance
answer the Health tab needs under Cloud Run: the Api instance that answers a request can only
describe itself, so the fleet is read from the table, not from the process.

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
- `GET maintenance/health` (this instance, live), `GET maintenance/instances/overview`,
  `GET maintenance/instances` (grid), `GET maintenance/instances/{id}`
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

One additive migration in the Platform context (`AddApplicationInstances`): the table
`platform."ApplicationInstances"` with indexes on `LastHeartbeatAt` (list order, housekeeping) and
`Role`. Applied by the DbMigrator to the host and every tenant database (the schema is shared);
only the host database is written. Expand only; nothing is renamed or dropped.

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
- **Health:** cards (this instance's live status, running, unresponsive, stopped, heartbeat period),
  the instance grid (`platform.maintenance.instances`, running first, then unresponsive, then
  stopped) and `InstanceDetailDialog` (General: lifecycle and process; Health: the last heartbeat's
  entries). **Refresh** re-reads the list and re-runs this instance's checks.
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
| Instance registry: born, alive, dying in the host database whatever tenant is current; statuses; housekeeping; host-only view | Critical | `InstanceRegistryIntegrationTests` (PostgreSQL), `ApplicationInstanceTests` |
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

No new service, no new min instances, no egress. The registry adds one small UPDATE per instance
per heartbeat period (30 s) plus one health run and one DELETE by index on the host database: at
ten instances, twenty writes a minute. Each Databases page costs one catalog query per
PostgreSQL server; each Outbox page one short count per listed database; each detail open one
connection to that database. Health runs the registered checks (one database round trip).
Per-tenant margin unaffected.

## Assumptions

1. AP-12: the role reaches the registry through configuration (`InstanceRegistry:Role`); the role
   split's environment variable maps onto it.
2. Cloud Run delivers SIGTERM and allows the shutdown to finish; a hard kill is covered by the
   unresponsive rule, not by the stop.
3. PostgreSQL 17 catalog columns (`pg_stat_database`, `pg_stat_user_tables`) as read by the
   provider; `AbpBlobs` lives in the `public` schema of every database (the application context
   has no default schema).
4. AP-13 (Redis) will replace the cache; until then the per-instance note stands.
5. Tenants without a connection string are shared tier (CLAUDE.md, multi-tenancy model).

## Security quickscan

No PII; connection strings and passwords never reach the API (the server column is host and
port). Every action is behind an ABP permission; host-only ones are host-side by definition and
guarded again in the service. Exception messages shown in the Outbox tab's last error and the
Background jobs tab come from the process's own exceptions, visible to host admins only.
