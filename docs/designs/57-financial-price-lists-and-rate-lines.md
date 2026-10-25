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

## Security quickscan

Permissions as named; input bounded (≤ 50 columns, ≤ 30 brackets, ≤ 10,000 rules, ≤ 500 rate lines per anchor, decimal
ranges); import files ≤ 5 MB, parsed with a streaming reader, cells validated; tenant-scoped; no raw SQL except the
cell bulk insert (parameterised); logs ids, counts and versions; no personal data.
