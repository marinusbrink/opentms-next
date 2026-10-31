# Design 68: Planning & Execution — Planboard reads and commands, availability, route geometry, stop optimisation

**PBI:** design 66 (Planboard run).
**Status:** approved (PO 2026-10-31: D1–D4, D7–D12, D16, D18–D20 and the appendix questions taken with their
recommendation).
**Date:** 2026-10-31
**Depends on:** designs 49, 53, 59, 60, 61; 67 (record-type channel), 69 (`IMasterDataResourceQuery`).
**Input:** `docs/input/planboard-assessment/scheduler.md` (S-01…S-42), `panes.md` (P-01…P-52), `map-live-layout.md`
(L-01…L-22).

## Domain impact

**Module:** Planning & Execution, schema `planning`. New folders `Planboard/`, `Availability/`. Migration
`PlanningPlanboard` (expand only).

### 1. Trip vehicles from a combination (D12)

`TransportTrip.SetVehicles(combinationId, vehicleIds, combinationMemberIds)`: when a combination is chosen, its active
members (Master Data `GetVehicleCombinationsAsync`) are stored as `TripVehicle` rows in member order with
`FromCombination = true`; `VehicleCombinationId` stays for display. A later edit of the combination in Master Data does
not change existing trips (the trip's vehicles are Planning's fact, design 48 assumption 1). The existing conflict rule
(combination **and** loose vehicles) is unchanged. The data seed/migration fills the members for existing trips that
have a combination and no vehicle rows (done by the migration's companion `PlanningPlanboardBackfill` step in the
DbMigrator seed contributor, idempotent).

Indexes: `TripVehicles (TenantId, VehicleId)`, `TripDrivers (TenantId, DriverId)`, `TransportTrips (TenantId,
PlannedDepartureUtc)` and `(TenantId, PlannedArrivalUtc)` where missing.

### 2. Resource availability (D9, D10)

`ResourceUnavailability` (aggregate, `FullAuditedAggregateRoot<Guid>`, `IMultiTenant`):

| Field | Type | Rule |
|---|---|---|
| `ResourceKind` | enum `{ Vehicle = 1, Driver = 2 }` | |
| `ResourceId` | Guid (Master Data id, no FK) | |
| `From`, `Till` | DateTimeOffset | `Till > From`, max 366 days |
| `Reason` | enum `{ Leave = 1, Sick = 2, Training = 3, Maintenance = 4, Inspection = 5, Other = 99 }` | |
| `Remarks` | string(512)? | |
| `RecurrenceWeekdays` | int? (flags Mon=1…Sun=64) | weekly repetition of the From–Till time of day |
| `RecurrenceUntil` | DateOnly? | required with `RecurrenceWeekdays` |

`ResourceWorkingPattern` (one row per resource and weekday): `ResourceKind`, `ResourceId`, `Weekday` (0–6), `Start`,
`End` (TimeOnly, `End > Start`), unique `(TenantId, ResourceKind, ResourceId, Weekday)`. No row for a weekday = no
working-time constraint that day.

Indexes `(TenantId, ResourceKind, ResourceId, From)` and `(TenantId, From, Till)`.

`AvailabilityExpander` (domain service): expands recurrences into concrete blocks for a window; the inverse of the
working pattern produces "outside working time" blocks.

### 3. Warnings (D10)

New `TripWarningCode` values, written by `TripCalculator` (never on read):

- `ResourceDoubleBooked = 20` (`Data` = vehicle or driver id): another non-cancelled, non-archived trip uses the same
  vehicle or driver and the planned departure–arrival intervals overlap.
- `ResourceUnavailable = 21` (`Data` = resource id): a vehicle or driver of the trip has an unavailability block that
  overlaps the trip.
- `OutsideWorkingTime = 22` (`Data` = driver id): the trip interval is not inside the driver's working pattern.

A change to unavailability requests recalculation of the trips it overlaps. A trip whose time or resources change
requests recalculation of the trips it overlapped before and after (so the other side of a double booking updates too).

### 4. Route geometry (D3)

`TripCalculator` asks `IRoutingService.GetMatrixAsync` with geometry and toll. Stored on the trip (derived values):
`RouteSegments` (jsonb list, one per consecutive stop pair: `FromStopId`, `ToStopId`, `EncodedPolyline`, `DistanceM`,
`DurationS`, `TollDistanceM`, `TollAmount`), `TollAmount` decimal(18,2)?, `TollCurrencyCode` string(3)?, `TollDistanceM`
int?, `RouteProvider`, `RoutedAt`. When routing is not configured they stay null and the map draws straight lines.

### 5. Stop optimisation (D20)

