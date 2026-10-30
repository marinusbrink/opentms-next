# Pricing rebuild: assessment of the OpenTMS price and cost engine and the rate-line target

Date: 2026-10-25. Scope: price lists, the sales and purchase price calculation (`QueryFreightPrices`), discounts and
surcharges on orders, additional services, fixed prices, trip cost and the allocation of cost and revenue over legs.
Purpose: input for the Financial part of the Transport run (design 57 in the numbering proposed by
[transport-rebuild-assessment.md](transport-rebuild-assessment.md) §8). Detail with citations:
[transport-assessment/pricing.md](transport-assessment/pricing.md) (10 sections, 80 scenarios).

Decisions this document builds on: AD-07 (derived values stored, background, pushed), AD-08 (one rate-line model),
AP-07 (one rate-line table, PROPOSED), design 52 (decimal money, validity rows, D&S value kinds, `AdditionalService`
without a price, `DiscountOrSurcharge.Code` as the stable key), design 49 (the derived-value mechanism). **AO-11 is open**
(transport assessment Q1). The price-list model has no decision (design 52 assumption 6): section 3 proposes one (Q3).

## 1. What OpenTMS does, in one page

- **Price list** (`pricing.md` §1): a header with `Outbound`/`Inbound` type, validity, a multiplier option (per unit, per
  line, per amount, per last waypoint), a graduated-lookup option (which quantity picks the column: this line's value or
  a sum over sibling lines sharing the lane, optionally grouped by unit or product), a relation-lookup option (match on
  first, last or both stops), a unit conversion factor and **one hundred `Offset` columns** = the upper bounds of the
  horizontal brackets. **Items** are lane rows: first/last relation or region, transport unit, product, vehicle type,
  one `RowOffsetProperty` (amount, positions, weight, colli, volume, loading metres, load factor, EPQ, distance,
  duration) with **thirty `RowOffset` columns** = the vertical brackets, and **one hundred `Price` cells**. Both axes are
  addressed by reflection over string-built property names; a second copy of the column loop is unused.
- **Linking** (`pricing.md` §1): an order (and a relation as default) has **twelve `ChargeBy<Basis>` booleans each with a
  `PriceListId`**, a fixed-price flag, a "highest wins" flag and a pipe-joined string of D&S ids.
- **Sales calculation** (`pricing.md` §2): per line and basis, resolve the lane (exact relation beats region, first/last
  per the lookup option), the column quantity, the item through a **specificity cascade** (unit + vehicle type + product →
  two → one → none), the row bracket by this line's own value, then `Price = round(quantity × unit price, 2)`. Manual and
  adjusted lines survive a requery (flags set by the **client**). Per-km D&S become "manual" distance lines recognised
  later only by `Description == Name`. No validity check, no currency, min/max dead, every list line `OrderNumber = 1`,
  highest-wins can delete manual lines.
- **Purchase calculation** (`pricing.md` §3): a 250-line twin of the sales calculator on the carrier's
  `IncomingInvoicesChargeBy…` set, per *segment* (one line on one trip, distance allocated over legs with the COFRET rule);
  plus an **older second engine** that computes expected incoming invoices per trip. Trip cost reads the incoming
  invoices, not the purchase lines.
- **D&S** (`pricing.md` §4): applied in two passes (before/after) over the running subtotal, selected in Angular by weekday,
  destination region and limit-to-one; values read **live** at every revenue computation and at invoicing; four copies of
  the applier.
- **Cost and allocation** (`pricing.md` §5): trip cost = incoming invoices (actual ?? expected) + own-fleet €/km
  (vehicle → vehicle type → tenant setting) × distance, only when the carrier is the own company; order cost of revenue =
  trip cost when alone on the trip, else a leg-km share; trip revenue = Σ leg km × order revenue per km. All computed on
  every grid read in three implementations.
- **When it runs** (`pricing.md` §6): synchronously in the request from four Angular call sites, on accept, return
  orders, redistribution, imports, FMS completion, and inside an approval validator; invoicing only reads stored lines.
