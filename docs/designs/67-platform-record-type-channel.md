# Design 67: Platform — record-type channel on the record-updates hub

**PBI:** design 66 (Planboard run), PO decision D4.
**Status:** approved (PO 2026-10-31, D4). Risk class **critical** (Platform change).
**Date:** 2026-10-31
**Extends:** design 49 (`/signalr-hubs/record-updates`, `IRecordUpdateNotifier`, `RecordUpdateWatchPolicy`,
`src/lib/realtime/`).

## Problem

Design 49 lets a screen watch up to 200 known record ids and pushes only when a calculation job finishes. A planboard
pane needs to hear about rows it does not know yet (a trip another planner created, a leg that became unplanned, a
trip moved into my Scheduler's window) and about plain changes that run no calculation.

## Domain impact

**Module:** Platform (`RecordUpdates/`), the API host's hub. No tables, no migration.

1. **Watchable types without a calculator.** `DerivedValueOptions.AddWatchable(targetType, watchPermission)` registers a
   type for watching only (no calculator). `AddCalculator` keeps registering both. `Find` returns either.
2. **Routing hints.** `RecordUpdate` gains `RecordUpdateHints? Hints`:
   `record RecordUpdateHints(DateOnly? From, DateOnly? Till, Guid? OrganizationUnitId, IReadOnlyList<Guid>? ResourceIds, bool Removed)`.
   Ids and dates only, never data (rule 5). `IRecordUpdateNotifier.NotifyAsync` gets an overload with hints; the
   existing signature is unchanged.
3. **Type group.** Every notice goes to the record's group (unchanged) **and** to the type group
   `"<tenant N or host>:<type>:*"` (`RecordUpdate.TypeGroupName`). The tenant part always comes from `ICurrentTenant` on
   the server, never from the client.
4. **Hub.** `RecordUpdatesHub.WatchType(string targetType)` / `UnwatchType(string targetType)`. Policy:
   `RecordUpdateWatchPolicy.ResolveTypeGroupAsync(targetType)` returns the type group when the type is registered and the
   caller holds its watch permission, otherwise null → `HubException("RecordUpdates:NotAllowed")`.
5. **Message.** `recordUpdated` keeps its shape and adds `hints: { from, till, organizationUnitId, resourceIds, removed }`
   (null when none).
6. **Coalescing.** Notices raised in one unit of work for the same record are sent once (the last one wins), so a
   command touching 50 legs sends at most one notice per leg.

## Frontend (`src/lib/realtime/`)

- `RecordUpdatesClient.watchType(targetType)` (reference-counted, re-sent after a reconnect like `watch`).
- `useRecordTypeUpdates(targetType, onUpdate, enabled)` hook: subscribes to the type and calls `onUpdate(message)` for
  every message of that type; on reconnect it calls `onReconnect` so the pane refetches.
- Messages keep arriving through the existing single connection.

## API contract

No REST change. Hub methods `WatchType`, `UnwatchType` added.

## Test risk analysis (critical)

- Unit: policy refuses an unregistered type and a type without permission; type group name carries the server tenant
  and ignores any client value; coalescing sends one notice per record per unit of work; hints serialise as ids/dates.
- Negative tenant isolation: a notice raised in tenant A is sent only to A's type group (transport fake records groups).
- Frontend vitest: `watchType` reference counting, re-watch after reconnect, hook filters by type.

## Flag & rollout plan

No flag: an additive hub method. Clients that never call `WatchType` see no change.

## Cost & SLO impact

One extra group send per notice, in process (one instance, D5). With AP-13 later the Redis backplane carries it.

## Security quickscan

The tenant comes from the server; permission per type; ids only; no new anonymous surface.

## Assumptions

1. Fan-out per tenant and type is cheap at planboard scale (tens of connected planners per tenant).
