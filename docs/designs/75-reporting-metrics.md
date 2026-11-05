# Design 75: Reporting — metrics for open questions through the AI connector

**PBI:** Marinus, 2026-11-06 (chat): customers can think of countless questions, so how do we make sure they can be answered,
with short response times? He accepted the proposal of a fixed list of metrics and breakdowns on Reporting, as a design of
its own after design 74.
**Status:** in review. Risk class **high** (tenant data in a new read path, money behind permissions); critical parts named
under *Test risk analysis*. Builds on design 74, which must be built first.
**Date:** 2026-11-06

## PO decisions (2026-11-06)

Recorded as AD-23 in `docs/architecture/decisions.md`.

1. **No tool per question, and no SQL from a model.** One tool answers from a fixed list of metrics, breakdowns and filters.
2. **Numbers come from Reporting's own tables,** prepared in advance and updated through events.
3. **The promise is honest:** every question gets a correct answer or a clear "not available", never a guess. Every answer
   states its definition, its period and what it left out.

**For gate 1: added by this design, beyond the three decisions.**

- **The first list of metrics** and its definitions, above all *on time*: a stop is on time at most 15 minutes after its
  planned arrival. The 15 minutes is this design's choice.
- **Left out of the first list:** the region breakdown from the earlier example (see *Out of scope*), drivers (PO decision 4
  of design 74).
- **Three new events.** AO-01 keeps events open and says a design depending on it stops, so these three need the PO's
  approval here: `TransportOrderRecalculatedEto`, `TransportOrderDeletedEto`, `TransportTripDeletedEto`. The rest of AO-01
  stays open.
- **Denormalised tables, on purpose.** The PO rejected Planning-owned read tables for the planboard. These tables are not that:
  they are Reporting's, serve no planboard and hold no copies of names. They are still a second copy of numbers, so the PO
  sees that here.
- **Feature `Reporting.Metrics`,** two new published interfaces and the two background processes below.

## How the question types are answered

| Question | Example | Answered by |
|---|---|---|
| Finding a record | "Where is order 123?" | The search and detail tools of design 74 |
| Numbers and totals | "Revenue per customer per month" | `query_metrics` |
| Combining and explaining | "Which customer fell the most?" | The AI tool, calling `query_metrics` more than once |
| Anything else | "Why is this customer angry?" | "Not available": the tool's answer says what does exist |

## Domain impact

**Architecture references.** AD-23, AD-22 (the connector), AD-07 and flow 2 of `02-flows.md` (a stored calculated value uses
the derived-value mechanism; **this design extends flow 2**), AD-16 (events through the outbox), AD-03. The domain map gives
Reporting "dashboards, data warehouse, sustainability/CO₂; read-only". Dashboard and Reports may later read the same tables,
but no screen is part of this design.

**Module: Reporting.** It owns its entities, its `reporting` schema and the metric catalog.