- **Maintenance** (`pricing.md` §7): CRUD, copy with new validity, percentage adjustment with rounding to cent / quarter /
  half / whole euro and re-pointing relation defaults, header-only Excel export, no import. A redesign mockup
  (`pricing.md` §8) sketches rules ordered by specificity tier with a cascade trace.

## 2. Mapping OpenTMS concepts onto the rate-line model (AD-08)

| OpenTMS | New (Financial, submodule `Tariffs`) | Anchor | Side |
|---|---|---|---|
| `TransportOrderFreightPrice` type Sales | `RateLine` | order line | Revenue |
| `TransportOrderFreightPrice` type Purchase (`TransportFileId`) | `RateLine` | leg (the leg of that line on that trip) | Cost |
| `ChargeFixedPrice` + `FixedPrice` on the order | `RateLine` with `OverridesChildren = true` | order | Revenue |
| D&S id in the pipe string (percentage, fixed) | `RateLine` with `DiscountOrSurchargeId` and the value row used | order | Revenue (or Cost per `Side`) |
| Per-km D&S "manual distance line" | `RateLine` with `DiscountOrSurchargeId`, `Quantity` = km, `QuantitySource` Distance | order line | Revenue |
| Toll-only D&S | same, `QuantitySource` TollDistance (from `IRoutingService`) | order line | Revenue |
| `TransportOrderAdditionalService` | `RateLine` with `AdditionalServiceId`, quantity in the service's unit | order (or stop when the design adds a stop anchor for waiting time) | Revenue |
| Outsourcing incoming invoice with `ExpectedPrice` / "Tevens trip" buying price | `RateLine` (agreed carrier price) | trip | Cost |
| Own-fleet €/km × distance | `RateLine` with `QuantitySource` TripDistance, unit price from vehicle/type/tenant | trip | Cost |
| Incoming invoice `ActualPrice` | the purchasing run: actual cost line replaces the expected one | trip | Cost |
| `TransportOrderAttributes.Revenue`, grid cost/profit | derived values on order, trip and leg (transport assessment §4.11) | — | — |

`RateLine` (AP-07, one table `financial."RateLines"`): `TenantId`, `Side` (Revenue/Cost), exactly one anchor of
`OrderId`, `OrderLineId`, `LegId`, `StopId`, `TripId` (check constraint; ids without FKs across modules, AO-11), `Kind`
(PriceList, Manual, FixedPrice, DiscountOrSurcharge, AdditionalService, AgreedPrice, OwnFleetDistance), `Position`,
`Description` (localised key or free text), `Quantity` decimal(18,4), `QuantityUnit` enum, `QuantitySource` enum
(Manual, LineQuantity, Positions, Colli, WeightKg, VolumeM3, LoadingMeters, LoadFactor, Epq, Distance, TollDistance,
Duration, VehicleType, PerLine, TripDistance), `UnitPrice` decimal(18,4), `Amount` decimal(18,2), `CurrencyCode`,
`OverridesChildren`, `IsManual`, `IsAdjusted`, `AppliesAfterOthers` (D&S order), and **provenance**: `PriceListId`,
`PriceListRuleId`, `PriceListBracketId`, `DiscountOrSurchargeValueId`, `VatPercentageRateId`, `CalculatedAt`,
`CalculationVersion`. Money is `numeric` (design 52 §3.5). The total of a level is its overriding line if present,
otherwise the sum of its children (AD-08); detail lines under a fixed price stay as the breakdown and as the allocation
key.

## 3. Price-list model proposal (Q3)

Replaces the 100 × 30 × 100 matrix while keeping every lookup capability `pricing.md` §1–2 documents.

