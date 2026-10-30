# Design 61: the Transport app (frontend) and the Master data lists of the run

**PBI:** design 56 (umbrella), frontend part
**Status:** approved (PO 2026-10-25; UI per `docs/conventions/ui/ui-rules/`)
**Date:** 2026-10-25
**Depends on:** designs 56–60 (the APIs), the UI rules, the component map. Input:
`docs/input/transport-assessment/screens.md` (56 scenarios; UX findings §6), transport assessment §6.2.

## Domain impact

None (views on Orders, Planning & Execution, Financial, Master Data). Frontend folders: `src/domains/orders/`
(`transport-orders.ts`, `templates.ts`, `consignment-notes.ts` hooks), `src/domains/planning-execution/` (`trips.ts`,
`legs.ts`), `src/domains/financial/` (`rate-lines.ts`, `price-lists.ts`, `relation-financial-settings.ts`),
`src/apps/transport/` (views, editors, wizards), `src/apps/master-data/` (the new lists), `src/components/ui/`
(`wizard.tsx`, `use-open-record.ts`).

## API contract

Consumes the endpoints of designs 57–60 through the generated client; no hand-written fetches. Realtime:
`useRecordUpdates("Orders.TransportOrder" | "PlanningExecution.TransportTrip" | "Financial.OrderRateLines", ids)` and
`useDerivedValueStates` for the calculating indicators.

Amendment (Marinus 2026-10-30, price preview of a new order): Orders adds `POST /api/orders/transport-order/preview-new-order-prices`
(`PreviewNewOrderPricesAsync`, permission `Orders.TransportOrders.Create`, feature `Apps.Transport.Orders`): the customer,
order date, terms and items of an order that was never saved, taken as Create takes them (no terms → the customer's
defaults, no date → today), priced by Financial's `IPricePreviewService` without an order id; nothing is stored.
Financial's `PricePreviewLineDto` gains `LinePosition`, the 1-based position of the item in the posted snapshot (saved or
not; null on order-level lines), so the screen can show an unsaved item's own preview lines: in the item dialog, and as
the item's amount in the freight items grid and its total while a preview stands.

## Migration strategy

Not applicable (no data).

## UI design

### Navigation (`apps.config.ts`, features of design 56)

Transport app (`/transport`, `Apps.Transport`): **Orders** (`/transport/orders`, `Apps.Transport.Orders`; views as saved
filters: Today, Tomorrow, This week, Open, Drafts, All), **Templates** (`/transport/templates`,
`Apps.Transport.Orders.Templates`), **Trips** (`/transport/trips`, `Apps.Transport.Trips`; Today, This week, Open, All),
**Documents** (`/transport/documents`, `Apps.Transport.Documents`: printed CMRs). Master data app: Orders → Transport
orders gains **CMR profiles**, **CMR fixed texts**, **Order attributes**; Planning & Execution → Trips gains **Via depot
rules**, **Trip attributes**; Financial → Pricing gains **Price lists**, **Relation financial settings**.

### Orders list

`OpenTmsGrid` (server-side, `GridRequest`), columns: Number, Status (`StatusBadge`), Order date, Customer, Reference,
Lines, First stop (name, city, date), Last stop (name, city, date), Revenue, Cost, Margin, CO₂, Trips, Progress, type,
organisation unit, attributes (hidden by default). Toolbar filters: view, date range (for All), status multi-select,
customer `EntityLookup`, organisation unit. Command bar (selection-aware, UI-CMD rules): New (per type, dropdown), New
from template, Copy, Print CMR, Approve (validate dialog), Cancel, Delete, Export. Row actions: Open, Print CMR, Copy,
Cancel, Delete. Double-click opens the editor (`useOpenRecord`: route `/transport/orders/{id}`, so a record is a URL).
Derived columns show `CalculatingIndicator` while pending.

### Order page, legacy tab structure (PO decisions 2026-10-26 and 2026-10-27, replaces "Order editor" below)

The PO judged the first order page less usable than the legacy screen: the order → freight item hierarchy was split over
order-wide tabs (Lines, Pricing, Planning), the order-level stop pattern made no sense once an order's legs can go to
different trips, and entry was too slow for manual data entry (2026-10-26). A one-page variant without tabs was then
rejected ("the tabs are gone, look at the original again", 2026-10-27). The page follows the legacy screen's tab
structure (`docs/input/transport-assessment/screens.md` §"Tabs, in order") with the improvements below. The backend and
the data model stay; "line" in the API is "freight item" on screen. Binding:

1. **Main tabs** (UI-FORM-008, end-aligned, directly under the command bar): **General**, **Financial**, **Tevens
   trip**, **Schedule** (template only), then the standard icon tabs **Documents** (`Paperclip`; CMR prints, order only,
   once stored), **Remarks** (`MessageSquareText`; the order's dated remarks such as revision reasons, read-only) and
   **Record identification** (`Fingerprint`). Invoices and Callbacks of the legacy screen are out of scope. A tab that
   holds a validation error is marked; a failed submit opens the first such tab (UI-FORM-006/007).
2. **General**: the order header on top (number (read-only), order date, organisation unit, order type (locked once
   numbered), customer (required, "Add new…"), contact, customer reference; template: name instead of number and date),
   then **nested tabs** (start-aligned, text only, one level): **Freight items**, **Additional services**, **Remarks
   for the planner** (`internalRemarks`), **Other references** (`references`: type, reference, description; add and
   remove rows), **Custom fields** (`attributes`; only when the order type defines them).
3. **Freight items** works like the legacy entry, top to bottom:
   - a **stop strip for the new freight item**: one row per stop with activity (waypoint type), relation + address (the
     default or only address preselected, "Add new…"), contact, date, time slot, reference, remarks; at least 2 stops,
     **+** adds a stop, **−** removes one (never below 2). The strip starts with the order type's default waypoints
     (number and activities; Load / Unload when the type defines none). The strip belongs to the item being entered,
     not to the order (no order stop pattern, point 7).
   - the **freight fields** of the new item in one row (transport unit, quantity, product, positions, colli, weight,
     loading metres, volume; visible and required per the order type where the type configures it) and **Add**.
     **Enter adds the item** (when no list is open), keyboard first: Tab moves on, every picker offers "Add new…" from
     the keyboard (UI-LOOK-004). The new item gets a copy of the strip's stops. After Add the strip keeps its stops
     (the next item often shares them), the freight fields clear except the transport unit, and focus returns to the
     last stop's relation. **Paste from Excel** next to Add (tab-separated rows: from relation, to relation, date,
     transport unit, quantity, weight, reference; relations and units matched on code or name; a preview marks
     unmatched cells).
   - the **freight items grid** (`LinesGrid`): position, from → to (first and last stop: relation, city), date, transport
     unit, quantity, positions, weight, loading metres, reference, price (the item's stored rate-line total with
     `CalculatingIndicator`), trip (number link when the item's legs are on a trip); footer: the freight items total.
     Double-click, Enter or the pencil opens **Edit freight item**; Delete asks "Delete freight item {n}?".
4. **Edit freight item** (`EntityDialog` size `full`, tabs of its own; the order → freight item → legs and pricing
   hierarchy lives here):
   - **Freight details**: cargo and quantities (unit, quantity, positions, colli, weight, volume, loading metres, EPQ,
     load factor, product, product relation code, vehicle type, temperature, required characteristics, payment
     instruction, barcode), then nested tabs Container, Measurements, Texts and documents, Custom fields.
   - **Stops and legs**: the item's stops (the same row editor as the strip; add, remove, move) and below them the
     item's **legs** generated from the stops (origin → destination, dates, distance, CO₂, trip link, allocated
     cost/revenue; the editable leg fields and leg operations as before). Before Confirm the legs part says they are
     created on Confirm.
   - **Pricing**: the item's rate lines (kind badge, provenance, quantity, unit price, amount; add manual line, adjust
     unit price, remove manual) and the item total; an unsaved change shows the price preview, an item not saved yet
     (on a new order too) its own preview lines, matched on `LinePosition`.
   The dialog edits the item in the order's form: **Apply** keeps the changes, Cancel restores the item as it was when
   the dialog opened, and the order's Save stores them. Rate-line actions act on the stored item, as before.
5. **Additional services** (nested tab of General): the order-level rate lines of kind additional service, with Add
   additional service (picks from the additional-services list, fills ledger account, cost centre and unit price) and
   Remove; available once the order is stored (before that the tab says so).
6. **Financial** (legacy blocks): **Price calculation** (fixed price vs priced per freight item, the price lists,
   "apply only the highest rate on multiple matches"), **Revenue overview** (read-only: fixed price, freight items,
   additional services, discounts or surcharges, total revenue, with `CalculatingIndicator`), **Invoicing** (currency,
   VAT, discounts or surcharges with the defaults marked, payment term, freight documents with the invoice), **Remarks
   for invoicing** (`invoicingRemarks`), and the order-level manual lines and the fixed price with their actions. The
   financial defaults are copied when the customer is chosen (unchanged rule).
7. **No stop pattern on the order.** Stops exist only per freight item. The order type's default waypoints only seed
   the stop strip; the template keeps its stops per item as day offsets.
8. **Tevens trip** (main tab): as before, plus **which freight items go on the trip** (checkbox per item, default all;
   backend `tripRequest.lineIds`, null = all). Only the legs of the chosen items are put on the trip on Confirm; the
   other legs stay unplanned for the Planboard.
9. **New order opens the page directly** (no New dialog): order type = the default type, order date = today, focus on
   Customer. Shortcuts: Ctrl/⌘+S save, Ctrl/⌘+Enter confirm (Draft). A stop without a usable address names the stop in
   its message ("Stop 2 (Unload): …"); a date field's calendar button is not a Tab stop.

The section "Order editor" below is superseded by this one; templates use the same page in template mode.

### Order page review (PO decisions 2026-10-28)

The PO compared the rebuilt order page with the legacy screen and found five gaps; each proposal below was approved in
chat on 2026-10-28. They extend flow 1 (order intake) and, for the purchase prices, flow 2 (derived values); the model
decisions QP5 and QP7 stay as they are. All of it sits behind the Transport app's existing features (on by default in
this run, PO deviation from rule 4 recorded in design 56).

1. **"Tevens trip" in English.** The English tab is **Trip with order**, its switch **Create a trip with this order**;
   every English text that said "Tevens trip" follows. Dutch keeps "Tevens trip".