`IRouteOptimizer` (Integrations, published, in-process, `Routing/IRouteOptimizer.cs`):
`Task<RouteOptimizationResult?> OptimizeAsync(RouteOptimizationRequest request)` with the start/end places, the stops
(id, place, window, service minutes) and precedence pairs (pickup before delivery of the same leg). Adapter: VROOM over
HTTP when `Routing:Vroom:BaseUrl` is configured; otherwise a built-in heuristic (nearest neighbour then 2-opt, honouring
precedence and the first/last stop rule) on the OSRM matrix, or on great-circle distance when routing is off.

`POST /api/planning/planboard/trips/{id}/optimize-stops` checks status and permission, enqueues a Hangfire job in the
tenant's context and returns 202 at once. The job reorders the stops (`MoveStop`), requests the trip calculation and
notifies. Manual stops stay where the first/last stop rule puts them.

### 6. Narrow Planboard commands (D19, D11, S:Q-15)

All in `PlanboardAppService` (`/api/planning/planboard`), each one unit of work, each returning
`PlanboardChangeDto { Id, ConcurrencyStamp, LastModificationTime }` (plus the new id for creates). Every command takes
`ConcurrencyStamp` and rejects a stale one (409). Every command requests the trip calculation and notifies the trip (and
the legs it touched) on the record channel with hints (design 67).

