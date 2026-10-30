# Transport rebuild: assessment of the current OpenTMS implementation and PO decisions

Date: 2026-10-25. Scope: the **Transport app** of opentms-next in one run — transport orders, transport trips, the
price and cost calculation (OpenTMS `QueryFreightPrices`), CO₂, CMR printing, order templates and recurrence.
Purpose: input for the design and build of that run, so that every functional scenario of the traditional OpenTMS is
carried over on the new architecture and no known bad decision is cloned. Companion document:
[pricing-rebuild-assessment.md](pricing-rebuild-assessment.md) (price lists, rate lines, cost allocation).

Detail per area, with `path:line` citations into the OpenTMS checkout (`~/dev/OpenTMS`, branch `Development`, commit
`5f547cc6b`, 2026-10-20), lives in [transport-assessment/](transport-assessment/): [orders.md](transport-assessment/orders.md)
(72 scenarios), [trips.md](transport-assessment/trips.md) (62), [pricing.md](transport-assessment/pricing.md) (80),
[screens.md](transport-assessment/screens.md) (56), [routing-co2-cmr.md](transport-assessment/routing-co2-cmr.md) (34).
**A cloud session cannot read `~/dev/OpenTMS`** (Azure DevOps remote, local disk only); these files are the only copy it
gets. Eight key claims were spot-checked against the source on 2026-10-25 (the disabled CO₂ job, the write-only distance
cache, trip id = order id, VROOM as default engine, the random ETA forecast, recurrence by reflection, highest-wins
pruning, the unauthorised `Reset`): all correct.

Section 2 records the PO decisions already taken; section 3 the decisions still needed, each with a recommendation;
sections 4–6 the findings and the requirements that follow. Items marked **KEEP** are deliberate and go into the design
doc as such; **CHANGE** items are requirements for the rebuild.

## 1. What the run covers

| In this run (PO 2026-10-25) | Out of this run (PO 2026-10-25) |
|---|---|
| Transport orders: header, lines (OpenTMS *freights*), stops (*waypoints*), legs, references, lifecycle | Importers of any kind (later via platform-level AI agents, `06-agents-and-ai.md`) |
| Transport trips (OpenTMS *transport files*): header, stops, leg assignment, vehicles, drivers, lifecycle | Fleet-management-system (FMS) sending, redraw, FMS-sourced trips and redistribution |
| Price and cost calculation: sales rate lines on orders, purchase rate lines on trips, trip cost, allocation | ETA e-mails, proforma wizard, incoming (carrier) invoices as documents, SSP (self-service portal) approval |
| CO₂ per leg, trip and order | Invoicing itself (the Financial invoicing run reads what this run stores) |
| CMR print (profiles, fixed descriptions, PDF) | BigMile publication, TrackOnline, distribution portal, questionnaires |
| Order templates and recurrence | Planboard (own app): trip creation and vehicle/carrier assignment live there, except the order option **"Tevens trip"** |
| Routing adapter for distances, durations and ETA | Route optimisation UI (planboard), FMS positions (ingest, `07-ingest.md`) |

## 2. PO decisions already taken

| Date | Decision | Consequence for the design |
|---|---|---|
| 2026-10-22 | OpenTMS *TransportFile* is **TransportTrip**, *TransportFileType* is **TransportTripType** (glossary) | Names in code, routes, features and manual |
| 2026-10-25 | Trip creation and assignment to vehicle and carrier belong to the **Planboard**, except the order option **"Tevens trip"**: the order can ask for a trip to be created with it. Inspiration: the OpenTMS order tab *Vrachtdossier* (`AssignTransportFile`), but "easier and clearer" | §4.7 and §6.2; the Transport app creates a trip only through that option, and shows trips read-only plus the trip editor for non-planning fields |
| 2026-10-25 | Distances for price per km and ETA: **the engine OpenTMS uses today** | The finding (§4.10): production uses **VROOM + OSRM**, not Valhalla; Valhalla sits behind a Cloud Function for geometry, toll and CO₂. See Q4 |
| 2026-10-25 | Importers out of scope | No import profiles, no `TransportOrderImportFile`, no OTM5 intake in this run |
| 2026-10-25 | Of the supporting topics only **CO₂, CMR print, templates and recurrence** are in; FMS, ETA mail, proforma, incoming invoices, SSP approval are out | §4.12, §4.13, §4.6 |
| 2026-10-25 | **Feature flags default ON** for this run (explicit deviation from constitution rule 4) | Every `Apps.Transport.*` feature and the Financial pricing features default on; the PR description says so |
| 2026-10-16 | CMR fixed texts, CMR print profile and transport profiles belong to the **transport domain**, not Platform | §4.13: CMR profile and fixed descriptions are Orders entities |
| 2026-10-08/09 | AD-07 (derived values stored, calculated in the background, pushed), AD-08 (one rate-line model), AD-12 (routing engine is a separate service), AD-06 (jsonb for document-shaped parts), AD-15 (facts from outside go through ingest) | §4.9–4.12 and the pricing document |

## 3. Decisions needed before the design (PO)

A design that depends on an OPEN item must stop (`docs/architecture/README.md`). The build session can only be
first-time-right when these are answered first. Each has a recommendation; "agree with all recommendations" is a valid
answer.

