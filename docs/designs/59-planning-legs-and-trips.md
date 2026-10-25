# Design 59: Planning & Execution — legs, transport trips, stops, assignment, the calculators

**PBI:** design 56 (umbrella), Planning & Execution part
**Status:** approved (PO 2026-10-25: Q1, Q2, Q7, Q8, Q9, Q10 decided)
**Date:** 2026-10-25
**Depends on:** designs 53 (trip type), 49, 56, 57 (`IRateLineReader`), 58 (`IOrderPricingReader`, events), 60
(`IRoutingService`, `ICarbonFootprintCalculator`). Input: `docs/input/transport-assessment/trips.md` (scenarios 1–62),
`orders.md` §4 (leg generation).

## Domain impact

**Module:** Planning & Execution, schema `planning`, folders `Legs/`, `TransportTrips/`, `ViaDepots/`, `Calculators/`.
Migration `PlanningLegsAndTrips`.

### Leg (Q2)

| Field | Type | Rule |
|---|---|---|
| `OrderId`, `OrderLineId` | Orders ids (no FK) | |
| `Position` | int | 1..n within the line |
| `OriginStopId`, `DestinationStopId` | Orders stop ids | the match key of regeneration |
| origin/destination snapshots | `WaypointTypeId`, `RelationId`, `RelationAddressId`, `Name`, `City`, `CountryCode`, `Latitude/Longitude`, `TimeZoneId`, `PlannedDate` DateOnly, `WindowFrom/Till` DateTimeOffset?, `Reference`, `PlannedDurationMin` | copied from the order's stops and the address at generation; refreshed on regeneration |
| `IsViaLeg`, `ViaDepotRuleId?` | | a leg created by a via-depot split |
| `TripId?`, `TripPosition?` | Planning ids | assignment |
| stored derived | `DistanceM` int?, `DurationS` int?, `TollDistanceM` int?, `RouteProvider` string(32)?, `RoutedAt`; `EstimatedCo2Kg` decimal(18,3)?, `Co2Provider`, `Co2FactorSet` string(64)?; `AllocatedCostAmount`, `AllocatedRevenueAmount` decimal(18,2)? (Q1), `AllocationVersion` long?, `AllocatedAt` | written by the calculators |
| quantities snapshot | `Quantity`, `WeightKg`, `VolumeM3`, `LoadingMeters`, `Epq`, `LoadFactor` | copied from the line for allocation and capacity |

Unique `(TenantId, OrderLineId, OriginStopId, DestinationStopId)`; indexes `(TenantId, OrderId)`, `(TenantId, TripId)`,
`(TenantId, TripId) WHERE TripId IS NULL` (the unplanned list), `(TenantId, origin PlannedDate)`.

**Generation** (`LegGenerator`, Domain service; `orders.md` §4 made correct): on `TransportOrderConfirmed`,
`TransportOrderStopsChanged`, `TransportOrderLinesChanged` (Orders events; handler `OrderLegsHandler`), for each line:
one leg per consecutive stop pair; then the via-depot rules (§ViaDepotRule) in order relation of either stop → customer
relation → geographical region of either stop: a matching rule splits the pair at the depot (origin → depot with the
rule's `ArrivalWaypointTypeId`, depot → destination with the waypoint type's `SuccessiveWaypointTypeId` or the rule's
`DepartureWaypointTypeId`), the depot leg's planned date = origin date (+ 1 day when the rule says overnight). Legs are
matched to existing ones on the unique key: unchanged → keep id and trip; changed snapshot on an unassigned leg → update;
changed snapshot on an assigned leg → **unassign** (stop composition rerun, `LegUnassigned`, trip warning
`LegChangedAfterPlanning`); a leg no longer generated → deleted (unassigned first). `TransportOrderCancelled` deletes all.
Publishes `LegsGeneratedEto { OrderId, LegIds[] }` and requests `PlanningExecution.Leg` per leg (routing).

### ViaDepotRule

`Scope` enum `{ Relation = 1, Customer, Region }`, `RelationId?` / `GeographicalRegionId?`, `DepotRelationId`,
`DepotAddressId`, `ArrivalWaypointTypeId`, `DepartureWaypointTypeId?`, `Overnight` bool, `Position`, `IsArchived`. List in
the Master data app under Planning & Execution → Trips.

