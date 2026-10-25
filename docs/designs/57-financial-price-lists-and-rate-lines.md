# Design 57: Financial — price lists, rate lines, relation financial settings, the calculators

**PBI:** design 56 (umbrella), Financial part
**Status:** approved (PO 2026-10-25: Q3, QP1–QP11 decided)
**Date:** 2026-10-25
**Depends on:** design 50 (reference-data base), design 52 (VAT, D&S, additional services, invoice types), design 49
(derived values), design 56 (ownership, events, calculators). Input: `docs/input/pricing-rebuild-assessment.md` and
`docs/input/transport-assessment/pricing.md` (scenarios 1–80).

## Domain impact

**Module:** Financial, schema `financial`, submodule folder `Tariffs/` (price lists, rate lines, calculators, preview) and
`Invoicing/` (relation financial settings: VAT, payment term). Migration `FinancialTariffs`.

### PriceList (Q3, QP5)

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16) | upper case, unique per tenant (`NULLS NOT DISTINCT`, soft-delete filter) |
| `Name` | string(128) | required |
| `Side` | enum `RateSide { Revenue = 1, Cost = 2 }` | revenue = applied to a customer's order lines; cost = applied to a carrier's legs on a trip |
| `CurrencyCode` | string(3) | ISO 4217, active in Master Data (QP2) |
| `Basis` | enum `QuantitySource` (§Shared enums) | the unit price's unit: per line quantity, kg, km, hour, m³, loading metre, EPQ, load factor, vehicle type, per line |
| `Multiplier` | enum `PriceMultiplier { PerUnit = 1, PerLine, PerLineQuantity, PerLastStop }` | OpenTMS `PriceMultiplierOptions` (QP3) |
| `ColumnQuantity` | enum `ColumnQuantitySource` | which quantity selects the column bracket: `LineBasis`, `SumOfLaneLines`, `SumByTransportUnit`, `SumByProduct`, `SumByTransportUnitAndProduct`, `LineWeightKg`, `LaneWeightKg`, `LineVolumeM3`, `LaneVolumeM3`, `LineLoadingMeters`, `LaneLoadingMeters`, `LineDistanceKm`, `LaneDistanceKm`, `LineDurationH`, `LaneDurationH` (OpenTMS `GraduatedPriceLookupOptions`, QP3) |
| `StopMatching` | enum `StopMatching { First = 1, Last, Both }` | which stop(s) of the line a rule's lane must match |
| `UnitConversionFactor` | decimal(18,6), default 1 | the quantity is divided by it |
| `ValidFrom`, `ValidTill` | DateOnly, DateOnly? | enforced in the calculation on the as-of date (QP1) |
| `IsArchived` | bool | |
| `Description` | string(2500)? | |
| child **PriceListColumn** | `Position` 1..n, `UpperBound` decimal(18,4)? (null only on the last column) | the horizontal brackets; strictly ascending; ≤ 50 |
| child **PriceListRule** | `Position`; `FromRelationId?`, `FromRegionId?`, `ToRelationId?`, `ToRegionId?` (Master Data ids; at least one side set; relation and region not both on one side); `TransportUnitId?`, `ProductId?`, `VehicleTypeId?`; `RowQuantity` `QuantitySource`; `MinAmount?`, `MaxAmount?` decimal(18,2) (QP8) | ≤ 10,000 per list |
| child **PriceListBracket** (of a rule) | `Position`; `UpperBound` decimal(18,4)? (null = the open row, exactly one per rule, last) | strictly ascending; ≤ 30 |
| child **PriceListCell** | `(BracketId, ColumnPosition)` → `UnitPrice` decimal(18,4)? | null = no price in that cell; one row per bracket × column |

`Tier(rule)` = number of dimensions set (0–3): the specificity used by the lookup and shown in the editor.

### RateLine (AD-08, AP-07)