| # | Question | Recommendation |
|---|---|---|
| **Q1** | **AO-11**: where do allocated values (cost per leg, revenue per leg, CO₂ per leg) live across module boundaries? | Planning & Execution stores them **on its legs** as derived values (AD-07 columns with provenance), written by a Planning calculator that reads Financial's published rate-line contract. Financial owns the rate lines and the trip-cost total; Planning owns the allocation result the grids read. No read model. Flip AO-11 to decided in `decisions.md` in the design PR. |
| **Q2** | Which module owns **legs**? `04-data-and-tenancy.md` says Planning & Execution; OpenTMS keeps them under the order (`TransportOrderFreightLeg`). | **Planning & Execution** owns legs, trips, trip stops. Orders owns the order, its lines and its requested stops. Orders publishes `TransportOrderPlaced/Changed/Cancelled`; Planning (re)generates the legs idempotently from the order's stops (§4.3). The Transport app shows an order's legs from Planning. |
| **Q3** | **Price-list model**: no decision exists (design 52 assumption 6). | The model in the pricing document §3: one `PriceList` with typed dimension rules and bracket rows, decimal money, validity, currency, explicit specificity cascade, a simulation endpoint. |
| **Q4** | **Routing engine** for the new system. OpenTMS: VROOM (`mvrp.services.opentms.nl`) for optimisation and ETA, OSRM (`osrm.services.opentms.nl`) for every stored distance, the Valhalla Cloud Function (`micro-services-valhalla`) for geometry, toll and GLEC CO₂. | One `IRoutingService` in Integrations (AD-12) with **OSRM** as the first provider for distance and duration (what the stored numbers come from today), the **Valhalla function** as the toll and CO₂ provider, VROOM only when the planboard run needs optimisation. Hosts from configuration, never literals. |
| **Q5** | **Order states**. OpenTMS: a `[Flags]` enum with `PendingAcceptance = -1`, `Invoiced` OR-ed onto other states, cancel decided by the client. | Enum `TransportOrderStatus { Draft, Open, ReadyForInvoicing, Invoiced, Archived, Cancelled }` with server-side transitions (§4.2). *Draft* replaces *PendingAcceptance* and is the "unconfirmed order" the boundary agents need (`06-agents-and-ai.md`). Execution progress (Unplanned, Planned, InProgress, Delivered) is a **separate stored derived value**, not a state. |
| **Q6** | **Approval for invoicing** ("Goedkeuren voor facturatie", 12 validators, two nightly workers): this run or the invoicing run? | The **transition** Open → ReadyForInvoicing with its command and the validator framework belong to this run (the state exists); the 12 validators are ported as typed rules selectable per tenant (no class names), the two auto-approval workers come with the invoicing run. |
| **Q7** | **Trip states**. OpenTMS has independent booleans (`IsCanceled`, `IsArchived`) and infers execution from actual times. | Enum `TransportTripStatus { Planned, InProgress, Completed, Cancelled }` plus `IsArchived`; InProgress and Completed are set by the actual departure and arrival commands. A cancelled or archived trip refuses leg assignment. |
| **Q8** | **Vehicles on a trip**: three fixed slots (tractor, trailer, trailer) or the design-48 `VehicleCombination`? | A trip references **one `VehicleCombinationId`** or, when no standing combination exists, an ad-hoc list of vehicle ids (child table with position). The OpenTMS slots map onto that list. Drivers: child table with position. |
| **Q9** | **"Tevens trip" semantics** when the order is edited later. OpenTMS overwrites the trip's carrier, vehicles and drivers from the order on every save and gives the trip the order's primary key. | The option is a one-time **instruction at order creation** (trip type, departure/arrival depot, planned departure, optional carrier/vehicle/drivers). After creation the trip is Planning's: later order edits do **not** overwrite it; the screen links to the trip. Own trip id; link by `CreatedFromOrderId`. |
| **Q10** | **CO₂ method**: OpenTMS stores *CPI × amount × great-circle distance* per freight and never fills the leg values. | GLEC v3 / ISO 14083 WTW through the Valhalla function (already implemented there) as the first `ICarbonFootprintCalculator`, stored per leg, trip and order line as AD-07 derived values with provenance (factor set, distance source, vehicle spec). CPI fallback only when the function is unavailable. |
| **Q11** | **CMR as a document**: OpenTMS renders per freight on the fly, no number, nothing archived unless signed. | A printed CMR becomes a **stored document** (AD-09 reference, a CMR number from a Platform number sequence per tenant, immutable PDF) rendered through Platform's `IDocumentRenderer`. That renderer is design 55 (report rendering), merged from `feat/platform-master-data` on 2026-10-25, see §7. |
| **Q12** | **Custom properties** (`CustomProperty0..9` on order, line and trip, labels per tenant). | One `Attributes` jsonb per aggregate (AD-06) with a tenant-level attribute definition list (key, label nl/en, type) in Orders / Planning. Not ten string columns. |
| **Q13** | **Flags, highlight colours, organisation units** on orders and trips. | Organisation unit: keep as a plain `OrganizationUnitId` (Platform id). Flags and personal highlight colours: assessment §3.12 put them outside the domain base; park them (not in this run) unless the PO wants them now. |
| **Q14** | **Grid permissions**: OpenTMS has permissions per *menu view* (Today, This week, Open, All, Templates), none per action; the whole app service carries one `[AbpAuthorize]`. | Permissions per **action** (`Orders.TransportOrders`, `.Create`, `.Update`, `.Delete`, `.Cancel`, `.Approve`, `.Revise`, `.Print`, `.ManageTemplates`; `PlanningExecution.TransportTrips`, `.Create`, `.Update`, `.Delete`, `.Cancel`, `.Archive`, `.Complete`, `.AssignLegs`; `Financial.RateLines.*`, `Financial.PriceLists.*`). Views are saved grid filters, not permissions. |
| **Q15** | **Revise** of an invoiced order back to Open (OpenTMS allows it). | Refuse while Invoiced; a correction runs through a credit note in the invoicing run. Revise allowed from ReadyForInvoicing and Archived. |
| **Q16** | Design numbering: main has designs 49–53; `feat/platform-master-data` had its own 49 and 50 (Platform master data, report rendering). | Done 2026-10-25: merged via its own PR, renumbered to 54 and 55. The transport designs start at 56. |

