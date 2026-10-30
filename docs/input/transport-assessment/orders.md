<!-- Appendix to docs/input/transport-rebuild-assessment.md and pricing-rebuild-assessment.md. Produced 2026-10-25 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 5f547cc6b); not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in that checkout. F = fact read in code, I = inferred. -->

# Legacy OpenTMS — Transport Order assessment

Source root: `/Users/marinusbrink/dev/OpenTMS/aspnet-core/src` (paths below are relative to it). Read-only review, 2026-10-25. "TOAS" = `FiLogic.Application/BusinessServices/TransportOrder/TransportOrderAppService.cs`; "RH" = `.../TransportOrderRepositoryHandler.cs`; "ENT" = `FiLogic.Core/BusinessEntities/TransportOrder/`.

## 1. Entity model

All entities derive from `FullAuditedFiLogicEntity` (Guid id, TenantId, Flags colour bits, soft-delete, audit). SQL Server tables `tbl*`. No navigation properties, no FK constraints except the dead `tblTransportOrderDiscountAndSurcharge` (migrations: `FK_tblTransportOrderDiscountAndSurcharge_*`). All money and quantities are `double?`; dates `DateTime` (date-only by convention, stored UTC, see §11); durations are **strings** parsed with `TimeSpan.TryParse` (`ENT/TransportOrderFreight.cs:56-66`, `ENT/TransportOrderFreighLeg.cs:29-36`); lists are pipe-delimited GUID strings.

### TransportOrder (`tblTransportOrder`, `ENT/TransportOrder.cs`)

| Group | Fields (line) |
|---|---|
| Header | `IsTemplate` (13), `SourceTemplateId` (15), `RelationId` (customer, 18), `RelationContactPersonId` (21), `TransportOrderTypeId` req. (25), `OrganizationUnitId` (28), `OrderState` (31), `OrderNumber` req. string (34; empty for templates/pending), `OrderDate` (38), `OrderReference` (41), `InternalTransportOrderRemarks` (60), `Flags` (base) |
| Financial | `CurrencyId` req., `VatId` req., `PaymentTerm` int days, `SurchargesAndDiscounts` = pipe-joined D&S GUIDs (54), `SendFreightDocumentsAlongWithInvoice`, `InternalInvoicingRemarks` |
| Charge-by selection | 13 boolean/price-list pairs: `ChargeFixedPrice`+`FixedPrice` (double?), `ChargeByAmount/Positions/Colli/Volume/Weight/Epq/LoadingMeters/LoadFactor/Distance/Duration/VehicleType/FreightItem` each with `…PriceListId` (62-111); `OnlyApplyHighestPriceWhenHavingMultiplePriceLists` (113). Copied from the Relation by `TransportOrderEditDto.ApplyRelationToTransportOrder` (`Dto/TransportOrderEditDto.cs:20-66`). |
| Template/recurrence | `RecurrenceType` (None/Weekly/Monthly), `RecurrenceInterval`, `RecurrenceOnMonday…Sunday` (bool?), `RecurrenceOnDayOfTheMonth`, `TemplateName` (119-168) |
| "Tevens vrachtdossier" | `CreateTransportFileAlongWithTemplate` (139), `TemplateTransportFileTypeId`, `…DepartureRelationId`, `…ScheduledTimeOfDeparture`, `…ArrivalRelationId`, `…ScheduledTimeOfArrival`, `…TransportCompanyId`, `…TransportCompanyContactPersonId`, `…Primary/Secondary/ThirdVehicleId`, `TemplateTransportFileDrivers` (pipe-joined relation GUIDs, 161), `…BuyingPrice` (double?), `…DiscountAndSurchargeId` (141-165); `TransportFileFillsTransportOrder`, `TransportFileStartFillingWaypointTypeId`, `TransportFileStopFillingWaypointTypeId` (170-174) |
| Custom | `CustomProperty0…9` strings, labels from `CustomProperty` table where `EntityType == "TransportOrder"` (TOAS:1172) |

### Children

