# Design: Transactional outbox per tenant (AD-16)

**PBI:** none — built on the PO's direct instruction from the solution architecture
(`03-events-and-outbox.md`, decision AD-16) on 2026-10-10; no issue exists for it
**Status:** in review
**Date:** 2026-10-10

<!-- All nine sections below are mandatory (design §4.2). "Not applicable" is an
     acceptable answer only with a one-line reason; a missing section is not.
     Gate 1 approves this document by merging the PR — after that, implementers
     build exactly this, so vagueness here becomes iteration later. -->

> **Platform change — risk class critical by default** (design §3.1): every module will publish
> through this. The design and the implementation travel in one PR because the PO asked for the
> build directly; gate 1 and gate 2 are the same review.

## Domain impact

**Module:** Platform (horizontal). No domain module changes; no domain entity is added.

**What it does.** A module publishes an event with `IDistributedEventBus.PublishAsync(eto)` from
an application-service method that changes data. The event is stored in the table
`platform."AbpEventOutbox"` of **the same database as the fact, in the same transaction**: the
tenant's own database for a database-per-tenant tenant, the host database for a shared-tier
tenant or for host-level work. A dispatcher publishes the stored events afterwards, **at least
once**, with handlers running in the context of the tenant the event was raised for, and then
deletes them. Delivery is still in-process today; the event spine (AD-14) later replaces the
publish step without touching publishers or the table.

**Two buses, two guarantees** (this replaces "local event bus only" in rule 2 of `/CLAUDE.md`,
open point AO-03 — the PO decides it by merging this PR):

| Bus | Guarantee | Use it for |
|---|---|---|
| `IDistributedEventBus` | Written to the outbox in the same transaction and database as the fact. Published afterwards, at least once, in the tenant's context. | Every event another module, another runtime role or an agent reacts to. |
| `ILocalEventBus` | In-process, inside the current transaction. Nothing survives a crash. | A reaction that belongs to the same transaction. |

**New Platform types** (all in `OpenTms.Platform`):

| Type | Project | Role |
|---|---|---|
| `Events.OutboxEvents` | Domain.Shared | The selector: only types in an `OpenTms.*` namespace use the outbox; ABP's own events (identity, tenant management, entity-change ETOs) keep direct in-process delivery. Also the extra-property key for the tenant stamp. |
| `Events.Outbox.OpenTmsDistributedEventBus` | Domain | ABP's `LocalDistributedEventBus` plus one rule: **publishing an outbox event outside a transactional unit of work throws** `OutboxRequiresTransactionException`. Also logs a warning when a stored event has no subscribed handler type (ABP drops those silently). |
| `Events.Outbox.OutboxDispatcher` | Domain | Visits one database (host or one tenant), publishes what is waiting oldest-first, deletes each event after its handlers succeeded. Takes an in-process lock per **database** (hash of the resolved connection string), so two shared-tier tenants never drain the host table concurrently. Stops at the first failing event to preserve order within that database. |
| `Events.Outbox.OutboxDispatchWorker` | Domain | Background worker: dispatches the tenant that just committed (signal) and sweeps every database every `Outbox:SweepPeriod` (default 5 min) as a safety net, plus once at start-up. Registered by the host only when `Outbox:DispatchEnabled` is true. |
| `Events.Outbox.IOutboxSignal` / `OutboxSignal` | Domain | In-process channel from "unit of work committed" to the worker; a burst of commits for one tenant collapses into one visit. |
| `Events.Outbox.OutboxOptions` | Domain | `DispatchEnabled` (true), `SweepPeriod` (00:05:00), `BatchSize` (100); bound from the `Outbox` configuration section. |
| `EntityFrameworkCore.PlatformEventOutbox` | EntityFrameworkCore | ABP's `DbContextEventOutbox<PlatformDbContext>` plus: stamps the raising tenant onto the row (`OpenTms.TenantId` extra property) and registers the commit signal. The DbContext is resolved per call, so the row lands in the current tenant's database. |
| `PlatformDbContext : IHasEventOutbox` | EntityFrameworkCore | Maps `OutgoingEventRecord` to `platform."AbpEventOutbox"` (ABP's `ConfigureEventOutbox()` under the module's default schema). |

**Why the tenant stamp.** A shared-tier tenant has no database of its own; its rows sit in the
host table next to host-level events. Without the stamp the host sweep would run that tenant's
handlers in the host context — a tenant-isolation bug (constitution rule 1). With it, the
dispatcher switches `ICurrentTenant` per event.

**Why ABP's own outbox sender is switched off** (`IsSendingEnabled = false`, never to be turned
on again): it resolves the outbox DbContext in the host context only, so tenant databases would
never be drained.

**Events between domains:** none added. This is the mechanism; the first domain events arrive with
their own designs (`03-events-and-outbox.md`, starter list, open point AO-01).

## API contract

Not applicable — no endpoint, DTO or controller changes; `openapi/opentms-next.json` is unchanged.

## Migration strategy

**Expand only.** Platform migration `AddEventOutbox` creates `platform."AbpEventOutbox"`
(`Id uuid PK`, `EventName varchar(256)`, `EventData bytea`, `CreationTime timestamp`,
`ExtraProperties text`, index on `CreationTime`) in the host
database and in every tenant database. Applied by `OpenTms.DbMigrator` only (never at startup).

**Contract step:** none — nothing is renamed or removed.

**Rollout order:** migrate first, deploy second. A host revision without this code tolerates the
extra table; a revision with this code against a database without the table fails on the first
publish, which is why the migrator runs before the deploy, as always.