One table `financial."RateLines"`. `TenantId`; `Side` `RateSide`; exactly one anchor of `OrderId`, `OrderLineId`,
`LegId`, `StopId`, `TripId` (check constraint; ids without FK, AO-11/Q1); `AnchorOrderId` (always set for an order-side
line, so "all lines of order X" is one indexed read) and `AnchorTripId` (always set for a trip-side line); `Kind` enum
`RateLineKind { PriceList = 1, Manual, FixedPrice, DiscountOrSurcharge, AdditionalService, AgreedPrice, OwnFleetDistance }`;
`Position`; `DescriptionKey` string(64)? (a localisation key for calculated lines) and `Description` string(256)? (free
text for manual lines); `Quantity` decimal(18,4); `QuantitySource` enum; `UnitPrice` decimal(18,4); `Amount`
decimal(18,2); `CurrencyCode`; `OverridesChildren` bool; `IsManual`, `IsAdjusted` bool; `AppliesAfterOthers` bool;
provenance `PriceListId?`, `PriceListRuleId?`, `PriceListBracketId?`, `ColumnPosition?`, `DiscountOrSurchargeId?`,
`DiscountOrSurchargeValueId?`, `AdditionalServiceId?`, `VatPercentageId?`, `VatPercentageRateId?`, `CalculatedAt`
DateTimeOffset?, `CalculationVersion` long?. Indexes: `(TenantId, AnchorOrderId)`, `(TenantId, AnchorTripId)`,
`(TenantId, LegId)`. Soft delete; audit.

**Totals** (AD-08): `Total(anchor)` = the overriding line at that anchor if any, else the sum of its own lines plus the
totals of its children (order → lines; trip → legs). Implemented in `RateLineTotals` (Domain, pure) and exposed by
`IRateLineReader`.

### RelationFinancialSettings (assessment §2, QP5)

Keyed by `RelationId` (Master Data id, unique per tenant): `RevenuePriceListIds` (child table `RelationPriceList` with
`Side`, `PriceListId`, `Position`), `MultipleMatches` enum `MultipleMatches { All = 1, HighestWins, LowestWins }` per side,
default D&S (child `RelationDiscountOrSurcharge`: `DiscountOrSurchargeId`, `ValidFrom`, `ValidTill?`), `VatPercentageId?`
(null = tenant default), `PaymentTermDays` int?, `ChargeFixedPriceByDefault` bool, `SendFreightDocumentsWithInvoice`
bool, `CurrencyCode?` (null = the relation's currency from Master Data). Created on first edit; the Master data app
shows it as a tab-like list "Relation financial settings" (search by relation) and the relation editor of Master Data
gets a link to it (no cross-app import: the link is a route).

### Shared enums (Domain.Shared `Tariffs/`)

`QuantitySource { Manual = 0, LineQuantity = 1, Positions, Colli, WeightKg, VolumeM3, LoadingMeters, LoadFactor, Epq,
DistanceKm, TollDistanceKm, DurationH, VehicleType, PerLine, TripDistanceKm }`, `RateSide`, `RateLineKind`,
`PriceMultiplier`, `ColumnQuantitySource`, `StopMatching`, `MultipleMatches`.

### The calculation (pricing assessment §3–§5; scenarios 1–80)

`PriceResolver` (Domain, pure, unit-tested): input = a `PricingSnapshot` (order lines with quantities, stops with
relation/address/region ids, legs with distance and toll km, the applicable lists, the D&S selection with value rows,
the as-of date); output = calculated `RateLine`s with provenance. Steps per list and line (QP1–QP9):

1. Validity: list `ValidFrom ≤ asOf ≤ ValidTill`; otherwise skip.
2. Lane: rules whose from/to match the line's first/last stop per `StopMatching`; exact relation beats region; "both"
   needs both sides.
3. Specificity cascade: tier 3 → 0 over (unit, product, vehicle type); inside a tier exact relation before region;
   first rule by `Position` wins ties.
4. Row bracket: the line's `RowQuantity` value against the rule's brackets (upper bound inclusive, open row last); no
   value → no line.
5. Column: the list's `ColumnQuantity` value (sum over sibling lines sharing the lane when the source says so) against
   the columns (upper bound inclusive, open column last).
6. `UnitPrice` = the cell; null → no line. `Quantity` per `Multiplier` divided by `UnitConversionFactor`, rounded to 4;
   `Amount = round(Quantity × UnitPrice, 2)`, clamped to `MinAmount/MaxAmount` (QP8).
7. Per order: `MultipleMatches` prunes calculated lines only (QP9); manual lines are re-valued from current quantities
   and adjusted lines keep their unit price (QP6); the fixed-price line stays as is; D&S lines (percentage over the sum
   of the lines they apply to in two passes, fixed once, per km per line with billable percentage and round trip, toll
   with the tolled km) are regenerated by id; additional-service lines are manual-kind lines kept as they are.
8. Currency: a list in another currency than the order's is skipped and reported as a warning code on the state
   (`Financial:CurrencyMismatch`) (QP2).

`OrderRateLineCalculator` (Application, `IDerivedValueCalculator` for `Financial.OrderRateLines`): loads the snapshot
through `IOrderPricingReader` and `ILegReader`, the lists and D&S from its own tables, runs the resolver, replaces the
order's non-manual revenue lines (delete + insert inside the job's transaction), keeps manual/adjusted lines (re-valued),
publishes `RateLinesChangedEto { OrderId, Version }`. Requested by Financial's handlers of `TransportOrderLinesChanged`,
`TransportOrderStopsChanged`, `TransportOrderFinancialTermsChanged`, `LegsGenerated` and `TransportTripRecalculated`
(distance may change), and by the bulk re-run endpoint.