### TransportTrip (Q7, Q8)

| Field | Type | Rule |
|---|---|---|
| `Number` | string(32) | from `PlanningExecution.TransportTripType:<typeId>` on create; unique per tenant |
| `TransportTripTypeId` | FK (restrict) | |
| `TripDate` | DateOnly | numbering period lock as orders |
| `Name` | string(128)? | |
| `Status` | enum `TransportTripStatus { Planned = 1, InProgress, Completed, Cancelled }` | |
| `IsArchived` | bool | only Completed/Cancelled |
| `Execution` | enum `TripExecution` (design 53) | default from the type |
| `CarrierRelationId?`, `CarrierContactPersonId?` | Master Data | null = own company |
| `DepartureRelationId?`, `DepartureAddressId?`, `ArrivalRelationId?`, `ArrivalAddressId?` | | |
| `PlannedDeparture`, `PlannedArrival?`, `ActualDeparture?`, `ActualArrival?` | DateTimeOffset | |
| `StopCompositionMode` | enum `{ MergeByLocationAndActivity = 1, OneStopPairPerLeg }` | default from the type (OpenTMS `StopCreationMode`) |
| `FirstLastStopRule` | enum `{ None = 0, DepartureIsFirstStop = 1, ArrivalIsLastStop = 2, Both = 3 }` | default from the type |
| `VehicleCombinationId?` or child `Vehicles` (`Position` 1..3, `VehicleId`) | | one of the two |
| child `Drivers` | `Position`, `DriverId` (design 43) | ≤ 3 |
| `Remarks`, `DriverInstructions` | string(2500)? | |
| `OrganizationUnitId?`, `Attributes` jsonb, `CreatedFromOrderId?` | | |
| `PlannedDistanceM`, `ActualDistanceM` int? | | actual by command |
| stored derived | `PlannedDurationS`, `LoadFactor` decimal(9,4)?, `LoadingMeters` double?, `Co2Kg`, `Warnings` jsonb (`[{ code, legId?, stopId?, data }]`), `CostAmount`, `RevenueAmount` decimal(18,2)?, `CalculatedAt` | `TripCalculator` |
| child `Stops` → `TripStop` | `Position`, `WaypointTypeId`, `RelationId`, `RelationAddressId`, `Name/City` snapshot, `IsManual`, `PlannedArrival/Departure`, `EstimatedArrival/Departure`, `ActualArrival/Departure` DateTimeOffset?, `PlannedCumulativeDistanceM`, `ActualCumulativeDistanceM` int?, `Remarks`; child `TripStopLeg` (`LegId`, `Role` Origin/Destination; unique per leg and role) | |

### Lifecycle and commands

