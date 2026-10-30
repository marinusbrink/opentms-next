# PBI: The Transport app: transport orders, transport trips, pricing and cost, CO₂, CMR, templates

<!-- Written on the PO's instruction of 2026-10-25: "bouw de transport app in 1x: transport orders en transport trips,
     inclusief prijscalculaties (queryFreightPrices), alle functionaliteiten en functional scenarios van het traditionele
     OpenTMS op de nieuwe architectuur; first-time-right." Inputs: docs/input/transport-rebuild-assessment.md (Q1–Q16
     decided by the PO on 2026-10-25), docs/input/pricing-rebuild-assessment.md (QP1–QP11 decided), the five
     appendices in docs/input/transport-assessment/ (304 scenarios). No GitHub issue; 56 follows design 55. -->

## Story

As an order clerk I want to enter, price, plan-request, print and follow a transport order; as a planner I want to see
and adjust the trips my orders end up on, their stops, costs and CO₂; as the finance clerk I want every order and trip
to carry stored, traceable revenue and cost lines; because this is what the traditional OpenTMS does today and the
tenants that move to opentms-next must lose no scenario of it, while the new system saves at once and calculates in the
background (flow 2), publishes every fact as an event (flow 1) and keeps money history safe (AD-07, AD-08).

## Affected area