| Command | Route | Rule |
|---|---|---|
| Reschedule | `POST trips/{id}/reschedule` `{ plannedDeparture, plannedArrival?, concurrencyStamp }` | moves the trip; arrival keeps its distance to departure when omitted; Planned/InProgress only (no moving a completed or cancelled trip — the legacy drag of completed trips, scheduler.md §9, is not cloned); recomputes stop planned times by the same delta |
| Change resource | `POST trips/{id}/resource` `{ mode: Tractor\|Trailer\|Driver, fromResourceId?, toResourceId, concurrencyStamp }` | Tractor: replaces position 1. Trailer: replaces `fromResourceId`'s position, or takes the first free trailer position. Driver: replaces `fromResourceId`'s position, or adds the next position (max 3). **D11:** a tractor change fills only empty slots — trailers from the tractor's standing combination (none in Master Data today → skip), drivers from the tractor's default drivers, carrier from the vehicle owner when the trip has none; it never overwrites what is set |
| Add driver | `POST trips/{id}/drivers/add` `{ driverId, concurrencyStamp }` | Resources pane drop (S:Q-15): next position, max 3; duplicate = no-op |
| Update header field | `POST trips/{id}/field` `{ field: Name\|Remarks\|DriverInstructions, value, concurrencyStamp }` | inline edits of the trips pane |
| Create trip | `POST trips` `{ transportTripTypeId?, mode?, resourceId?, plannedDeparture, legIds? }` | from an empty Scheduler cell or a leg drop on a row: the trip type (default: the tenant's first active type) fills empty groups (design 61 "Trip type defaults"), then the resource goes in its mode's slot; legs assigned in the same unit of work |
| Assign legs | `POST trips/{id}/legs` `{ legIds, concurrencyStamp }` | reuses `LegAssignmentService` |
| Unassign legs | `POST trips/{id}/legs/remove` `{ legIds, concurrencyStamp }` | |
| Complete | `POST trips/{id}/complete` `{ actualDeparture, actualArrival, concurrencyStamp }` | P:Q17: stamps both; fills only missing stop actuals |

The existing `TransportTripAppService` commands stay as they are (the Transport app's editor uses them) and also notify
on the record channel after commit.

### 7. Legs without a trip (P:Q11)

`LegAppService`: `POST /api/planning/leg/{id}/split-line` and `/return-leg` for an unplanned leg (same rules as on a
trip, without the trip checks).

## API contract — reads

All reads are SQL projections, keyset or offset paging ≤ 200 rows, nothing calculated.

| Route | Returns |
|---|---|
| `GET /api/planning/planboard/scheduler` `mode, from, to (≤ 14 days), organizationUnitIds[], search, skip, take (≤ 200)` | `rows[]` (resource id, name, plate or code, kind, type name, default driver ids) from design 69, `bars[]` (trip id, number, name, status, execution, planned departure/arrival, actual departure/arrival, vehicle ids by position, driver ids by position, carrier relation id, first/last stop city, stop count, load factor, warning codes, concurrency stamp) of every trip using a row's resource in the mode and overlapping the window, `blocks[]` (unavailability and outside-working-time blocks of the rows in the window), `names` (id → display name for every vehicle, driver and carrier on the bars, one batched lookup), `kpis` (rows, trips, in progress, rows without a trip, utilisation % = planned hours / (rows × window hours)), `totalRows` |
| `GET /api/planning/planboard/scheduler/bars?ids=` | the same bar shape for given trip ids (live-update refetch, ≤ 200) |
| `GET /api/planning/planboard/trips/{id}/map` | stops (id, position, name, city, lat/lon, waypoint type id, planned/estimated/actual arrival and departure, window from/till of its legs, lateness = estimated arrival after window till, minutes), `RouteSegments`, toll totals, distance, duration, CO₂, status, version |
| `GET /api/planning/planboard/map/trips` `from, to, organizationUnitIds[]` (≤ 500 trips) | per trip: id, number, status, first and last stop lat/lon, stop count, warning count (the "all trips" layer, D16) |
| `GET /api/planning/planboard/map/unplanned-near` `tripId, radiusKm (≤ 100), from, to` (≤ 200 legs) | unplanned legs whose origin or destination lies within the radius (bounding box in SQL, then haversine) of any stop of the trip |
| `GET /api/planning/resource-availability` `resourceKind, resourceIds[] (≤ 500), from, to` | blocks expanded for the window + patterns |
| `POST/PUT/DELETE /api/planning/resource-availability[/{id}]`, `PUT /api/planning/resource-availability/patterns/{kind}/{resourceId}` | CRUD |
| `GET /api/planning/planboard/resources/drivers` `from, to, search, skip, take` | drivers (design 69) with their blocks and the ids of their trips in the window |

Existing lists used by the panes, extended:
- `TransportTripAppService.GetList`: filter `organizationUnitIds[]`, `executionFrom/To`; `?ids=` for live refetch.
- `LegAppService.GetList` (unplanned view): server-side paging (already `GridRequest`), filter `organizationUnitIds[]`
  (via the order's OU snapshot if present, else none), grouping (`groupBy` ∈ origin date, customer, origin city,
  destination city → group rows with counts; D18), `?ids=` for refetch.

## Features and permissions (D7)

`AppFeatures.PlanboardApp.{Trips, Scheduler, Orders, Stops, Legs, LegsToSchedule, Map, Resources}` (Platform, default
ON). `PlanboardAppService` requires `Apps.Planboard`; `TransportTripAppService` and `LegAppService` accept
`Apps.Transport.Trips` **or** `Apps.Planboard` (`RequiresAll = false`). Permissions reuse `TransportTrips.*` and
`Legs.*`; new `PlanningExecution.ResourceAvailability` (`.Manage`).

## Live updates (design 67)

`AddWatchable("PlanningExecution.ResourceAvailability", …)`. After every committed Planning command:
`PlanningExecution.TransportTrip` with hints (trip date range from planned departure/arrival, OU, resource ids =
vehicle and driver ids before and after, `Removed` on delete/archive), `PlanningExecution.Leg` with hints (origin date,
`Removed` when it left the unplanned list). `OrderLegsHandler` notifies the legs it created, changed or deleted.

## Migration strategy

Expand only, `PlanningPlanboard`: `planning."ResourceUnavailabilities"`, `"ResourceWorkingPatterns"`, new trip columns
(`RouteSegments` jsonb, toll columns, `RouteProvider`, `RoutedAt`), `TripVehicles.FromCombination` bool default false,
the indexes above.

## Test risk analysis (high)

- Scheduler read: rows per mode (tractor = Tractor/Rigid/Van, trailer = Trailer, driver), bars overlap logic at window
  edges, a trip in two trailer rows, combination members, KPIs, names batched (one lookup call), ≤ 200 rows, negative
  tenant isolation.
- Commands: reschedule shifts stop times; completed trip refuses; stale stamp 409; tractor change fills only empty
  slots; driver add max 3; create from empty cell with type defaults; legs assigned in the same unit of work.
- Availability: CRUD validation, recurrence expansion across DST, working-pattern inverse, warnings written by the
  calculator, recalculation of overlapping trips.
- Geometry stored by the calculator; map read returns segments; lateness computed from stored estimates only.
- Optimiser: precedence honoured, first/last stop rule, heuristic without routing.
- Record channel: each command notifies the trip with correct hints after commit; nothing on rollback.
- Query budget: each read executes a bounded number of SQL statements (asserted with a command counter) — no N+1.

## Cost & SLO impact

No infrastructure. The k6 script (design 66) covers every read and command above.

## Assumptions

1. The OU filter of legs uses the order's organisation unit snapshot where the leg carries one; legs without one show
   under every OU (legacy behaviour).
2. VROOM is not deployed; the heuristic is the default until it is.

## Security quickscan

All endpoints authorised per permission and feature; resource ids are validated against Master Data on write;
optimisation job runs in the tenant's context; no PII beyond driver names.
