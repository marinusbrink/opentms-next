<!-- Appendix to docs/input/transport-rebuild-assessment.md and pricing-rebuild-assessment.md. Produced 2026-10-25 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 5f547cc6b); not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in that checkout. F = fact read in code, I = inferred. -->

# Legacy OpenTMS pricing and cost engine — assessment

Read-only research of `/Users/marinusbrink/dev/OpenTMS/aspnet-core/src` (paths below are relative to that folder) and `mockups/price-list-redesign.html`. Everything marked **fact** is read from code; **inferred** is my reading of behaviour the code does not state. Nothing was built or run.

## 1. Price list model

**Header `tblPriceList`** (`FiLogic.Core/BusinessEntities/PriceList/PriceList.cs`): `Name`, `Description`, `PriceListType` (`Outbound` = applied to customer orders, `Inbound` = applied to trips executed by a carrier, `PriceListType.cs:9-16`), `ValidFrom`/`ValidTill` (lines 90-93), `PriceMultiplierOptions` (96), `GraduatedPriceLookupOptions` (99), `RelationLookupOption` (101), `UnitPriceConversionFactor` (103) and **one hundred nullable doubles `Offset0..Offset99`** (106-223, `MaxColumnCount = 100`, line 11). The offsets are the **horizontal axis**: upper bounds of graduated brackets. N filled offsets give N+1 price columns: `Price0` is "≤ Offset0", `Price1` is "> Offset0 and ≤ Offset1", …, `PriceN` is "> OffsetN-1" (open-ended). What the axis *measures* is not stored on the list; it is derived at lookup time from `GraduatedPriceLookupOptions` and the charge basis (§2).

- `PriceMultiplierOptions` (`PriceMultiplierOptions.cs:9-30`): `PricePerUnit` (unit price × the basis quantity), `PricePerFreightItem` (× 1), `PricePerAmount` (× `Freight.Amount` regardless of basis), `PricePerLastWaypoint` (× 1 for the first freight sharing the last-waypoint relation, × 0 for the rest).
- `GraduatedPriceLookupOptions` (`GraduatedPriceLookupOptions.cs:13-99`): which quantity selects the column — this freight's basis value (`ByFreightItem`), the sum over all freights of the order that share the same first/last relation (`SumFreightItems`), that sum restricted to the same transport unit / product / both (`GroupBy…`), or weight/volume/loading metres/distance/duration of this freight (`…OfThisFreightItem`) or of all matching freights (`…OfAllFreightItemsOnStop`).
- `RelationLookupOptions` (`RelationLookupOptions.cs:5-9`): match items on the last waypoint only, the first only, or both.
- `UnitPriceConversionFactor`: the quantity is *divided* by it before multiplying (`FreightSalesPriceCalculator.cs:520-523`), e.g. kg price list applied to a freight in tonnes.

**Item `tblPriceListItem`** (`PriceListItem.cs`): `PriceListId`, `FirstGeographicalRegionId`, `LastGeographicalRegionId`, `FirstRelationId`, `LastRelationId`, `TransportUnitId`, `ProductId`, `VehicleTypeId` (13-27), `MinPrice`/`MaxPrice` (29-31, added by migration `20261020_Added_MinPrice_and_MaxPrice_to_PriceListItem` and **read by no calculator** — fact, grep over `FiLogic.Application`), `RowOffsetProperty` (33, one of `Amount, Positions, Weight, Colli, Volume, LoadingMeters, LoadFactor, Epq, Distance, Duration`, `PriceListRowOffsetProperty.cs:7-16`), `RowOffset0..RowOffset29` (35-66, the **vertical axis**) and `Price0..Price99` (69-183, the matrix cell row). Each item is one row of one lane: an item with `RowOffset3 = 150` is "tier 3, ≤ 150 <RowOffsetProperty>"; an item with no row offset filled is the infinity tier. Validation (`PriceListAppService.cs:829-914`) enforces: one `RowOffsetProperty` per list, per lane-and-dimension group exactly one infinity row, each tier row has exactly one `RowOffsetN` set, and the tiers form an uninterrupted strictly rising sequence 0..n (`Dto/Extensions.cs:60-114`). Offsets must be gap-free and ascending (`PriceListAppService.cs:730-755`). Every item needs a relation or region on at least one side (720-721).

**Matrix lookup.** Column: `PriceListItemLookupHelper.GetPriceListItemProperty` (`PriceListItemLookupHelper.cs:58-96`) walks `columnIndex = -1..98`, reads `Offset{i}` and `Offset{i+1}` by reflection (missing/null → `Int32.MinValue`/`MaxValue`) and returns `PropertyInfo` of `Price{i+1}` when `amount > Offset{i} && amount <= Offset{i+1}`. `PriceList.FindOffset` (`PriceList.cs:35-74`) is a second, unused copy of the same loop. Row: `GetPriceListItemByRowOffset` (188-216) takes the matched items, builds a sorted map `{tierIndex → (bound, item)}` from the first item carrying each `RowOffsetN` (`Extensions.cs:116-142`), reads the freight's own value of `RowOffsetProperty` through reflection (`RowOffsetPropertyMapper.cs:25-30`), returns the first tier with `value <= bound`, else the infinity item, else null (no price). If `RowOffsetProperty` is null the first matched item wins.