## 4. Findings and requirements

Paths are relative to `aspnet-core/src/` of the OpenTMS checkout unless stated; the appendices hold the full citations.

### 4.1 Order aggregate: KEEP the shape, CHANGE the mechanics

OpenTMS (`orders.md` §1): `TransportOrder` (207 lines of properties) → `TransportOrderFreight` (the order line: unit,
product, quantities, dimensions, texts, container fields) → `TransportOrderFreightWaypoint` (≥ 2 per line) and
`TransportOrderFreightLeg` (origin/destination **copies** of waypoint data, `TransportFileId` = the trip); plus prices,
additional services, references, order-level "freight stops", capabilities (FK column misnamed), a dead D&S join table,
and a denormalised `TransportOrderAttributes` row kept in sync by handler code, a SQL trigger **and** a resync in
redistribution. No FKs, no unique index on `OrderNumber`, no concurrency token; money and quantities `double?`,
durations strings, lists pipe-joined GUID strings.

Requirements:

- **Orders aggregate** `TransportOrder` with children `TransportOrderLine` (one row per OpenTMS freight; quantities with
  the unit in the name: `Quantity`, `Positions`, `Colli`, `WeightKg`, `VolumeM3`, `LoadingMeters`, `LoadFactor`, `Epq`,
  `LengthCm`, `WidthCm`, `HeightCm`; measured weights/volumes as a child table `LineMeasurement`, not six columns),
  `TransportOrderStop` (the requested places: `WaypointTypeId`, `RelationId` **and `RelationAddressId`** — assessment
  §3.4 requires the address to be explicit — `Date`, `TimeSlotId`, `TimeWindowFrom/Till` stored as instants with the
  address's IANA zone, `Reference`, `Remarks`, position), `TransportOrderReference` (typed), plus `Attributes` jsonb (Q12).
  Container fields (`ContainerNumber`, `SizeOrIso`, `Pincode`, `ShippingCompany`, `ShipperId`) stay on the line.
- Stops belong to the **line** in OpenTMS and are copied per line in the UI (`screens.md` §2). Keep that: a line has its
  own ordered stops; the order screen's "waypoint strip" is the default stop set for new lines (§6.2).
- Real FKs inside Orders; unique `(TenantId, OrderNumber)` `NULLS NOT DISTINCT`; `ConcurrencyStamp` on the aggregate;
  server-generated ids; separate Create/Update DTOs (assessment §3.14).
- Grid read model: **no attributes row**. The list reads stored columns on the order (counts, first/last stop names,
  totals) that the aggregate maintains on save, and the derived values (§4.11) that the calculators write. No trigger.
- Financial defaults copied from the relation at creation (currency, VAT category, payment term, price lists, D&S) are
  **ids with the validity row** (assessment §3.4), stored on the order as Orders' own `OrderFinancialTerms` (ids only,
  validated through Financial's and Master Data's published lookups).
- Cross-module ids (relation, address, product, unit, vehicle type, temperature, VAT, D&S, price list) are ids without
  database constraints, validated through `IMasterDataReferenceLookup` (design 51) and a new `IFinancialReferenceLookup`.

### 4.2 Order lifecycle: CHANGE to a server-side state machine (Q5, Q6, Q15)

OpenTMS (`orders.md` §2): `[Flags] { PendingAcceptance = -1, Open = 0, Canceled = 1, ReadyForInvoicing = 2, Invoiced = 4,
Archived = 8 }`; invoicing OR-s the `Invoiced` bit in; **cancel has no server rule** (the client posts the state);
`Revise` resets anything back to Open, even Invoiced; delete allowed only Open/Pending without assigned legs and leaves
references, stops and callbacks orphaned; the order number is drawn by read-max-then-insert with an instance-field hack.

Requirements:

- `TransportOrderStatus` enum (Q5) with commands `Confirm` (Draft → Open, draws the number), `Cancel` (Open → Cancelled;
  refused while a leg is on a trip that is InProgress; unassigns planned legs through an event), `Approve`
  (Open → ReadyForInvoicing or Archived after the validators, Q6), `Revise` (ReadyForInvoicing/Archived → Open, reason
  required, stored as a remark), `Archive`. `Invoiced` is written by the invoicing run only.