| Entity (table) | Parent → cardinality | Key fields |
|---|---|---|
| **TransportOrderFreight** = order line (`ENT/TransportOrderFreight.cs`) | Order 1→N | `TransportUnitId` req., `ProductId`, `ProductRelationCodeId`, `VehicleTypeId`, `TemperatureRangeId`, quantities `Amount, Positions, Colli, Volume, MeasuredVolume1-3, Weight, MeasuredWeight1-3, Epq, LoadingMeters, LoadFactor` (double?), `Duration` string, `Distance, Length, Width, Height, CarbonFootprint`, texts `InvoiceReference, InternalRemarks, AttachedDocuments, InstructionsForwarder, Remarks, SpecialAgreements`, `FreightPaymentInstruction` (NotSet/Free/NotFree), container fields `ContainerNumber, SizeOrIso, Pincode, ShippingCompany, ShipperId, Barcode`, `CustomProperty0-9` |
| **TransportOrderFreightWaypoint** | Freight 1→N (≥2 required, RH:573-575) | `WaypointTypeId`, `OrderNumber` (1-based sequence), `RelationId` (address = relation's visiting address), `RelationContactPersonId`, `Date`, `TransactionTime` TimeSpan?, `TimeSlotId`, `Reference`, `Remarks` |
| **TransportOrderFreightLeg** (`ENT/TransportOrderFreighLeg.cs`) | Freight 1→N, chained; `TransportFileId` nullable = the trip | `OrderNumber`, `Distance`, `Duration` string, `Estimated/ActualCarbonFootprint`, `OrganizationUnitId`, origin block (`OriginWaypointTypeId/RelationId/ContactPersonId`, `DepartureFromOrigin`, `OriginTransactionTime/TimeSlotId/Reference/Remarks`) and mirror destination block (`ArrivalAtDestination`…). Origin/destination are **copies** of waypoint data, not references. |
| **TransportOrderFreightStop** | Order 1→N; `TransportOrderFreightId` nullable ("manual/loose" stop when null) | `OrderNumber`, `WaypointTypeId`, `RelationId?`, `CalculatedTimeOfArrival/Departure` — order-level stop schema synced to the along-with trip (TOAS:3120) |
| **TransportOrderFreightPrice** | Freight 1→N | `TransportOrderFreightPriceType` Sales/Purchase, `PriceSpecification` (SpecifiedByAmount…FixedPrice), `TransportFileId?` (Purchase must have, Sales must not: RH:306-313), `OrderNumber`, `Description`, `Amount, UnitPrice, Price` double?, `IsCreatedManually`, `IsAdjustedManually` |
| **TransportOrderAdditionalService** | Order 1→N | `Date`, `LedgerAccount`, `CostCenter`, `Description`, `Amount/UnitPrice/Price` |
| **TransportOrderReference** | Order 1→N | `ReferenceType`, `ReferenceValue`, `ReferenceDescription` |
| **TransportOrderFreightCapability** | Freight N↔M Capability | FK column misnamed `TransportOrderFreightCapabilityId` (= freight id, todo at `ENT/TransportOrderFreightCapability.cs:11`) |
| **TransportOrderDiscountAndSurcharge** | Order N↔M D&S | **Dead**: only touched by a raw SQL re-parent in `ReferencedPropertiesSynchronizer.cs:79`; the real data is the pipe string. |
| **TransportOrderAttributes** (`ENT/TransportOrderAttributes.cs`) | Order 1→1 denormalised grid row | `Revenue`, computed `NrOfFreights/NrOfWaypoints/NrOfLegs`, concatenated `WaypointDates/Names/Cities/References` (+Origin/Destination variants), `DiscountsAndSurcharges`, `InvoiceReferences`, `TransportFiles`, `Primary/Secondary/ThirdVehicles`, `Vehicles`. Maintained by RH `SynchronizeTransportOrderAttributes` (RH:1842-2010) and a SQL trigger `trigger_TransportOrderFreightLeg_TransportFileIdChanged` (`FiLogicDbContext.cs:472`). |
| **TransportOrderCallback** | Order 1→N | `Name, Url, Headers ("H1=1;H2=2"), Method, LastCallback, LastCallbackResult` |
| View `vwTransportOrder` (`ENT/View/TransportOrder.cs`, `FiLogicDbContext.cs:815`) | read model for the grid | order + attributes + names |

Legs ↔ trips: a leg is on a trip when `TransportFreightLeg.TransportFileId != null`; the trip's `TransportFileStop.TransportOrderFreightLegIdentifiers` (delimited string) points back. A freight is "assigned" when any leg has a file. Purchase prices carry the file id.

Indexes (`FiLogicDbContext.cs:432-496`): per child three overlapping indexes on `(ParentId, TenantId, IsDeleted)`, `(ParentId, TenantId)`, `(ParentId)`; order has the same triple on `OrderDate`, `OrderState`, `IsTemplate`. **No index or unique constraint on `OrderNumber`, `OrderReference`, `SourceTemplateId` or `Leg.TransportFileId`+freight.** No concurrency token anywhere.

## 2. Lifecycle

`TransportOrderState` is `[Flags]`: `PendingAcceptance=-1, Open=0, Canceled=1, ReadyForInvoicing=2, Invoiced=4, Archived=8` (`ENT/TransportOrderState.cs`). Invoicing **adds the bit** (`Invoice/OutboundInvoiceAppService.cs:1132`: `OrderState.Add(Invoiced)`), credit notes remove/re-add it (1274-1280), so real values include `ReadyForInvoicing|Invoiced`=6 and `Archived|Invoiced`=12. Grid filter special-cases this (`GetAllTransportOrderDtoQuerySelector.cs:134-135`).

| Transition | Trigger |
|---|---|
| (new) → Open | `CreateOrUpdate` from the UI/import; number drawn in `CreateBatchAsync` (RH:753) unless template or pending. |
| (new) → PendingAcceptance | Self-service portal (`SelfServicePortalAppService.cs:689,1531`) and importers; `OrderNumber = ""`. Pending orders skip leg validation (RH:458, 567). |
| PendingAcceptance → Open | `Accept` (TOAS:2671): prices queried, default legs generated for freights without legs, state Open, handler draws the number on the state change (RH:933-941), optional confirmation e-mail job. |
| Open → ReadyForInvoicing / Archived | Approval (`TransportOrderApprovalValidator.ApproveAsync`, §5). Archived when `OnlyArchive` or when NextInvoicePrice validator says nothing to invoice. |
| Open → Canceled | **No server rule**: the UI posts `OrderState` in the DTO and RH copies it (RH:855). Only `Transporeon…ImportService.cs:234` sets it in code. |
| ReadyForInvoicing → +Invoiced | Outbound invoice creation. |
| any non-Open/Pending → Open | `Revise` (TOAS:1765-1795): rejects Pending/Open, otherwise resets to Open (even Invoiced). |
| Delete | Only `PendingAcceptance`/`Open` and no leg on a trip (RH:1697-1720); soft-deletes order, freights, legs, waypoints, prices, additional services — **not** references, freight stops, callbacks (RH:1724-1744). |

Numbering (`TransportOrderNumberGenerator.cs`): per `TransportOrderType.OrderNumberCreationOption` (plain index, YY/YYYY prefix, YY/YYYY+MM prefix, with `OrderNumberPrefix`, `YearSuffix`, `MonthSuffix`, `OrderNumberFormat`). Year-mode finds the max by string length/ordinal after stripping the prefix (71-74); month/default modes take the **last created** order (95-96, 111-112). An instance field `_lastGeneratedOrderNumber` papers over races (25, 45-49, 77-79). Changing `OrderDate` across a year/month boundary is refused when numbering depends on it (RH:580-606). `IsTemplate` may not change after creation (RH:608-617).

Concurrency: none — no rowversion, last write wins; number generation is read-max-then-insert inside one transaction without a lock.

## 3. Operations

### ITransportOrderAppService (TOAS; class-level `[AbpAuthorize()]` only, TOAS:55 — no per-method permission)

| Group | Operation | Purpose / side effects |
|---|---|---|
| CRUD | `GetSingle`, `GetMultiple` (1138-1602) | Loads full `TransportOrderEditDto` (order, freights, waypoints, legs, stops, prices, add. services, references, D&S ids, drivers, type defaults, custom property definitions). Without `Id` returns a **default new order** (type defaults, default currency/unit, `DefaultWaypointTypes`). ~30 queries per call. |
| | `CreateOrUpdate` (1607) | `RH.ValidateCreateOrUpdate` → `RH.CreateOrUpdate` → along-with-trip create/update (§7) → re-assign kept legs (setting) → sync manual stops to trip → `TransportOrderForwardService.Forward` (gateway jobs) → `TransportOrderUpdatedEventData` (webhooks). |
| | `CreateOrUpdateBatch` (1678) | Same for a list (importers). |
| | `Delete` (1739) | See §2. |
| | `Revise` (1765) | Back to Open. |
| | `SetFlags` | Colour flags. |
| Freight/line | `GetFreightsByTransportOrder` (686) | Freight DTOs with origin/destination waypoint projections and trip references. Capability join is broken (723 filters by row id, not freight id). |
| Legs | `GetLegsByTransportOrderFreight` (822), `CreateOrUpdateLegs` (848), `DeleteLegs` (955), `GenerateDefaultTransportOrderFreightLegs` (1901) | §4. `DeleteLegs` re-chains neighbours, requeries distances, forbids deleting the last leg (973-979). |
| Pricing | (inside `CreateOrUpdate`) | Prices come **from the client**; server only validates ordering, fixed-price exclusivity (RH:1463-1488), purchase/sales ↔ file binding (RH:300-342), and replaces purchase prices recalculated by leg (un)assignment (RH:1192-1201, 1323-1336). `Accept`, `GenerateReturnTransportOrder`, redistribution call `IPriceListAppService.QueryFreightPrices`. |
| | `ApplyDiscountsAndSurcharges` (250) | Bulk add/remove/replace D&S ids on open non-template orders; edits the pipe string. |
| Approval | `Accept` (2671) | §2. |
| Templates | `CreateFromTemplate` (2338), `GetTransportOrderFreightByTemplate` (2384), `GenerateReturnTransportOrder` (1797) | §6; return order swaps first/last waypoint, keeps day span, requeries prices. |
| Bulk | `RedistributeByTransportOrderFreight`, `RedistributeFmsSourced` | §8. |
| Export | `ExportAllToExcel` (652) background job; `DownloadTransportOrderImportTemplateExcelFile`; `Import` | Importers out of scope. |
| Lookups | `GetAll` (576) grid; `GetPrintEmailTransportOrderSelection`/`GetTransportOrderSelectionShort` (584-649, keyset-paged full selection); `GetTransportOrderStateList`; `GetAllAttributes`/`GetTransportOrderFreightAttributes`/`GetTransportOrderFreighttWaypointAttributes` (column metadata); `GetList` throws `NotImplementedException` (1111); `Reset` hard-deletes everything (1120, no auth). |

### ITransportOrderApprovalAppService (`TransportOrderApprovalAppService.cs`)

`GetApprovalValidatorList` (reflection over `IApprovalValidator` types, excludes system ones), `ValidateApprove` and `Approve` both enqueue background jobs (`TransportOrderApprovalJobs.cs`) returning a job id to poll.

## 4. Leg generation (`GenerateDefaultTransportOrderFreightLegs`, TOAS:1901-2281)

Input: the freight's waypoints (ordered), optional customer `RelationId`, `OrganizationUnitId`, `UpdatedWaypointId`, `CalculateLegDistance`.

1. If tenant setting `PreventChangingWaypointRelationWhenLegAssigned` and the edited waypoint is first/last of a freight with an assigned leg → only patch that leg's origin/destination (`UpdateTransportOrderLegs`, 2284-2335) or throw `UnableToUpdateWaypointBecauseLegIsAssigned`.
2. If the tenant has **no** via-waypoint configuration (`RelationDefaultViaWaypoint`, `GeographicalRegionDefaultViaWaypoint`, cached per service instance 1935-1936): one leg per consecutive waypoint pair, distance/duration from `IRelationAppService.QueryDistance` (1945-1985). Every leg gets `OrderNumber = 1` (1960) and the sort is by `OrderNumber` only (1943, chained `OrderBy` overrides) — works because the client sends one freight at a time.
3. Otherwise, per pair, find default via waypoints in order: relation of either waypoint (+ waypoint type), then the customer relation, then the geographical regions of the waypoints (2035-2104). Vias equal to origin/destination are dropped (2108). Vias are ordered: matching source type, unmatched, matching destination type (2117-2124). Each via **splits the previous leg**: new leg from via to the old destination; the previous leg now ends at the via; the via's waypoint type is `WaypointType.SuccessiveWaypointTypeId` (e.g. unload-at-depot → load-at-depot) or the via type itself depending on which side matches (2195, 2225); `DepartureFromOrigin` of the new leg = previous departure + driving duration, date only (2199). Bug: `previousLeg = LastOrDefault()` of the whole list (2132), so with >2 waypoints vias attach to the wrong pair; the "next waypoint" lookup compares a freight id with itself (2018).
4. Legs get tenant id and optional organisation unit override (2274-2279). Nothing is persisted; the client posts legs with the order.

Default waypoints: `TransportOrderType.DefaultWaypoints` is a `|`/`;` delimited list of waypoint-type GUIDs, ≥2 required (`ResolveDefaultWaypointTypes`, 3322-3350; note 3341 checks the **waypoint** repository for the type id, which is always 0). `TransportOrderType` also carries `DefaultFreightItemFields`, `RequiredFreightItemFields` (validated by reflection, RH:355-363), `DefaultDurationBetweenFirstAndLastWaypoint`, `DefaultOrganizationUnitId`, `Mailbox`.

Regeneration: the client regenerates and posts a full leg list; the server diffs by id (RH:1156-1264). Legs that disappear or whose origin/destination relation changed while on a trip are **unassigned from the trip** via `AssignTransportFileFreightLegs(TransportFileId = null)` in a separate transaction (RH:1171-1190), which recalculates stops and purchase prices; with setting `KeepLegsAssignedWhenOverridedByDefault` the changed legs are re-assigned to their old file afterwards (RH:1206-1216, TOAS:1635-1648). Chain validation: first leg origin = first waypoint, last leg destination = last waypoint, consecutive legs connect, order numbers 1..n, departure ≤ arrival with silent date fixes (TOAS:3239-3293, RH:454-540; duplicated). Assigning a leg to a trip may rewrite leg and waypoint dates to the trip's scheduled dates when `AdjustTransportOrderFreightLegDatesToTransportFile` is on (`TransportFile/TransportOrderFreightLegAssignmentManager.cs:135-147, 272-300`).

## 5. Approval ("Goedkeuren")

`TransportOrderApprovalValidator.ValidateAsync` (`TransportOrderApprovalValidator.cs:38-150`) loads DTOs, runs every `ISystemApprovalValidator` plus the user validators named in tenant setting `App.TransportOrder.ActiveApprovalValidators` (261-282, by class name), merges results. Default outcome without warnings = `ReadyForInvoicing`; a warning carries a proposed state; conflicting proposed states among `ReadyForInvoicing/Invoiced/Archived` become an error (`ApprovalValidatorBase.cs:30-35`); any error → stays `Open`. `OnlyArchive` skips validation and sets `Archived`. `ApproveAsync` re-validates and refuses if the client's results differ (`TransportOrderApprovalValidationResultsNotEqual`, 176-177), then writes `StateAfterApprove` and fires the updated event. Approval changes **only `OrderState`**.

| Validator | System? | Check → outcome |
|---|---|---|
| OrderStateApprovalValidator | yes | state must be Open → error |
| ForeignKeyReferencesApprovalValidator | yes | relation, order number, currency, VAT, order type exist → errors |
| NextInvoicePriceApprovalValidator | yes | revenue (fixed price or Σ sales prices, D&S applied in two passes, + add. services) vs Σ historical invoice lines; both 0 → warn Archived; delta < 0.50 → warn Archived |
| FreightDocumentsAvailableApprovalValidator | yes | `SendFreightDocumentsAlongWithInvoice` orders must have a binary object of an `IncludeInInvoice` type → warn |
| CorrectAppliedPriceListApprovalValidator | user | unit price ≠ price-list unit price → warn |
| CorrectPriceCalculationApprovalValidator | user | price amount ≠ freight quantity → warn; `Amount×UnitPrice ≠ Price` → **error** |
| DuplicateOrderReferenceApprovalValidator | user | same `OrderReference` in batch or in this calendar year → warn |
| EmptyFreightPriceApprovalValidator | user | freight without non-zero sales price → warn |
| FinancialDefaultsMismatchApprovalValidator | user | currency/VAT/payment term/send-docs/D&S differ from relation → warn |
| FixedPriceApprovalValidator | user | `ChargeFixedPrice` with empty/0 price → warn |
| FreightLegsDistanceMatchApprovalValidator | user | Σ leg distance ≠ freight distance → warn |
| TaskStatusApprovalValidator | user | not all trip stops of the order have `ActualTimeOfArrival` → warn |

(12 concrete validators + base + 2 interfaces = the 14 files.) Automatic approval: `AutomaticTransportOrderApprovalBackgroundWorker` (every 2 h, overnight, once per day per tenant with setting `ApproveTransportOrdersAutomatically`) validates all Open orders and approves those with `ReadyForInvoicing` and **no warnings**, then notifies every user (49-138). `RelationConfigured…Worker` does the same for orders of relations with `AutomaticallyApproveTransportOrders` (38-52).

## 6. Templates and recurrence

A template is an order with `IsTemplate = true`: no number, no legs persisted (RH:744-746, 950-954), no D&S of its own (TOAS:2440-2446), permission `TransportApp_TransportOrdersTemplates`. `GetTransportOrderFreightByTemplate` (TOAS:2384-2643) deep-copies the source for `NrOfCopies` per `(TransportOrderId, OrderDate "yyyy-MM-dd")`: regenerates legs for templates, re-ids everything, shifts waypoint dates by the original day offsets from the new order date, resets leg `TransportFileId`, drops purchase prices, re-dates additional services and freight stops, shifts the along-with-trip scheduled times by the same day offset, sets `IsTemplate=false`, `SourceTemplateId`, empty number, `Open`, recurrence cleared. The same wizard copies existing orders ("Nieuw van sjabloon"). `CreateFromTemplate` persists one copy via `CreateOrUpdate`.

`PeriodicTransportOrderCreatorBackgroundWorker` (every 5 min, tenants found by raw SQL `IsTemplate=1 AND RecurrenceInterval>0`, `TransportOrderTemplateLookupService.cs:22`): for each template and each of the next 5 days, creates a copy if `RecurrenceOn<DayOfWeek>` (looked up by **reflection**, 62) is true and no order with that `SourceTemplateId` and `OrderDate` exists (65-68). `RecurrenceType`, `RecurrenceInterval` (beyond >0) and `RecurrenceOnDayOfTheMonth` are **not evaluated** — monthly recurrence is dead.

## 7. "Tevens vrachtdossier" (`CreateTransportFileAlongWithThisTransportOrder`, nl:4108)

When a non-template order has `CreateTransportFileAlongWithTemplate`, `CreateOrUpdateAssociatedTransportFile` (TOAS:2920-3100) runs after every save, in three transactions: (1) load or create the trip **with the same id as the order** (2961), set `FileDate=OrderDate`, `FileName=TemplateName`, scheduled departure/arrival (template times or order date + default times), departure/arrival relation, type (template or default), carrier + contact, three vehicles, drivers (explicit pipe list or the vehicles' default drivers), and an outsourcing incoming invoice with `ExpectedPrice = BuyingPrice` adjusted by the single D&S when the carrier is not "me" (2997-3039); (2) assign all unassigned legs of the order to that trip with `ApplyFreightStopOrderNumbers` (3081-3093); (3) `CalculateEtas`. Then `SynchronizeFreightStopsToTransportFile` mirrors manual (`TransportOrderFreightId == null`) order stops into trip stops and renumbers (3120-3237). If `TransportFileFillsTransportOrder` and the trip already has an actual departure, nothing is touched (2947-2948); `TransportFileStart/StopFillingWaypointTypeId` are stored only (no reader found in this folder). Later edits repeat the whole procedure; vehicles/carrier are overwritten from the order each time (2971-2975).

## 8. Redistribution

- `TransportOrderFreightRedistributionService` (`RedistributionServices/…:49-230`): all selected orders must share one customer; freights are grouped by a **property name resolved by reflection** (`RedistributeProperty`, 297-311); extra orders are created from defaults; freights with legs, waypoints and prices are moved between orders; stop actual times are snapshotted and restored; legs re-assigned to their trips; prices requeried; attributes resynced because the DB trigger does not fire on INSERT (200-215). Purpose: split/merge orders per e.g. destination or product.
- `FmsSourcedRedistributionService` (86-348): for a trip filled from the board computer (FMS), drag consecutive trip stops onto existing/new orders (`RedistributeTransportOrderTargets`), creating freight/waypoints/leg per target, unlinking the parent order, requerying prices.

## 9. Events, webhooks, forwarding, callbacks, references

- `TransportOrderUpdatedEventData` (`ENT/Events/…`, ids + tenant + trace headers) is fired fire-and-forget from five sites (TOAS:1673, RH:1455, approval 206, …). `TransportOrderWebhookEventHandler` enqueues `TransportOrderWebhookBuildJob` only when a `TransportOrderCallback` row exists; the job builds an OTM5 message and pushes it to a GCP queue with dedup.
- Forwarding: `TransportOrderForwardService` enumerates `ITransportOrderForwardJob` implementations by reflection; `TransportOrderForwardToGatewayJob` sends the order to exchange gateways configured per tenant setting or per relation (`ForwardTransportOrderUpdatesToGateway`, pipe list `TransportOrderForwardGateways`).
- `TransportOrderCallbackAppService`: CRUD/grid/excel of callback registrations.
- `TransportOrderReference`: free-form typed references, saved as a child list (RH:1085-1114), shown as "Orderreferenties".
- Importers (out of scope, one line): Afas, AhrendInternational, AlbertHeijn, AsiaExpressFood, BrinkXL, CargoOffice, CSV/Excel profile-based, DeGroot, DriesenBeterBad, Easyflor, Emt, Florisoft, GS1, GiesingTransport, Greenline, HoekFlowers, Hoogvliet, Kivo/SapKivo, Opstal, OTM5, PlanAndGo, Proteus, RemixExactGlobe, SAP purchase order, Transporeon, TransportInfo, VanVessem, Verdel, XML/text base classes.

## 10. Querying and grids

`GetAll` → `GetAllTransportOrderDtoQuerySelector` over `vwTransportOrder` filtered by `IsTemplate` (52-53), then `DateRange`, `OrderState` (flag-aware), `RelationId(s)`, `OrganizationUnitId(s)`, `FlagFilterEnum`, free-text `Filter` over ~25 string columns (131-171). Columns (`Dto/GetAllTransportOrderDto.cs`): email confirmation status, `TaskStatus` (Requested/Accepted/Planned/InProgress/Completed from trip stop ATAs, 413-421), state, relation/contact, type, org unit, number, date, reference, waypoint counts and concatenated dates/names/cities/references (all, origin, destination), invoice references, total duration/distance/weight, attachments, D&S, `Revenue`, `RevenueBeforeDiscountAndSurcharges`, `CostOfRevenue` (`TransportOrderCostCalculator`), profit margin and %, CO₂, trip numbers, vehicles, currency, VAT, custom 0-9. Excel export runs the same query page-wise in a job. Lookups for planning: `GetTransportOrderSelectionShort` (keyset over the grid query), `GetFreightsByTransportOrder`, `GetLegsByTransportOrderFreight`; `TransportOrderFreightLegQuerySelector` serves the planboard's open-leg list. Permissions are per **menu view** (`AppPermissions.cs:75-82`: Today, ThisWeek, Open, All, PendingAcceptance, TodayAndTomorrow, Templates), not per operation.

## 11. Known problems — do not clone

| # | Problem | Citation |
|---|---|---|
| 1 | Pipe-delimited GUID strings for D&S, drivers, default waypoints, via gateways, stop→leg refs | `ENT/TransportOrder.cs:54,161`; RH:683-686; TOAS:3044, 3331 |
| 2 | Durations stored as strings, parsed with culture-dependent `TimeSpan.TryParse`; invalid ones silently nulled | `ENT/TransportOrderFreight.cs:56-66`; TOAS:731-733 |
| 3 | `double?` for money and quantities; 2-decimal rounding by hand | `ENT/TransportOrderFreightPrice.cs:33-39`; RH:643-662 |
| 4 | Reflection for required fields, recurrence days, approval validators, forward jobs, redistribution grouping, entity equality | RH:359-363; `PeriodicTransportOrderCreatorBackgroundWorker.cs:62`; `TransportOrderApprovalValidator.cs:270`; `…Redistribution….cs:297-311`; RH:111-137 |
| 5 | Class-name / string matching: validators by `typeImplementing.Name`, custom properties by `EntityType == "TransportOrder"`, waypoint stop matching by `(WaypointTypeId, RelationId)` | `TransportOrderApprovalValidator.cs:272`; TOAS:1172, 3154-3157 |
| 6 | `[Flags]` state enum with `PendingAcceptance = -1`; `Invoiced` is a bit OR-ed onto other states | `ENT/TransportOrderState.cs`; `OutboundInvoiceAppService.cs:1132` |
| 7 | Cancel, charge-by selection, leg regeneration and price calculation live in the client; server trusts posted `OrderState`, `Prices`, `Legs` | RH:855; TOAS:1607-1626 |
| 8 | Trip shares the order's primary key (`TransportFile.Id = TransportOrder.Id`) | TOAS:2961, 3088 |
| 9 | Order number: read-max-then-insert without lock, instance counter hack, month mode uses last `CreationTime` | `TransportOrderNumberGenerator.cs:25,45-49,95-96` |
| 10 | No unique index on `(TenantId, OrderNumber)`; no FKs; triple redundant indexes; `TransportOrderDiscountAndSurcharge` table dead; `TransportFileStart/StopFillingWaypointTypeId`, `RecurrenceType/Interval/OnDayOfTheMonth` effectively dead | `FiLogicDbContext.cs:432-496`; §6 |
| 11 | N+1 / fan-out: `GetMultiple` ~30 queries; `GenerateDefaultLegs` one distance call per pair; capability `Query` per freight; `DeleteLegs` distance per neighbour; `Thread.Sleep(1)` to order inserts | TOAS:1160-1376, 1953; RH:1079, 1113 |
| 12 | Bugs: inverted null check dereferences null (TOAS:854-857); capability lookup by wrong column (723); discarded `Distinct()` (836, RH:630); self-comparison (2018); all generated legs `OrderNumber=1` (1960); via split uses global last leg (2132); `OrderDate` setter bumps dates after 21:00 UTC (`Dto/TransportOrderDto.cs:46-47`); `Reset()` hard-deletes a tenant's orders behind plain `[AbpAuthorize]` (1120) |
| 13 | Date-only semantics emulated by `DateTime` + tenant-time-zone `.Date` fixes | TOAS:3295-3319 |
| 14 | Denormalised attributes maintained in three places (handler, SQL trigger, redistribution resync) | RH:1842; `FiLogicDbContext.cs:472`; `…Redistribution….cs:200-215` |
| 15 | Fire-and-forget event bus, `Task.Wait()`/`.Result` in workers, `Console.WriteLine` timing logs | TOAS:1673; `AutomaticTransportOrderApprovalBackgroundWorker.cs:161,188`; RH:197 |
| 16 | Single permission for the whole service; view-based permissions instead of actions | TOAS:55; `AppAuthorizationProvider.cs:132-139` |
| 17 | Delete leaves references, freight stops and callbacks orphaned | RH:1731-1744 |

## 12. Functional scenario catalogue

Format: Role — trigger — expected outcome. **F** = seen in code, **I** = inferred.

### Create / edit
1. Order clerk — opens "new order" — defaults: today, default type, default currency, default transport unit, type's default waypoint types, type's default org unit (TOAS:1182-1224). F
2. Order clerk — selects a customer — currency, VAT, payment term (30 if none), invoicing remarks, all charge-by/price-list settings and valid D&S copied from the relation (`TransportOrderEditDto.cs:20-66`). F
3. Order clerk — customer has `DefaultFirstWaypointRelationId` — first waypoint relation prefilled (TOAS:1397-1398). I (DTO field only)
4. Order clerk — saves an order whose type requires freight fields — save refused listing the missing fields (RH:355-363). F
5. Order clerk — saves a freight with <2 waypoints — `ApplicationException` (RH:573-575). F
6. Order clerk — saves a non-pending, non-template freight without legs — `ApplicationException "has no legs"` (RH:567-570). F
7. Order clerk — saves — order number generated per type option; templates and pending get none (RH:753). F
8. Order clerk — changes order date to another year on a year-prefixed type — refused (RH:593-597). F
9. Order clerk — toggles IsTemplate on an existing order — refused (RH:614-615). F
10. Order clerk — removes a freight — its waypoints/legs/prices/capabilities removed; a soft-deleted freight id re-posted is revived (RH:760-778, 976). F
11. Order clerk — sets `ChargeFixedPrice` and still posts freight prices — refused (RH:1485-1486). F
12. Order clerk — posts a sales price with a trip id or a purchase price without — refused (RH:306-313). F
13. Order clerk — changes a freight quantity on a freight with assigned legs — trip attributes resynced after commit (RH:1760-1838). F
14. Order clerk — saves without any change — no audit update, no event for that order (`IsAnyChanges`, RH:843, 1604). F (event still fires in TOAS:1673)
15. Order clerk — adds required capabilities to a freight — rows upserted, unknown capability refused (RH:619-641, 1419-1423). F
16. Any save — order attributes (revenue = fixed price or Σ sales + Σ add. services; concatenated waypoint strings) recomputed (RH:1924-2010). F
17. Any save — gateway forwarding jobs and webhook build job enqueued when configured (TOAS:1657, 1673). F

### Legs
18. Order clerk — adds a second unloading waypoint — client regenerates: A→B, B→C; prices requeried client-side. I (server returns legs; nothing persists them)
19. Order clerk — tenant without via config — one leg per consecutive pair with distance/duration (TOAS:1938-1986). F
20. Order clerk — customer has a default via depot — leg split into A→depot, depot→B; depot leg type = successive type (TOAS:2114-2234). F
21. Order clerk — changes origin address of a freight whose leg is on a trip, setting PreventChanging… on — the leg's origin is patched in place or `UnableToUpdateWaypointBecauseLegIsAssigned` (TOAS:1911-1931, 2295). F
22. Order clerk — deletes a middle leg — neighbours re-chained, distances requeried, renumbered (TOAS:1008-1100). F
23. Order clerk — deletes the only leg — refused (TOAS:973-979). F
24. Order clerk — posts legs whose chain is broken — `DestinationRelationOfLegShouldMatchOriginRelationOfNextLeg` etc. (RH:493-524). F
25. Order clerk — posts legs with departure after arrival — dates silently aligned (RH:529-533). F
26. Order clerk — changes a leg's origin/destination while it is on a trip — leg unassigned, trip stops and purchase prices recalculated; re-assigned when `KeepLegsAssignedWhenOverridedByDefault` (RH:1163-1216; TOAS:1635-1648). F
27. Planner — assigns a leg to a trip with `AdjustTransportOrderFreightLegDatesToTransportFile` — leg and first/last waypoint dates become the trip's scheduled dates (`…LegAssignmentManager.cs:272-300`). F
28. Planner — unassigns a leg — trip stops referencing only that leg are deleted (`…LegAssignmentManager.cs:152-159`). F

### Pending / accept
29. Customer (SSP) — submits an order — state PendingAcceptance, no number (SSP:689). F
30. Order clerk — accepts pending orders — prices queried, default legs generated where missing, state Open, number drawn, optional confirmation e-mail job per order (TOAS:2671-2767). F
31. Order clerk — accepts an order that is not pending — refused (TOAS:2683-2684). F
32. Order clerk — accept with e-mail but no template/report — refused; relation without e-mail — refused (TOAS:2773, 2797). F

### Approve / invoice / archive
33. Finance — validates approval — per order: proposed state + warnings/errors from active validators (§5). F
34. Finance — approves with results that no longer match — `TransportOrderApprovalValidationResultsNotEqual` (177). F
35. Finance — approves an order with a warning — allowed; state = proposed state (ReadyForInvoicing or Archived). F
36. Finance — approves an order whose price ≠ amount×unit price — error, stays Open. F
37. Finance — approves an order already fully invoiced (revenue delta < 0.50) — proposed Archived. F
38. Finance — "only archive" — Archived without validation. F
39. System — nightly auto-approval — only warning-free orders approved; all users notified (worker:86-122). F
40. System — relation with `AutomaticallyApproveTransportOrders` — same, restricted to its orders. F
41. Finance — creates an invoice — `Invoiced` bit added; credit note removes it (OutboundInvoiceAppService). F
42. Finance — revises an invoiced/archived order — state back to Open; revising Open/Pending refused (TOAS:1788-1792). F
43. Finance — bulk apply/remove/replace D&S on all open orders — pipe string edited, templates and non-open reported as errors (TOAS:250-358). F

### Cancel / delete
44. Order clerk — cancels — client posts `Canceled`; server stores it without checks (RH:855). F
45. Order clerk — deletes an Open order with a leg on a trip — refused (RH:1717-1719). F
46. Order clerk — deletes a ReadyForInvoicing order — refused (RH:1702-1710). F
47. Order clerk — deletes an Open order — order, freights, legs, waypoints, prices, add. services soft-deleted; references/stops/callbacks remain (RH:1724-1744). F

### Templates / recurrence
48. Order clerk — saves a template — no number, legs not persisted, D&S ignored (RH:744-753; TOAS:2443). F
49. Order clerk — "new from template" with date and copies — N orders with shifted waypoint dates, regenerated legs, relation D&S for that date, weekday/region D&S filter, Open, SourceTemplateId (TOAS:2384-2643). F
50. Order clerk — copies an existing order — same, keeping its D&S and sales prices, dropping purchase prices (2449-2451, 2561). F
51. System — template with Monday checked and interval > 0 — Monday orders created up to 5 days ahead, once per date (worker:46-68). F
52. System — template with RecurrenceType Monthly / day-of-month — nothing happens. F
53. Order clerk — generates a return order — new order same customer, waypoints reversed, same day span, amounts from input, prices requeried (TOAS:1797-1896). F

### Along-with trip
54. Order clerk — ticks "Maak direct een vrachtdossier aan" and saves — trip with the order's id, type/depot/times/carrier/vehicles/drivers from the order, all legs assigned, ETAs calculated (TOAS:2920-3100). F
55. Order clerk — fills carrier ≠ me and buying price + D&S — outsourcing incoming invoice with adjusted expected price (2997-3039). F
56. Order clerk — carrier = me — outsourcing invoice removed (3007-3008). F
57. Order clerk — no explicit drivers — vehicles' default drivers added (3053-3073). F
58. Order clerk — edits the order later — trip fields overwritten from the order, new legs assigned, manual stops re-synced and renumbered (3120-3237). F
59. Order clerk — trip already departed and `TransportFileFillsTransportOrder` — order save leaves the trip untouched (2947-2948). F
60. Order clerk — adds a manual stop in the order's stop schema — appears as a trip stop at its position (3152-3190). F
61. Order clerk — template with along-with trip — copies get scheduled times shifted by the same day offset (2598-2610). F

### Redistribution / bulk
62. Planner — redistributes orders of one customer by freight property — freights regrouped into N orders, legs re-assigned, prices requeried, attributes resynced. F
63. Planner — selects orders of different customers — refused (`SelectedTransportOrdersMustHaveSameCustomer`). F
64. Planner — redistributes FMS trip stops to orders — per target a freight with waypoints/leg, parent unlinked, prices requeried. F

### Grid / lookups
65. Any user — filters grid on state ReadyForInvoicing — also matches ReadyForInvoicing|Invoiced (134-135). F
66. Any user — free-text search — matches across names, numbers, waypoint strings, vehicles, D&S (141-171). F
67. Any user — opens templates view — same grid with `IsTemplate = true`. F
68. Any user — exports — Excel job paging the grid query. F
69. Planner — asks for open legs — `TransportOrderFreightLegQuerySelector` (not read in detail). I
70. Integration — registers a callback URL — every later save/approval triggers an OTM5 webhook. F
71. Integration — relation configured to forward to a gateway — every save enqueues a gateway send. F
72. Any user with any login — calls `Reset` — all orders of the tenant hard-deleted. F (security hole)