*Worked example (fact-based):* list "AMS→BRU by weight", `Offset0=100, Offset1=500, Offset2=1000`, `GraduatedPriceLookupOptions = ByWeightOfThisFreightItem`, `PriceMultiplierOptions = PricePerUnit`, order charges by amount (pallets). Items for lane AMS→BRU, transport unit "pallet": row A `RowOffsetProperty=Distance, RowOffset0=50, Price0..3 = 12,11,10,9`; row B `RowOffset1=150, Price0..3 = 15,14,13,12`; row C (infinity) `Price0..3 = 20,19,18,17`. Freight: 3 pallets, 350 kg, 120 km. Column: 350 > 100 and ≤ 500 → `Price1`. Row: distance 120 > 50, ≤ 150 → row B. Unit price `14`; amount = 3 (per unit, basis amount); line `3 × 14 = 42.00`. Note the two axes read *different* quantities from the freight, and the horizontal one may be a sum over sibling freights while the vertical one is always this freight's own value (`FreightSalesPriceCalculator.cs:434-478` vs `505-508`).

**Linking to relation and order.** `TransportOrder` carries twelve `ChargeBy<Basis>` booleans each with a `ChargeBy<Basis>PriceListId` (`TransportOrder.cs:62-111`: Amount, Positions, Colli, Volume, Weight, Epq, LoadingMeters, LoadFactor, Distance, Duration, VehicleType, FreightItem) plus `ChargeFixedPrice`/`FixedPrice` (67-68), `OnlyApplyHighestPriceWhenHavingMultiplePriceLists` (113), `SurchargesAndDiscounts` (54, a **pipe-joined string of GUIDs**, `TransportOrderRepositoryHandler.cs:683-684, 860`), `CurrencyId`, `VatId`, `PaymentTerm` (47-52). `Relation` carries the same set as customer defaults (`Relation.cs:73-141`) and a second mirrored set `IncomingInvoicesChargeBy…` for the carrier side (313-360) including both `…OnlyApplyLowest…` and `…OnlyApplyHighest…`. A basis with its flag on and a price list id produces one price line per freight item from that list; a flag on without a list produces an empty line the user fills manually. `OnlyApplyHighestPriceWhenHavingMultiplePriceLists` keeps, per freight, only the line with the highest `Price` and deletes every other line (`FreightSalesPriceCalculator.cs:294-312`) — including manual lines that were passed through, since they are in the same collection (inferred).

## 2. Sales price calculation

Entry points: `PriceListAppService.QueryFreightPrices` (`PriceListAppService.cs:121-137`, whole order DTO in, authoritative) → `FreightPriceQueryManager.QueryAllFreightPrices` (`FreightPriceQueryManager.cs:98-151`); and `QueryFreightSalesPrices` (59-96, lean path used by the freight-detail dialog, the surcharge selection is treated as stale). Inputs: freight items, waypoints (relation per freight ordered by `OrderNumber`), existing price lines, the order's charge-by definitions, selected D&S ids, `RequestId`. Master data is loaded per call (646-692): all referenced price lists and items, and `RelationGeographicalRegion` for every waypoint relation. **No validity filter is applied** — `ValidFrom/ValidTill` are only used by the picker (`PriceListAppService.cs:328-333`) and by copy; fact.

Algorithm (`FreightSalesPriceCalculator.QueryFreightPrices`, `FreightSalesPriceCalculator.cs:20-322`):

1. Drop every incoming line that is neither `IsCreatedManually` nor `IsAdjustedManually` (26).
2. If no price list id is configured at all (33-265): re-value manual lines from the freight's current quantity (`Amount = basis value`, `Price = Amount × UnitPrice`, 38-67), add an empty line (`UnitPrice = null`) for each basis flag that has no line yet (72-251), renumber `OrderNumber` 1..n per freight (254-262), return.
3. Otherwise for each freight and each of the twelve bases call `CalculateFreightPrices` (273-290):
   - flag off → pass through existing lines of that specification (338-339);
   - flag on, no list → re-value manual lines, add an empty one if none (344-355);
   - a manually adjusted line exists → keep it, overwrite `Amount` with the current basis quantity (except for `PricePerFreightItem` lists) and recompute `Price` (359-376);
   - else resolve first/last waypoint relation and their regions (388-392), collect sibling freights that share the relation *by id, never by region* (396-429), compute the column quantity per `GraduatedPriceLookupOptions` (434-478), find the column property (484), prefilter items on lane (`GetFilteredPriceListItemsToSearch`, 98-155: exact relation first, region otherwise, mixed for first-and-last), then the **specificity cascade**: three dimensions (transport unit + vehicle type + product) → two → one → none (`FindByThree/Two/One/NoMatchingProperties`, 219-777; inside each level exact relation beats region, and the fixed order is transport-unit-null, then vehicle-type-null, then product-null), then the row tier (505-508).
   - Amount per multiplier option (514-516), `UnitPrice = Price{col}` of the matched item or null (517), conversion factor (520-523), `PricePerLastWaypoint` zeroing (526-533), `PricePerFreightItem` keeps a touched line's amount and only refreshes its unit price (538-548). New line: new sequential id, `OrderNumber = 1` (558 — the counter is local, so every list line gets 1; the query manager renumbers only the per-km lines, `FreightPriceQueryManager.cs:138-148`), `Price = (amount × unitPrice).Round(2)` (562). `Description` is never set for list lines.