- Numbers from the Platform `INumberSequenceGenerator` (design 50), key `Orders.TransportOrderType:<id>`, drawn on
  Confirm; a Draft has none. Changing `OrderDate` across a year or month boundary of the format is refused (as OpenTMS).
- Delete: only Draft and Open without assigned legs; cascades to every child. Soft delete.
- A template is **not** an order with `IsTemplate`: see §4.6.

### 4.3 From stops to legs: CHANGE the generator, KEEP the via-depot rule (Q2)

OpenTMS (`orders.md` §4): one leg per consecutive waypoint pair, or split through default via waypoints resolved in order
relation → customer → geographical region, with `WaypointType.SuccessiveWaypointTypeId` deciding the depot's activity.
Known bugs: every generated leg gets `OrderNumber = 1`, the via split attaches to the global last leg, a self-comparison.
The client regenerates legs on every waypoint change and posts them; the server diffs by id and unassigns legs that
changed while on a trip (optionally re-assigning them per tenant setting).

Requirements:

- Planning & Execution owns `Leg` (`OrderId`, `OrderLineId`, `OriginStopId`, `DestinationStopId` as ids; origin and
  destination **snapshots** of relation, address, activity, planned date and window; `Position`; `DistanceKm`,
  `Duration` as `TimeSpan`/interval; `TripId` nullable; stored derived values §4.11). A leg is the plannable unit.
- Legs are generated by a Planning domain service from the order's stops when Orders publishes
  `TransportOrderStopsChanged` (ids only, outbox). Idempotent: legs are matched on `(OrderLineId, OriginStopId,
  DestinationStopId)`; an unchanged leg keeps its id and its trip; a changed leg on a trip is **unassigned and the
  planner is told** (a record-update push plus a trip warning), never silently re-assigned. The tenant setting
  `KeepLegsAssignedWhenOverridedByDefault` is dropped (Q2).
- Via depots: `DefaultViaWaypoint` configuration per relation (Master Data id), per customer, per region, in that order
  (as OpenTMS), with the successive-activity rule from design 51. Fixed in the rebuild: each via splits the correct pair.
- Distances per leg from the routing service (§4.10), requested as a derived value, never inline in the save.
- The order screen shows and lets the clerk **adjust** legs (dates, via depot, split) through Planning commands; the
  edits survive a regeneration because regeneration matches on stop ids.

### 4.4 Save pipeline: CHANGE from "trust the client" to commands

OpenTMS (`orders.md` §3, `pricing.md` §6, `screens.md` §2): the Angular screen computes prices (`QueryFreightPrices`
on every change), regenerates legs, decides D&S applicability (weekday, region, limit-to-one) and the cancel state, then
posts the whole `TransportOrderEditDto`; the server validates chains and fixed-price exclusivity and persists what it
got. Manual-price flags are set by the client.

Requirements: the application service exposes **commands** (create, update header, add/update/remove line, set stops,
set financial terms, set "Tevens trip", confirm, cancel, approve, revise) and the server derives everything else:
legs (§4.3), rate lines (pricing document §4), D&S applicability, totals. The screen never posts calculated values.
Price *preview* for an unsaved change is the one synchronous Financial query the design states explicitly (pricing
document §4.3).

### 4.5 Approval for invoicing: KEEP the rules, CHANGE their wiring (Q6)

OpenTMS (`orders.md` §5): 4 system + 8 user validators selected per tenant **by class name** in a setting; each proposes a
target state; conflicting proposals or errors keep Open; approval changes only the state. Two nightly workers approve
warning-free orders.

Requirements: `ApprovalRule` enum in Orders `Domain.Shared` (OrderMustBeOpen, ReferencesMustExist, NextInvoicePrice,
FreightDocumentsAvailable, PriceListApplied, PriceCalculationCorrect, DuplicateOrderReference, EmptyLinePrice,
FinancialDefaultsMismatch, FixedPriceFilled, LegDistanceMatchesLine, AllStopsArrived), a tenant setting holding the
active set, one domain service with an explicit switch (assessment §3.10), results typed (rule, severity, proposed
status, message key). The approve command re-validates and refuses when the client's view is stale (as OpenTMS).
Revenue-dependent rules read stored rate lines (AD-07), never recalculate.

### 4.6 Templates and recurrence: CHANGE

OpenTMS (`orders.md` §6): a template is an order with `IsTemplate` (no number, legs not persisted, D&S ignored); the
generate wizard deep-copies with date shifting; the periodic worker evaluates only `RecurrenceOn<Weekday>` by
**reflection**, 5 days ahead; `RecurrenceType` Monthly, `Interval` and `OnDayOfTheMonth` are **dead**.

Requirements: `TransportOrderTemplate` as its own Orders aggregate (name, the order payload as the same child shapes,
no number, no state), `RecurrenceSchedule` typed (`None | Weekly { Interval, DaysOfWeek flags } | Monthly { Interval,
DayOfMonth }`, horizon in days, `ValidFrom/Till`), a Hangfire job per tenant that materialises orders idempotently on
`(TemplateId, OrderDate)`, date shifting by day offsets (as OpenTMS), copies as Open orders with `SourceTemplateId`.
"New from template" and "copy order" are one wizard (§6.3). Monthly recurrence works.

### 4.7 "Tevens trip": CHANGE (Q9)

