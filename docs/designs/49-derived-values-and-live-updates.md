# PBI: Platform, derived values recalculated in the background and pushed live to the screen

<!-- Written on the PO's instruction of 2026-10-22: "bouw de infra voor kosten per leg, SignalR, etc. Doe dit
     generiek, want transport orders, transport trips en andere transport gerelateerde operations dienen gebruik te
     kunnen maken van hetzelfde mechanisme." No GitHub issue; "49" is the next free number after design 48. -->

## Story

As a planner I want a save to return at once while the cost, revenue and CO₂ that depend on it are recalculated in
the background, and I want every screen that shows the record to refresh those values by itself a moment later
and to show "calculating" until then, because a trip with four legs and eight stops must not make me wait, and I
must never act on stale numbers (flow 2, `02-flows.md`). As a developer of Orders, Planning & Execution and Financial
I want one mechanism for this, so the transport order, the transport trip and every later transport operation
plug in a calculator instead of building their own job, versioning and push.

## Affected area

Platform (horizontal): a derived-value scheduler, a per-target calculation state, a background job, a calculator
registry, a SignalR hub for live record updates, two read/maintenance endpoints under `/api/platform/derived-values`,
and in the frontend a realtime client in `src/lib/realtime/` plus a `CalculatingIndicator` in `src/components/ui/`.
No domain module changes; the first calculators arrive with the transport order and transport trip designs.

## Tenant dimension

Every state row, job and push is tenant-scoped. A job runs under the tenant that requested it, a hub group carries the
tenant id, a client can only join groups of its own tenant. No feature flag: infrastructure without user-visible
behaviour until a domain calculator exists; that design carries the flag (as the outbox, design outbox-per-tenant).

## Expected volumes

Per tenant, steady state: up to 5,000 recalculation requests a day (every trip or order save, rate change and actual
cost that arrives), peaks of 50 a minute at planning time; bulk re-runs of up to 10,000 targets after a retroactive
rate change. State rows: one per target that was ever calculated (≈ trips + orders; up to 500,000 per large tenant
after years). Open screens: up to 200 concurrent connections per tenant, each watching at most 200 records.

## Known edge cases

- Two saves of one trip in quick succession: the first job must not overwrite the second job's result.
- A save that rolls back after requesting: no job, no state change, no push.
- A calculator that throws: the old values stay, the state says Failed, Hangfire retries.
- The same event delivered twice (at least once): one effect.
- A client that reconnects after a network drop must re-join its groups and refetch.
- A user who watches an id of another tenant, or a record type they have no permission for.
- A target type without a registered calculator (a misconfigured module).

## Privacy note

No personal data: states, events, job arguments and push messages carry type names, ids, versions and statuses only.

---

# Design: Platform, derived values and live record updates

**PBI:** above (this file)
**Status:** approved (gate 1, PO instruction 2026-10-22: "bouw de infra … generiek"; the PR is the review of the
details below, Assumptions first)
**Date:** 2026-10-22
**Depends on:** the transactional outbox (design outbox-per-tenant, AD-16), Hangfire (AD-13).

> **Platform change — risk class critical by default.** Every module that stores a calculated value will run through
> this. Design and implementation travel in one PR, as the outbox did.

## Domain impact

**Module:** Platform (horizontal). No domain entity; one Platform table.

**Decisions relied on:** AD-01 (the core writes facts and publishes events), AD-07 (derived values are stored and
never calculated on read; a save returns at once; the result is pushed), AD-13 (Hangfire computes), AD-16 (the event
is written to the outbox with the fact), AD-18 (application services are the command layer). **Flow:** this *is* flow
2 of `02-flows.md` (A–D), made generic. **AD-08 and AO-11 are not touched:** this design stores no money and decides
nothing about rate lines or where allocated values live; it only runs a module's calculator and tells screens about
it. The cost-per-leg calculator itself, and whether its result is copied onto the leg or lives in a read model
(AO-11), belong to the trip and rate-line designs.

### The mechanism