4. Highest-wins pruning (294-312), `TenantId`/`IsDeleted` stamping (315-319).

Rounding: quantities `Round(5)` (`GetFreightItemAmount`, 584); DTO setters round `UnitPrice` to 5 and `Price` to 2 decimals (`TransportOrder/Dto/TransportOrderFreightPriceDto.cs:43,49`). All money is `double`. Currency is **not** part of pricing at all (no reference in `BusinessServices/PriceList`); the order's `CurrencyId` only reaches the invoice description. Min/max price: not applied.

**Resulting line** (`TransportOrderFreightPrice.cs`): `TransportOrderFreightId`, `TransportFileId` (purchase only), `TransportOrderFreightPriceType` (Sales/Purchase), `PriceSpecification` (one of the twelve bases incl. `FixedPrice` = per freight item), `OrderNumber`, `Description`, `Amount`, `UnitPrice`, `Price`, `InternalRemarks`, `IsCreatedManually`, `IsAdjustedManually`. The sales line is anchored to the freight item only — not to a leg, stop or order.

**Manual lines.** The Angular price table sets `isCreatedManually = true` on *any* form change (`angular/src/app/transport/transport-order-freight-price-panel/.../transport-order-freight-price-table.component.ts:128-130`), both flags on an added line (162-163), `isAdjustedManually` on a unit-price edit (201). The flags are therefore client-defined; the backend only honours them.

**Per-km D&S as distance lines** (`FreightPriceQueryManager.GeneratePricePerKilometerSalesPrices`, 180-383). Only when the order does not charge by distance through a list. A D&S with `ValueIsPricePerKilometer` becomes a `SpecifiedByDistance` sales line per freight with `Description = D&S.Name`, `Amount = km`, `UnitPrice = rate`, `IsCreatedManually = true` (deliberately, so the calculator keeps re-valuing it from `Freight.Distance`, 366-378). Region-restricted D&S apply only when a freight's destination relation lies in one of the regions (334-341). Two modes: authoritative selection (main screen, grid, wizard) regenerates and drops stale lines (291-293); non-authoritative (dialog) self-sustains existing lines and replicates them to freights that lack one (211-289), recognising toll lines **by `Description == D&S.Name`** (222-243). Modifiers (`CalculatePerKilometerLine`, 397-424): non-toll lines bake `MaximumBillableKilometerPercentage` and round-trip into the *rate*; toll lines apply them to the *kilometres* (rounded to 3 decimals) and keep the pure levy rate. Toll kilometres come from `ITollKilometersService.QueryTollKilometers(origin, destination)` (`IntegrationServices/RouteFunctionServices/ITollKilometersService.cs:22`), a Google cloud function call with a 5-minute cache and 20 s timeout (`TollKilometersService.cs:31-39`); a failed lookup silently yields no line (348-357). These rules are documented by `test/FiLogic.Tests/Unit/FreightPricePerKilometerRate_Tests.cs:36-142` and `Integration/PriceList/FreightPriceQueryManager_TollDeselect_Tests.cs:99,139`.

**Fixed price.** `ChargeFixedPrice` + `FixedPrice` on the order; saving a fixed-price order that still has freight price lines throws (`TransportOrderRepositoryHandler.cs:1485-1486`). Revenue sync uses `FixedPrice` instead of the lines (1940-1943); invoice puts it on the level-one line and nulls the freight lines (`DefaultInvoiceLineCreationService.cs:204-210, 292, 315`). The newer `TransportOrderRevenueCalculator` adds `FixedPrice ?? 0` *and* the line sum without checking the flag (`Calculations/ITransportOrderRevenueCalculator.cs:75-79`) — inconsistent, fact.

## 3. Purchase price calculation

