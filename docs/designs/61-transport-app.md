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
     unit price, remove manual) and the item total; an unsaved change shows the price preview.
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
  stored lines after save.
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