OpenTMS (`orders.md` §7, `screens.md` §2 tab 6, §6.1): fifteen `TemplateTransportFile*` columns on the order; the trip gets
the **same primary key as the order**; carrier, vehicles and drivers are overwritten from the order on every save; an
outsourcing incoming invoice with buying price ± one D&S is created; manual order stops are mirrored into trip stops; the
tab's copy was written for templates and shows on every order; one toggle silently nulls fifteen fields.

Requirements: an optional `TripRequest` value object on the order (`TransportTripTypeId`, `DepartureRelationId`,
`ArrivalRelationId`, `PlannedDeparture`, optional `CarrierRelationId`, `VehicleCombinationId` or vehicle ids, driver ids,
optional `AgreedPurchasePrice` + currency) saved with the order. On Confirm, Orders publishes `TransportOrderConfirmed`;
Planning reacts: creates the trip (own id, `CreatedFromOrderId`), assigns all legs, requests ETA; Financial reacts to
`TransportTripCreated` with the agreed purchase price as a cost rate line on the trip. Later order edits leave the trip
alone (Q9). The screen (§6.2) is one compact section with a switch, four fields and an "advanced" disclosure.

### 4.8 Trip aggregate: KEEP the fields that are facts, CHANGE the structure (Q7, Q8)

OpenTMS (`trips.md` §1–2): `TransportFile` with three fixed vehicle columns, a driver child table without order, stops
whose link to legs is a **`;`-delimited GUID string** searched with `Contains`, a parallel attributes cache maintained by
stored procedures and triggers, no state machine, number generation by string sort, `FileNumber` unique only per
`(Tenant, IsDeleted)` so delete renames the number, route and stop modes read from the **type and tenant setting**
rather than the trip.

Requirements:

- `TransportTrip` (`TransportTripTypeId`, `Number`, `TripDate`, `Name`, `OrganizationUnitId`, `Execution` enum from the
  type (OwnFleet/Subcontracted), `CarrierRelationId`, `CarrierContactPersonId`, departure/arrival relation + address ids,
  `PlannedDeparture/Arrival`, `ActualDeparture/Arrival` as instants, `PlannedDistanceKm`, `ActualDistanceKm`,
  `Status` (Q7), `IsArchived`, `Remarks`, `DriverInstructions`, `Attributes` jsonb, stored derived values §4.11),
  children `TripVehicle` (position, `VehicleId`) or `VehicleCombinationId` (Q8), `TripDriver` (position, `DriverId` —
  the design-43 Driver, not a relation), `TripStop` (position, `WaypointTypeId`, `RelationId`, `RelationAddressId`,
  planned/estimated/actual arrival and departure, cumulative distances, `IsManual`, remarks) and **`TripStopLeg`**
  (join table stop ↔ leg with `Role` Origin/Destination, unique per leg and role).
- Numbers from the Platform generator, key `PlanningExecution.TransportTripType:<id>` (design 53); unique
  `(TenantId, Number)`; soft delete keeps the number (no rename).
- Stop-creation mode and route mode are **trip fields** defaulted from the type, as design 53 already decided for
  `DefaultExecution`.
- Commands: create (from the Planboard or §4.7), update header, set carrier, set vehicles, set drivers, assign/unassign
  legs, add/move/remove manual stop, set stop actuals, start (actual departure), complete, cancel, archive, delete
  (unassigns legs). Cancelled/archived trips refuse assignment (Q7). The Transport app offers everything except create,
  assign and vehicle/carrier assignment (Planboard).

### 4.9 Leg assignment and stop generation: KEEP the two modes, CHANGE the execution

OpenTMS (`trips.md` §3–4): assignment validates existence only; stops are regenerated by two calculators chosen from the
type's `DefaultStopCreationMode` (merge by location + activity, or one stop pair per leg for shuttles); manual stops
survive; re-sequencing nulls every calculated time; post-processing (first/last-stop relations, ETA or optimisation,
purchase prices, expected incoming invoices, attribute sync, CO₂) runs in nested `RequiresNew` transactions and, from the
planboard, in a fire-and-forget `Task.Run` with swallowed exceptions. The time-based stop generator is dead; the stop
ETA forecast returns **random** delays.

Requirements:

- One Planning domain service `TripStopComposer` with the two modes as an enum on the trip; manual stops keep their
  place; stops that lose their last leg are removed; positions renumbered 1..n; the first/last-stop rule from the type.
- Assignment validation in the command: trip not Cancelled/Archived/Completed, leg not already on another trip unless
  moving, leg dates inside the trip window or an explicit override flag; capacity, capability and incompatibility checks
  produce **stored warnings** (§4.11), not refusals (as OpenTMS, but computed once, not per read).
- Everything after the save is **flow 2**: the command writes legs, stops and the event; `IDerivedValueScheduler`
  requests `PlanningExecution.TransportTrip` (ETA, distances, warnings, load factor, CO₂) and Financial reacts to
  `TransportTripLegsChanged` for purchase rate lines and cost (pricing document §5). No nested transactions, no
  `Task.Run`. The screen shows `CalculatingIndicator` (design 49).
- Split a line across trips, return leg, carry out separately, outsource an activity to a hub trip: port as Planning
  commands (`trips.md` §3 and scenarios 20–24), each going through the assignment service (OpenTMS bypasses it in
  `OutsourceOriginActivity`).