`FreightPurchasePriceCalculator` (`FreightPrices/Calculators/FreightPurchasePriceCalculator.cs`) mirrors the sales calculator with the carrier's `IncomingInvoicesChargeBy…` definitions (`FreightPriceQueryManager.cs:592-644`; the relation's `…OnlyApplyHighest…` flag is **not** mapped, only `Lowest`, line 640). Unit of work is a `FreightPricingSegment` = one freight on one trip (`Dto/FreightPricingSegment.cs`): origin = first leg's origin, destination = last leg's destination, distance/duration = sum over the freight's legs on that trip (`FreightPurchasePriceCalculationInputBuilder.cs:135-172`). When the carrier charges by distance or duration, the trip's `ActualDistance ?? ScheduledDistance` (or actual/scheduled departure→arrival hours) is **allocated over the legs with the Cofret shipment allocation** using leg amounts and relation coordinates (181-263, `FiLogic.Shipments.Allocation.Cofret`); without coordinates it falls back to the leg's own distance. The builder reloads *all* legs and freights of the affected trips from the database (280-340) so the price of every freight on the trip is recomputed; a comment admits unsaved leg deletions are not handled (71). Lines are stamped `TransportFileId` and type `Purchase` (219-234); per segment only manual lines survive (93-94, 99), highest/lowest pruning is per segment (55-68). It runs server-side, synchronously in the request, when legs are assigned/unassigned (`TransportFileAppService.cs:2370-2372`: `ClearPricesForUnassignedLegs` then `UpdatePurchasePricesByFileIds`), when a trip's carrier/distance fields change (`TransportFileRepositoryHandler.cs:94-112`), after route optimisation (`RouteOptimalizationAppService.cs:137`) and on explicit refresh (`PriceListAppService.cs:139-142`). Persistence replaces the freight's purchase lines by id diff (`FreightPriceCalculationService.cs:73-109`).

A **second, older purchase engine** exists: `IncomingInvoicesQueryManager` (`PriceList/IncomingInvoices/IncomingInvoicesQueryManager.cs:123-215`) computes *expected incoming invoices* per trip from the same inbound lists via `FindByOneMatchingPropertiesForLeg` (`PriceListItemLookupHelper.cs:575-718`), applies carrier D&S with validity on `FileDate` (797-815) and lowest-wins (183-205). Both run on leg assignment (`TransportFileAppService.cs:2353-2358` and 2370-2372). Trip cost reads the incoming invoices, not the purchase lines (§5).

## 4. Discounts, surcharges and additional services

`tblDiscountAndSurcharge` (`DiscountAndSurcharge/DiscountAndSurcharge.cs:15-77`): `Name`, `Value`, `ValueIsPercentage`, `ValueIsPricePerKilometer`, `MaximumBillableKilometerPercentage`, `OnlyApplyTollKilometers`, `CalculateKilometersAsRoundTrip`, `ApplyAfterOtherDiscountAndSurcharges`, seven `AppliesExclusivelyOn<Weekday>` booleans, `LimitToOneTransportOrder`, `GeneralLedgerAccount`; regions via `tblDiscountAndSurchargeGeographicalRegion`. Relation defaults live in `RelationDiscountAndSurcharge` with `ValidFrom/ValidTill` (`Relation/RelationDiscountAndSurcharge.cs:12-19`); `GetSingle` returns the ids valid on the order date (`TransportOrderAppService.cs:1401-1402`). Which of those are *applied* is decided **in the Angular order screen** (`angular/.../transport.order.details.component.ts:2289-2320`): weekday match, destination-region match, and — when `LimitToOneTransportOrder` is false — the id is pushed **once per matching destination**, so the same surcharge can appear several times in the pipe string. Imports replicate the rule server-side (`IntegrationServices/DataImportServices/TransportOrder/TransportOrderImportServiceBase.cs:135-200`, duplicated in `DataImportServices/TransportOrder/TransportOrderImportService.cs:1242-1278`). The order stores only ids; **values are read live** from the catalogue at every revenue computation and at invoicing (no snapshot).

Application: percentage D&S are computed over the running subtotal in two phases, "before" ones over the freight sum, "after" ones over freight sum + before-D&S; fixed ones add `Value`; per-km ones are skipped because they already exist as distance lines. This rule is implemented four times with small differences: `TransportOrderRevenueCalculator.ApplyDiscountAndSurcharges` (126-176), `NextInvoicePriceApprovalValidator.CalculateCurrentRevenue` (70-119), `TransportFileCostCalculator.CalculateRevenueAfterDiscountsAndSurcharges` (435-488, subtracts "apply-after per-km" lines matched by name, 456-462), `GetAllTransportOrderDtoQuerySelector` (345-387). Invoice lines (`DefaultInvoiceLineCreationService.cs:321-395`): one line per D&S id, `Price = Math.Round(runningTotal/100 × Value, 2)` or `Value`, description "`Name over € subtotal`" (`DiscountAndSurcharge.cs:123-128`). VAT: single `VatId` per order/relation (`Vat.cs:13-38`, percentage, `CalculateVatAmount` rounds to 2) applied to the invoice, not per line; D&S carry a ledger account only.

Additional services (`AdditionalService/AdditionalService.cs`, `TransportOrder/TransportOrderAdditionalService.cs`): free lines on the order with `Date`, `LedgerAccount`, `CostCenter`, `Description`, `Amount`, `UnitPrice`, `Price`, copied by value from the catalogue; added to revenue *after* D&S (`ITransportOrderRevenueCalculator.cs:84-86`) and emitted as their own invoice lines (398-410).

## 5. Cost side