## UI design

Not applicable — no user-visible behavior. (The browser is told about results later through
SignalR, AD-07; that is a separate design.)

## Test risk analysis

| Part | Risk class | Coverage (all delivered in this PR) |
|---|---|---|
| Event stored in the **tenant's own database**, not the host's; handlers run in **that tenant's** context; shared-tier tenant's event in the host database dispatched under its own tenant id; dispatching tenant A never touches host or other-tenant rows (negative isolation) | **critical** | PostgreSQL integration tests `OutboxPerTenantIntegrationTests` against real databases created by the real DbMigrator service (host + `OpenTms_Tests_TenantA` + a shared-tier tenant) |
| Fact and event commit together: a unit of work that fails after publishing leaves no row and sends no signal | **critical** | same |
| Publishing outside a transaction throws (no UoW, non-transactional UoW) | **critical** | same |
| At-least-once: failing handler keeps the row; next run delivers; no double delivery on a second run | **critical** | same |
| Non-`OpenTms.*` events bypass the outbox | high | same |
| Batch paging through a backlog | high | same (2 × batch + 3 events) |
| Namespace selector, signal collapsing, waiting/time-out | low | unit tests in `OpenTms.Domain.Tests` |
| Worker loop (sweep timing, stop) | medium | **not covered by an automated test**; verified manually against the running host (see PR description). The loop is small and defensive; a timing test would be flaky. |

**Performance at volume × 5** (test-engineer rule): the expected volume below is zero today, so no
load test is meaningful yet. The first domain design that publishes an event states its volume and
inherits the obligation.

**Test infrastructure added:** `OpenTmsPostgreSqlTestModule` — the production EF Core stack
(Npgsql, real transactions, database per tenant) against `localhost:5432`, overridable via
`OPENTMS_TEST_PG`. CI already runs a `postgres:17` service for the backend job, so these tests run
there as they run locally. SQLite tests bypass the outbox (`Outboxes.Clear()`), as the
architecture says: they run without transactions, so the guarantee cannot hold there.

## Flag & rollout plan

**No feature flag — stated deviation, not an omission.** Constitution rule 4 covers user-visible
behavior; the outbox is infrastructure with no user-visible effect until the first event exists.
The per-instance switch is `Outbox:DispatchEnabled` (default true), which exists for the role
split (AD-11): after it, only the Jobs role dispatches. Existing tenant data: untouched; the new
table starts empty everywhere.

## Cost & SLO impact

- **Cloud SQL:** one small table per database; rows live seconds to minutes. Negligible storage.
- **Connections:** the sweep opens every tenant database once per `SweepPeriod` (5 min), one after
  another, with a short non-transactional read. At 1,000 tenants that is ~3.3 connections per
  second on average from one instance — within budget, but it is the reason the sweep is a
  safety net and the signal is the normal path. The signal path costs one extra connection to the
  committing tenant's database, right after the commit.
- **Mutation budget (p95 < 500 ms):** publishing adds one `INSERT` to the same transaction.
  Measured impact is sub-millisecond; no budget row changes.
- **Cloud Run min instances / egress / external calls:** none.
- **Per-tenant margin:** unaffected.

**Known limits, not fixed here** (open points AO-07, AO-10 in `decisions.md`; they do not change
the table or how modules publish): sweep and signals share one loop; databases are visited
sequentially; one event at a time; the signal and the lock are in-process (the Jobs role needs
Redis, AP-13); a persistently failing event blocks the events behind it in that database — there
is no dead-letter store.

## Assumptions

1. **AO-03 is decided by merging this PR:** rule 2 of `/CLAUDE.md` becomes "distributed events
   through the outbox, or the local event bus for same-transaction reactions"; the PR edits
   `/CLAUDE.md` and `backend/modules/README.md` accordingly. If the PO wants the rule change in a
   separate step, revert those two files and keep the code.
2. "Publishing outside a transaction throws" is enforced at publish time on the unit of work's
   options (`IsTransactional`), not by probing an open database transaction. An application-service
   method that changes data is transactional by ABP convention; `GET`-named methods are not and
   must not publish.
3. The tenant that a shared-tier event belongs to is taken from `ICurrentTenant` at publish time
   and stored on the row; rows without a stamp (none exist yet) would be dispatched in the context
   of the database being visited.
4. An event whose type has no subscribed handler in the dispatching process is dropped and deleted
   (ABP behavior), with a warning logged. Acceptable while every event has a consumer by design
   ("do not emit everything"); the event spine will change this.
5. The dispatcher's lock is keyed on the resolved connection string, so two tenants pointing at
   one database are treated as one outbox. Tenants spread over several PostgreSQL servers (AP-09)
   need no change.
6. CI's `postgres:17` service is available to `dotnet test` (it is: services start before steps).
7. The PO accepts no feature flag for this infrastructure change (Flag & rollout plan).

## Security quickscan

- **Permissions:** none; no endpoint. The table is written and read only by the Platform module
  through ABP's store; "never read or write the outbox table yourself" stands.
- **Input validation:** none new; payloads are our own serialized ETOs.
- **Attack surface:** unchanged. The dispatcher is a background worker; nothing is exposed.
- **Personal data:** the table stores event payloads. The rule for payloads (`03-events-and-outbox.md`,
  constitution rule 5) applies to every event design: ids, not copies of names, addresses or
  licence plates. Rows are deleted on delivery, so no retention question arises here; an event
  design that must carry personal data answers art. 15/17 itself.
- **Logging:** event name, event id and tenant id only — no payload, no PII.