| Entity | Fields | Replaces |
|---|---|---|
| `PriceList` | `Code`, `Name`, `Side` (Revenue = outbound, Cost = inbound), `CurrencyCode`, `Basis` (`QuantitySource` of the rule's unit price: per pallet, per kg, per km, per hour, per m³, per loading metre, per EPQ, per load factor, per vehicle type, per line), `Multiplier` (`PerUnit`, `PerLine`, `PerLineQuantity`, `PerLastStop`), `ColumnQuantity` (what picks the column bracket: this line's basis value, the sum over lines sharing the lane, grouped by unit / product / both, or weight, volume, loading metres, distance, duration of this line or of the lane), `StopMatching` (First, Last, Both), `UnitConversionFactor` decimal, `ValidFrom`/`ValidTill` (DateOnly, enforced), `IsArchived`, remarks | header + the twelve `ChargeBy` slots: a list knows its own basis |
| `PriceListColumn` | child, `Position`, `UpperBound` decimal (null = open-ended) | `Offset0..99` |
| `PriceListRule` | child, the lane and dimensions: `FromRelationId` / `FromRegionId`, `ToRelationId` / `ToRegionId` (at least one side set), `TransportUnitId?`, `ProductId?`, `VehicleTypeId?`, `RowQuantity` (`QuantitySource` of the row brackets), `MinAmount?`, `MaxAmount?` (made real), `Position` | item lane + dimensions + `RowOffsetProperty` |
| `PriceListBracket` | child of a rule, `Position`, `UpperBound` decimal (null = infinity row), `Prices` decimal[] with one value per column (stored as a child table `PriceListCell (BracketId, ColumnPosition, UnitPrice)` or as a jsonb array per AD-06; the design picks, cells as rows are the safer default for constraints) | `RowOffset0..29` × `Price0..99` |
| `RelationPriceLists` (Financial `RelationFinancialSettings`, assessment §2) | per relation: ordered list of `PriceListId`s for revenue, another for cost (as carrier), `MultipleMatches` (`All`, `HighestWins`, `LowestWins`), default D&S with validity, default VAT, payment term, fixed-price default | the twelve relation slots and `IncomingInvoicesChargeBy…` |
| Order financial terms (Orders, ids only) | the order's effective list of `PriceListId`s, `MultipleMatches`, D&S ids, fixed price | the twelve order slots and the pipe string |