**Trip cost** (`TransportFile/TransportFileCostCalculator.cs:192-254`, and the newer `TransportFile/Calculators/ITransportFileCostCalculator.cs:37-134`): `sum(incoming invoices: ActualPrice ?? ExpectedPrice)` + own-fleet cost = `costPerKm × (ActualDistance ?? ScheduledDistance)` where `costPerKm` is the sum over primary/secondary/third vehicle of `Vehicle.CostPricePerKm` falling back to `VehicleType.CostPricePerKm`, else the tenant setting `CostPricePerKm` (121-189), and only when the trip's `TransportCompanyId` is the tenant's own relation (`IsMe`). D&S on the trip are not part of cost; the purchase price lines of §3 are not part of cost either (fact — only `IncomingInvoice` rows are summed).

**Order cost of revenue** (`TransportOrder/TransportOrderCostCalculator.cs:38-188`, duplicated in `GetAllTransportOrderDtoQuerySelector.cs:393-407, 583`): if all legs of the order sit on one trip and that trip has no other legs, cost = trip cost; else `Σ leg.Distance × (trip cost / Σ assigned leg km of that trip)`, rounded per leg (184).

**Revenue allocated to trips** (`TransportFileCostCalculator.cs:257-330`, newer `Calculators/ITransportFileRevenueAllocator.cs:29-97`): order revenue (fixed or sales lines → D&S → + additional services) divided by the order's total leg distance gives a rate per km (`ITransportOrderRevenueCalculator.cs:97-123`); trip revenue = Σ over its legs of `leg.Distance × rate`; if that is 0 and the trip holds all freights of exactly one order, the full order revenue.