```
A  module save ──► IDerivedValueScheduler.RequestAsync("PlanningExecution.TransportTrip", tripId, "CarrierChanged")
                    ├─ upsert DerivedValueState: RequestedVersion + 1, Status Pending      ┐ same transaction,
                    └─ publish DerivedValuesRequestedEto {type, [(id, version)]}  (outbox)   ┘ same database
B  outbox dispatcher ─► DerivedValuesRequestedHandler ─► Hangfire job DerivedValueCalculationJob {tenant, type, id, version}
C  job (tenant context, own transaction):
     state.RequestedVersion ≠ version or CalculatedVersion ≥ version ─► skip (a newer job will write)
     calculator[type].CalculateAsync(id, version) ─► writes stored columns in its own module tables
     complete: UPDATE state SET CalculatedVersion = version, Status = UpToDate WHERE RequestedVersion = version
       0 rows (a save moved on meanwhile) ─► roll back the whole transaction: the result is discarded
     publish DerivedValuesRecalculatedEto {type, id, version} (outbox) for other modules and agents
D  after commit ─► IRecordUpdateNotifier ─► SignalR group "<tenant>:<type>:<id>" ─► "recordUpdated" {type, id, version, status}
     screen ─► invalidates its TanStack queries for that record; the values appear, the indicator goes away
```