### 4.10 Routing: CHANGE to one adapter behind AD-12 (Q4)

OpenTMS (`routing-co2-cmr.md` §1): two reflection-based static factories; VROOM default (hard-coded host) for optimisation
and ETA, OSRM (hard-coded host) for all stored distances with a silent **Google** fallback on 0 km, PTV opt-in per tenant,
NextBillion and SmartRoute disabled, MapBox and RouteXl mostly commented out; the Valhalla Cloud Function for geometry,
toll and GLEC CO₂, URL fallback in code; a distance cache whose **read side is disabled** while writes grow forever under
tenant 1; OSRM km rounded to integers at the source; durations as strings; a 10-minute ETA worker for in-flight FMS trips.

Requirements:

- Integrations publishes `IRoutingService` (`Application.Contracts`): `GetDistanceAsync(from, to, vehicleSpec?)`,
  `GetMatrixAsync(points, vehicleSpec?)`, `GetRouteAsync(points, vehicleSpec?)` → distance m (integer metres, never
  rounded km), duration seconds, geometry, toll segments, CO₂e when the provider supports it; `IRouteOptimizer` is a
  separate interface the Planboard run adds. Providers: OSRM (first), Valhalla function (toll, CO₂, geometry), PTV later.
  Hosts and keys from configuration and the secret store (AD-09 style), **never** literals; one provider per tenant via a
  setting, no reflection, no static service locator.
- A **real** distance cache in Integrations keyed on rounded coordinates and vehicle class, with a TTL, per tenant
  database; cache hits are facts (AD-15 does not apply: a routing result is a query, not a report).
- Leg distance and duration, trip planned distance and stop planned times are derived values (§4.11); ETA for
  in-progress trips comes from the Planboard/ingest run (positions are ingest, AO-12), not from this run. The random
  forecast is not reproduced.
- Speed-correction factor per tenant (OpenTMS default +20 % duration) stays a setting; transaction times per stop come
  from the waypoint type's default duration (design 51) and product/unit transaction times (Master Data, later).

### 4.11 Derived values on legs, trips and orders: CHANGE to AD-07 (Q1)

OpenTMS (`trips.md` §6, `pricing.md` §5): cost, revenue, load factor and warnings are computed on **every grid read** by
two generations of calculators that drift; a stored-procedure attribute cache exists in parallel; a stored
`TransportOrderAttributes.Revenue` excludes D&S; order cost of revenue = trip cost if the order is alone on the trip, else
a leg-kilometre share; trip revenue = leg km × order revenue per km with a single-order fallback.

Requirements: three calculators on the design-49 mechanism, each writing stored columns with provenance:

| Target type | Writes | Triggered by |
|---|---|---|
| `PlanningExecution.TransportTrip` | per stop planned arrival/departure and cumulative distance; trip planned distance and duration; `LoadFactor`, `LoadingMeters`, warnings (jsonb list of typed warning codes); `Co2Kg` per trip and the allocation per leg (Q10); **allocated cost and revenue per leg** (Q1) read from Financial's rate-line contract | leg assigned/unassigned, stop moved, carrier/vehicle changed, rate lines changed (`RateLinesChanged` event from Financial) |
| `PlanningExecution.Leg` | distance, duration, CO₂ estimate | stops of the order changed, routing provider changed |
| `Orders.TransportOrder` | revenue, cost of revenue, margin, execution progress (Q5), counts and first/last stop columns for the grid | rate lines changed, leg/trip changed (events from Financial and Planning) |

Grids read these columns only. The allocation rules (trip cost → legs by assigned km; order revenue → legs by km with
the single-order fallback) are ported as stated in `pricing.md` §5 and tested with its scenarios 62–69.

### 4.12 CO₂: CHANGE (Q10)

OpenTMS (`routing-co2-cmr.md` §2): freight footprint = CPI × transport-unit amount × great-circle km over **unordered**
waypoints, rewritten for the whole tenant by raw SQL when the CPI setting changes; the trip job (Cloud Function + COFRET
allocation to legs) is enqueued on every save and **permanently disabled**, so leg values are never filled; GLEC/ISO 14083
WTW CO₂e from the Valhalla function is display-only; BigMile publication is manual (out of scope).

Requirements: `ICarbonFootprintCalculator` in Integrations with the Valhalla function as the first provider (GLEC v3,
Tier 1 defaults, Tier 2 when `Vehicle.FuelConsumption` is known), computed per trip and allocated to legs by tonne-km
(COFRET as OpenTMS intended), stored per leg, trip and order line with provenance (§4.11). The great-circle CPI method
stays as the fallback and for unplanned order lines (an order has an estimate before it is planned). Vehicle inputs come
from design 48 (`FuelType`, `GrossVehicleWeightKg`, Euro class as enum, consumption). Reporting reads the stored values.

### 4.13 CMR print: CHANGE (Q11)

OpenTMS (`routing-co2-cmr.md` §3, `screens.md` §4): no entity; the PDF is drawn with iTextSharp per freight, 1–5 coloured
copies, barcode = freight GUID, no number, held in process memory for five minutes; only signed CMRs are stored; the
profile holds box options as **hard-coded GUIDs** and `;`-joined lists with a tenant-specific hack for two tenants; the
dialog offers profile, copies, borders, QR, group-by-leg, a picker per box and the lines to include; print, download,
e-mail.