**Stored vs computed.** Stored: `TransportOrderAttributes.Revenue` = fixed price or sales line sum + additional services, *before* D&S, synced on order save and after FMS pricing (`TransportOrderRepositoryHandler.cs:1842-1990`, `TransportOrderFreightPricingService.cs:129-130`); `TransportFileAttributes.Cost` exists (`TransportFile/TransportFileAttributes.cs:20`). Computed on every grid read: D&S application, cost of revenue, profit margin (`GetAllTransportOrderDtoQuerySelector.cs:227-255, 345-407`, gated by whether the user's grid shows those columns, 543-575); trip revenue/cost (`GetAllTransportFileDtoQuerySelector.cs:263-273` behind a tenant setting; a third implementation per page in `TransportFileAppService.cs:1531-1583`). No value is tied to a leg or stop in storage.

## 6. When pricing runs

All synchronous inside the request/unit of work; none queued except the FMS worker.

| Trigger | Where | Path |
|---|---|---|
| Order screen: freight edited, waypoint/leg/distance change, charge-by change | Angular `transport.order.details.component.ts:2426`, `transport-order-freight.component.ts:522`, wizard `freight-details.component.ts:363` | `QueryFreightPrices` (authoritative); result is put in the DTO and saved with the order |
| Freight detail dialog | `edit-order-freight-dialog.component.ts:344` | `QueryFreightSalesPrices` (non-authoritative) |
| Order save | `TransportOrderRepositoryHandler` | no recalculation; persists the lines sent by the client, syncs `Revenue` attribute (817, 1449) |
| Accept pending orders | `TransportOrderAppService.cs:2690` | full requery |
| Return-order generation | `TransportOrderAppService.cs:1891` | full requery |
| Freight redistribution (manual and FMS-sourced) | `RedistributionServices/*.cs:138-144` | clear + requery |
| Imports (~30 import services) | `TransportOrderImportService.cs:589-598`, `TransportOrderImportServiceBase.cs:111-120` | requery after mapping |
| FMS completion of a trip | `TransportOrderFreightPricingService.cs:67-140` | prices only if none exist yet, inserts directly, logs errors |
| Questionnaire-driven freight update | `TransportFileQuestionnaireResultTransportOrderFreightUpdateBackgroundWorker.cs:191` | requery |
| Approval validation | `CorrectAppliedPriceListApprovalValidator.cs:48` | requery and compare unit prices → warning |
| Leg assignment/unassignment, trip save, route optimisation | §3 | purchase lines + expected incoming invoices |
| Invoice generation | `DefaultInvoiceLineCreationService.cs:118-119` | reads stored sales lines only; never recalculates |

Approval validators: `CorrectPriceCalculationApprovalValidator` (amount ≠ freight quantity → warning; `Amount×UnitPrice ≠ Price` → error, 20-37), `EmptyFreightPriceApprovalValidator` (freight without a non-zero price → warning), `FixedPriceApprovalValidator` (fixed-price flag without value → warning), `NextInvoicePriceApprovalValidator` (current vs already invoiced revenue; < €0.50 difference → archive instead of invoice, 59-63), `FinancialDefaultsMismatchApprovalValidator` (order defaults drift from relation).

## 7. Price list maintenance

`PriceListAppService.cs`: `GetAll` paged grid (93-99); `GetList` from `PriceListCacheManager` with `ValidOn` filter (321-342); `GetSingle` with name enrichment (345-456) and `GetSingleStructured` grouping items for the editor (458-463, `PriceListMappingHelper.cs`); `CreateOrUpdate` (167-203) in a `RequiresNew` transaction, items diffed by id, prices copied with reflection over `Price0..99` (601-608) and row offsets (610); `Delete` refuses when any order or relation references the list in any of the twelve slots (206-237); `Copy`/`CopyWithValidOptions` clone header and items with new ids, new name "(copy)" and new validity (465-525); `AdjustPriceList` (527-546) loads, multiplies every property whose name starts with "price" (`PriceListAdjustmentService.cs:30-59`) by ±percent, rounds up or down to cent/quarter/half/whole euro, saves, and optionally re-points every relation default from the source list to this one (`UpdateRelationPriceLists`, 916-941, again by reflection over `*PriceListId`). There is no absolute adjustment. Excel: `ExportAllToExcel` exports the **header grid only** (`PriceListExcelExportBackgroundJob.cs:18-26`); no price list import exists (fact, grep). Caching: `PriceListCacheManager` is a generic `EntityCacheManager` for the list DTOs (`PriceListCacheManager.cs:12-19`), refreshed on save/delete (191, 248); calculators bypass it and query the repository per call (`FreightPriceQueryManager.cs:673-678`). Validity is never enforced on orders: a list stays applied after `ValidTill`.

## 8. The redesign mockup

`mockups/price-list-redesign.html` ("Option B + D") sketches a master-detail editor: left a filterable list of **rules** ordered by specificity tier T3 (lane + transport unit + product + vehicle type) → T0 (lane only, then global fallback), grouped as "Overrides" and "Defaults"; right the selected rule in three questions — *where* (from/to lane picker), *for which freight* (three dimension cards with "Clear → any", which demotes the tier), *what does it cost* (a row-offset ladder with weight brackets as columns and tiers as rows, "+ Add tier"). Option D adds a **cascade trace**: simulate a freight (lane, dimensions, weight) and see which rule wins and the full ranked candidate list. It keeps the two-axis matrix and the specificity cascade but makes them explicit and testable in the UI; it says nothing about validity, currency, multiple lists per order or rounding.

## 9. Known problems — do not clone

1. **Fixed-width matrix and reflection.** 100 `Offset` columns, 30 `RowOffset` and 100 `Price` columns per item, addressed by string-built property names (`PriceList.cs:18-19`, `PriceListItemLookupHelper.cs:64-69, 90`, `PriceListAppService.cs:601-608, 731-747`, `PriceListAdjustmentService.cs:30`, `Extensions.cs:15-21`). Two copies of the column loop (`PriceList.FindOffset` vs `GetPriceListItemProperty`).
2. **Doubles for money and quantities** everywhere (`TransportOrderFreightPrice.cs:33-39`, `DiscountAndSurcharge.cs:23`), with ad-hoc `.Round(2)`/`.Round(5)`.
3. **Matching by name.** Per-km D&S lines are linked to their D&S only through `Description == Name` (`FreightPriceQueryManager.cs:222-243, 373`; `DefaultInvoiceLineCreationService.cs:218-246`; `TransportFileCostCalculator.cs:456-462`); renaming a surcharge silently breaks invoicing and revenue. D&S ids are a pipe-joined string with possible duplicates (`TransportOrderRepositoryHandler.cs:860`, Angular 2305-2309).
4. **Live reads of D&S values**: orders store ids, invoices and revenue read today's `Value` (`ITransportOrderRevenueCalculator.cs:137`, `DefaultInvoiceLineCreationService.cs:324-331`).
5. **Revenue, D&S effect, cost and profit are computed on every grid read**, three implementations (§5), plus a stored `Revenue` attribute that excludes D&S.
6. **No validity enforcement** of price lists in calculation (§2); relation D&S validity is enforced only when defaults are copied; carrier D&S validity only in incoming invoices.
7. **N+1 / per-call loading**: master data reloaded per query (`FreightPriceQueryManager.cs:646-692`), `FirstOrDefault` loops inside `foreach` over freights and relations throughout (`FreightSalesPriceCalculator.cs:276, 397-429`), the purchase input builder reloads whole trips (280-340), the toll service does an HTTP call per freight (306-311).
8. **Duplicated logic**: sales vs purchase calculators (two 250–600 line twins), `FindBy*` lookups copied for legs (`PriceListItemLookupHelper.cs:575-718`), two purchase engines (§3), four D&S appliers (§4), two import D&S rule copies, two revenue allocators.
9. **UI-side rules**: manual flags and weekday/region/limit-to-one D&S selection decided in Angular (§2, §4); the server trusts the DTO. The "stale selection" dual path exists only to compensate for this (`FreightPriceQueryManager.cs:71-78, 114-122`).
10. **Per-km surcharges modelled as manual freight lines** with modifiers baked into either rate or kilometres depending on type (397-424) — a hack to survive recalculation.
11. `OrderNumber` collisions (every list line gets 1, `FreightSalesPriceCalculator.cs:558`) and highest-wins pruning that can delete manual lines.
12. `FixedPrice` naming collision: order-level fixed price vs `PriceSpecification.FixedPrice` (= per freight item from a list); inconsistent fixed-price revenue (§2).
13. `MinPrice`/`MaxPrice` and `IncomingInvoicesOnlyApplyHighest…` exist in the schema but are dead.
14. Empty "placeholder" lines with `UnitPrice = null` are persisted when a basis is on without a list or no item matches (344-355, 517), so "no price" and "€0" are indistinguishable from a null.

## 10. Functional scenario catalogue

Format: *Role — trigger — expected outcome* (F = fact from code, I = inferred).

**Price list lookup**
1. Planner — order AMS→BRU, list `FirstAndLastWaypointRelationsShouldMatch`, item with both relations — that item wins over region items (F, `PriceListItemLookupHelper.cs:113-115`).
2. Planner — same, only region items exist, origin relation in region "NL-West", destination in "BE" — region-to-region item applies (F, 128-130).
3. Planner — origin exact relation, destination region — mixed item applies before region-to-region (F, 123-125).
4. Planner — list `OnlyLastWaypointRelationShouldMatch`, delivery relation has an exact item and a region item — exact wins (F, 145-150).
5. Planner — freight with transport unit, product and vehicle type all matching an item — T3 item wins over any partial item (F, 227-231).
6. Planner — only transport unit matches — item with TU set and product/vehicle null wins over item with only product (F, 438-451 order).
7. Planner — no dimension matches — lane-only item (all three null) applies (F, 729-732).
8. Planner — no lane matches at all — line created with `UnitPrice = null`, `Price = null` (F, 517, 562).
9. Planner — weight-graduated list, 350 kg, offsets 100/500/1000 — column `Price1` (F, 58-96).
10. Planner — 1,500 kg, same list — open-ended `Price3` (F).
11. Planner — exactly 500 kg — `Price1` (upper bound inclusive) (F, line 88).
12. Planner — list `SumFreightItems`, two freights to the same relation 300 + 300 kg — both priced in the 600 kg column (F, `FreightSalesPriceCalculator.cs:459-463`).
13. Planner — same but second freight goes to a relation in the same region, not the same relation — not summed (F, 404-407 comment).
14. Planner — `GroupByTransportUnit`, pallets and rolls to same relation — pallets summed only with pallets (F, 464).
15. Planner — row offsets by distance (50/150/∞), 120 km — tier ≤150 row (F, 209-215).
16. Planner — freight has no distance, row offset property Distance — no price (null item) (F, 201).
17. Planner — per pallet type: items per `TransportUnitId`, `PricePerUnit`, basis amount — `Amount × UnitPrice` (F, 514).
18. Planner — per vehicle type list (`ChargeByVehicleType`) — one line per freight with amount 1 (F, 580).
19. Planner — per km list (`ChargeByDistance`) — amount = freight distance (F, 578); per-km D&S then not generated (F, `FreightPriceQueryManager.cs:193-194`).
20. Planner — per hour list (`ChargeByDuration`) — amount = `Duration.TotalHours` (F, 579).
21. Planner — `UnitPriceConversionFactor = 1000`, 2,500 kg — amount 2.5 (F, 520-523).
22. Planner — `PricePerLastWaypoint`, three freights to one delivery — first gets amount 1, others 0 (F, 526-533).
23. Planner — `PricePerFreightItem` and user changed amount to 0.5 — amount kept, unit price refreshed (F, 538-548).
24. Planner — order charges by amount and by weight with two lists, `OnlyApplyHighest…` on — per freight only the higher line remains (F, 294-312).
25. Planner — same with highest flag off — both lines, both invoiced (F, I).
26. Planner — min/max price set on the item — ignored (F, dead fields).
27. Planner — price list expired yesterday — still applied (F, no validity check).
28. Planner — basis flag on, no list — empty line to fill by hand (F, 344-355).

**Manual lines, requery, fixed price**
29. Planner — manually added line, then distance changed → requery — line kept, amount re-read from freight, price recomputed (F, 38-67, 346-351).
30. Planner — unit price edited on a list line (adjusted), requery — unit price kept, amount refreshed, price recomputed (F, 359-376).
31. Planner — charge-by-weight switched off — its auto line disappears, manual lines stay (F, test `FreightSalesPriceCalculator_Tests.cs:57`).
32. Planner — adjusted line of a switched-off basis — kept (F, test :85).
33. Planner — freight distance changed in the detail dialog — per-km lines re-valued from the new distance without the selection (F, `FreightPriceQueryManager.cs:245-248`).
34. Planner — toll surcharge deselected on the main screen — toll line dropped (F, `…TollDeselect_Tests.cs:99`).
35. Planner — detail dialog with stale empty selection — toll line preserved with tolled km (F, :139).
36. Planner — new freight added while per-km D&S exists — line replicated with same rate (F, 253-286).
37. Planner — order set to fixed price €800 with freight lines present — save refused (F, `TransportOrderRepositoryHandler.cs:1485`).
38. Finance — fixed-price order invoiced — one level-one line €800, freight lines without price (F, `DefaultInvoiceLineCreationService.cs:204-210, 292`).
39. Finance — fixed price flag on, value 0 — approval warning (F, `FixedPriceApprovalValidator.cs:15-18`).

**Discounts and surcharges, additional services, toll**
40. Planner — relation default "fuel 5% (before)" on a €1,000 order — revenue €1,050 (F, `ITransportOrderRevenueCalculator.cs:140`).
41. Planner — "fuel 5% before" + "admin 2% after" — after applies on €1,050 → €1,071 (F, 143).
42. Planner — fixed surcharge €25 — added once regardless of freights (F, 171).
43. Planner — weekend surcharge `AppliesExclusivelyOnSaturdays`, order date Saturday — selected; Monday — not (F, Angular 2292-2294, import 152-155).
44. Planner — region-restricted D&S, two freights to that region, limit-to-one off — id pushed twice → charged twice (F, Angular 2305-2309; I on the double charge).
45. Planner — same with limit-to-one on — once (F).
46. Planner — per-km D&S €0.20, 150 km — distance line 150 × 0.20 = €30.00, description = D&S name (F, 366-378).
47. Planner — per-km with 80 % maximum billable — rate 0.16 × 150 (F, 417-419).
48. Planner — per-km round trip — rate doubled (F, 420-421).
49. Planner — toll-only D&S, route has 40 tolled km — 40 × levy, km rounded to 3 decimals (F, 400-412).
50. Planner — toll-only, lookup fails — no line, warning logged (F, 353-355).
51. Planner — toll-only, round trip + 50 % — kilometres 40 × 2 × 0.5 (F, 407-410).
52. Finance — D&S renamed after orders were priced — per-km lines no longer recognised at invoice/revenue (I from name matching).
53. Finance — D&S value changed after pricing — open orders invoice with the new value (F, live read).
54. Planner — additional service "waiting time 2 × €45" — €90 added after D&S, own invoice line with ledger account (F, 84-86, 398-410).
55. Finance — relation D&S default valid till last month, new order today — not proposed (F, `TransportOrderAppService.cs:1401`).

**Purchase, cost, allocation**
56. Planner — leg assigned to charter trip, carrier charges by amount via inbound list — purchase line per freight with `TransportFileId` (F, `FreightPurchasePriceCalculator.cs:219-234`).
57. Planner — carrier charges by distance, trip actual 300 km over three legs — Cofret-allocated km per leg summed per freight (F, `InputBuilder.cs:139-149, 181-216`).
58. Planner — relation lacks coordinates — leg's own distance used (F, 141-148, 265-278).
59. Planner — leg moved to another trip — old trip's purchase lines for that freight deleted, both trips recomputed (F, `FreightPriceCalculationService.cs:37-60`, `TransportFileAppService.cs:2370-2372`).
60. Planner — carrier has two inbound lists, lowest-wins on — one line per segment (F, 55-68).
61. Planner — manual purchase line, trip re-saved — kept and re-valued (F, 99-105, 122-132).
62. Finance — own-fleet trip, vehicle €1.10/km, trailer type €0.30/km, 200 km — cost €280 (F, `TransportFileCostCalculator.cs:176-178, 247`).
63. Finance — charter trip with incoming invoice expected €500, actual €520 — cost €520 (F, 240).
64. Finance — order's legs all on one trip that carries nothing else — cost of revenue = trip cost (F, `TransportOrderCostCalculator.cs:136-143`).
65. Finance — order on a shared trip, 100 of 400 assigned km — cost = 100 × (trip cost / 400) (F, 172-187).
66. Finance — trip with legs of two orders — trip revenue = Σ leg km × order revenue per km (F, `ITransportFileRevenueAllocator.cs:43-52`).
67. Finance — trip holds all freights of one order and legs have no distance — full order revenue (F, 60-85).
68. Finance — two orders fully on one trip, no distances — revenue 0 (F, 60-67 `Count == 1`).
69. Finance — grid without profit columns — cost not computed (F, `GetAllTransportOrderDtoQuerySelector.cs:543-575`).

**Approval and invoicing**
70. Finance — stored unit price differs from list — warning, not blocking (F, `CorrectAppliedPriceListApprovalValidator.cs:56-57`).
71. Finance — line `Amount × UnitPrice ≠ Price` — error (F, `CorrectPriceCalculationApprovalValidator.cs:30-36`).
72. Finance — freight without any non-zero price — warning (F, `EmptyFreightPriceApprovalValidator.cs:20-22`).
73. Finance — order re-invoiced with < €0.50 difference — archived instead (F, `NextInvoicePriceApprovalValidator.cs:59-63`).
74. Finance — freight with one price line and the basis field in the description — level-two line carries amount/unit price/price (F, `DefaultInvoiceLineCreationService.cs:286-293`).
75. Finance — freight with two lines — level-three line per price line (F, 300-313).
76. Finance — invoice type groups prices — one level-two line with the sum (F, 292).
77. Finance — per-km surcharge line — always its own line "`Name km à € rate`" (F, 319-333).
78. Finance — previously invoiced order — credit line for the previous value first (F, 186-200).
79. Finance — percentage D&S — invoice line rounded to cents over the running total (F, 331).
80. FMS — trip completed, order has no prices yet — prices computed and inserted, revenue attribute synced; errors logged, not thrown (F, `TransportOrderFreightPricingService.cs:67-140`).
