# Design 58: Orders — transport orders, templates and recurrence, CMR

**PBI:** design 56 (umbrella), Orders part
**Status:** approved (PO 2026-10-25: Q5, Q6, Q9, Q11, Q12, Q14, Q15 decided)
**Date:** 2026-10-25
**Depends on:** designs 50, 51 (order type, waypoint type, time slot), 52, 54/55 (report definitions, `IDocumentRenderer`),
56, 57 (`IFinancialReferenceLookup`, `IPricePreviewService`, `IRateLineReader`). Input:
`docs/input/transport-assessment/orders.md` (scenarios 1–72), `screens.md`, `routing-co2-cmr.md` §3.

## Domain impact

**Module:** Orders, schema `orders`, folder `TransportOrders/` (order, lines, stops, references, terms, trip request,
templates, attribute definitions) and `ConsignmentNotes/` (profile, fixed descriptions, documents). Migration
`OrdersTransportOrders`.

### TransportOrder

| Field | Type | Rule |
|---|---|---|
| `Number` | string(32)? | drawn on Confirm from `Orders.TransportOrderType:<typeId>`; null while Draft; unique per tenant (`NULLS NOT DISTINCT` excludes null) |
| `Status` | enum `TransportOrderStatus { Draft = 1, Open, ReadyForInvoicing, Invoiced, Archived, Cancelled }` | §Lifecycle |
| `TransportOrderTypeId` | FK → TransportOrderTypes (restrict) | required |
| `CustomerRelationId`, `CustomerContactPersonId?` | Master Data ids | required relation with role Customer (validated through `IMasterDataReferenceLookup`) |
| `OrderDate` | DateOnly | default today (tenant zone); changing it across the type's numbering period is refused once numbered (OpenTMS rule) |
| `CustomerReference` | string(128)? | |
| `OrganizationUnitId` | Guid? | Platform id |
| `InternalRemarks`, `InvoicingRemarks` | string(2500)? | |
| `Attributes` | jsonb dictionary | Q12 |
| `Source` | enum `OrderSource { Screen = 1, Template, Agent, Api }` | who created it; a Draft from an agent carries `DraftConfidence` decimal(5,4)? |
| `SourceTemplateId` | Guid? | |
| `FinancialTerms` | owned: `CurrencyCode`, `VatPercentageId`, `PaymentTermDays`, `PriceListIds` (child `TransportOrderPriceList` with position), `MultipleMatches`, `DiscountOrSurchargeIds` (child with `Source` Default/Manual), `FixedPriceAmount` decimal(18,2)?, `SendFreightDocumentsWithInvoice` | copied from `RelationFinancialSettings` (design 57) at creation and on customer change (the clerk is asked), ids validated through `IFinancialReferenceLookup` |
| `TripRequest` | owned, nullable (Q9): `TransportTripTypeId`, `DepartureRelationId`, `DepartureAddressId`, `ArrivalRelationId`, `ArrivalAddressId`, `PlannedDeparture` DateTimeOffset, `PlannedArrival?`, `CarrierRelationId?`, `VehicleCombinationId?`, `VehicleIds` (≤ 3), `DriverIds` (≤ 3), `AgreedPurchaseAmount` decimal(18,2)?, `AgreedPurchaseCurrencyCode?`, `TripId?` (set by Planning's `TransportTripCreated` handler) | "Tevens trip"; honoured once on Confirm |
| stored derived (design 56): `RevenueAmount`, `CostOfRevenueAmount`, `MarginAmount` decimal(18,2)?, `Co2Kg` decimal(18,3)?, `ExecutionProgress` enum, `LineCount`, `StopCount`, `FirstStopName/City/Date`, `LastStopName/City/Date`, `TripNumbers` string(512)?, `CalculatedAt` | written by `OrderSummaryCalculator` only |
| children | `Lines`, `References` (`Type` string(32), `Value` string(128), `Description` string(256)?; ≤ 50) | |

### TransportOrderLine (OpenTMS freight)

`Position`; `TransportUnitId` (Master Data, required); `ProductId?`, `ProductRelationCodeId?`, `TemperatureId?`,
`VehicleTypeId?`; quantities `Quantity` decimal(18,4) (OpenTMS Amount), `Positions`, `Colli` decimal(18,4)?, `WeightKg`,
`VolumeM3`, `LoadingMeters`, `LoadFactor`, `Epq` double?, `LengthCm`, `WidthCm`, `HeightCm` double?; `Measurements`
child (`Kind` Weight/Volume, `Position` 1..3, `Value`); texts `InvoiceReference`, `Remarks`, `InternalRemarks`,
`ShipperInstructions`, `SpecialAgreements`, `AttachedDocuments` string(512)?; `PaymentInstruction` enum `{ NotSet = 0,
Free, NotFree }`; container `ContainerNumber`, `SizeOrIso`, `PinCode`, `ShippingCompany` string(64)?, `ShipperRelationId?`;
`Barcode` string(64)?; `RequiredTransportCharacteristicIds` child (Master Data ids); `Attributes` jsonb; stored derived
`DistanceM`, `DurationS`, `Co2Kg` (copies written by `OrderSummaryCalculator` from Planning's legs). `Stops` child
(≥ 2). Required fields per the type's `RequiredFields` flags (design 51) enforced on Confirm, not on Draft save.

### TransportOrderStop (the requested place; OpenTMS waypoint)

`Position`; `WaypointTypeId` (design 51, required); `RelationId` (required), `RelationAddressId` (required: the
address type of the stop is explicit, assessment §3.4; default the relation's loading address else visiting),
`ContactPersonId?`; `Date` DateOnly; `TimeSlotId?` or `WindowFrom`/`WindowTill` TimeOnly? (one of the two; the window
is computed with `TimeSlot.ToWindow` in the address's zone when the leg is generated); `Reference` string(128)?,
`Remarks` string(1000)?; `PlannedDurationMin` int? (default the waypoint type's). New lines start with the stops of the
order's **stop pattern**: the order keeps a `DefaultStops` child list (the "waypoint strip") copied from the type's
default waypoints and edited by the clerk; adding a line copies it.

### Lifecycle (Q5, Q6, Q15)

| Command | From → to | Rules and effects |
|---|---|---|
| `Create` | — → Draft (or Open when `confirm: true`) | terms copied; stops from the pattern; publishes nothing until Confirm except `TransportOrderLinesChanged` for Draft pricing |
| `Confirm` | Draft → Open | required fields; ≥ 1 line, each ≥ 2 stops; ids valid; number drawn; publishes `TransportOrderConfirmed` (Planning creates legs and, with `TripRequest`, the trip) and `TransportOrderLinesChanged`/`StopsChanged` |
| `Update*` (header, line add/update/remove, stops, references, terms, trip request, attributes) | Draft/Open | Open: stops of a line whose leg is on an InProgress trip are refused (`Orders:StopLockedByTrip`); publishes the matching `*Changed` event; requests `Orders.TransportOrder` |
| `Cancel` | Draft/Open → Cancelled | refused when a leg is on an InProgress or Completed trip (`Orders:CancelRefusedTripInProgress`); publishes `TransportOrderCancelled` (Planning unassigns and deletes the legs) |
| `Approve` | Open → ReadyForInvoicing or Archived | the approval rules (§Approval); stale client result → `Orders:ApprovalResultsStale`; publishes `TransportOrderStatusChanged` |
| `Revise` | ReadyForInvoicing/Archived → Open | reason required (stored as a `Remark` of kind Revision); refused from Invoiced (Q15) |
| `Archive` | ReadyForInvoicing/Invoiced → Archived | |
| `Delete` | Draft/Open without assigned legs | soft delete of the aggregate and children; publishes `TransportOrderCancelled` so Planning drops legs |

Legs are never posted by the client; Planning derives them (design 59). `KeepLegsAssignedWhenOverridedByDefault` and
`PreventChangingWaypointRelationWhenLegAssigned` of OpenTMS become the one rule above plus Planning's warning.

### Approval (Q6)

`ApprovalRule` enum in Domain.Shared `{ OrderMustBeOpen = 1, ReferencesMustExist, NextInvoicePrice, FreightDocumentsAvailable,
PriceListApplied, PriceCalculationCorrect, DuplicateCustomerReference, EmptyLinePrice, FinancialDefaultsMismatch,
FixedPriceFilled, LegDistanceMatchesLine, AllStopsArrived }`; tenant setting `Orders:ActiveApprovalRules` (csv of enum
names; default the four system rules: first four) ; `ApprovalEvaluator` (Domain service, explicit switch) → results
`{ rule, severity Warning|Error, proposedStatus, messageKey, data }`; `ValidateApproval(ids)` returns them per order;
`Approve(ids, results hash)` re-evaluates and refuses when different. Revenue rules read `IRateLineReader` (stored
lines). The two nightly auto-approval workers are the invoicing run's.

### Templates and recurrence

`TransportOrderTemplate`: `Name` string(128) unique per tenant, `Description`, the order payload (`TransportOrderTypeId`,
customer, terms, lines with stops as **day offsets** from the order date, references, trip request with time-of-day),
`Schedule` owned: `Recurrence { None = 0, Weekly, Monthly }`, `Interval` int (1..52), `DaysOfWeek` flags, `DayOfMonth`
int?, `HorizonDays` int (1..60, default 5), `ValidFrom`, `ValidTill?`, `LastMaterialisedDate` DateOnly?, `IsActive`.
Commands: CRUD; `Materialise(templateId, dates[], copies)` (the wizard; also "copy order": `CreateTemplateFromOrder`
then materialise, or `CopyOrder(orderId, dates[], copies)` directly); the Hangfire recurring job
`TemplateMaterialisationJob` (hourly, per tenant) creates for every active template the orders of the dates in the
horizon that match the schedule and do not exist yet (`SourceTemplateId` + `OrderDate` + copy index unique). Created
orders are Open (confirmed) with their number; failures are logged per template, never block others.

### CMR (Q11; `routing-co2-cmr.md` §3)

- `ConsignmentNoteProfile`: `Name` unique per tenant, `IsDefault` (one), `Copies` 1..5, `ShowBorders`, `ShowQrCode`,
  `GroupByLeg`, box sources `Box1Sender`, `Box2Consignee`, `Box3DeliveryPlace`, `Box4TakingOverPlace`, `Box16Carrier`,
  `Box17SuccessiveCarrier`, `Box21EstablishedBy` as enum `CmrPartySource { Customer = 1, OriginAddress, DestinationAddress,
  OwnCompany, Carrier, Shipper, TripDeparture, TripArrival, Empty }`; child `Box6Columns` (enum `CmrGoodsColumn` set),
  `Box13Items`, `Box18Items` (enum sets `CmrInstructionItem`, `CmrRemarkItem`) and `FixedDescriptionIds` for boxes 6/13.
- `ConsignmentNoteFixedDescription`: `Name` unique, `Text` string(2000), `IsArchived`.
- `ConsignmentNoteDocument`: `Number` (from `Orders.ConsignmentNote`), `OrderId`, `LineIds` (child), `TripId?`,
  `ProfileId`, `ReportDefinitionId`, `PrintedAt`, `PrintedByUserId`, `BlobName`, `ContentType`, `SizeBytes`, `Sha256`,
  `DataSnapshot` jsonb (the catalogue payload rendered); blob in container `consignment-notes` (ABP blob storing,
  provider is configuration, AD-09).
- Command `PrintConsignmentNote { orderLineIds | tripId, profileId?, reportDefinitionId?, overrides }` →
  `ConsignmentNoteDocumentDto`; the payload is composed by `ConsignmentNoteComposer` (Domain service, the box mapping of
  the appendix §3.2 made explicit per `CmrPartySource`; needs Planning's legs/trip through `ILegReader`/`ITransportTripLookup`
  for boxes 16/17/21 and the trip's parties) and rendered by `IDocumentRenderer.RenderAsync(reportDefinitionId, data)`
  with purpose `ConsignmentNote`; one number, one blob, one row per print. `GET /document/{id}/download` streams the
  blob.

### Attribute definitions (Q12)

`OrderAttributeDefinition`: `Target` enum `{ Order = 1, Line }`, `Key` string(32) unique per target, `Name`
`LocalizedName`, `Position`, `IsArchived`; list in the Master data app under Orders → Transport orders.

### Events (Domain.Shared/Events, ids only)

`TransportOrderConfirmedEto { OrderId }`, `TransportOrderLinesChangedEto { OrderId, LineIds[] }`,
`TransportOrderStopsChangedEto { OrderId, LineIds[] }`, `TransportOrderFinancialTermsChangedEto { OrderId }`,
`TransportOrderCancelledEto { OrderId }`, `TransportOrderStatusChangedEto { OrderId, Status }`. Handlers in Orders:
`TransportTripCreated` (sets `TripRequest.TripId`), `LegAssigned/Unassigned`, `TransportTripStatusChanged`,
`TransportTripRecalculated`, `RateLinesChanged{OrderId}` → `RequestAsync("Orders.TransportOrder", id, reason)`.

### Published contract

`IOrderPricingReader.GetSnapshotAsync(orderId)` → `OrderPricingSnapshotDto` (terms, lines with quantities and stop
relation/address/region ids, as-of dates) and `GetSnapshotsAsync(ids)`; `ITransportOrderLookup.GetAsync(ids)` → id,
number, customer name, status.

## API contract

Route root `orders`; permissions and features per design 56.

| Service | Route | Operations |
|---|---|---|
| `TransportOrderAppService` | `/api/orders/transport-order` | `GET` list (`status[]`, `dateFrom/To` on order date, `customerRelationId`, `organizationUnitId`, `view` Today/Tomorrow/ThisWeek/Open/Drafts/All, search over number, reference, customer, stop names/cities), `GET /{id}` (full editor DTO incl. legs via Planning? **no**: the screen loads legs from `/api/planning/leg?orderId=` and rate lines from `/api/financial/rate-line?orderId=`), `POST` (create, `confirm` flag), `PUT /{id}` header, `PUT /{id}/lines` (replace list, each with stops), `PUT /{id}/lines/{lineId}`, `POST /{id}/lines`, `DELETE /{id}/lines/{lineId}`, `PUT /{id}/default-stops`, `PUT /{id}/references`, `PUT /{id}/financial-terms`, `PUT /{id}/trip-request`, `PUT /{id}/attributes`, `POST /{id}/confirm`, `/cancel`, `/approve` `{ results hash }`, `/revise` `{ reason }`, `/archive`, `DELETE /{id}`, `POST /bulk-delete`, `POST /approval/validate` `{ ids }`, `POST /approval/approve` `{ ids, hash }`, `POST /{id}/preview-prices` (calls `IPricePreviewService` with the posted draft state), `GET /{id}/summary` (the stored derived values + state) |
| `TransportOrderTemplateAppService` | `/api/orders/transport-order-template` | CRUD, `POST /{id}/materialise` `{ dates[], copies }` → created order ids, `POST /from-order/{orderId}`, `POST /copy-order/{orderId}` `{ dates[], copies }` |
| `ConsignmentNoteAppService` | `/api/orders/consignment-note` | `POST /print` → document, `GET /document?orderId=` list, `GET /document/{id}`, `GET /document/{id}/download` |
| `ConsignmentNoteProfileAppService`, `ConsignmentNoteFixedDescriptionAppService`, `OrderAttributeDefinitionAppService` | `/api/orders/consignment-note-profile`, `/consignment-note-fixed-description`, `/order-attribute-definition` | design-50 shape (+ `set-default` on profiles) |

Errors: `Orders:StopLockedByTrip`, `Orders:CancelRefusedTripInProgress`, `Orders:ApprovalResultsStale`,
`Orders:RequiredFieldMissing {field}`, `Orders:LineNeedsTwoStops`, `Orders:OrderDatePeriodLocked`,
`Orders:ConsignmentNoteTemplateMissing`, `Orders:InvalidTransition {from,to}`, `Orders:TemplateInactive`, plus
Platform `ReferenceData.*`.

## Migration strategy

Expand only, migration `OrdersTransportOrders`: `orders."TransportOrders"`, `"TransportOrderLines"`,
`"TransportOrderLineMeasurements"`, `"TransportOrderLineCharacteristics"`, `"TransportOrderStops"`,
`"TransportOrderDefaultStops"`, `"TransportOrderReferences"`, `"TransportOrderPriceLists"`,
`"TransportOrderDiscountsOrSurcharges"`, `"TransportOrderTemplates"` (+ the same children with `Template` prefix or a
`TemplateId` discriminator: **separate tables** with `TemplateId`), `"ConsignmentNoteProfiles"` (+ children),
`"ConsignmentNoteFixedDescriptions"`, `"ConsignmentNoteDocuments"` (+ `LineIds` child),
`"OrderAttributeDefinitions"`. Indexes: `(TenantId, Number)` unique, `(TenantId, Status, OrderDate)`,
`(TenantId, CustomerRelationId)`, `(TenantId, SourceTemplateId, OrderDate)` unique with copy index, trigram on
`Number`/`CustomerReference`/`FirstStopName`/`LastStopName`, `(TenantId, OrderId)` on documents. Seed: profile `Standaard`
(default, 4 copies, borders, QR, OpenTMS defaults per box). Number sequence key `Orders.ConsignmentNote` registered.

## UI design

Design 61 (the Transport app and the Master data lists).

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Tenant isolation: orders, lines, stops, templates, documents, readers | **Critical** | PostgreSQL negative tests |
| Status machine: every transition and every refusal (`orders.md` 7–9, 29–47) | **Critical** | SQLite service tests |
| Numbering on Confirm (gapless, period lock) | **Critical** | PostgreSQL |
| Events published with the fact, idempotent handlers | **Critical** | PostgreSQL outbox tests |
| Required fields per type; two stops per line; address explicit; terms copied and validated | **High** | SQLite |
| Templates: materialisation idempotent, weekly/monthly/horizon/validity (`orders.md` 48–53) | **High** | SQLite + PostgreSQL job |
| Tevens trip request stored and handed to Planning once (54–61) | **High** | SQLite + an integration test with Planning |
| CMR: composer per box source, number once, blob stored, download authorised | **High** | SQLite with a fake renderer; PostgreSQL for the number |
| Approval rules per rule; stale hash | **High** | SQLite |
| Grid filters, views and search | **High** | PostgreSQL volume (500k orders) |
| Permissions, features | **Critical** | architecture + service tests |

## Flag & rollout plan

Features of design 56, default on. Rollback: features off.

## Cost & SLO impact

See design 56; order save p95 < 500 ms (the aggregate, ≤ 5 outbox rows, one derived-value upsert); CMR print ≈ 1–3 s
(renderer), synchronous because the user waits for the PDF (as OpenTMS), with a 30 s timeout → `Orders:RenderTimedOut`.

## Assumptions

1. The order holds its own stop pattern (`DefaultStops`) seeded from the type; a changed pattern applies to new lines only.
2. Time windows: either a time slot or an explicit window per stop; both absent means the whole day.
3. Approval's default active rules are the four OpenTMS system rules; tenants add the others in TMS settings (later
   screen; this run: the setting exists).
4. The CMR report data shape is the catalogue entry of design 56 (Platform changes 1); templates are the tenant's.
5. "Copy order" materialises through a transient template object (no stored template), so the one code path serves both.
6. A Draft can be saved incomplete; Confirm validates. Boundary agents (later) create Drafts through `Create` with
   `Source = Agent`.

**Build notes (implementation of 2026-10-25, branch `feat/orders-transport-orders`).** Decisions the design left open and
deviations, each small and reversible:

7. **Financial terms come with the request.** Design 57 publishes no reader of `RelationFinancialSettings`, so Orders
   cannot copy the customer's defaults itself: `Create` and `PUT /{id}/financial-terms` take the terms as posted (the
   screen reads `/api/financial/relation-financial-settings/by-relation/{id}` and asks the clerk on a customer change);
   without terms an order starts with EUR, no lists. The approval rule `FinancialDefaultsMismatch` passes for the same
   reason until Financial publishes the defaults. The ids posted are validated through `IFinancialReferenceLookup`.
8. **Master Data additions (the one exception to "only your module").** `IMasterDataReferenceLookup` gained
   `GetRelationAddressesAsync(ids)` (address, coordinates, zone, region ids via the postal-code matcher plus the relation's
   own regions), `GetRelationDefaultAddressesAsync(relationIds)` (the relation list's address rule, for the CMR parties),
   `GetReferencesAsync(kind, ids)` (names of transport units, products, product codes, temperatures, vehicle types,
   characteristics, vehicles, combinations, drivers, contact persons) and `GetOwnCompanyAsync()`. The customer's *role* is
   not checked (the lookup carries no roles); the trip type id of a trip request is Planning's to check.
9. **Shapes.** `FreightDetails`, `StopDetails` and `TransportOrderFinancialTerms` are owned types shared by the order, its
   lines and stops, the pattern and the template; the order's price lists and discounts/surcharges are child rows of the
   order (`TransportOrderPriceLists`, `TransportOrderDiscountsOrSurcharges`), not of the owned terms. The trip request's
   vehicle and driver ids, a profile's box sets and fixed-description ids, and a template line's measurements and
   characteristics are jsonb (AD-06); the order line keeps its measurement and characteristic tables. The template's
   stops live in two tables (`TransportOrderTemplateStops` per line, `TransportOrderTemplateDefaultStops` the pattern).
   The order stores `CustomerName` (the list's search and sort column; refreshed on every save of the customer) and a
   `Remarks` child for the revision reasons. `(TenantId, Number)` is unique with the filter `Number IS NOT NULL`,
   `(TenantId, SourceTemplateId, OrderDate, CopyIndex)` with `SourceTemplateId IS NOT NULL` (a plain `NULLS NOT DISTINCT`
   would make two Drafts or two orders of one date collide); the latter is named `IX_TransportOrders_TenantId_Template_Date_Copy`
   because the conventional name exceeds PostgreSQL's 63 characters, as do seven FK names, which carry short explicit names.
10. **Routes.** ABP's conventional routing gives `PUT /{id}/line?lineId=` and `DELETE /{id}/line?lineId=` for the single-line
    commands, `POST /validate-approval` and `/approve-many` for the approval of several orders, `GET /{id}/download` and
    `GET /document-list` on the consignment note, `POST /{id}/materialise`, `/from-order/{orderId}` → `POST /create-from-order?orderId=`
    and `POST /copy-order?orderId=`; design 61's client follows the generated contract.
11. **CMR.** A print makes **one document per order** among the selected lines (a trip print therefore returns a list); the
    `GroupByLeg` flag travels to the template. The profile holds an optional `ReportDefinitionId`; without one the tenant's
    default of purpose `ConsignmentNote` in the UI culture (else any culture) renders. Box 1's customer and box 21's own
    company print the relation's default address; stops print their explicit address. The 30 s render budget is a
    constant (`ConsignmentNoteConsts.RenderTimeoutSeconds`); `Orders:RenderTimedOut` travels as 409. Numbers are
    `CMR-<year>-<6 digits>` from `Orders.ConsignmentNote`.
12. **Approval.** `NextInvoicePrice` proposes Archived below € 0,50 (or when never calculated); `PriceCalculationCorrect`
    fails while the rate lines have no calculation version; `FixedPriceFilled` fails on a fixed price of 0;
    `ReferencesMustExist` checks the customer and the stop relations through the lookup; `AllStopsArrived` reads the legs'
    destination actuals. The approval hash is SHA-256 over the ordered results (`ApprovalEvaluator.Hash`).
13. **Attributes.** Keys must be defined (`OrderAttributeDefinition` of the target; archived definitions keep their stored
    values readable); the server refuses an undefined key with `Orders:InvalidAttributes {key}`.
14. **Templates.** The hourly job is an ABP periodic background worker registered by the Orders application module behind
    `Orders:TemplateMaterialisation:Enabled` (default true, period `Orders:TemplateMaterialisation:Period`, default 1 h); it
    visits every tenant and runs each template in its own transaction. Copies of "copy order" are Open orders with
    `Source = Screen` and no template id; materialised orders carry `Source = Template`.
15. **Platform.** `ReportPurpose.ConsignmentNote = 6` needed its localization texts (`Platform:ReportPurpose.ConsignmentNote`)
    for the Platform parity test; the catalogue fragment has 232 fields. The frontend's purpose table
    (`frontend/src/domains/platform/master-data.ts`) is design 61's to extend.
16. **Tests.** Financial's and Planning's published contracts have no implementation in this branch: `backend/test/
    OpenTms.EntityFrameworkCore.Tests/Fakes/OrdersFakes.cs` holds `Fake*` stand-ins (deleted at integration). SQLite runs
    without transactions, so the SQLite test module swaps the number-sequence generator and the derived-value scheduler for
    lenient/recording doubles; both rules are covered on PostgreSQL. The render-timeout path has no automated test (the budget is a constant).
17. **Found in Platform, fixed at integration (2026-10-25).** `DerivedValuesRequestedHandler` opened no unit of work; dispatched
    from the outbox its state read threw "A DbContext can only be created inside a unit of work" and the event blocked the
    tenant's outbox. The `OutboxDispatcher` now runs every event's handlers inside a transactional unit of work of its own
    (committed before the row is deleted; at least once holds), the handler carries `[UnitOfWork]` too, and
    `DerivedValuesPostgreSqlTests` dispatches a request through the real path and asserts the job enqueued and the row gone.
    Orders' handlers join that unit of work (`[UnitOfWork]`) instead of opening their own.
18. **2026-10-25, integration.** Build note 7 is closed: `Create` and a customer change on the header copy the customer's
    defaults from Financial's `GetRelationFinancialSettingsAsync` when the request carries no terms (`TransportOrderUpdateDto.FinancialTerms`
    is new and optional); posted terms are kept; `FinancialDefaultsMismatch` warns with the fields that differ. The internal
    reference DTO is `TransportOrderReferenceEntryDto` (it clashed with the published `TransportOrderReferenceDto`).
    `ITransportOrderLookup.GetWaypointTypesAsync(ids)` publishes activity, successor and default stay for Planning's via-depot
    departure. The fakes of build note 16 are deleted; the tests run on Planning's and Financial's real rows
    (`TransportRunFixture`), and `TransportRunChainTests` drives the whole chain through the real dispatcher and jobs.
19. **2026-10-26, follow-ups.** *Export* (UI-GRID-013): `POST /api/orders/transport-order/export` takes the list's own request
    plus `columns[] { field, header? }` (the grid's column ids in display order; empty = every exportable column) and
    `allRows` (every order of the tenant, ignoring filters, view and search; the sort applies) and answers an xlsx file. The
    client sends the headers it shows; a missing one is localized on the server in the UI culture. Read in pages of 1,000
    with one batch of lookups each, written forward-only into a self-deleting temporary file; above the tenant setting
    `Orders:Export:MaxRows` (default 50,000) `Orders:ExportTooManyRows`, an unknown column `Orders:ExportUnknownColumn`
    (both 400). The organisation unit is exported as its id (Platform publishes no name lookup). *Wizard details*:
    `materialise` and `copy-order` take `overrides[] { date, copyIndex, customerReference?, lines[] { lineId, quantity?,
    weightKg? } }` per generated order; the line ids are the template's (its DTO's lines now carry `id`; a template update
    replaces them) or the copied order's; unknown lines `Orders:MaterialisationLineUnknown`, a date or copy not being created,
    a duplicate or a negative value `Orders:MaterialisationOverrideInvalid` (400), checked before anything is created. *Trip
    request warning*: Planning's `TransportTripRequestNotHonouredEto` (design 59 build note 14) sets
    `TripRequest.WarningCode` (`OpenTms.Orders:TripRequestNotHonoured`, a warning code in `OrdersWarningCodes`, not an error code), `WarningField` and `WarningAt` (migration
    `OrdersTripRequestWarning`); the row DTO carries `tripRequestWarningCode`; recording a trip clears it, request edits keep
    it (the request is honoured once, on Confirm). *Approval*: `FinancialDefaultsMismatch` stays out of the default active
    rules — it is a user validator in OpenTMS (`orders.md` §5) and Q6 / Assumption 3 make the four system rules the
    default; a tenant switches it on in `Orders:ActiveApprovalRules`.

## Security quickscan

Permissions per design 56; input bounded (≤ 200 lines, ≤ 50 stops per line, ≤ 50 references, texts ≤ 2,500); ids
validated through lookups; documents downloadable only with `Orders.TransportOrders` and the tenant filter; blob names are
GUIDs; no personal data in logs or events; the CMR snapshot holds party names and addresses (business data, retained
with the document).