**Versioning without touching the module's entity.** The trip "carries a version number" (flow 2 C) through its
state row: `RequestedVersion` grows on every request, `CalculatedVersion` is the version whose result is stored.
The module's own aggregate needs no extra column. A screen shows "calculating" while `RequestedVersion >
CalculatedVersion` (status Pending) and "calculation failed" when the latest attempt failed.

**Why the result is discarded by a guarded update.** The request upserts the state row; the job's completing update
is guarded by `RequestedVersion = version`. PostgreSQL row locks order the two: a request committed before the job
completes makes the guard fail and the job rolls back; a request still in flight holds the row lock, so the guard waits
for it and then fails; a request after the job's commit simply starts the next job. The calculator's writes share the
job's transaction, so a discarded result never reaches the database.

**Failure.** A calculator exception rolls back its writes. In a new unit of work the state becomes Failed (only if the
version is still current), `FailedAttempts` + 1, `LastErrorCode` = the business-exception code or the exception type
name (never the message: it may carry data); the screen gets a push with status Failed; the exception is rethrown so
Hangfire retries (its default policy). A later success sets UpToDate and resets the counter. A failing target never
blocks other targets: one job per target, not an ordered queue. AO-10 (dead letters for events) is not depended on:
the event itself succeeds as soon as the job is enqueued.

### New Platform types

| Type | Project | Role |
|---|---|---|
| `DerivedValues.DerivedValuesRequestedEto`, `DerivedValuesRecalculatedEto` | Domain.Shared | request: `{ TargetType, Targets: [{ TargetId, Version }], Reason }` — one record for a save, up to 1,000 per event for a bulk request (so 10,000 records cost ten outbox rows); recalculated: `{ TargetType, TargetId, Version }`. Ids only (rule 5); `OpenTms.*` namespace, so the outbox carries them. Names follow AP-06 as far as a generic event can (Assumption 2). |
| `DerivedValues.DerivedValueStatus` | Domain.Shared | `Pending = 1, UpToDate = 2, Failed = 3` |
| `DerivedValues.DerivedValueConsts` | Domain.Shared | `MaxTargetTypeLength = 64`, `MaxReasonLength = 64`, `MaxErrorCodeLength = 128`, `MaxTargetsPerRequest = 10_000`, `MaxTargetsPerEvent = 1_000`, `MaxStatesPerRead = 500`, `MaxWatchPerCall = 200` |
| `DerivedValues.DerivedValueState` | Domain | aggregate: `TenantId`, `TargetType`, `TargetId`, `RequestedVersion` (long), `CalculatedVersion` (long), `Status`, `FailedAttempts`, `LastErrorCode`, `RequestedAt`, `CalculatedAt` |
| `DerivedValues.IDerivedValueCalculator` | Domain | implemented by a module: `Task CalculateAsync(DerivedValueCalculationContext context)`; the context carries `TargetType`, `TargetId`, `Version`, `CancellationToken` |
| `DerivedValues.DerivedValueOptions` | Domain | the registry: a module calls `Configure<DerivedValueOptions>(o => o.AddCalculator<TCalculator>("<Module>.<Entity>", watchPermission))`, or `AddWatchable(type, permission)` for a type that only sends change notices; a duplicate type, a type of the wrong shape or a missing permission fails start-up |
| `DerivedValues.IDerivedValueScheduler` / `DerivedValueScheduler` | Domain | `RequestAsync(type, id, reason)`, `RequestManyAsync(type, ids, reason)`; refuses an unknown type and a non-transactional unit of work (as the outbox); returns the new version(s) |
| `DerivedValues.IDerivedValueStateStore` | Domain | the atomic statements (upsert-increment, a single-statement bulk upsert over `unnest` arrays, guarded complete, guarded fail) plus reads; implemented in EntityFrameworkCore with parameterised SQL over the mapped table name |
| `DerivedValues.DerivedValueCalculationJob` | Application | the Hangfire job (ABP `AsyncBackgroundJob`); arguments: tenant id, type, id, version — nothing else (Hangfire stores arguments in plain text). In Application, as the other Platform jobs (the DbMigrator never runs jobs) |
| `DerivedValues.DerivedValuesRequestedHandler` | Application | outbox handler: enqueues one job per record whose state still asks for that version (idempotent: a duplicate or stale delivery enqueues nothing) |
| `RecordUpdates.RecordUpdateWatchPolicy` | Application | which groups a hub client may join (registered type, its watch permission, ≤ 200 ids, the caller's own tenant) |
| `RecordUpdates.IRecordUpdateNotifier` | Domain | `NotifyAsync(type, id, version, status)`; also usable without a calculator, for "this record was changed by someone else" (`status` null). Sends after the current unit of work commits; outside one, at once. |
| `RecordUpdates.RecordUpdatePermissions` | Domain | the watch permission per target type: from the calculator, or registered on its own by a module that only notifies |
| `RecordUpdatesHub`, `SignalRRecordUpdateNotifier` | HttpApi.Host | ABP `AbpHub` at `/signalr-hubs/record-updates` (`[Authorize]`); `Watch(type, ids[])`, `Unwatch(type, ids[])`; checks the type's watch permission; groups `<tenantId or "host">:<type>:<id>` |
| `DerivedValues.DerivedValueAppService` | Application | `GET /api/platform/derived-value/states`, `POST /api/platform/derived-value/recalculate` |

**Transport.** The hub lives in the API host (one image, roles later, AD-11). When `SignalR:Redis:Configuration` is
set, SignalR uses the Redis backplane, so a job on one instance reaches a browser connected to another (AP-13,
Assumption 3); without it (local dev, one instance) delivery is in-process. Today the job runs in the API host's
Hangfire server, so in-process delivery is correct until the Jobs role is split off; that split must set Redis.

**Authentication on the socket.** Browsers cannot send a bearer header on a WebSocket, so the SignalR client sends the
token as `access_token` in the query string; a small middleware copies it into the `Authorization` header **only for
`/signalr-hubs/*` and only when no header is present**, before authentication runs. The token is never logged:
request logging of that path drops the query string.

### How a module uses it (the transport order and trip designs)

1. Write a calculator in the module (`IDerivedValueCalculator`, e.g. `TransportTripCostCalculator`) that reads the
   facts and writes the stored values in the module's own tables.
2. Register it in the module's `ConfigureServices`:
   `Configure<DerivedValueOptions>(o => o.AddCalculator<TransportTripCostCalculator>("PlanningExecution.TransportTrip", PlanningExecutionPermissions.Trips.Default));`
3. In every application-service method whose save changes what the values depend on, call
   `await DerivedValueScheduler.RequestAsync("PlanningExecution.TransportTrip", trip.Id, "CarrierChanged")` (or
   `RequestManyAsync` for a retroactive change). Nothing else: version, outbox, job, retry and push are Platform's.
4. In the screen: `useRecordUpdates("PlanningExecution.TransportTrip", [tripId])` refetches the trip's queries when it
   was recalculated; `useDerivedValueStates(...)` plus `<CalculatingIndicator state={…} />` show "calculating".

## API contract

Conventional controller of `DerivedValueAppService` (route root `platform`):

| Operation | Route | Permission | Notes |
|---|---|---|---|
| States of a set of records | `GET /api/platform/derived-value/states?TargetType=&TargetIds=` | the type's watch permission | at most 500 ids; returns `DerivedValueStateDto[] { targetId, requestedVersion, calculatedVersion, status, lastErrorCode, calculatedAt }`; ids without a state are absent (never calculated) |
| Recalculate | `POST /api/platform/derived-value/recalculate` `{ targetType, targetIds[] }` | `Platform.DerivedValues.Recalculate` | at most 10,000 ids; returns the number requested; the bulk re-run after a retroactive change (flow 2) |

Hub contract (not in OpenAPI; typed in `src/lib/realtime/record-updates.ts`):

| Direction | Name | Payload |
|---|---|---|
| client → server | `Watch` / `Unwatch` | `(targetType: string, targetIds: string[])`; at most 200 ids per call; an unknown type or a missing permission fails the call (`HubException` with code `RecordUpdates:NotAllowed`) |
| server → client | `recordUpdated` | `{ targetType, targetId, version, status }` (`status` null for a plain change notice) |

Errors: `Platform:DerivedValues.UnknownTargetType`, `Platform:DerivedValues.TooManyTargets`,
`Platform:DerivedValues.RequiresTransaction`. OpenAPI and the typed client regenerated.

## Migration strategy

Expand only. Platform migration `AddDerivedValueStates`: table `platform."DerivedValueStates"` (`Id`, `TenantId`,
`TargetType varchar(64)`, `TargetId uuid`, `RequestedVersion bigint`, `CalculatedVersion bigint`, `Status int`,
`FailedAttempts int`, `LastErrorCode varchar(128)`, `RequestedAt`, `CalculatedAt`, `ExtraProperties`,
`ConcurrencyStamp`), unique `(TenantId, TargetType, TargetId)` `NULLS NOT DISTINCT`, index `(TenantId, TargetType,
Status)` for "what is still pending". No soft delete (a state is not a record a user deletes). Applied by the
DbMigrator in the host and every tenant database. `Down` drops the table. No contract step.

## UI design

No screen of its own. What the domain screens get:

- `src/lib/realtime/record-updates.ts`: one `HubConnection` per browser tab (`@microsoft/signalr`), started lazily on
  the first watch, automatic reconnect; on reconnect it re-sends every active `Watch` and invalidates the watched
  queries (anything may have changed while offline). Reference-counted, so two components watching one trip share one
  group.
- `useRecordUpdates(targetType, ids, onUpdate?)`: watches while mounted; on `recordUpdated` it invalidates the query
  keys the caller registered for that record (default: every query whose key contains the id) and calls `onUpdate`.
- `useDerivedValueStates(targetType, ids)`: TanStack query over `GET …/states`, refreshed by the push.
- `CalculatingIndicator` (`src/components/ui/calculating-indicator.tsx`): for a value or row whose state is Pending it
  renders the spinner icon with the localized "Calculating…" (`Platform:DerivedValues.Calculating`) as visible text on
  `md` and as the accessible name everywhere; Failed renders the warning icon with "Calculation failed"
  (`Platform:DerivedValues.Failed`) and the error code in the tooltip. Tokens only (UI-TOK-001); no layout shift (the
  indicator takes the width of the icon). Values under a Pending state stay visible but muted (`text-muted-foreground`),
  so a planner sees they are about to change (flow 2: "a visible calculating state").

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Request and fact commit together: a rolled-back save leaves no state change and no event | **Critical** | PostgreSQL integration test |
| Tenant isolation: a job runs under its tenant and writes only that tenant's database; states of tenant A are invisible to B; hub groups carry the tenant | **Critical** | PostgreSQL negative tests; hub group-name unit test |
| Superseded result discarded: request during calculation → guard fails → calculator's writes rolled back; the next job writes | **Critical** | PostgreSQL integration test with a probe calculator that blocks until a second request commits |
| At-least-once: duplicate event and duplicate job are one effect; stale job skips | **Critical** | PostgreSQL + SQLite tests |
| Failure: Failed state, attempts counted, old values kept, exception rethrown, later success resets | **High** | SQLite tests with a throwing probe calculator |
| Registry: duplicate or malformed target type fails start-up; unknown type refused by scheduler, states and hub | **High** | unit tests |
| Watch authorization: missing permission and unknown type refused; at most 200 ids | **Critical** | hub unit tests with substituted permission checker |
| Socket token middleware: only hub paths, never overrides a header | **High** | middleware unit tests |
| Endpoints: permissions, 500/10,000 limits | **High** | SQLite service tests, authorization architecture test |
| Frontend: watch/unwatch reference counting, re-watch on reconnect, invalidation, indicator states | **Medium** | vitest with a fake hub connection |
| Volume ×5: 25,000 requests in a day per tenant → one upsert + one outbox row per save; bulk re-runs of 10,000 records | **High** | PostgreSQL volume test: `RequestManyAsync` of 10,000 ids in one transaction < 2 s (measured 0.29 s: one upsert, ten outbox rows); 10,001 refused |

## Flag & rollout plan

No feature flag (infrastructure, no user-visible behaviour; see Tenant dimension). The first domain calculator's
design carries its flag. Rollout: migrate, then deploy. Rollback: no calculator registered = nothing happens; the
table stays. Multi-instance production needs `SignalR:Redis:Configuration` before the Jobs role is split (AD-11).

## Cost & SLO impact

- **Cloud SQL:** one row per calculated target (≈ 120 bytes; 500,000 rows ≈ 60 MB per large tenant), one outbox row
  per request (seconds), Hangfire job rows in the host database (Hangfire's own retention).
- **Save budget (p95 < 500 ms):** one upsert + one outbox insert, both in the existing transaction; measured
  sub-millisecond to a few ms.
- **Jobs:** one job per request; the calculation's own cost is the calculator's design. Duplicates skip after one
  indexed read.
- **SignalR:** one WebSocket per open tab; messages are ≈ 100 bytes. Redis (when configured): Memorystore basic tier,
  shared with the later cache and lock (AP-13).
- **Egress / external calls:** none. Per-tenant margin: unaffected.

## Assumptions

1. **The state row carries the version** (flow 2 C), not a column on the module's aggregate. A module may still keep its
   own version; the mechanism does not need it.
2. **Generic event names.** `DerivedValuesRequestedEto` and `DerivedValuesRecalculatedEto` carry the target type, so
   one handler serves every module. AP-06 (`OpenTms.<Domain>.<FactInPastTense>`) is PROPOSED; a module that wants
   "trip recalculated" as its own event publishes it from its calculator.
3. **AP-13 (Redis as SignalR backplane) is PROPOSED;** this design makes it optional configuration, off by default. The
   lock and outbox signal stay in-process (unchanged).
4. The push goes from the job straight to the hub after commit (`IRecordUpdateNotifier`), not through the outbox:
   a lost push only delays the screen until its next fetch, which the reconnect refetch covers.
5. A user may watch any record of their tenant whose type's watch permission they hold; the push reveals only that a
   version changed, never data. Record-level visibility (organisation units) is checked when the data is read, not on
   the push.
6. "Kosten per leg" itself is not built here: no rate line, no allocation, no stored cost column (AD-08, AO-11).

## Security quickscan

- **Permissions:** `Platform.DerivedValues.Recalculate` (tenant and host); the states endpoint and the hub check the
  target type's watch permission; the hub is `[Authorize]`.
- **Input validation:** target type must be registered; ids bounded (200 / 500 / 10,000); reason ≤ 64 chars.
- **Attack surface:** one hub, two endpoints, all authenticated. The query-string token is accepted only on hub paths
  (standard SignalR practice) and not logged.
- **Tenant isolation:** state rows `IMultiTenant`; the job switches to the stored tenant explicitly
  (`ICurrentTenant.Change`, visible at the call site, as the outbox dispatcher); groups are prefixed with the tenant.
- **SQL:** two parameterised statements (upsert-increment, guarded update) on the Platform's own mapped table; no
  string concatenation of input.
- **Logging:** type, id, version, tenant id; no payloads, no exception messages in the state.