Requirements: Orders entities `ConsignmentNoteProfile` (name, default, copies, borders, QR, group-by-leg, one typed
option enum per box — `CmrBoxSource { Customer, OriginAddress, DestinationAddress, OwnCompany, Carrier, Shipper,
TripDeparture, TripArrival, Empty }` — and child rows for the multi-value boxes 6, 13, 18) and
`ConsignmentNoteFixedDescription` (PO 2026-10-16), both in the Master data app under **Orders → Transport orders**.
Command `PrintConsignmentNote(orderLineIds | tripId, profileId, overrides)` renders through Platform's
`IDocumentRenderer` (§7), stores the PDF as a document (AD-09) with a CMR number from a Platform sequence and the
rendered data snapshot, returns the document reference; the screen downloads or prints from storage. The box mapping of
`routing-co2-cmr.md` §3.2 is the functional spec. E-mailing the CMR uses the Platform mail run (out of this run: the
command returns the document; the mail button comes with e-mail templates).

### 4.14 Dates, time zones, units: CHANGE

OpenTMS: `DateTime` without kind, `DateTime.Now` and `UtcNow` mixed, Windows time-zone ids, date-only semantics emulated
with `.Date` fixes in the tenant zone, durations as strings, km rounded to integers, kg guessed from magnitude.

Requirements (coding conventions, design 51 precedent): instants as `DateTimeOffset`/`timestamptz`, dates as `DateOnly`,
durations as `TimeSpan`/interval, the stop's IANA zone from its address, windows from `TimeSlot.ToWindow`, metres and
kilograms as integers or decimals with the unit in the name.

### 4.15 Permissions and features: CHANGE (Q14)

Per-action permissions in each module's `Application.Contracts/Permissions/`, the architecture test, and features
`Apps.Transport` (exists), `Apps.Transport.Orders`, `Apps.Transport.Orders.Templates`, `Apps.Transport.Trips`,
`Apps.Transport.Documents` (CMR), `Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles`,
`….ConsignmentNoteFixedDescriptions`, `Apps.MasterData.Financial.Pricing.PriceLists` — all default **on** (PO).

### 4.16 Grids, search, export: CHANGE the pipeline, KEEP the columns

OpenTMS (`orders.md` §10, `trips.md` §9, `screens.md` §1): ag-grid server-side model with columns equal to the list DTO,
free text over 25–39 columns with `ToLower().Contains`, grouping, per-user column state, Excel export as a job, filter
views by permission. Requirements: `OpenTmsGrid` with the design-6 settings store, paged queries with an explicit filter
contract (date range, status, relation, organisation unit, carrier, free text over a defined column set with a trigram
index), stored derived columns, the same column set as a starting point, Excel export through the existing job pattern.
Filter views (Today, Tomorrow, This week, Open, Drafts, All, Templates) are saved filters in the pane, not permissions.

### 4.17 Security findings in the OpenTMS transport code

`Reset()` hard-deletes every order of a tenant behind a bare `[AbpAuthorize]` (`orders.md` §11 row 12); one permission for
the whole order and trip services; secrets and hosts in code (routing, PdfGeneratorApi key in
`appsettings.Development.json`); the write-only cross-tenant distance cache grows without bound. Not for the rebuild;
listed for the current product (assessment §6 style).

## 5. Functional scenario catalogue

The appendices hold 304 numbered scenarios, each marked fact or inferred, with citations. The design docs cite them by
appendix and number (for example `orders.md #54`). Grouped index:

| Area | Appendix | Numbers | Covers |
|---|---|---|---|
| Order create/edit | orders.md | 1–17 | defaults, relation defaults, required fields, numbering, date rules, line removal, fixed-price exclusivity |
| Legs | orders.md | 18–28 | generation, via depots, assigned-leg protection, re-chaining, trip effects |
| Draft/confirm (OpenTMS pending/accept) | orders.md | 29–32 | |
| Approve/invoice/archive/revise | orders.md | 33–43 | validators, auto-approval, bulk D&S |
| Cancel/delete | orders.md | 44–47 | |
| Templates/recurrence | orders.md | 48–53 | |
| Tevens trip | orders.md | 54–61 | |
| Redistribution (out of scope except #62–63 as a later planboard wizard) | orders.md | 62–64 | |
| Grid/lookups | orders.md | 65–72 | |
| Trip lifecycle and header | trips.md | 1–6, 34–38, 46–55 | |
| Leg assignment, stops, re-sequencing | trips.md | 7–28 | |
| Route, ETA, warnings | trips.md | 29–33, 44–45, 62 | |
| Trip cost, revenue, load factor | trips.md | 39–43 | |
| FMS and ETA mail (out of scope) | trips.md | 50–51, 58, 60–61 | |
| Pricing (all) | pricing.md | 1–80 | see the pricing document |
| Screens (UI point of view) | screens.md | 1–56 | |
| Routing, ETA, CO₂, CMR | routing-co2-cmr.md | 1–34 | |

Scenarios the new architecture **adds** (not in OpenTMS): save returns at once and the price, cost, CO₂ and ETA columns
show *Calculating…* until pushed (design 49); a retroactive rate change re-runs 10,000 orders in bulk; a changed stop
unassigns its leg and the planner sees a warning on the trip; a Draft order from a boundary agent is confirmed by a
person; a printed CMR is a numbered, immutable document.

## 6. Screens of the Transport app

### 6.1 What OpenTMS has (`screens.md`)

Two grids (orders with 7 filter views, trips with 4), a toolbar with seven buttons and a seven-item wizard menu, two
context menus, two detail **modals**: the order modal with 10 tabs (General with five nested tabs holding a waypoint strip,
a 40-field line entry form, the line grid and the sub-tabs; Financial; Invoices; Callbacks; Schedule; Trip; attachments;
remarks; record id; a hidden print tab), an edit-line dialog with three outer and four inner tab groups (stops per line,
legs, sales and purchase price tables), approve/revise/accept dialogs; the trip modal with 7 tabs (header, legs grid,
stops as a card list with a Google map dialog, driver and internal remarks, custom fields, costs, sensor readings). UX
problems the code shows: fields duplicated three times, legs regenerated on any stop change (manual edits lost), modal
stacking three deep with DOM hacks, a Close button that never shows, no unsaved-changes guard, swapped width/height
visibility flags, English-only texts, a command bar that is not selection-aware, print as a hidden tab.

### 6.2 Requirements for the new screens (UI rules `docs/conventions/ui/ui-rules/`)

- **Transport app nav**: Orders (views Today, Tomorrow, This week, Open, Drafts, All), Templates, Trips (Today, This
  week, Open, All), Documents (printed CMRs). Each node a feature (§4.15).
- **Order editor**: `EntityDialog size="xl"` or a full page via `useOpenRecord` (PO choice at design time), tabs
  *General* (header, customer, references, remarks), *Lines* (`LinesGrid` of lines with inline quantities; a line's stops
  and legs in a side panel, not a nested modal), *Pricing* (the rate lines of the order grouped by line, with the
  `CalculatingIndicator`, manual line add/adjust, fixed price, D&S, additional services, totals — one place instead of
  Financial tab + price panel + revenue overview), *Planning* (legs with their trips, the "Tevens trip" section §4.7),
  *Documents* (CMR print, attachments), *Record identification*. Every picker has "Add new…" (UI-LOOK-004).
- **Trip editor**: header, *Legs* (`OpenTmsGrid` with inline reference/remark edits), *Stops* (an `OpenTmsGrid`, not a
  card list, with move up/down, manual stop, actuals), *Cost and revenue* (purchase rate lines, trip cost, allocation
  per leg), *Documents*, *Record identification*. Vehicle and carrier fields read-only in the Transport app with a link
  to the Planboard (PO 2026-10-25), editable only when the trip was created through "Tevens trip" and has no actuals.
- **Wizards** (`Wizard` component, built by the first feature that needs it): New from template / Copy order
  (source → recurrence or dates → details → confirm), Print CMR (profile and box options → lines → output), Archive trips.
- Status with `StatusBadge`; money, distances and dates through `useFormat()`; unsaved-changes guard on every editor.

## 7. Dependencies and housekeeping

- **`feat/platform-master-data`** (Platform master data: e-mail templates, report definitions, document types, design 54;
  report rendering through `IDocumentRenderer` with pdfgeneratorapi, design 55) was merged on 2026-10-25 with main's
  number-sequence design (50) winning over its standalone list. CMR printing (§4.13) and CMR e-mailing build on it.
- Master Data run for **vehicle fuel and emission fields** (design 48 names them, not all built) and **product and
  transport-unit transaction times** (design 51 assumption 5) are inputs for CO₂ and stop durations; the design lists
  what it needs and adds the fields in the owning module if missing.
- The build environment of a cloud session must have .NET 10, Node 22 and PostgreSQL 17 for the PostgreSQL-backed tests;
  not verified on 2026-10-25.

## 8. Shape of the briefs for the build session

- One umbrella design `docs/designs/56-transport-run.md` (PBI + the nine sections) that cites AD-06/07/08/12/15/16/18, lists
  the Q1–Q16 answers as PO decisions, and splits into module designs built in this order on one branch:
  57 Financial price lists and rate lines (pricing document), 58 Orders transport order (incl. templates, CMR entities),
  59 Planning legs, trips, stops and the derived-value calculators, 60 Integrations routing and CO₂ adapters,
  61 Transport app screens. Each with its own migration (expand only), seed where the run needs one (a default CMR
  profile), permissions, features default on, localisation nl/en, OpenAPI and client regenerated, Dutch manual pages
  (`docs/manual/transportopdrachten.md`, `ritten.md`, `prijslijsten.md`, `cmr.md`).
- Hand this document and its appendices along; the session cites the appendix scenario numbers in its test risk
  analysis and marks each ported scenario with the test that covers it.
- Tests per the test-engineer matrix: tenant isolation on every aggregate and calculator (critical), state machines and
  assignment rules (critical), the pricing engine against `pricing.md` scenarios 1–80 (critical), derived-value
  idempotency (critical), routing adapter with a fake provider (high), screens with vitest (medium).
- Reserve a separate brief for the **data migration** from OpenTMS (flags enum → status, pipe strings → rows, GUID
  stop-leg links → join rows, `double` → `numeric`, trip ids equal to order ids → new ids with `CreatedFromOrderId`).