2. **Open the picked record (UI-LOOK-005).** Every single-value record picker in a form of the order page (header,
   freight item entry and dialog, stop strip, Financial and Trip with order tabs), the trip page and the Master data
   editors shows the pencil when a value is selected and the user may view that entity. It opens that entity's editor on
   top through the same table as "Add new …" (`domains/master-data/components/create-in-place.tsx`): in edit mode with
   the update permission, read-only with only the view permission (tooltip `Edit` resp. `Open`). A child record (an
   address, a contact person) opens its relation's editor on the matching tab. The form underneath keeps its unsaved
   changes; after a save in the editor on top, the picker shows the new name. `ReferenceCombobox` gains `onEdit` like
   `EntityLookup`. Not on `MultiSelect` (several values), list filters or enums.
3. **Price lists per basis (legacy layout, QP5 kept).** The order's Financial tab, block Price calculation, shows one row
   per basis in the legacy order: quantity, colli, EPQ, weight, distance, positions, fixed price per freight item
   (`PerLine`), volume, loading metres, load factor, duration, vehicle type, then toll distance. A row is a switch plus a
   `MultiSelect` of the revenue price lists of that basis (normally one; the model allows more). Choosing a list switches
   the row on; switching a row off clears its lists. A row that is **on without a list** makes the calculation write, per
   freight item, one **open line** for that basis: kind `PriceList` without a price list, the basis quantity of the item,
   unit price 0, shown with the badge "To be priced". The clerk prices it with the existing *Adjust unit price*; from then
   on it is an adjusted line (QP6: kept, re-valued from the item's quantity, kept when the basis is switched off again).
   An open line that was never adjusted disappears when its row is switched off. The fixed-price choice disables the rows
   (legacy). Data: the order terms (order and template) gain `chargedBases` (the bases switched on, `QuantitySource`
   values; a basis with a list counts as on), in the input, update and read DTOs and in `PricingSnapshotDto`; the customer's
   **Relation financial settings** gain `revenueChargedBases` and show the revenue price lists in the same per-basis rows;
   choosing the customer copies both into the order (unchanged rule for the other defaults). The cost side of the relation
   settings keeps its plain list. The preview (`preview-prices`) writes the same open lines.
4. **Purchase prices per freight item.** The Pricing tab of Edit freight item gets a second table **Purchase prices**
   under the sales table (legacy "Inkooptarieven"): the cost lines of this freight item on all its trips (kind badge, trip
   number (link), description, basis, quantity, unit price, amount, provenance tooltip), with a total. Actions as on the
   sales side: *Add purchase line* (description, basis, quantity prefilled from the item's value for the basis, unit
   price, and the trip: one of the trips the item's legs are on, preselected when there is one), *Adjust unit price*
   (calculated lines), *Remove* (manual lines). **No purchase line before the item is on a trip** (PO 2026-10-28): until
   then the table says purchase prices appear once the freight item is on a trip, and the agreed purchase price of Trip
   with order covers the case. Backend: Financial reads the cost lines of an order (`GET /api/financial/rate-line` with a
   `side` filter, or a dedicated order-cost read; cost lines keep their trip anchor, filtered on `OrderId`, tenant-scoped);
   a manual cost line names `tripId`, `orderLineId` and the item's first `legId` on that trip, and is refused when that
   leg is not on that trip (`Financial:LegNotOnTrip`). Permissions are the ones of the trip's cost lines.
5. **Split a leg at an address.** The legs grid in Edit freight item → Stops and legs gets two row actions:
   *Split leg at …* (dialog: relation with address, the arrival activity at that address and the departure activity, the
   latter defaulting to the arrival's successive activity of design 51, and the date) turns A→B into A→X and X→B, and
   *Remove split* (on either half) turns them back into A→B. Planning commands `POST /api/planning/leg/{id}/split-at-address`
   and `POST /api/planning/leg/{id}/remove-split`; the new legs carry `LegSource.SplitAtAddress`, the address end is a
   Planning-only stop with a synthetic id (as the via depot's), quantities are copied, distances are requested as derived
   values, and the line counts as reshaped (design 59 build note 3: regeneration keeps its legs). Both commands are refused
   while a leg involved is on a trip (`Planning:LegOnTrip`: unassign it first). The trip's Legs tab gets the existing
   quantity split (`split-line`) as row action *Split quantity* (quantity, optional target trip).

#### Build notes (backend, 2026-10-28)

1. **Charged bases (point 3).** `chargedBases` (`QuantitySource[]`) on the order and template terms (input, update, read)
   and in `PricingSnapshotDto`; `revenueChargedBases` on the relation financial settings (upsert, read, and the published
   `RelationFinancialSettingsReferenceDto`, from which the server-side default terms copy it). Valid are the thirteen
   revenue bases of `ChargedBases.RevenueBases` (Financial `Domain.Shared`), each once: `Manual` and the cost-only
   `TripDistanceKm` are refused (`InvalidCode`, a repeat `DuplicateKey`, field `chargedBases` / `revenueChargedBases`). Stored
   as a bit mask (`ChargedBases` / `RevenueChargedBases` integer columns, 0 for existing rows); reads return the legacy
   order of the block. The backend does not add the bases of the chosen lists: a basis with a list counts as on either
   way. For the per-basis rows the read DTOs carry each list's basis (`financialTerms.priceListBases[]`, parallel to
   `priceListIds`; `RelationPriceListDto.basis`) and `GET /api/financial/price-list/lookup` takes `basis`.
2. **Open lines.** The resolver writes them for every charged basis that none of the order's lists (whatever their
   currency or validity) prices by: per line kind `PriceList`, `priceListId` null, `quantitySource` = the basis, the
   line's value of the basis (0 plus the `QuantityMissing` warning while unknown, e.g. no distance yet), unit price 0,
   amount 0, `descriptionKey` `Financial:RateLine.OpenBasis` ("To be priced"). **An open line in a read is kind PriceList
   with `priceListId` null and `isAdjusted` false**; adjusted, it is kept (QP6), matched to its basis on the next run and
   re-valued from the line's quantity, also after its basis is switched off. Open lines are not pruned by multiple
   matches, are absent under a fixed price, and a charged basis counts as applied for the per-kilometre surcharges (a
   distance charge suppresses the distance surcharge, scenario 19). The preview writes the same lines.
3. **Purchase prices (point 4).** `GET /api/financial/rate-line?OrderId=…&Side=2` returns the cost lines on the order's
   legs within the trip each leg is on now (trip-level lines such as the agreed price stay out); `RateLineDto` gains
   `anchorTripId` (always), `tripNumber` and `legOrderLineId` (this read only); totals per leg; the state is the least
   advanced of the trips' cost calculations. `POST manual` with `tripId`, `orderLineId` and `legId` is refused with
   `Financial:LegNotOnTrip` (409) when the leg is not on that trip or not of that line (checked for every leg-anchored
   trip line, through Planning's `ILegReader`). Permissions: `Financial.RateLines` / `.Update`, as the trip's lines.
4. **Split at an address (point 5).** `POST /api/planning/leg/{id}/split-at-address` `{ relationId, relationAddressId,
   arrivalWaypointTypeId, departureWaypointTypeId?, date? }` returns the two legs (A→X, X→B; `LegSource.SplitAtAddress` =
   6, `splitFromLegId` = the leg). X is the stop `SyntheticStopId("split-at", leg, address)`; the departure defaults to
   the arrival type's successor, else the arrival type; the date to the leg's origin date. The split leg is soft-deleted
   and `POST /api/planning/leg/{id}/remove-split` (either half; the service method is `UndoSplitAtAddressAsync`, mapped in
   the host) restores it with its own id and source and the halves' outer ends, so a generated or via-depot leg is the
   generator's again; a line whose legs are then all the generator's is regenerated from the order. Both publish
   `LegsGenerated` and request the new leg's routes. Refusals (409): `Planning:LegOnTrip`, `OrderCancelled`,
   `LegNotSplitAtAddress`, `SplitAtAddressNested` (the other half was split again: remove that split first); 400:
   `SplitAddressIsLegEnd`. Permission `PlanningExecution.Legs.Update`, feature as the other leg endpoints.
5. **Removing an open line; an adjusted open line is the item's line for its basis** (decided in the build, PO to
   confirm). `DELETE /api/financial/rate-line/{id}` also accepts an open line (kind `PriceList`, `priceListId` null),
   adjusted or not; other calculated lines stay refused (`CalculatedLineReadOnly`). While its basis is still charged
   without a list the next calculation writes a fresh, unadjusted open line again (legacy behaviour), so removing an
   adjusted open line returns it to "To be priced". Legacy rule "a manually adjusted line exists → keep it"
   (`docs/input/transport-assessment/pricing.md` §2): when a list of that basis is applied to the order, an adjusted open
   line of a freight item is kept and the lists of that basis write no line for that item (no double charge); an unadjusted
   open line disappears as before. Symmetrically, any kept adjusted line of a basis for a freight item suppresses the open
   line of that item and basis: an adjusted list line whose list was removed while its basis stays charged is the item's
   line for that basis (the basis of the list is read from Financial's own table, also for a list no longer on the order;
   an unknown list falls back to the line's quantity source). The preview has no stored lines, so it shows the list's line
   or the open line there until the save.
6. **Approval compares the charged bases** (decided in the build, PO to confirm). The rule `FinancialDefaultsMismatch`
   also compares the order's `chargedBases` with the customer's `revenueChargedBases`, alongside the price lists, on the
   **effective** sets: the order's `chargedBases` plus the bases of its price lists against the
   customer's `revenueChargedBases` plus the bases of the customer's revenue price lists ("a basis with a list counts as
   on"), so old settings without charged bases give no false warning. The bases come through Financial's published
   `IFinancialReferenceLookup.GetPriceListsAsync`; the field name in the message is `chargedBases`.
7. **The split date lies within the leg** (decided in the build, PO to confirm). `split-at-address` refuses a date before
   the leg's origin date or after its destination date with `Planning:SplitDateOutsideLeg` (400, data `legId`, `from`,
   `till`); a side without a date is not checked. The default date (the origin date) always passes.

### Financial tab compacted (PO request 2026-10-28)

The PO found the Financial tab inefficient: thirteen basis rows in Price calculation next to an almost empty Revenue
overview. Changed, the model untouched (QP5 and "Order page review" 3 stay):

1. **A row per charged basis only.** `PriceListsPerBasis` shows a row for each basis that is switched on or has a chosen
   list, in the legacy order: the basis name, the MultiSelect of that basis's revenue lists and a remove button (which
   clears the row's lists and switches the basis off). **Add price basis** (a menu of the bases not shown, legacy order)
   switches one on without a list (an open line per item until a list is chosen) and moves the focus to the new row's
   picker. With no basis the block says so. The same block serves the relation financial settings editor.
2. **Lookups for the shown rows only.** The per-basis lookups load only for the rows shown; a chosen list whose basis the
   record does not know loads them all once, to place it.
3. **Two independent columns.** Left: Price calculation, Invoicing; right: Revenue overview, Remarks for invoicing; the
   order-level lines span both. The columns do not share row heights, so no section stretches to its neighbour.
4. **Fixed price first.** In Price calculation the choice "This order has a fixed price" comes before "The freight items
   are priced individually".

### Via-depot rules on the stop's waypoint type (PO decision 2026-10-29)

The PO found that default legs ignored the stop's waypoint type. OpenTMS's `RelationDefaultViaWaypoint` and
`GeographicalRegionDefaultViaWaypoint` are "relation (or region) **plus the stop's waypoint type** → via relation and via
waypoint type" (`docs/input/transport-assessment/orders.md` §4 step 3); the summary in `transport-rebuild-assessment.md`
§4.3 and design 59 dropped the waypoint type, and design 59's generator put at most one depot in a pair. Approved in chat
(the four choices as recommended). Extends flow 1 (order intake → legs); behind the existing features (Master data node
`ViaDepotRules`, on by default in this run).

1. **The rule names the stop's waypoint type.** `ViaDepotRule.StopWaypointTypeId` (Orders id, no foreign key): a rule
   matches a stop when the stop's waypoint type is that type and the stop's relation (scope Relation), the order's
   customer (Customer) or one of the stop's regions (Region) is the rule's. Required on create and update (API `[Required]`,
   editor required field); every waypoint type of the rule (stop, arrival, departure) is checked through Orders'
   published `ITransportOrderLookup.GetWaypointTypesAsync`, so another tenant's or an unknown id is `ReferenceNotFound`.
2. **Relation → customer → region, a fallback per level.** Per stop pair the scopes are tried in that order; the next
   scope counts only when no rule of the current scope matches either stop (as OpenTMS).
3. **One depot per stop, two per pair.** Within the deciding scope, the first rule in position order that matches the
   origin stop and the first that matches the destination stop; the pair runs origin → origin's depot → destination's
   depot → destination; one depot named by both is visited once. A rule whose depot is one of the pair's own places is
   passed over. Each leg carries the rule of the depot it arrives at, the last one the rule of the depot it leaves;
   overnight adds a day per depot.
4. **Stored rules.** Migration `PlanningViaDepotStopWaypointType` adds the nullable column (expand only); a rule stored
   before has none and applies to a stop of any type until it is edited, when the editor asks for it.
5. **Screens.** The editor's Scope section gains **Activity of the stop**; the list gains the column (named from the
   waypoint types' lookup, "Any activity" for a stored rule without one, not sortable).

Test risk: the generator matching (type, fallback, two depots, deduplication, passed-over depot, regeneration) and the
migration are **Critical**; the rule service's validation and isolation **High**; the screens **Medium**.

### Trip type defaults (PO decisions 2026-10-29)

The PO asked that a trip type define the defaults of a new trip and that creating a trip preset them, also for "Create a
trip with this order". OpenTMS's `TransportFileType` had a default carrier (`DefaultTransportCompanyId`), departure and
arrival relation, `DefaultScheduledDepartureTime`, `DefaultStopCreationMode` and the first/last-stop flags; the PO's list
adds the carrier contact, vehicles, combination, drivers and the addresses. Decided in chat (2026-10-29): a **New** trip
on the trips list (superseding "no New" in "Trips list and editor"); in the trip-with-order prefill the **type's**
departure/arrival win over the order's first/last stop; the type also carries the stop composition mode and first/last
stop rule (design 59 said "default from the type" but the type never had them); and the **server fills** every empty
field of a new trip from its type. Extends flow 1 (order → trip) and the Planning trip lifecycle (design 59); behind the
existing features (Master data node of trip types, `Apps.Transport.Trips`), on by default in this run.

1. **The type's defaults** (`TransportTripType`, all optional): `DefaultCarrierRelationId?`, `DefaultCarrierContactPersonId?`
   (needs the carrier and must be one of its contacts); `DefaultVehicleCombinationId?` **or** `DefaultVehicleIds` (ordered,
   distinct, at most `MaxVehiclesPerTrip`), never both; `DefaultDriverIds` (ordered, distinct, at most `MaxDriversPerTrip`);
   `DefaultDepartureRelationId?`, `DefaultDepartureAddressId?` (needs the relation, must be one of its addresses),
   `DefaultDepartureTime` (`TimeOnly?`, local time at the departure address); `DefaultArrivalRelationId?`,
   `DefaultArrivalAddressId?`; `DefaultStopCompositionMode` (default `MergeByLocationAndActivity`) and
   `DefaultFirstLastStopRule` (default `None`). Master Data ids without a foreign key, validated on save through
   `IMasterDataReferenceLookup` in the tenant (another tenant's or an unknown id is `ReferenceNotFound` with the field); a
   contact of another relation than the carrier is `ReferenceNotFound` too, through the lookup's new
   `GetContactPersonsAsync` (id, relation, name; PO 2026-10-29: "a real check"). The id lists are stored so the SQLite tests run (a converter-backed
   column like `Attributes`, or child tables). Migration `PlanningTripTypeDefaults`, expand only.
2. **The server fills a new trip from its type** (`TransportTripCreateDto` through `TripOperations`, and the order's trip
   request on Confirm, which uses the same path), per group, only where the input leaves it empty: carrier null → the
   type's carrier and contact (a given carrier without a contact gets the type's contact only when it is the type's
   carrier); neither combination nor vehicles → the type's combination or vehicles; no drivers → the type's drivers;
   departure relation null → the type's departure relation and address (a given relation without an address gets the
   type's address only when it is the type's relation); arrival likewise; stop composition mode and first/last stop rule
   (become nullable in the create input) → the type's; execution → the type's (as today), or **Subcontracted** when the
   carrier came from the type (PO 2026-10-29). A default contact that is no longer the carrier's is skipped. The planned departure stays
   required and is never filled. A default the Master Data lookup no longer returns (archived or deleted since) is
   skipped, no error. Accepted consequence (PO): through the API an empty field cannot mean "deliberately none".
3. **Trip type editor** (Master data → Planning & Execution → Trips → Trip types): a tab **Defaults for new trips** with
   Carrier (relations with the carrier role) and Contact person (of that carrier), Vehicle combination or Vehicles (up to
   three), Drivers (up to three), Departure relation, address and time, Arrival relation and address, Stop composition
   mode and First/last stop rule. Every record picker has "Add new …" and the pencil (UI-LOOK-004/005), reusing the
   pickers of the trip page's resource dialogs.
4. **New trip** (Transport → Trips list, ribbon **New**, permission `PlanningExecution.TransportTrips.Create`): an
   `EntityDialog` with Trip type (the default type preselected), Trip date (today), Planned departure (date and time),
   Name, Execution, carrier and contact, combination or vehicles, drivers, departure and arrival (relation and address),
   stop composition mode and first/last stop rule; saved through `POST /api/planning/transport-trip`, then the trip page
   opens (legs are assigned there or on the Planboard later).
5. **Presets on screen** (New trip, and the Trip with order tab of an order and a template): choosing a type presets every
   field the type defines; switching to another type changes the fields that still hold the previous type's preset and
   keeps the fields the user changed. The departure time comes from the type (the date from the trip date, the order's
   first stop or the template's day offset). In the Trip with order tab the type's departure/arrival win; the order's
   first/last stop fills only what the type leaves empty; a stop's window start is the departure time only when the type
   has none.

Test risk: the server fill (both paths, every group, the "only when it is the type's" rules, a vanished default) and the
migration are **Critical**; the type's validation and tenant isolation **High**; the screens and presets **Medium**.

Build notes (2026-10-29):
1. The default drivers come back with their name only (the Master Data lookup returns no code); accepted by the PO.
2. A type switch on screen: carrier and contact switch as a pair, relation and address too (a replaced parent takes the
   new type's child); combination and vehicles switch together, only when both still hold the previous preset; drivers,
   departure time, execution, stop composition and first/last stop rule each on their own. The Tevens trip's date or day
   offset never switches. A saved request counts as holding its type's preset.
3. New trip: execution, stop composition and first/last stop rule are preset from the type too; the planned departure
   date follows the trip date until the user changes it; empty lists and enums are left to the server's fill. The
   type's departure time is combined with the date in the browser's time zone, as the trip page does.
4. The trip type editor does not preselect a relation's default address (a stored type would look changed on opening).
5. The shared pickers moved to `domains/master-data/components/` (`RelationFields`, `ResourceFields`) so the trip page,
   the Tevens trip tab, New trip and the trip type editor use one set.
6. The trip's own carrier command still checks only that the contact exists; the contact-of-carrier check is on the
   type's default.

### Order editor (superseded, see "Order page, legacy tab structure"; page with tabs, `useOpenRecord`; `EntityDialog size="xl"` for New)

Header: number or "New order", `StatusBadge`, ribbon per status (Save, Confirm (Draft), Approve ▾ (for invoicing /
archive), Revise, Cancel, Print ▾ (CMR, confirmation later), Close) with an unsaved-changes guard.

- **General** (`FormSection`s): Type (`ReferenceCombobox`, locked once numbered), Customer (`EntityLookup` relations with
  role Customer, "Add new…"), Contact (`ReferenceCombobox`), Order date (`DatePicker`), Customer reference, Organisation
  unit, Attributes (dynamic fields from the definitions), Internal remarks, **Stop pattern** (the default stops for new
  lines: `LinesGrid` with waypoint type, relation, address, date, time slot/window, reference).
- **Lines**: `LinesGrid` of lines (unit, product, quantity, positions, colli, weight, volume, loading metres, EPQ,
  distance, duration, CO₂ (stored), price (stored total of the line's rate lines)); inline edit of quantities; a side
  panel for the selected line (`FormSection`s: Cargo, Container, Documents/texts, Measurements, Attributes, and **Stops**
  as a `LinesGrid` with add/remove/move) — no nested modal (UX finding §6.3/6.4). Required fields per type marked.
- **Pricing**: the order's rate lines (`LinesGrid` grouped by line, then order-level lines: D&S, additional services,
  fixed price), with kind badge, quantity, unit price, amount, provenance tooltip (rule, bracket, column); actions: Add
  manual line, Add additional service, Adjust unit price (calculated lines), Remove (manual), Set fixed price; Financial
  terms (`FormSection`: currency, VAT, payment term, price lists `LinesGrid`, multiple matches, D&S multi-select with the
  defaults marked, documents with invoice); totals footer (before D&S, D&S, services, total) with
  `CalculatingIndicator`; **Preview** while editing an unsaved change (debounced `preview-prices`), replaced by the
  stored lines after save. A new order is previewed as it stands once its customer is chosen
  (`preview-new-order-prices`, Marinus 2026-10-30).
- **Planning**: legs of the order (`OpenTmsGrid`: position, origin → destination, planned dates, distance, duration,
  toll km, CO₂, trip number (link), allocated cost/revenue), editable leg fields; **Tevens trip** section: a switch
  "Create a trip with this order", then trip type, departure/arrival (relation + address), planned departure, and an
  "Advanced" disclosure for carrier, vehicles/combination, drivers, agreed purchase price; after Confirm the section
  shows the created trip as a link and is read-only (Q9).
- **Documents**: printed CMRs list (number, printed at, by, download) and Print CMR; attachments later.
- **Record identification** (`RecordIdentificationTab`).

### Templates

List (name, type, customer, recurrence, next date, active); editor = the order editor in template mode (Name instead of
number, **Schedule** section: recurrence, interval, weekdays / day of month, horizon, validity, active; stops as day
offsets); wizard **New from template / Copy order** (`Wizard`: Source (template or order) → Dates (per weekday over a
range, or a calendar with copies per day) → Details (editable reference, quantity, weight per generated order) →
Confirm with per-order progress).

### Trips list and editor

List columns: Number, Status, Date, Name, Type, Carrier, Vehicles, Drivers, Planned departure/arrival, Stops, Legs,
Customers, Orders, Distance, Load factor, CO₂, Cost, Revenue, Warnings (icon with tooltip), Archived (hidden). Filters:
view, date range, status, carrier, organisation unit, hide empty. Command bar: Print CMR, Archive (wizard), Cancel,
Complete, Export; **no New** (Planboard, PO) — the "Tevens trip" path creates trips from orders. Editor (page): ribbon
(Save, Start, Complete, Cancel, Archive/Unarchive, Print CMR); **General** (number, date, name, type, status, execution,
carrier + contact (read-only unless created from an order and Planned), vehicles/drivers (read-only, link to the
Planboard), departure/arrival, planned/actual times, distances, remarks, driver instructions, attributes); **Legs**
(`OpenTmsGrid` with inline reference/remarks/time edits, row actions Unassign, Carry out separately, Return leg,
Outsource origin; link to the order); **Stops** (`OpenTmsGrid`: position, activity, relation, address, planned/estimated/
actual times, cumulative distance, legs count, warnings; row actions Move up/down, Record actuals (dialog), Remove
(manual), Add manual stop); **Cost and revenue** (cost rate lines per leg and the trip lines with provenance, manual
line add, totals, allocation per leg table, `CalculatingIndicator`); **Documents**; **Record identification**.

### Documents view

Printed CMRs of the tenant: number, printed at, order, trip, lines, profile, by; download; search by order number.

### Master data lists (design-50 shape with `MasterDataListView`/`MasterDataEditor`)

CMR profiles (General: name, default, copies, borders, QR, group by leg; Boxes: one `Select` per party box, checkbox
groups for 6/13/18, fixed texts multi-select), CMR fixed texts (name, text, archived), Order attributes / Trip
attributes (target, key, name per language, position), Via depot rules (scope, relation/region, depot relation +
address, arrival/departure waypoint types, overnight, position), Price lists and Relation financial settings (design
57 §UI).

### Library components built in this run (component map)

- `Wizard` (`src/components/ui/wizard.tsx`): steps with validity, Back/Next/Finish, progress list for batch steps; the
  API the UI rule gives.
- `useOpenRecord` (`src/lib/use-open-record.ts`): opens a record route in the app, keeps the list's filters, handles
  "not found" and permission denied with `FeatureUnavailable`/`PermissionDenied`.
- Deviations (marked `DEVIATION(UI-XXX-nnn)` in code and listed in the PR for the PO's approval):
  - `UI-LOOK-004`, trip picker of "Outsource origin" (`TripLegsTab`): no "Add new …", a trip is never created in the
    Transport app (the Planboard creates trips, PO scope decision).
  - `UI-LOOK-004`, the cost-line "Leg" picker on the trip's Cost and revenue tab (`TripCostRevenueTab`): it picks one of
    the trip's own legs; legs derive from the orders' lines and are never created by hand.
  - `UI-LOOK-004`, the trip picker of "Add purchase line" (`ItemPurchasePrices`) and the target trip of "Split
    quantity" (`TripLegsTab`), "Order page review" 4 and 5 (2026-10-28): no "Add new …", for the same reason as
    "Outsource origin".
- "Order page review" 3 says "switch" for the per-basis rows; they were checkboxes, because UI-FORM-010 reserves a switch
  for a setting that takes effect without Save and the rows are saved with the order. Superseded the same day by the PO's
  request to compact the Financial tab (below): there is no per-row control any more, a row exists while its basis is
  charged.

### States

Loading skeletons; empty states with the primary action; error toasts via `getApiErrorMessage`; permission denied per
UI-PERM-002 (nodes hidden, actions disabled with reason); features off → node hidden, direct URL → `FeatureUnavailable`.
Optimistic updates: none (saves return at once; derived values arrive by push).

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Order editor: create/confirm flow, lines and stops editing, required marks, pricing tab (preview and stored lines), planning tab, Tevens trip section states | **Medium** | vitest with mocked client |
| Trip editor: legs/stops grids, actuals dialog, status ribbon | **Medium** | vitest |
| Wizards: template materialisation, print CMR, archive | **Medium** | vitest |
| Lists: columns, filters, views, command bar selection logic, calculating indicators | **Medium** | vitest |
| Features and permissions gating in nav and actions (55 + new nodes) | **High** | app-features tests updated |
| Localization nl/en parity for every new key | **Low** | key tests |
| "Order page review" 3: charged bases, open lines, approval on effective bases, relation defaults (money) | **Critical** | unit + SQLite + PostgreSQL through the derived-value job, tenant negatives |
| "Order page review" 4: an order's cost lines, manual purchase lines checked against the leg (money) | **Critical** | SQLite + PostgreSQL, tenant negatives, contract tests |
| Migrations `OrdersChargedBases`, `FinancialRevenueChargedBases` (every tenant database) | **Critical** | PostgreSQL migration tests (order, expand-only, old rows, old release's insert) |
| "Order page review" 5: split at an address and remove split (Planning commands) | **High** | SQLite service tests incl. refusals and tenant negatives |
| "Order page review" 2–5 screens: pencil, per-basis rows, purchase table, split dialogs | **Medium** | vitest with mocked client |

## Flag & rollout plan

Features of design 56, default on; the Transport tile shows when `Apps.Transport` is on (today's placeholder is replaced).

## Cost & SLO impact

Bundle: lazy chunks per app view (as the Master data app); grids virtualised; no new runtime services.

## Assumptions

1. `useOpenRecord` routes records as `/transport/orders/{id}` and `/transport/trips/{id}` (pages, not dialogs), the
   UI rule's intent; New uses a dialog for the minimum fields and then opens the page.
2. The order editor shows legs read from Planning and rate lines from Financial; it never computes totals itself.
3. The Planboard app stays a placeholder in this run; links to it are plain routes.

### Build notes (backend follow-ups, 2026-10-26)

The endpoints the screens asked for, as the generated client names them (details in the build notes of designs 57–59):

1. **Export** on the Orders and Trips lists: `POST /api/orders/transport-order/export` and `POST
   /api/planning/transport-trip/export` with the list's request plus `columns[] { field, header }` (the grid's column ids
   and headers, visible or all columns) and `allRows`; the answer is the xlsx file (`transport-orders-<date>.xlsx`,
   `transport-trips-<date>.xlsx`). Refusals: `ExportTooManyRows { max, count }`, `ExportUnknownColumn { field }`.
2. **Wizard "Details"**: `materialise` / `copy-order` take `overrides[]` per generated order (date, copy index, customer
   reference, per line quantity and weight); template lines now carry their `id`.
3. **Trip picker** for "Outsource origin": `GET /api/planning/transport-trip/picker` with `excludeTripId` = the leg's trip.
4. **Leg time windows**: `PUT /api/planning/leg/{id}` takes `originWindow` / `destinationWindow { from, till }`.
5. **Trip request warning**: `TransportOrderDto.tripRequest.warningCode / warningField / warningAt` and the row's
   `tripRequestWarningCode`; the text is the localized error code with `{field}`.
6. **Price lists**: the rules list takes `tiers[]` ("Overrides" = 1, 2, 3); Simulate's transport unit is optional.

### Build notes (frontend, last pass, 2026-10-26)

1. **Export** (UI-GRID-013): `GridExportDialog` (`components/ui`) on the Orders and Trips lists; OpenTmsGrid's Export
   button hands the page the list's request and its columns in display order (`buildExportContext`). Both server
   exports know `co2Kg`; only the Orders list's trip request warning icon is marked `context: { export: false }`.
2. **Wizard Details**: per generated order (date × copy) the customer reference and, in a LinesGrid, each line's
   quantity and weight; only values that differ from the source go out as `overrides[]` (per date, one call per date).
3. **Trip picker**: an EntityLookup over `/transport-trip/picker` without "Add new …":
   `DEVIATION(UI-LOOK-004)`, a trip is never created in the Transport app (no New, see "Trips list and editor").
4. **Leg windows**: one `LegDialog` for both tabs; a window is HH:mm on the end's planned date (the browser's zone,
   as the other time fields of the app), sent only when changed or when its date moved; an overnight window cannot be
   entered in this dialog.
5. **Concurrency on the order page**: the save never adopts a newer stamp for dirty sections when someone else
   changed the order's content (a 409 concurrency refusal, or a newer version a push brought in while dirty); the page
   offers Reload / Keep editing and blocks Save until a reload. A 409 while only derived values changed (the summary
   the recalculation writes changes the stamp) runs the step again once with the newest stamp.

## Security quickscan

No new client-side secrets; downloads through authenticated endpoints; permissions checked with `useIsGranted` for
actions and features for nodes; attribute keys rendered as text (no HTML).