`TripRateLineCalculator` (`Financial.TripRateLines`): per leg on the trip, the carrier's cost lists over the segment
(QP7; distance allocated over legs by tonne-km, else the leg's own distance); the own-fleet distance line
(`OwnFleetDistance`, QP10: unit price from `Vehicle.CostPricePerKm` → `VehicleType.CostPricePerKm` → tenant setting
`Financial:OwnFleetCostPerKm`, quantity = trip planned distance) when the trip's execution is `OwnFleet`; the
`AgreedPrice` line untouched. Publishes `RateLinesChangedEto { TripId, Version }`.

`PricePreviewService` (`IPricePreviewService`, Application.Contracts): `PreviewAsync(PricingSnapshotDto)` → the
would-be revenue lines; pure, no persistence, no event; the one synchronous call Orders makes while the clerk types.

### Deliberate differences from OpenTMS

Fixed-width matrix → rules, brackets, columns and cells; `double` → `decimal`; name matching → ids and provenance; live
D&S reads → the value row id stored; computed-on-read → stored lines and totals; twelve slots → a list of lists with
the basis on the list; validity enforced; min/max real; empty placeholder lines never persisted; manual flags set by
commands, not the client; one engine for both sides.

## API contract

Route root `financial`. Permissions `Financial.PriceLists` (+ `.Create`, `.Update`, `.Delete`, `.Adjust`, `.Simulate`),
`Financial.RateLines` (+ `.Update`), `Financial.RelationFinancialSettings` (+ `.Update`); features
`Apps.MasterData.Financial.Pricing.PriceLists`, `….RelationFinancialSettings` (lists in the Master data app).

| Service | Route | Operations |
|---|---|---|
| `PriceListAppService` | `/api/financial/price-list` | design-50 shape (`GET` list with `side`, `includeArchived`; `GET /{id}` with columns and rules; `GET /lookup?side=`; `POST`; `PUT /{id}` header + columns; `DELETE /{id}` refused when a relation or a rate line references it → archive; `POST /bulk-delete`); rules: `GET /{id}/rules` (paged, `tier`, `search`), `PUT /{id}/rules/{ruleId}`, `POST /{id}/rules`, `DELETE /{id}/rules/{ruleId}` (brackets and cells replaced with the rule); `POST /{id}/copy` `{ code, name, validFrom, validTill }`; `POST /{id}/adjust` `{ percentage? | amount?, rounding: None/Cent/FiveCents/Quarter/Half/Whole, preview: bool }` → the changed cell count (and, with `preview`, a sample); `POST /{id}/simulate` `{ line snapshot }` → `{ winningRuleId, candidates[] { ruleId, tier, reason }, unitPrice, quantity, amount }`; `GET /{id}/export` (xlsx of rules/brackets/cells), `POST /{id}/import` (xlsx, replace rules; validation report) |
| `RateLineAppService` | `/api/financial/rate-line` | `GET ?orderId=` / `?tripId=` → `RateLineSetDto { lines[], totals { byAnchor[], total } , currencyCode, state }`; `POST /manual` `{ anchor, side, kind Manual|AdditionalService, description, quantity, quantitySource, unitPrice, additionalServiceId? }`; `PUT /{id}` (manual: all fields; calculated: `unitPrice` only → `IsAdjusted`); `DELETE /{id}` (manual lines; a calculated line cannot be deleted, switch the list off instead); `POST /fixed-price` `{ orderId, amount }` / `DELETE /fixed-price/{orderId}`; `POST /recalculate` `{ orderIds[] | tripIds[] }` (bulk, `Platform.DerivedValues.Recalculate`) |
| `RelationFinancialSettingsAppService` | `/api/financial/relation-financial-settings` | `GET` list (by relation name through the lookup), `GET /by-relation/{relationId}`, `PUT /by-relation/{relationId}` (upsert) |
| `PricePreviewService` | not remote | `IPricePreviewService.PreviewAsync` |
| `FinancialReferenceLookup` | not remote | `GetVatPercentagesAsync(ids)`, `GetDiscountsOrSurchargesAsync(ids)`, `GetAdditionalServicesAsync(ids)`, `GetPriceListsAsync(ids)` → id, code, name, (side, currency) |

Errors: Platform `ReferenceData.*`; `Financial:PriceListInUse`, `Financial:ColumnsNotAscending`,
`Financial:BracketsNotAscending`, `Financial:OpenRowRequired`, `Financial:LaneRequired`,
`Financial:RelationAndRegionOnOneSide`, `Financial:CurrencyMismatch`, `Financial:CalculatedLineReadOnly`,
`Financial:ManualLineRequiresUnitPrice`, `Financial:ImportInvalid` (with row/column), `Financial:TooManyRules`.

## Migration strategy

Expand only: `financial."PriceLists"`, `"PriceListColumns"`, `"PriceListRules"`, `"PriceListBrackets"`, `"PriceListCells"`,
`"RateLines"`, `"RelationFinancialSettings"`, `"RelationPriceLists"`, `"RelationDiscountsOrSurcharges"`; unique
`(TenantId, Code)` on lists, `(PriceListId, Position)` on columns, `(RuleId, Position)` on brackets,
`(BracketId, ColumnPosition)` on cells, `(TenantId, RelationId)` on settings; the anchor check on rate lines; the three
rate-line indexes; FK cascade list → columns/rules → brackets → cells. No seed (price lists are tenant data). The
`FinancialDbContext` maps them; `numeric` precision as stated.

## UI design

Master data app, group **Financial → Pricing**: **Price lists** (list: Code, Name, Side, Currency, Basis, Valid from/till,
Rules, Archived; editor `EntityDialog size="xl"`: *General* (code, name, side, currency `ReferenceCombobox`, basis,
multiplier, column quantity, stop matching, conversion factor, validity `DatePicker`s, archived), *Columns*
(`LinesGrid`: upper bound), *Rules* (a master-detail: left an `OpenTmsGrid` of rules with tier badge, lane, dimensions,
"Overrides"/"Defaults" grouping; right the selected rule: lane pickers (`EntityLookup` relation or `ReferenceCombobox`
region, per side), three dimension pickers with "any", min/max, and the bracket ladder as a `LinesGrid` whose columns
are the list's columns), *Simulate* (a line snapshot form → winning rule and ranked candidates), *Remarks*, Record
identification; command bar Copy, Adjust (dialog with preview), Export, Import). **Relation financial settings** (list by
relation; editor `md`: relation `EntityLookup`, revenue lists `LinesGrid` (list `ReferenceCombobox` by side, position),
cost lists, multiple-matches per side, default D&S with validity, VAT, payment term, fixed price by default, documents
with invoice). The rate lines themselves have no screen of their own: the order and trip editors show them (design 61).

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| `PriceResolver`: every lookup scenario of `pricing.md` 1–28, manual/requery/fixed 29–39, D&S 40–55 (as unit tests over snapshots; decimal exactness) | **Critical** | unit tests, one per scenario, named by number |
| Purchase/cost side and allocation inputs 56–69 | **Critical** | unit + SQLite service tests |
| Tenant isolation: lists, rules, cells, rate lines, settings, readers, preview | **Critical** | PostgreSQL negative tests |
| Unique indexes, anchor check, FK cascades | **Critical** | PostgreSQL |
| Calculators: replace non-manual lines, keep manual, version guard, event published, idempotent on redelivery | **Critical** | PostgreSQL with probe readers |
| Validity, currency mismatch, min/max, multiple matches | **Critical** | unit |
| Import/export round trip; adjust with rounding kinds; copy | **High** | SQLite |
| Permissions and features | **Critical** | architecture + service tests |
| Screens: list editor with rule ladder, simulate, settings editor | **Medium** | vitest |
| Volume: 5,000 rules × 10 brackets × 20 columns list loads a rule page < 300 ms; resolver for a 50-line order < 200 ms | **High** | PostgreSQL volume test |

## Flag & rollout plan

Features `Apps.MasterData.Financial.Pricing.PriceLists` and `….RelationFinancialSettings` default on; the calculators run
regardless of features (they are not user-visible); rate lines appear in the Transport app under `Apps.Transport.Orders`.
Rollback: features off; tables stay.

## Cost & SLO impact

Cells up to 1 M rows per tenant (100 MB); rate lines ≈ 10 MB/day; resolver in memory per order (one query per table);
preview p95 < 500 ms; no external calls (toll km come from Planning's legs, stored).

## Assumptions

1. The as-of date for revenue is the first stop's planned date of the line, for cost the trip date (QP1).
2. Toll kilometres per leg are a stored leg value written by Planning's `LegRouteCalculator` (`TollDistanceM`); the
   resolver reads them, never calls routing.
3. Additional services on an order are rate lines of kind `AdditionalService` anchored to the order (QP11, first run).
4. The xlsx import/export uses the existing Excel job pattern of the grids (ClosedXML or the library already in the
   solution; the implementer picks what is referenced).
5. `Financial:OwnFleetCostPerKm` is an ABP setting (tenant), editable in the Finance app later; this run seeds no value
   (no own-fleet line without a price source; a warning on the trip instead).

### Build notes (backend, 2026-10-25)

What the implementer decided where this document left a detail open, and where it deviates and why:

1. **Table names carry the submodule prefix** (`backend/modules/financial/README.md`, binding over this document's
   unprefixed names): `financial."TariffsPriceLists"`, `"TariffsPriceListColumns"`, `"TariffsPriceListRules"`,
   `"TariffsPriceListBrackets"`, `"TariffsPriceListCells"`, `"TariffsRateLines"`, `"InvoicingRelationFinancialSettings"`,
   `"InvoicingRelationPriceLists"`, `"InvoicingRelationDiscountsOrSurcharges"`. The anchor check constraint is
   `CK_TariffsRateLines_OneAnchor`, written with `CASE WHEN` so SQLite runs it too.
2. **`DiscountOrSurcharge` gained four columns** the two-pass and per-kilometre behaviour of step 7 needs and design 52's
   entity lacked: `AppliesAfterOthers`, `KilometreBasis { Distance, TollDistance }`, `BillableKilometrePercentage`
   (`numeric(5,2)`, default 100) and `RoundTrip`. Additive on the design-52 DTOs and migration (`AddColumn` with defaults).
3. **The rules are not part of the list's detail** (`GET /{id}` returns the header, columns and `RuleCount`); they are paged
   through `GET /{id}/rules` and read one at a time through `GET /{id}/rules/{ruleId}` (brackets and cells). Rule commands
   read the list as a projection, never as a tracked entity, so a rule edit does not bump the header's concurrency
   stamp (ABP updates an aggregate root's stamp when a child row is inserted while it is tracked).
4. **Rule search** takes an id (relation, region, unit, product, vehicle type) or a position: rule names live in Master
   Data and `IMasterDataReferenceLookup` has no name search. Rows carry relation and region names (looked up per page);
   transport unit, product and vehicle type ids are returned as ids (no published lookup yet) and are not validated on
   save. The `rulecount` sort of the list is not offered (`UnknownSortColumn`).
5. **Lane fallback under `StopMatching.Both`**: a one-sided rule is a candidate when its set side matches (lane score 2
   for a relation, 1 for a region, 0 for an unset side); two-sided rules outrank it. Within a tier the order is lane
   score, then `Position`; OpenTMS's dimension order is not reproduced.
6. **Column value**: a missing value (no weight on a weight-graduated list) selects the first column (OpenTMS's
   behaviour); a missing *row* value or *basis* value produces no line and the warning `Financial:QuantityMissing`.
7. **Discounts and surcharges**: the order-level as-of date is the earliest line date, else the order date; a
   percentage of the second pass without regions applies over the lines plus the first pass, with regions over the
   matching lines only; a fixed amount is added once (once per order when any line matches its regions); a
   non-toll per-kilometre surcharge is skipped when a list with basis `DistanceKm` applies (scenario 19); a percentage
   line stores `Quantity` = the base amount and `UnitPrice` = percentage / 100 (4 decimals), the amount from the exact
   percentage. Names are never stored on calculated lines; the screen names them through `DiscountOrSurchargeId`.
8. **Fixed price**: the snapshot's `FixedPriceAmount` is the source when set (the calculator rewrites the overriding
   line); when null an existing fixed-price line set through `POST /fixed-price` is kept. Per AD-08 the total is the
   overriding line; the discount lines are computed over the breakdown and stay as the breakdown.
9. **Every rate-line command requests a recalculation** (manual add/update/delete, adjust, fixed price set/remove), so
   the discounts follow and `RateLinesChanged` travels with the new version; the commands publish no event themselves.
10. **Unchanged recalculations publish nothing**: `RateLineWriter` compares the stored set (kind, anchor, numbers,
    flags, provenance) with the new one and writes and publishes only on a difference. This makes a repeated delivery
    one effect and ends the chain `TransportTripRecalculated → OrderRateLines → RateLinesChanged → TripCalculator`.
11. **Cost segments**: one cost line per (order line, trip), anchored to the segment's first leg on the trip, with the
    legs' summed distances (the leg's own distance is what Planning stores; no allocation of a trip distance here);
    rule dimensions come from the order's snapshot (`IOrderPricingReader.GetSnapshotsAsync`), stop regions from the
    snapshot's stops matched by `StopId`.
12. **Trip facts** come through a Financial port `ITripCostFactsReader`, by default composed from
    `ITransportTripLookup` (date, carrier) and `ILegReader`. "Own fleet" is a trip without a carrier relation (design 59:
    null = own company). The vehicle → vehicle type cost-per-kilometre cascade (QP10) has no published source yet
    (`TransportTripReferenceDto` carries no vehicle ids and Master Data publishes no cost price): the own-fleet line
    prices from the tenant setting only, and without it logs `Financial:NoCostPriceSource` and writes no line.
13. **Rate lines are soft-deleted** on replacement (money history, §4.4); the calculator loads its inputs without change
    tracking and loads only the rules whose lane can match one of the order's lines (two bounded queries).
14. **xlsx** through ClosedXML (new package reference in `OpenTms.Financial.Application`): sheet `Rules` (one row per
    bracket, `Price1..n` per column) plus a reference sheet `Columns`; import replaces the whole rule set and reports
    `Financial:ImportInvalid` with `row`, `column`, `problem`; files above 5 MB are refused.
15. **Relation settings list search** by relation name resolves names through the lookup in chunks of 500 and filters in
    memory; without a term only the page is named.
16. **Extra error codes** beyond the list above: `Financial:AnchorRequired`, `Financial:TooManyRateLines`,
    `Financial:InvalidValidity`, `Financial:AdjustmentRequired`, `Financial:RateLineConflict` (409). Warning codes:
    `Financial:QuantityMissing`, `Financial:CurrencyMismatch`, `Financial:NoCostPriceSource`.
17. **`RateLineAppService`** is gated by `Apps.Transport` (the app, not a node): both editors of design 61 read it.
    `RecalculateAsync` needs `Platform.DerivedValues.Recalculate`, as the design's bulk re-run.
18. **Scenarios 63–69** (actual carrier invoices, allocation over legs, grid cost columns) are the purchasing run's and
    Planning's (AD-20) and have no test here; 70–80 are approval's and invoicing's.
19. **2026-10-25, integration.** QP10 closed: `TripCostFactsReader` reads "own fleet" from the trip's `Execution` and the
    vehicles' summed cost price per kilometre from Planning's `TransportTripReferenceDto.OwnFleetCostPerKm` (vehicle → vehicle
    type on Master Data's side); the tenant setting `Financial:OwnFleetCostPerKm` is the last step of the cascade, the
    `NoCostPriceSource` warning stays when both are absent. `IFinancialReferenceLookup.GetRelationFinancialSettingsAsync(relationId, asOf)`
    publishes the relation's defaults (lists in order, multiple-matches, default D&S valid on the date, VAT, payment term,
    fixed-price and documents flags, currency) for Orders. `TransportTripRecalculatedHandler` also requests the trip's own cost
    lines (the own-fleet line and the segments price on the legs' distances the recalculation stored; design 56
    "TransportTripChanged (… distance)"); the chain ends because an unchanged set publishes nothing. Every handler carries
    `[UnitOfWork]` and runs inside the outbox dispatcher's unit of work per event. The calculator tests run on real Orders and
    Planning rows (the fakes are gone); `TransportRunChainTests` covers the whole chain.

## Security quickscan

Permissions as named; input bounded (≤ 50 columns, ≤ 30 brackets, ≤ 10,000 rules, ≤ 500 rate lines per anchor, decimal
ranges); import files ≤ 5 MB, parsed with a streaming reader, cells validated; tenant-scoped; no raw SQL except the
cell bulk insert (parameterised); logs ids, counts and versions; no personal data.