Lookup algorithm (ported, made explicit and testable): filter rules of each applicable list on validity at the **as-of
date** (QP1), on the lane (exact relation beats region, per `StopMatching`), then the specificity cascade over the three
dimensions (3 → 2 → 1 → 0 matching, exact relation before region inside a tier — the mockup's tiers T3…T0), then the row
bracket by this line's `RowQuantity` value (upper bound inclusive, infinity row last), then the column by the list's
`ColumnQuantity` (upper bound inclusive, open-ended last). Unit price × quantity per `Multiplier`, divided by the
conversion factor, clamped to min/max, rounded to 2 decimals at the line, never earlier. A list whose match fails
produces **no line** (not an empty placeholder). A `Simulate` query returns the winning rule and the ranked candidates
(the mockup's cascade trace) and is what the price-list editor and the tests use.

## 4. Calculation in the new architecture

### 4.1 Sales rate lines of an order: flow 2

The order save (any command that changes lines, stops, financial terms or distances) ends with
`IDerivedValueScheduler.RequestAsync("Financial.OrderRateLines", orderId, reason)`; the Financial calculator
`OrderRateLineCalculator` (registered in Financial's `ConfigureServices`, design 49) reads the order through Orders'
published read contract (`IOrderPricingReader` in Orders `Application.Contracts`: lines, stops with relation/address/
region ids, distances from Planning's legs through `ILegReader`), the relation's lists and D&S through Financial's own
tables, and **replaces** the order's non-manual revenue rate lines in its own transaction; manual lines are re-valued
from the current quantities, adjusted lines keep their unit price (as OpenTMS, scenarios 29–32). It then publishes
`RateLinesChanged { OrderId, Version }` (outbox) so Orders stores revenue/margin and Planning allocates (transport
assessment §4.11), and the screen gets the push. Reads never calculate.

### 4.2 Cost rate lines of a trip: flow 2

`TransportTripLegsChanged`, `TransportTripCarrierChanged`, `TransportTripDistanceChanged` (Planning events) trigger
`TripRateLineCalculator`: per leg on the trip, the carrier's cost lists over the segment (distance allocated over legs
by tonne-km as the COFRET rule did, or the leg's own distance when coordinates are missing — `pricing.md` scenarios
57–58); the own-fleet distance line on the trip when `Execution = OwnFleet`; the agreed price line untouched. Trip cost =
the trip's overriding cost line if any, else Σ cost lines. Publishes `RateLinesChanged { TripId }`; Planning's
calculator writes the allocation per leg (Q1).

### 4.3 The one synchronous call: price preview

The order screen must show a price while the clerk types, before anything is saved. Design-stated exception to module
rule 3: Orders (the screen's API) calls `IPricePreviewService.PreviewAsync(OrderPricingSnapshot)` in Financial
`Application.Contracts`, a **pure** function over a snapshot DTO (no persistence, no event), returning the would-be rate
lines with provenance. The same code path the calculator uses; p95 budget < 500 ms for an order of 20 lines. Nothing
from the preview is posted back; the save recalculates (§4.1).

### 4.4 Provenance, validity and money history

Every calculated line stores the rule, bracket, D&S value row and VAT rate row it used (`§2`), so a later change to a
list or surcharge never changes an existing line (design 52 §3.4); a retroactive change is an explicit bulk
`POST /api/platform/derived-value/recalculate` over the affected orders (design 49), which rewrites non-manual lines
and keeps the old amounts in the audit log. VAT: the order's VAT category rate on the order date is stored per line as
provenance; VAT amounts are the invoicing run's.

## 5. Discounts and surcharges, additional services, fixed price

- D&S applicability (weekday, regions at loading/unloading, `LimitToOneOrder`) is evaluated **server-side** in the
  calculator from design 52's `DaysOfWeek`, regions and `Side`; the order holds the selected D&S ids (relation defaults
  valid on the as-of date plus manual picks or removals, as OpenTMS scenario 5 of `screens.md`). Percentage lines are
  computed over the sum of the lines they apply to in two passes (`AppliesAfterOthers`), fixed lines once, per-km lines
  per order line with `Quantity = distance × (1 or 2 for round trip) × billable percentage`, toll lines with the tolled
  kilometres from `IRoutingService`. Linked by **id**, never by name (`pricing.md` §9 row 3).
- Additional services: rate lines with `AdditionalServiceId`, quantity in the service's `Unit` (design 52), unit price
  entered or from a later price-list basis; VAT category from the service.
- Fixed price: one revenue line on the order with `OverridesChildren`; detail lines remain (AD-08). The OpenTMS refusal
  "fixed price with freight lines present" disappears: both exist by design.
- Multiple matching lists: `MultipleMatches` per relation/order (`All` default, `HighestWins`, `LowestWins`) applied to
  **calculated** lines only; manual lines are never pruned (`pricing.md` §9 row 11).

## 6. Price-list maintenance (Master data app, Financial → Pricing → Price lists)

List and editor per the design-41/52 shape; the editor follows the mockup: rules ordered by tier with "Overrides" and
"Defaults", a rule in three questions (where, for which freight, what does it cost) with the bracket ladder, and a
**Simulate** panel (lane, dimensions, quantities → winning rule and ranked candidates). Operations: copy with new
validity, **adjust** (percentage or absolute amount, rounding to cent / 0.05 / 0.25 / 0.50 / whole, optional re-point of
relation defaults, as a preview first), archive, Excel **export and import** of rules and brackets (OpenTMS has no import),
in-use check on delete (relations, orders with lines referencing it: refuse, offer archive).

## 7. Findings not to clone

`pricing.md` §9 lists fourteen, with citations. The ones that shape the design: the fixed-width reflective matrix;
`double` for money; name matching for per-km lines; live D&S reads; computing revenue, cost and margin on every grid read
in three implementations; no validity enforcement; sales/purchase twin calculators and a second purchase engine; UI-side
rules with an "authoritative vs stale selection" dual path; per-km surcharges as fake manual lines; `OrderNumber`
collisions and highest-wins deleting manual lines; two meanings of "FixedPrice"; dead `MinPrice`/`MaxPrice`; persisted
empty placeholder lines.

## 8. Functional scenario catalogue

`pricing.md` §10, scenarios 1–80, grouped: lookup 1–28, manual/requery/fixed 29–39, D&S/additional/toll 40–55,
purchase/cost/allocation 56–69, approval/invoicing 70–80 (70–73 belong to approval, Q6; 74–79 to the invoicing run and
are listed so the rate lines carry what invoicing needs). Scenario 26 (min/max ignored) and 27 (expired list applied)
invert in the rebuild: min/max apply, an expired list does not. Scenario 37 (fixed price refuses lines) inverts (§5).
Scenario 44 (a region surcharge charged twice through duplicate ids) becomes "charged once per matching order line, or
once per order when `LimitToOneOrder`". Added by the architecture: the save returns before prices exist and the Pricing
tab shows *Calculating…*; a preview while typing; a bulk retroactive re-run; a price list with mixed currencies is
refused.

## 9. Pricing decisions needed (PO), with recommendations

| # | Question | Recommendation |
|---|---|---|
| **Q3** | The model of §3. | Adopt. |
| **QP1** | **As-of date** for list and D&S validity: order date, first loading date, or last unloading date? OpenTMS uses the order date for relation defaults and nothing for lists. | Loading date of the line's first stop for revenue; trip date for cost; configurable per tenant later only if asked. |
| **QP2** | **Currency**: OpenTMS ignores it in pricing. | A list has one currency; an order has one currency (relation default); a list in another currency is refused (no FX conversion until `CurrencyDayRate` is actually built, design 52). |
| **QP3** | Keep the four **multiplier** options and the fourteen **column-quantity** options? | Keep all (each has a scenario in `pricing.md` 12–23); drop nothing without a customer check. |
| **QP4** | **Rounding**: unit price 4 decimals, line amount 2, totals the sum of rounded lines (invoice-compatible), or round only the totals? | Line amount rounded to 2 at the line; totals are sums of lines. Unit price decimal(18,4). |
| **QP5** | Replace the twelve `ChargeBy` slots by **a list of price lists** whose basis is on the list (§3)? | Yes; the migration maps each filled slot to the list it points at. |
| **QP6** | **Manual lines**: keep the OpenTMS semantics (manual lines re-valued from current quantities, adjusted lines keep their unit price, both survive requeries)? | Yes, with the flags set by the server from the command used (`AddManualRateLine`, `AdjustRateLine`), never by the client. |
| **QP7** | **Carrier (cost) price lists** per relation with the same model, computed per leg segment on the trip? | Yes (§4.2); the separate "expected incoming invoice" engine is not ported — the trip's cost lines are the expected cost, the purchasing run matches the carrier's invoice against them (chartermatch). |
| **QP8** | **Min/max** per rule: implement or drop? | Implement (the schema exists since 2026-10-20, so a customer asked for it). |
| **QP9** | **Highest/lowest wins** across lists: keep? | Keep as `MultipleMatches` on calculated lines only. |
| **QP10** | **Own-fleet cost per km** source: vehicle → vehicle type → tenant setting (OpenTMS), and nothing per hour? | Keep the cascade (design 41 parked `VehicleType.CostPricePerKm` for exactly this run); add `CostPricePerHour` as an optional second own-fleet line only if the PO wants it now. |
| **QP11** | Where do **waiting hours and other stop-bound services** anchor: stop (AD-08 says a stop) or order? | Stop, when the stop is a trip stop with actuals (Planning); order otherwise. The first run anchors additional services to the order and adds the stop anchor with the POD/actuals run. |