Orders (the order aggregate, templates, CMR), Planning & Execution (legs, trips, stops, the derived-value calculators),
Financial (price lists, rate lines, cost allocation), Integrations (routing and CO₂ adapters), Platform (one report
purpose, the Transport app's features), the Transport app in the frontend, Dutch manual pages. Split into module designs
57 (Financial), 58 (Orders), 59 (Planning & Execution), 60 (Integrations), 61 (Transport app).

## Tenant dimension

Configurable per tenant; every feature of this run defaults **on** (PO 2026-10-25, deviation from constitution rule 4).

## Expected volumes

Per large tenant: 2,000 orders a day (5,000 lines, 10,000 stops, 10,000 legs), 600 trips a day with up to 40 stops and
60 legs each, 50,000 rate lines a day, 200 price lists with up to 5,000 rules of 10 brackets × 20 columns each, 300 open
screens at planning time. Peaks: 50 saves a minute on one tenant; a retroactive rate change re-running 10,000 orders.

## Known edge cases

Two clerks saving one order; a stop changed while its leg is on a trip in progress; a trip cancelled with legs; a
template whose relation was archived; a price list that expires mid-month; a line without distance on a per-km list; a
toll lookup that fails; an order with lines in two currencies (refused); a CMR printed twice; a tenant whose routing
provider is down (saves still return, values stay "calculating" until the retry); an order confirmed by a boundary agent
as a draft; DST at a stop.

## Privacy note

Stops carry addresses of relations (business addresses, already in Master Data) and contact-person ids; drivers on a trip
are Master Data driver ids; CMR documents carry names and addresses of the parties as they appear on the consignment
note today. No new category of personal data; the GDPR answer of Master Data (relations, drivers) extends to the
documents: a CMR is a legal transport document kept for the statutory retention of the tenant, listed under the order.

---

# Design 56: the Transport run (umbrella)

**PBI:** above
**Status:** approved (gate 1 by the PO's instruction of 2026-10-25: all recommendations Q1–Q16 and QP1–QP11 decided)
**Date:** 2026-10-25
**Depends on:** designs 41–55 (all merged in main at c18cd40); the architecture folder; the two assessments.

This document fixes everything that crosses a module boundary: who owns what, which events travel, which calculators
write which stored values, the status machines, the shared conventions, the build order. The module designs 57–61 hold
the entities, endpoints, screens and tests of each module and must not contradict this one. Where a module design needs
a detail this one does not give, it decides it and notes it under *Assumptions*.

## Domain impact

### Decisions relied on

AD-01 (commands, facts, events), AD-02, AD-06 (jsonb for attributes and warnings), **AD-07** (stored derived values,
background, pushed), **AD-08** (rate lines), AD-09 (documents as references; the provider is configuration), AD-12 (routing
as a separate service behind an adapter), AD-15 (positions are ingest, out of this run), AD-16, AD-18. Proposals used and
named: AP-06 (event names `OpenTms.<Domain>.<FactInPastTense>`), AP-07 (one rate-line table). **AO-11 is decided by Q1**:
allocated values live on Planning's legs; this PR flips AO-11 in `decisions.md` and `01-overview.md`.

### Ownership

| Module | Owns | Published contract (Application.Contracts) | Events (Domain.Shared/Events) |
|---|---|---|---|
| **Orders** (design 58) | `TransportOrder` + `Line`, `Stop`, `Reference`, `FinancialTerms`, `TripRequest`; `TransportOrderTemplate` (+ schedule); `ConsignmentNoteProfile`, `ConsignmentNoteFixedDescription`, `ConsignmentNoteDocument` | `IOrderPricingReader` (the pricing snapshot of an order: lines, stops with relation/address/region ids, terms); `ITransportOrderLookup` (id → number, customer, status for grids of other modules) | `TransportOrderConfirmed`, `TransportOrderStopsChanged`, `TransportOrderLinesChanged`, `TransportOrderFinancialTermsChanged`, `TransportOrderCancelled`, `TransportOrderStatusChanged` |
| **Planning & Execution** (design 59) | `Leg`; `TransportTrip` + `TripStop`, `TripStopLeg`, `TripVehicle`, `TripDriver`; `ViaDepotRule`; the three calculators' stored columns | `ILegReader` (legs of an order or trip with distances and stop snapshots); `ITransportTripLookup` | `LegsGenerated`, `LegAssigned`, `LegUnassigned`, `TransportTripCreated`, `TransportTripChanged` (carrier, vehicles, distance), `TransportTripLegsChanged`, `TransportTripStatusChanged`, `TransportTripRecalculated` |
| **Financial** (design 57) | `PriceList` (+ `Column`, `Rule`, `Bracket`, `Cell`), `RateLine`, `RelationFinancialSettings` | `IRateLineReader` (rate lines and totals per anchor), `IPricePreviewService` (pure preview, QP6/§4.3 of the pricing assessment), `IFinancialReferenceLookup` (VAT, D&S, additional services, price lists by id) | `RateLinesChanged` |
| **Integrations** (design 60) | `IRoutingService` with the OSRM and Valhalla-function providers, `ICarbonFootprintCalculator`, `RouteCacheEntry` | both interfaces | none |
| **Platform** (this design, §"Platform changes") | `ReportPurpose.ConsignmentNote`, the CMR entry in the report data catalogue, the Transport app features, the number-sequence keys | — | — |

Cross-module ids carry **no database constraint** (rule 1); the owning module validates them through its published lookup
on every save and shows "(not found)" when a referenced row disappeared later (design 51 precedent).

### The three flows, applied

**Flow 1 (commands → facts → events).** Every change is an application-service command on the owning module. Orders
never calls Planning or Financial synchronously except the stated exceptions below. The four module-boundary rules of
`/CLAUDE.md` hold; the **synchronous calls this run allows** (rule 3) are:

| Caller → callee | Why synchronous | Contract |
|---|---|---|
| Orders → Master Data `IMasterDataReferenceLookup` | validation and names on save (design 51 precedent) | existing |
| Orders → Financial `IFinancialReferenceLookup` | validation and names of VAT, D&S, price lists on save | new |
| Orders → Financial `IPricePreviewService` | the price preview while the clerk types (pricing §4.3), pure, no persistence | new |
| Planning → Orders `IOrderPricingReader` / `ITransportOrderLookup` | leg generation and grid names | new |
| Planning → Master Data lookups | vehicles, drivers, relations, addresses | existing |
| Financial calculators → Orders `IOrderPricingReader`, Planning `ILegReader`, Master Data lookups | the calculator reads facts to price them (inside its own job) | new |
| Planning calculators → Financial `IRateLineReader` | allocation reads the stored rate lines | new |
| Orders, Planning → Integrations `IRoutingService`, `ICarbonFootprintCalculator` | inside calculators (jobs), never in a request | new |
| Orders → Planning `ILegReader` | the order's legs with their trip and status: the stop lock and the cancel refusal while a leg is on a trip in progress (or completed), the delete refusal for assigned legs, the approval rules on the order's legs, the order summary calculator (execution progress, trip numbers, CO₂), the pricing snapshot's distances per line, the CMR print of a trip's lines (review M1, 2026-10-26) | new |
| Orders → Planning `ITransportTripLookup` | trip number of the order's trip request in the order DTO, the trip (number, carrier, vehicles) on a CMR | new |
| Orders → Financial `IRateLineReader` | the order's stored revenue totals: approval on stored revenue (no price recalculation in the request) and the order summary calculator (revenue, cost of revenue, margin) | new |

Everything else is an event through the outbox (AD-16) with idempotent handlers (rule 3).

**Flow 2 (save without waiting).** Every save that changes an input of a derived value ends with
`IDerivedValueScheduler.RequestAsync(targetType, id, reason)` (design 49). The calculators of this run:

| Target type | Calculator (module) | Writes (stored columns, provenance per AD-07) | Requested by |
|---|---|---|---|
| `Financial.OrderRateLines` | `OrderRateLineCalculator` (Financial) | the order's non-manual revenue rate lines; publishes `RateLinesChanged{OrderId}` | Orders save of lines/stops/terms (via event handler in Financial), `PriceList`/`DiscountOrSurcharge` bulk re-run, `LegsGenerated` (distance known) |
| `Financial.TripRateLines` | `TripRateLineCalculator` (Financial) | the trip's cost rate lines per leg and the own-fleet line; publishes `RateLinesChanged{TripId}` | `TransportTripLegsChanged`, `TransportTripChanged`, cost price-list bulk re-run |
| `PlanningExecution.Leg` | `LegRouteCalculator` (Planning) | `DistanceM`, `DurationS`, `RouteProvider`, `RoutedAt`; `EstimatedCo2Kg` for an unplanned leg (great-circle fallback) | `LegsGenerated`, routing provider changed |
| `PlanningExecution.TransportTrip` | `TripCalculator` (Planning) | per stop planned arrival/departure and cumulative distance; trip `PlannedDistanceM`, `PlannedDurationS`; `LoadFactor`, `LoadingMeters`; `Warnings` (jsonb list of typed codes); `Co2Kg` and the per-leg allocation; **`AllocatedCostCents`/`AllocatedRevenueCents` per leg** (Q1) from `IRateLineReader`; `TransportTripRecalculated` event | leg assigned/unassigned, stop moved, carrier/vehicle changed, `RateLinesChanged{TripId or OrderId}` |
| `Orders.TransportOrder` | `OrderSummaryCalculator` (Orders) | `RevenueCents`, `CostOfRevenueCents`, `MarginCents`, `ExecutionProgress`, `Co2Kg`, grid columns (`LineCount`, `FirstStop*`, `LastStop*`, `TripNumbers`) | `RateLinesChanged{OrderId}`, `LegAssigned/Unassigned`, `TransportTripStatusChanged`, `TransportTripRecalculated` |

Screens watch `Financial.OrderRateLines`, `PlanningExecution.TransportTrip` and `Orders.TransportOrder` with
`useRecordUpdates`/`useDerivedValueStates` and show `CalculatingIndicator` (design 49). **Reads never calculate.**

**Flow 3 (reports from outside).** Not extended: FMS positions, actual arrivals from devices and ETA are the Planboard/
ingest run. A planner records actuals with commands.

### Status machines (Q5, Q7)

```
TransportOrderStatus:  Draft ──Confirm──▶ Open ──Approve──▶ ReadyForInvoicing ──(invoicing run)──▶ Invoiced ──Archive──▶ Archived
                       Draft/Open ──Cancel──▶ Cancelled      ReadyForInvoicing/Archived ──Revise──▶ Open      Open ──Approve(archive)──▶ Archived
TransportTripStatus:   Planned ──Start──▶ InProgress ──Complete──▶ Completed      Planned/InProgress ──Cancel──▶ Cancelled   (+ IsArchived flag on Completed/Cancelled)
ExecutionProgress (order, derived): Unplanned | PartlyPlanned | Planned | InProgress | Delivered
```

Transitions are commands with server-side rules (design 58 §lifecycle, design 59 §lifecycle); the client never posts a
status. `Invoiced` is written by the invoicing run only.

### Shared conventions of this run

- **Money** is `decimal` in C# and `numeric` in PostgreSQL (design 52 §3.5): amounts `numeric(18,2)`, unit prices and
  quantities `numeric(18,4)`. Stored allocated values on legs are `numeric(18,2)` too. (The `*Cents` names above mean
  "money column", not an integer type.)
- **Measures** carry the unit in the name: `DistanceM` (int metres), `DurationS` (int seconds), `WeightKg`, `VolumeM3`,
  `LoadingMeters`, `LengthCm` as `double?` (design 41 precedent), `Co2Kg` `decimal(18,3)`.
- **Instants** are `DateTimeOffset` (`timestamptz`); dates `DateOnly`; a stop's local zone is its address's IANA id.
- **Attributes** (OpenTMS custom properties, Q12): one `Attributes` jsonb (`Dictionary<string,string>`, ≤ 20 keys, keys
  ≤ 32, values ≤ 500) on order, line and trip, with a tenant-level `AttributeDefinition` list per aggregate type in the
  owning module (key, nl/en label, position). The Master data app shows the definitions under the owning domain group.
- **Positions** of children are 1..n without gaps, renumbered by the aggregate.
- **Concurrency**: every aggregate carries `ConcurrencyStamp`; commands take it; a stale stamp is `AbpDbConcurrencyException`
  → 409 (existing mapping).
- **Numbers**: `INumberSequenceGenerator` (design 50) with keys `Orders.TransportOrderType:<id>`,
  `PlanningExecution.TransportTripType:<id>` (existing) and `Orders.ConsignmentNote` (new, format `CMR`, year, `-`, 6
  digits, per tenant; no type).
- **Events**: ids only; name per AP-06; handler idempotency through the derived-value version or a natural key. One
  stated exception (review m1): `TransportTripCreatedEto` carries the agreed purchase amount and currency of a "Tevens
  trip" (design 59 §Commands, assumption 4), because Planning stores no money and the amount exists nowhere else for
  Financial to read; Financial turns it into the trip's cost line once (natural key on the trip).
- **Logging**: ids, counts, versions; never names, addresses or amounts.

### Platform changes (critical by default; small)

1. `ReportPurpose.ConsignmentNote = 6` and its catalogue entry in `ReportDataCatalogue` (the CMR box data of
   `docs/input/transport-assessment/routing-co2-cmr.md` §3.2 as fields: sender, consignee, places, goods rows, boxes
   13/18/19/21, parties); a seeded default report definition is **not** possible (pdfgeneratorapi template ids are
   per workspace): the tenant picks a template; without one the print command fails with
   `Orders:ConsignmentNoteTemplateMissing`.
2. `AppFeatures.Transport` gets children (§Flag & rollout). `AppFeatures.MasterData.Orders.TransportOrders` gets
   `ConsignmentNoteProfiles`, `ConsignmentNoteFixedDescriptions`, `OrderAttributeDefinitions`;
   `AppFeatures.MasterData.PlanningExecution.Trips` gets `ViaDepotRules`, `TripAttributeDefinitions`;
   `AppFeatures.MasterData.Financial.Pricing` gets `PriceLists`, `RelationFinancialSettings`.
3. `OpenTmsHttpApiModule` maps the new modules' error codes (each module keeps `ConflictCodes`/`BadRequestCodes`).
4. **The outbox dispatcher runs every event in a transactional unit of work of its own** (`OutboxDispatcher`, built
   2026-10-26; outbox-per-tenant design). The handlers of this run write their module's tables, request derived values
   and publish facts of their own, and an outbox publish outside a transaction throws: so each event is published
   inside `Begin(isTransactional: true, requiresNew: true)` in the raising tenant's context, the unit of work commits,
   and only then is the outbox row deleted (in a unit of work of its own). A handler that fails rolls back everything it
   wrote and published; the event stays waiting and blocks the events behind it in that database until the next run
   (order preserved, AO-10). A crash between the commit and the delete re-delivers the event, which the idempotent
   handlers absorb (rule 3). `DerivedValuesRequestedHandler` carries `[UnitOfWork]` so it joins that unit of work.
   Covered by `OutboxPerTenantIntegrationTests` (PostgreSQL), including the failure path: a handler that writes a row
   and publishes an `OpenTms.*` event, then throws, leaves neither the row nor the new outbox row and keeps the original
   event waiting; the next run commits both exactly once.

## API contract

One route root per module (`/api/orders/...`, `/api/planning/...`, `/api/financial/...`, `/api/integrations/...`); the
list contract of design 50 (`StartRow, EndRow, WildcardSearch, SortBy, SortDir` + typed filters) for every grid; commands
as `POST /{id}/<verb>`; children replaced as whole lists on `PUT` except where a design names row commands (legs, stops,
rate lines). The exact endpoints are in designs 57–61; the OpenAPI spec and the typed client are regenerated once per
module and committed.

## Migration strategy

Expand only, one migration per module (`FinancialTariffs`, `OrdersTransportOrders`, `PlanningLegsAndTrips`,
`IntegrationsRouting`, Platform none: enum values need no migration), every new query's index in the same migration,
unique indexes `NULLS NOT DISTINCT` with the soft-delete filter, check constraints for enums and positions. Seeds: a
default `ConsignmentNoteProfile`, two `ViaDepotRule`-free defaults (none), nothing else; price lists and rate lines are
tenant data. No contract step in this run. The data migration from OpenTMS is a separate brief (transport assessment §8).

## UI design

Design 61. The Transport app (feature `Apps.Transport`, exists) gets the nav tree Orders (Today, Tomorrow, This week,
Open, Drafts, All), Templates, Trips (Today, This week, Open, All), Documents; the order editor (General, Lines, Pricing,
Planning, Documents, Record identification), the trip editor (General, Legs, Stops, Cost and revenue, Documents, Record
identification), the wizards New from template / Copy, Print CMR, Archive trips; the Master data app gains the lists
named under Platform changes (2). Library components built by this run (UI rules): `Wizard`, `useOpenRecord`; existing:
`EntityDialog`, `LinesGrid`, `OpenTmsGrid`, `StatusBadge`, `FormSection`, `EntityLookup`, `ReferenceCombobox`,
`DatePicker`, `CalculatingIndicator`, `NumberingFields`.

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Tenant isolation of every new aggregate, calculator, reader and hub group | **Critical** | PostgreSQL negative tests per module |
| Money: price-list lookup, rate lines, D&S, fixed price, allocation (pricing appendix scenarios 1–80) | **Critical** | PostgreSQL + unit tests, decimal exactness |
| Authorization: every endpoint behind a permission, features default on but gate when off | **Critical** | architecture + service tests |
| Status machines and command rules (orders, trips) | **Critical** | SQLite service tests per transition incl. refusals |
| Leg generation idempotency (same stops → same legs; changed stop → unassign + warning) | **Critical** | PostgreSQL tests |
| Derived values: superseded results discarded, at-least-once events one effect, failed calculator keeps old values | **Critical** | PostgreSQL integration tests with probe providers |
| Routing/CO₂ adapters: provider contract, fallback, cache TTL, hosts from configuration | **High** | unit tests with fake HTTP, contract tests against recorded responses |
| CMR: data mapping per box, number drawn once, document stored once per print | **High** | SQLite + PostgreSQL |
| Templates and recurrence (weekly, monthly, horizon, idempotent on template+date) | **High** | SQLite + a PostgreSQL job test |
| Screens: editors, grids, wizards, calculating states | **Medium** | vitest |
| Localization parity nl/en | **Low** | key tests |
| Volume ×5: order save < 500 ms with 50 lines; order list p95 < 300 ms at 500k orders; calculator per trip < 2 s | **High** | PostgreSQL volume tests |

## Flag & rollout plan

Features (all default **on**, PO 2026-10-25): `Apps.Transport` (exists), `Apps.Transport.Orders`,
`Apps.Transport.Orders.Templates`, `Apps.Transport.Trips`, `Apps.Transport.Documents`, plus the Master data nodes of
Platform changes (2). The edition/tenant Features tab can switch each off; off hides the node and gates its endpoints
(design 54 pattern: class attribute for the app, chain on the screen methods, lookups need only the app). Rollback: a
feature off; tables stay. Health tenant → friendly tenants → all is the host's choice through editions.

**Background paths (review B2, 2026-10-26).** A path without a user is gated by the feature of what it *originates*,
checked through `IFeatureChecker` for the tenant it runs in; a path that only *reacts* to a fact is not gated, because
the fact itself could only come from a gated command:

| Path | Gated? | Why |
|---|---|---|
| Hourly template materialisation (`TemplateMaterialisationRunner.RunForCurrentTenantAsync`) | **yes**: `Apps.Transport` + `Apps.Transport.Orders` + `Apps.Transport.Orders.Templates`, all on | it creates new orders on its own; off stops new materialisation, the templates and the orders already made stay |
| Planning's `OrderLegsHandler` (legs on confirm/stops/lines changed/cancel, the "also a trip" of the trip request) | no | it follows an order fact; an order confirmed while the feature was on still gets its legs and its requested trip when the event is dispatched after the switch-off |
| Financial's rate-line handlers and calculators, Planning's leg/trip calculators, Orders' summary calculator and handlers | no | they keep stored money, distances and summaries of existing records correct (AD-07); switching them off would leave stale values, not hide a screen |
| Route-cache purge, outbox dispatcher | no | infrastructure, no user-visible behaviour |

## Cost & SLO impact

- **Cloud SQL**: orders 2,000/day × (1 order + 2.5 lines + 5 stops) rows ≈ 7 MB/day; legs and trips ≈ 3 MB/day; rate
  lines ≈ 10 MB/day; price-list cells up to 1 M rows per tenant (≈ 100 MB); outbox rows seconds-lived; derived-value
  states one per target. Indexes on every grid filter.
- **Jobs**: 3–5 calculator jobs per order save, 2–3 per trip change; each < 2 s; one Hangfire worker set.
- **External calls**: OSRM self-hosted (no per-call cost; ≈ 20,000 routes/day per large tenant); the Valhalla Cloud
  Function (toll and CO₂) ≈ 1,200 trips/day (it runs always-on at ≈ €150/month today); pdfgeneratorapi per CMR print
  (metered per document). Cache hits (route cache, 30 days per coordinate pair and vehicle class) take most repeats.
- **Budgets**: save p95 < 500 ms (one upsert + outbox rows); list p95 < 300 ms (stored columns, indexed); SignalR
  messages ≈ 100 bytes; cold start unaffected.

## Assumptions

1. Everything the PO decided on 2026-10-25 (Q1–Q16, QP1–QP11) is final and cited by number in the module designs.
2. The Planboard run will reuse Planning's commands (create trip, assign legs, set vehicles/carrier) unchanged; this
   run exposes them under `/api/planning` already, used by "Tevens trip" and the tests.
3. `IPricePreviewService` is the only synchronous Financial call from a request; it has a p95 budget of 500 ms for an
   order of 20 lines and no persistence.
4. Positions (ETA from devices), FMS sending and e-mail are not part of the run; the commands and events they will need
   (`StartTrip`, `RecordStopActual`, `CompleteTrip`) exist.
5. The OSRM instance of OpenTMS (`osrm.services.opentms.nl`) and the Valhalla function stay reachable from the new
   environments; both are configuration (`Integrations:Routing:*`), never literals.
6. A tenant without a CMR report definition cannot print a CMR; a FiLogic standard template arrives through the Platform
   report-definition editor (design 55), not through code.

## Security quickscan

- **Permissions** (per action, Q14): `Orders.TransportOrders` (+ `.Create`, `.Update`, `.Delete`, `.Confirm`, `.Cancel`,
  `.Approve`, `.Revise`, `.Archive`, `.Print`), `Orders.TransportOrderTemplates` (+ CUD), `Orders.ConsignmentNoteProfiles`
  (+ CUD), `Orders.ConsignmentNoteFixedDescriptions` (+ CUD), `Orders.OrderAttributeDefinitions` (+ CUD);
  `PlanningExecution.TransportTrips` (+ `.Create`, `.Update`, `.Delete`, `.Cancel`, `.Archive`, `.Start`, `.Complete`,
  `.AssignLegs`, `.ManageStops`), `PlanningExecution.Legs` (+ `.Update`), `PlanningExecution.ViaDepotRules` (+ CUD),
  `PlanningExecution.TripAttributeDefinitions` (+ CUD); `Financial.PriceLists` (+ CUD, `.Adjust`, `.Simulate`),
  `Financial.RateLines` (+ `.Update`: manual lines and adjustments), `Financial.RelationFinancialSettings` (+ `.Update`);
  `Integrations.Routing` (read: the routing status endpoint). Seeded to the admin role on both sides.
- **Input**: enums, ranges, lengths and child counts bounded (≤ 200 lines, ≤ 50 stops per line, ≤ 100 stops per trip,
  ≤ 500 rate lines per order, ≤ 10,000 rules per list); ids checked through published lookups; attributes bounded.
- **Attack surface**: new conventional controllers under the module roots, all `[Authorize]`; the hub unchanged
  (watch permissions per target type); the CMR download returns a stored document the caller may read
  (`Orders.TransportOrders`), never a path.
- **Tenant isolation**: every aggregate `IMultiTenant`; jobs switch tenant explicitly (design 49); readers are
  tenant-filtered; the route cache is per tenant database.
- **Secrets**: routing hosts and keys, pdfgeneratorapi key in configuration and user secrets only; the OpenTMS literals
  are not copied.
- **Privacy**: see the PBI note; no personal data in logs or events (ids only).

## Build notes

1. **2026-10-26, background paths run without a user.** In the browser run a confirmed order never got legs: the outbox
   dispatcher's `OrderLegsHandler` called `IOrderPricingReader`, which carried `[Authorize]`, and outbox handlers and the
   Hangfire derived-value jobs have no current user, so every delivery threw `AbpAuthorizationException` and the
   tenant's outbox stalled behind it. The tests missed it because the test base granted every check
   (`AddAlwaysAllowAuthorization`) and ran as a fake admin. Now the published cross-module readers (`MasterDataReferenceLookup`,
   `OrderPricingReader`, `TransportOrderLookup`, `FinancialReferenceLookup`, `RateLineReader`, `PricePreviewService`,
   `LegReader`, `TransportTripLookup`) are in-process (`[RemoteService(IsEnabled = false)]`) and anonymous: they carry no
   `[Authorize]` at all, so ABP attaches no authorization interceptor (ABP honours `[AllowAnonymous]` on methods only;
   the class-level one states the intent), and the HTTP-facing app service that calls them authorizes. Every handler,
   calculator and the template materialisation worker reach other modules only through these readers, domain services
   and repositories. Two guards in `AuthorizationCoverageTests` hold it: an implementation of an `OpenTms.*.Published`
   interface has no `[Authorize]` on its class or methods, and an `[AllowAnonymous]` application service or controller
   action is `[RemoteService(IsEnabled = false)]` or on the (empty) allow-list of deliberate anonymous HTTP endpoints.
   The test base now refuses `[Authorize]` and policy checks for a caller without a user
   (`AddSignedInUserAuthorization`). The chain test, the derived-value, rate-line and Planning calculator tests run the
   dispatcher and the jobs through `AsBackgroundProcess()` (no user), and the hourly template run has an anonymous test.