`Create` (Planboard or `TransportOrderConfirmed` with a `TripRequest`: own id, `CreatedFromOrderId`, the request's
fields, then assign all legs of the order, then `AgreedPrice` through `TransportTripCreatedEto { TripId, OrderId?,
AgreedAmount?, Currency? }` which Financial turns into the trip's cost line); `UpdateHeader`; `SetCarrier`;
`SetVehicles`; `SetDrivers`; `AssignLegs { legIds[], afterStopId? }` / `UnassignLegs`; `MoveLegs { legIds[], toTripId }`;
`AddManualStop`, `MoveStop { stopId, position }` (refused when the stop has actuals), `RemoveStop` (manual only);
`RecordStopActual { stopId, arrival?, departure?, cumulativeDistanceM? }`; `Start` (Planned → InProgress, sets
`ActualDeparture`); `Complete` (→ Completed, fills missing actuals as OpenTMS `Complete`); `Cancel` (Planned/InProgress →
Cancelled; legs unassigned); `Archive`/`Unarchive`; `Delete` (Planned only, legs unassigned); `SplitLine`,
`CreateReturnLeg`, `CarryOutSeparately`, `OutsourceOriginActivity { legId, hubRelationId, toTripId }` (`trips.md` 20–24,
through the assignment service). Refusals: `Planning:TripNotAssignable` (Cancelled/Archived/Completed),
`Planning:LegOnOtherTrip` (unless moving), `Planning:LegOutsideTripWindow` unless `allowOutsideWindow`,
`Planning:StopHasActuals`, `Planning:InvalidTransition`.

### Stop composition (`TripStopComposer`, Domain service)

Per trip after any assignment change: `MergeByLocationAndActivity` matches a leg's origin/destination to a stop with the
same `(RelationAddressId, WaypointTypeId)` without actuals, else creates one; `OneStopPairPerLeg` creates two stops per
leg; manual stops stay; stops without legs (not manual) are removed; the first/last stop rule sets departure/arrival;
positions renumbered. Deterministic order: by the legs' planned window start, then order of assignment.

### Calculators (design 49)

- `LegRouteCalculator` (`PlanningExecution.Leg`): `IRoutingService.GetRouteAsync(origin, destination, vehicleSpec?)`
  → `DistanceM`, `DurationS` (× tenant speed-correction factor, setting `Planning:SpeedCorrectionPercent`, default 120),
  `TollDistanceM` when the provider gives it; `EstimatedCo2Kg` by `ICarbonFootprintCalculator.EstimateLegAsync`
  (great-circle × tenant CPI when no trip); provenance; failures → state Failed, retried by Hangfire; a leg without
  coordinates gets null and warning `AddressNotGeocoded`.
- `TripCalculator` (`PlanningExecution.TransportTrip`): (a) stop times: from `PlannedDeparture`, per stop
  `IRoutingService.GetMatrixAsync` legs between consecutive stops → `PlannedArrival`, `PlannedDeparture` (+ planned
  duration), `PlannedCumulativeDistanceM`; trip `PlannedDistanceM`, `PlannedDurationS`, `PlannedArrival` when the type
  says so; (b) `LoadFactor`/`LoadingMeters` from the legs' quantities vs the vehicles' capacity (Master Data lookup of
  vehicle types' `MaxEpq`, `MaxLoadingMeters`, `MaxPayloadKg`); (c) warnings: missing transport characteristic,
  incompatible temperatures/products/units on one trip (Master Data incompatibilities lookup), leg dates outside the
  trip window, ETA outside a stop's window, `LegChangedAfterPlanning`, `AddressNotGeocoded`, `NoCostPriceSource`;
  (d) CO₂: `ICarbonFootprintCalculator.EstimateTripAsync(route, vehicleSpec, legs)` → `Co2Kg` and the per-leg
  allocation by tonne-km (COFRET), written on the legs; (e) allocation (Q1): `IRateLineReader.GetTotalsAsync(tripId)` →
  trip `CostAmount`; `GetTotalsAsync(orderIds of the legs)` → revenue per order; per leg `AllocatedCostAmount` = trip cost
  × leg share (leg km over assigned km; equal shares when no distances), `AllocatedRevenueAmount` = order revenue ×
  leg km over the order's leg km (full order revenue when the trip holds all its legs and no distances); trip
  `RevenueAmount` = Σ; publishes `TransportTripRecalculatedEto { TripId, OrderIds[] }`.

Requests: `TransportTripLegsChanged`, `TransportTripChanged` (carrier/vehicles/distance), stop moved/actuals,
`RateLinesChanged { TripId }`, `RateLinesChanged { OrderId }` (for the trips holding its legs), `LegsGenerated` (for
assigned legs), Master Data vehicle changes are not watched (recalculate on next change or bulk).

### Published contract and events

`ILegReader.GetByOrderAsync(orderId)`, `GetByTripAsync(tripId)`, `GetAsync(ids)` → leg DTO with snapshots, distance, toll,
trip number; `ITransportTripLookup.GetAsync(ids)` → number, status, carrier name, vehicles, drivers. Events:
`LegsGeneratedEto`, `LegAssignedEto { LegId, TripId, OrderId }`, `LegUnassignedEto`, `TransportTripCreatedEto`,
`TransportTripChangedEto { TripId, Change }`, `TransportTripLegsChangedEto { TripId, OrderIds[] }`,
`TransportTripStatusChangedEto { TripId, Status, OrderIds[] }`, `TransportTripRecalculatedEto`.

## API contract

Route root `planning`; permissions per design 56.

| Service | Route | Operations |
|---|---|---|
| `TransportTripAppService` | `/api/planning/transport-trip` | `GET` list (`status[]`, `dateFrom/To` (trip date), `executionFrom/To`, `carrierRelationId`, `organizationUnitId`, `isArchived`, `hideEmpty`, `view`, search over number, name, carrier, vehicles, customers, stop cities), `GET /{id}` (header, vehicles, drivers, stops with their legs, derived values), `POST` (create), `PUT /{id}` header, `POST /{id}/carrier`, `/vehicles`, `/drivers`, `/assign-legs`, `/unassign-legs`, `/move-legs`, `/stops` (add manual), `PUT /{id}/stops/{stopId}` (move, remarks), `DELETE /{id}/stops/{stopId}`, `POST /{id}/stops/{stopId}/actuals`, `/start`, `/complete`, `/cancel`, `/archive`, `/unarchive`, `DELETE /{id}`, `POST /{id}/split-line`, `/return-leg`, `/carry-out-separately`, `/outsource-origin`, `GET /{id}/summary` |
| `LegAppService` | `/api/planning/leg` | `GET ?orderId=` / `?tripId=` / unplanned list (`dateFrom/To`, `customerRelationId`, search), `PUT /{id}` (references, remarks, planned dates within the stop's window — the editable leg fields of OpenTMS's leg grid), `GET /{id}` |
| `ViaDepotRuleAppService` | `/api/planning/via-depot-rule` | design-50 shape |
| `TripAttributeDefinitionAppService` | `/api/planning/trip-attribute-definition` | design-50 shape |

## Migration strategy

Expand only, `PlanningLegsAndTrips`: `planning."Legs"`, `"TransportTrips"`, `"TripVehicles"`, `"TripDrivers"`,
`"TripStops"`, `"TripStopLegs"`, `"ViaDepotRules"`, `"TripAttributeDefinitions"`; indexes as named plus
`(TenantId, Status, TripDate)`, `(TenantId, CarrierRelationId)`, trigram on `Number`/`Name`; unique `(TenantId, Number)`;
unique `(LegId, Role)` on stop legs. No seed.

## UI design

Design 61.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Tenant isolation: legs, trips, stops, readers, calculators | **Critical** | PostgreSQL |
| Leg generation idempotency and via-depot splitting (`orders.md` 18–28 corrected) | **Critical** | PostgreSQL |
| Assignment rules, stop composition in both modes, manual stops, renumbering (`trips.md` 7–28) | **Critical** | SQLite service tests |
| Status machine (46–55) | **Critical** | SQLite |
| TripCalculator: times, load factor, warnings, CO₂ allocation, cost/revenue allocation (39–45, 62–69 of pricing) | **Critical** (money) | PostgreSQL with fake routing/CO₂/rate-line readers |
| Superseded calculation discarded; failed provider keeps old values | **Critical** | PostgreSQL |
| Tevens trip: created once on Confirm, agreed price event, later order edits do not touch it | **High** | integration with Orders |
| Grids, unplanned legs list | **High** | PostgreSQL volume |
| Permissions, features | **Critical** | architecture + service |

## Flag & rollout plan

Features `Apps.Transport.Trips`, Master data nodes `ViaDepotRules`, `TripAttributeDefinitions`; default on.

## Cost & SLO impact

Per trip calculation: one matrix call (≤ 40 stops) and one CO₂ call; < 2 s; routing results cached (design 60);
save p95 < 500 ms.

## Assumptions

1. Route optimisation (stop reordering by VROOM) is the Planboard run; this run orders stops by planned windows and the
   planner's moves.
2. Capacity figures come from Master Data vehicle types (design 48 fields); a trip without vehicles has no load factor.
3. ETA from live positions is the ingest run; `EstimatedArrival` here equals the planned time until then.
4. The agreed purchase price of a "Tevens trip" is handed to Financial by event; Planning stores no money.

### Build notes (backend, 2026-10-25)

Decisions the build had to take where this design left room, reported for the reviewer and the PO:

1. **Error codes** are `OpenTms.PlanningExecution:<Name>` (the module's code namespace), not `Planning:<Name>` as written
   above; the names are the design's (`TripNotAssignable`, `LegOnOtherTrip`, `LegOutsideTripWindow`, `StopHasActuals`,
   `InvalidTransition`) plus the refusals the commands needed (`LegNotOnTrip`, `TripArchived`, `TripDateLocked`,
   `NotManualStop`, `OrderCancelled`, `ReturnLegExists`, `SameTrip`, the bounds). `PlanningExecutionErrorCodes` carries the
   `ConflictCodes`/`BadRequestCodes` arrays; the host mapping is added at integration.
2. **Leg columns beyond the table above:** `CustomerRelationId` (the unplanned list's customer filter and the customer-scope
   via rules), `Source` (`LegSource`: Generated, ViaDepot, Split, Return, Outsourced) next to `IsViaLeg`, `SplitFromLegId`,
   `IsCarriedOutSeparately`, `Remarks`, `TransportUnitId`/`ProductId`/`TemperatureId`/`RequiredTransportCharacteristicIds`
   (the compatibility warnings need them). **Stops that exist only in Planning** (the depot of a via split, a split's ends,
   an outsourcing hub) get a deterministic synthetic stop id derived from their parts, so the unique key of the design holds
   and regeneration finds the same legs again.
3. **Regeneration and the planner's legs:** a line with a split, return or outsourced leg is "reshaped": regeneration keeps
   its legs and only refreshes the snapshots of ends that still point at an order stop (an assigned leg whose end changed
   is still unassigned with `LegChangedAfterPlanning`). A quantity-only change updates an assigned leg in place and requests
   its trip; only a changed origin or destination takes the leg off the trip. `TransportOrderCancelled` removes every leg as
   a fact, whatever the reader still shows.
4. **Depot departure type:** Orders publishes no waypoint-type lookup, so the onward leg departs the depot with the rule's
   `DepartureWaypointTypeId`, else the arrival type itself (not the arrival type's successor). Open for integration.
5. **TripStop** carries `Latitude/Longitude/TimeZoneId` and `PlannedDurationMin` (the matrix call and the stay need them);
   **TransportTrip** carries `PlannedDepartureUtc`/`PlannedArrivalUtc` (`timestamp`, the sortable form the lists use) and a
   lower-cased `SearchKey` (number, name, carrier, combination, vehicles, customers, order numbers, stop cities) with a
   trigram index, so the list search of the API contract needs no cross-schema join.
6. **Capacity figures** come from the vehicle's own `MaxEpq`/`MaxLoadingMeters`/`MaxPayloadKg`, falling back to the
   type's (assumption 2 named the type; design 48 copies the type's figures onto the vehicle). The load factor is the worst
   ratio of EPQ, loading metres and payload against the summed capacity; `CapacityExceeded` names the measure.
7. **Planned arrival** is written by the calculator when the trip has an arrival address or the `ArrivalIsLastStop` rule;
   `TransportTripType` has no "set arrival" flag.
8. **`NoCostPriceSource`** is raised when the trip has legs and Financial reports no cost total and no fixed price.
9. **Routes:** the conventional controller derives `POST /{id}/set-carrier`, `/set-vehicles`, `/set-drivers`,
   `/add-stop`, `PUT /{id}/stop/{stopId}`, `DELETE /{id}/stop/{stopId}`, `POST /{id}/record-stop-actuals`, `/split-line`,
   `/return-leg`, `/carry-out-separately`, `/outsource-origin`; the typed client follows the generated spec.
10. **Return leg:** loads where the original unloads and unloads where it loaded (the activities swap with the places).
11. **Master Data lookup additions** (the one allowed exception): `GetRelationAddressesAsync`, `GetAddressesOfRelationsAsync`,
    `GetVehiclesAsync`, `GetVehicleCombinationsAsync`, `GetDriversAsync`, `GetIncompatibilitiesAsync` on
    `IMasterDataReferenceLookup`.

## Security quickscan

Permissions per design 56; bounded children (≤ 100 stops, ≤ 3 vehicles/drivers); ids validated through lookups; jobs
switch tenant explicitly; warnings carry codes and ids, no texts; no personal data (driver ids only).