**New entities** (`reporting` schema, in the tenant's database, `IMultiTenant`; ids only, no names):

`OrderFact`, one row per order:

| Field | Type | Note |
|---|---|---|
| `Id` | `Guid` | The order's id |
| `TenantId` | `Guid?` | |
| `OrderDate` | `DateOnly` | |
| `OrderWeek`, `OrderMonth` | `DateOnly` | Monday of the week and first of the month, for grouping |
| `Status` | `TransportOrderStatus` | |
| `CustomerRelationId` | `Guid` | |
| `OrganizationUnitId` | `Guid?` | |
| `TransportOrderTypeId` | `Guid?` | |
| `CurrencyCode` | `string(3)` | |
| `RevenueAmount`, `CostOfRevenueAmount`, `MarginAmount`, `Co2Kg` | `decimal?` | Null while not calculated |
| `FactUpdatedAt` | `DateTime` | |

`TripFact`, one row per trip:

| Field | Type | Note |
|---|---|---|
| `Id` | `Guid` | The trip's id |
| `TenantId` | `Guid?` | |
| `TripDate` | `DateOnly` | |
| `TripWeek`, `TripMonth` | `DateOnly` | |
| `Status` | `TransportTripStatus` | |
| `Execution` | `TripExecution` | Own fleet, subcontracted or carrier |
| `CarrierRelationId` | `Guid?` | |
| `PrimaryVehicleId` | `Guid?` | The first vehicle by position; a trip counts once |
| `OrganizationUnitId` | `Guid?` | |
| `TransportTripTypeId` | `Guid?` | |
| `CurrencyCode` | `string(3)?` | See *Assumptions* |
| `CostAmount`, `RevenueAmount`, `Co2Kg`, `LoadFactor` | `decimal?` | |
| `PlannedDistanceM`, `ActualDistanceM` | `int?` | |
| `StopsMeasured`, `StopsOnTime` | `int` | Stops with both a planned and an actual arrival; of those, on time |
| `FactUpdatedAt` | `DateTime` | |

**Indexes,** with `IncludeProperties` of the amounts so that a group query reads only the index:

- `OrderFact`: (`TenantId`, `OrderDate`), (`TenantId`, `CustomerRelationId`, `OrderDate`), (`TenantId`, `TransportOrderTypeId`,
  `OrderDate`), (`TenantId`, `OrganizationUnitId`, `OrderDate`).
- `TripFact`: (`TenantId`, `TripDate`), (`TenantId`, `CarrierRelationId`, `TripDate`), (`TenantId`, `PrimaryVehicleId`,
  `TripDate`), (`TenantId`, `TransportTripTypeId`, `TripDate`), (`TenantId`, `OrganizationUnitId`, `TripDate`).

**How a fact stays current (flow 2).** Target types `Reporting.OrderFact` and `Reporting.TripFact`, registered with
`AddCalculator` (watch permissions `Orders.TransportOrders` and `PlanningExecution.TransportTrips`; no screen watches them
yet). Handlers in `OpenTms.Reporting.Application` skip a tenant without the feature, and otherwise call
`IDerivedValueScheduler.RequestAsync`. The calculator reads the source through the published reader and upserts or deletes
the fact. It is idempotent, and a newer save discards an older run, as for every derived value.

| Event | Emitted by | Consumed by | New? |
|---|---|---|---|
| `TransportOrderRecalculatedEto { OrderId, Version }` | Orders (`OrderSummaryCalculator`, as `TripCalculator` does) | Reporting | **New** |
| `TransportOrderStatusChangedEto` | Orders | Reporting | Existing |
| `TransportOrderDeletedEto { OrderId }` | Orders (`DeleteAsync`, `BulkDeleteAsync`) | Reporting | **New** |
| `TransportTripRecalculatedEto` | Planning & Execution | Reporting | Existing |
| `TransportTripStatusChangedEto` | Planning & Execution | Reporting | Existing |
| `TransportTripChangedEto` | Planning & Execution | Reporting | Existing |
| `TransportTripDeletedEto { TripId }` | Planning & Execution (`DeleteAsync`) | Reporting | **New** |

**Cross-module synchronous calls (module boundary rule 3): stated here on purpose.** All are in-process and read-only.

| Caller | Interface | Status |
|---|---|---|
| Reporting | `IOrderFactReader.GetAsync(orderId)`, `.GetIdsAfterAsync(afterId, take)`, `.GetIdsModifiedSinceAsync(since, afterId, take)` in `OpenTms.Orders.Application.Contracts/Published/` | **New** |
| Reporting | `ITripFactReader`, the same three methods, in `OpenTms.PlanningExecution.Application.Contracts/Published/` | **New** |
| Reporting | `IMasterDataReferenceLookup`, for relation and vehicle names at read time | Existing |
| Reporting | `IOrderFactReader` and `ITripFactReader` also return type names, each module's own | New, as above |
| Integrations | `IMetricQuery` in `OpenTms.Reporting.Application.Contracts/Published/` | **New** |

Reporting reads no other module's table and joins nothing across schemas. Names are looked up per request for at most 100
rows, so a renamed customer is right at once and no copy of a name exists.

**Out of scope:**

- **A region breakdown.** A geographical region needs the rule that places an order or trip in a region, which does not
  exist. It returns as an addition to the catalog with that rule.
- **Drivers,** by PO decision 4 of design 74.
- **Order lines, products and legs as a grain,** such as revenue per product. A later catalog addition, which would add
  a third fact table.
- **Screens,** Dashboard and Reports. Write tools. Forecasts and targets. Comparing tenants, which constitution rule 1 forbids.
- **`get_customer_revenue`** stays as design 74 defines it. A test keeps both answers equal; retiring it is a later choice.

## API contract

**No HTTP route for tenants' browsers.** `IMetricQuery` is in-process, so the OpenAPI document and typed client do not change.
Both tools are added to `/mcp` of design 74, with all of its checks.

### `list_metrics`

No input. Answers only what this user may ask: `{ metrics[{ key, unit, description, definition, breakdowns, filters }], factsReady }`.

### `query_metrics`

| Input | Rule |
|---|---|
| `metrics` | 1 to 5 keys, all of one grain (order or trip) |
| `dateFrom`, `dateTo` | Required; at most five years |
| `groupBy` | 0 to 2 breakdowns allowed for that grain |
| `filters` | `status[]`; order grain: `customerRelationId`, `transportOrderTypeId`, `organizationUnitId`; trip grain: `carrierRelationId`, `vehicleId`, `transportTripTypeId`, `execution`, `organizationUnitId` |
| `sort` | A chosen metric, or a period ascending; default the first metric descending |
| `top` | Default 25, at most 100 groups |

**Answer**

```
{
  status: "ok",
  period: { dateFrom, dateTo },
  definitions: [{ metric, text }],
  rows: [{ groups: [{ breakdown, id?, name }], currencyCode?, values: { <metric>: number | null } }],
  totalGroups, truncated,
  coverage: { recordsIncluded, recordsWithoutValue, excludedByStatus },
  lastFactUpdateAt
}
```

Amounts are **never added across currencies**: a money metric always splits by `currencyCode`, on top of `groupBy`.

**Refusals** come back as a tool result, not a server error, so the AI tool can say what happened. `{ status, message, hint }`:

| Status | When |
|---|---|
| `unknown_metric`, `unsupported_combination` | Not in the catalog, or a breakdown of the other grain; `hint` names what is allowed |
| `invalid_period` | Missing, reversed or over five years |
| `not_allowed` | The user lacks a metric's permission or feature. Same answer whether or not the metric exists, for metrics the user cannot see |
| `not_ready` | The backfill of this tenant has not finished; `hint` gives the progress |
| `timeout` | The query passed 2 seconds; `hint` asks for a shorter period or a filter. Never a partial result |

### The catalog (a list in code, in `OpenTms.Reporting.Domain.Shared/Metrics/`)

Default statuses: orders `Open`, `ReadyForInvoicing`, `Invoiced`, `Archived`; trips every status except the cancelled one.

**Order grain.** Breakdowns: `customer`, `order_type`, `organization_unit`, `status`, `day`, `week`, `month`. Features for all:
`Apps.Transport`, `Apps.Transport.Orders`.

| Metric | Definition | Permissions |
|---|---|---|
| `order_count` | Number of orders by order date | `Orders.TransportOrders` |
| `order_revenue` | Sum of stored calculated revenue. Not invoiced revenue | `Orders.TransportOrders`, `Financial.RateLines` |
| `order_cost` | Sum of stored cost of revenue | `Orders.TransportOrders`, `Financial.RateLines` |
| `order_margin` | Sum of stored margin | `Orders.TransportOrders`, `Financial.RateLines` |
| `order_co2_kg` | Sum of stored CO₂ in kg | `Orders.TransportOrders` |

**Trip grain.** Breakdowns: `carrier`, `vehicle`, `trip_type`, `execution`, `organization_unit`, `status`, `day`, `week`, `month`.
Features for all: `Apps.Transport` or `Apps.Planboard`, and `Apps.Transport.Trips` or `Apps.Planboard`.

| Metric | Definition | Permissions |
|---|---|---|
| `trip_count` | Number of trips by trip date | `PlanningExecution.TransportTrips` |
| `trip_cost` | Sum of stored trip cost | `PlanningExecution.TransportTrips`, `Financial.RateLines` |
| `trip_revenue` | Sum of stored revenue allocated to the trip | `PlanningExecution.TransportTrips`, `Financial.RateLines` |
| `trip_margin` | `trip_revenue` minus `trip_cost`, over trips that have both | `PlanningExecution.TransportTrips`, `Financial.RateLines` |
| `trip_co2_kg` | Sum of stored CO₂ in kg | `PlanningExecution.TransportTrips` |
| `average_load_factor` | Plain average over trips with a load factor | `PlanningExecution.TransportTrips` |
| `planned_distance_km`, `actual_distance_km` | Sum of stored distance, in km | `PlanningExecution.TransportTrips` |
| `on_time_stop_percentage` | Stops at most 15 minutes after planned arrival, as a share of stops with both a planned and an actual arrival. Coverage reports how many trips had none | `PlanningExecution.TransportTrips` |

Query implementation: LINQ over the facts through ABP's repositories, so the tenant filter applies; no raw SQL and no
`IgnoreMultiTenancy`. Adding a metric or breakdown means one catalog entry and, when a column is new, one column, one index and a
backfill.

## Migration strategy

Expand-only. One migration on `ReportingDbContext` adds both tables and indexes; the DbMigrator applies it everywhere. Facts stay
empty until a tenant turns the feature on.

- **Backfill:** a `ReportingFactBackfillWorker` (`AsyncPeriodicBackgroundWorker`, every 15 minutes, per tenant in its own context,
  like `TemplateMaterialisationWorker`) finds tenants with the feature on and no `Reporting.Metrics.BackfillCompletedAt` setting.
  It requests 500 orders and 500 trips per pass through the derived-value scheduler, keeps its position in a setting, and sets
  the completion setting at the end. Nothing else writes the facts, so a concurrent save cannot conflict. Until it completes
  `query_metrics` answers `not_ready`.
- **Repair:** a nightly `ReportingFactRepairWorker` requests recalculation of records modified in the last 48 hours. Delivery is
  at least once and failing events have no dead-letter store (AO-10), so this catches a lost update within a day.
- **Feature off, then on again:** facts go stale while it is off. The nightly worker clears the completion setting of a tenant whose
  feature is off, so turning it on starts a new backfill. A feature turned off and on within one day keeps its facts, and the 48-hour
  repair covers that gap.

There is no contract step. Removing the feature leaves unused rows, which tenant deletion removes with the database.

## UI design

Not applicable: no screen. The feature appears in the Tenants app like every feature. The manual page
`docs/manual/ai-koppeling.md` of design 74 gains a section on the questions it answers, the definitions, and that revenue here is
calculated revenue rather than invoiced.

## Test risk analysis

| Part | Risk | Must prove |
|---|---|---|
| Tenant isolation | **Critical** | On PostgreSQL, for tenants with their own database and a shared-tier pair: each tool, each grain and a group query return only the tenant's facts; a job and a handler write only under the raising tenant; a filter id of another tenant gives zero rows and no name |
| Money behind permissions | **Critical** | Each money metric refused without `Financial.RateLines`, absent from `list_metrics`, and refused in a mix with an allowed metric; no refusal reveals a value |
| Correctness | **High** | For a seeded dataset, each metric, breakdown and filter equals a sum computed independently from the source tables; `order_revenue` per customer equals `get_customer_revenue`; several currencies never add up; null counts in coverage, not as zero; on-time boundary at exactly 15 minutes |
| Fact updates | **High** | On PostgreSQL through the outbox: a change, status change and delete each reach the fact; a duplicate event changes nothing; a newer save discards an older run; a lost event is repaired by the nightly worker |
| Backfill | **High** | Resumes after an interruption; `not_ready` until complete; results equal the source afterwards; no run for a tenant with the feature off; feature off and on restarts it |
| Module boundaries | **High** | No cross-schema mapping or join; the architecture tests pass with the new references |
| Performance | **High** | k6 and PostgreSQL volume test: p95 < 300 ms for `query_metrics`, warm, at 1,000,000 orders and 250,000 trips in one tenant, at the worst case of a year's data grouped by customer and month. A miss goes to the PO, with a day-level rollup table as the prepared fallback |
| Freshness | **Medium** | p95 under 60 seconds from a save to the fact, under normal job load |
| Refusals and limits | **Medium** | Each status above; `top` capped; a 2-second timeout gives `timeout`, not a partial answer; logs hold no filter values |
| Answer quality | **Medium, manual** | A list of 50 to 100 questions with known answers at `docs/testing/ai-connector-questions.md`, run on staging per release on ChatGPT, Claude and Gemini. Results go to the PO. The PO supplies real customer questions; a model can still explain a right number wrongly, which is what this measures |

## Flag & rollout plan

- **Feature:** `Reporting.Metrics`, default off per tenant, set by the host. It only has an effect with `Apps.Integrations` and
  `Apps.Integrations.AiConnector` on. Without it, both tools are absent from `tools/list`, the handlers skip, and no fact is written.
- **Order:** staging with `demo` → the health tenant → friendly tenants → all on request. Each tenant's backfill finishes before
  its first answer.

## Cost & SLO impact

- **Tokens:** none for us; the customer's AI subscription pays. Fewer, smaller tool answers also cost the customer fewer tokens.
- **Compute:** about one to two extra derived-value jobs per save that changes an order or trip, for tenants with the feature on.
  The Jobs role is not split yet (AD-11), so these run in the one host; the load test covers it.
- **Storage:** two small tables and nine indexes per tenant database; the amount is measured in the volume test. No new service.
- **Budgets touched:** interactive reads p95 < 300 ms (the query) and heavy operations "never block a request over 2 s" (the
  timeout). Mutations are not affected: a save only writes outbox rows as today, plus one new event per recalculation.
- **What we do not control:** the time the AI tool itself takes. A complete answer in ChatGPT includes its model time and is measured
  on staging, but no budget of ours can promise it.
- **Margin per tenant:** small and only for tenants with the feature on. Whether to charge for it is the PO's choice and not part of
  this design.

## Assumptions

- **AP-06** (event names) and **AP-17** (the mapping of events onto domains) are followed for the new events. **AO-10** stays open, which
  is why the repair worker exists.
- **Trip currency.** `TransportTrip` stores amounts but no currency for them. `ITripFactReader` takes it from the trip's rate-line
  totals (`IRateLineReader`) at build. If that source has none, the money metrics of the trip grain go back to the PO before they are built.
- **Status names:** the trip's cancelled status and the default statuses are checked against `TransportTripStatus` at build.
- **Names at read time:** `IMasterDataReferenceLookup` returns relation and vehicle names in batches of 100, and organisation-unit names
  come from a published Platform reader. A missing one is added as a published read in the same change.
- **One vehicle per trip for the `vehicle` breakdown:** the first by position. A trailer's numbers appear only under the vehicle in first
  position. The definition says so.
- **Order date** is the period for order metrics, not the first or last stop date. A trip's numbers count by trip date.
- **Reach of the fallback:** covering indexes are expected to meet the budget, without a measurement yet. The volume test decides.
- **15 minutes** applies to all tenants in this version.

## Security quickscan

**New permissions, in full:** none. The metrics use existing permissions:

`Orders.TransportOrders`, `PlanningExecution.TransportTrips`, `Financial.RateLines`. Connecting needs `Integrations.AiConnector.Use`
from design 74.

**New features, in full:** `Reporting.Metrics`.

**Input validation:** keys only from the catalog, typed ids, a limited period, group count and row count. No text filter, so no search on
hidden values and no user-supplied text reaches a query.

**Attack surface:** two more tools on `/mcp`, in-process interfaces, no new route. `IMetricQuery` has no ABP attributes for permissions, so it
checks each metric's permissions and features itself, and a test calls it directly without them.

**Inference from totals:** a user who may read orders can already see them one by one, and an aggregate reveals nothing more. Money stays behind
`Financial.RateLines` in every form, including `order_margin` as a difference.

**Prompt injection:** only numbers, ids and names of master data return. Customer names can carry text, as in design 74; the tool still cannot write.

**Logging (constitution rule 5):** tenant id, user id, metric keys, breakdown keys, status, duration and row count. No filter ids or values.

**GDPR:** the facts hold no names, addresses or driver data, and no user ids. Relation ids of sole traders and vehicle ids are references only:
names come at read time, so anonymising or deleting the relation removes them from answers. Art. 15 and art. 17 need no new route for these tables.
Data sent to an AI vendor is covered in design 74.
