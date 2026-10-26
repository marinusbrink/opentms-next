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

### Order page, revised (PO decision 2026-10-26, replaces "Order editor" below)

The PO judged the first order page less usable than the legacy screen: the order → freight item hierarchy was split over
order-wide tabs (Lines, Pricing, Planning), the order-level stop pattern made no sense once an order's legs can go to
different trips, and entry was too slow for manual data entry. Binding from now on (the backend and the data model stay;
"line" in the API is "freight item" on screen):

1. **One page, no top-level tabs for the order's content.** From top to bottom: the header (number, status, ribbon;
   customer, order type, order date, customer reference, contact, organisation unit, remarks behind a disclosure),
   **Freight items**, **Totals**, then the order-level sections Financial terms, Tevens trip, References and
   attributes, Documents, Record identification (collapsible `FormSection`s, not tabs).
2. **Freight items** is a list (`LinesGrid`): position, from → to (first and last stop: relation, city), date/time,
   transport unit, quantity, weight, reference, price (the item's stored rate-line total with `CalculatingIndicator`),
   trip (number link when its legs are on a trip). Selecting an item shows its detail **inline under the list** with
   three tabs of its own:
   - **Freight details**: cargo and quantities first (unit, quantity, positions, colli, weight, volume, loading
     metres, EPQ, load factor, product, product relation code, vehicle type, temperature, required characteristics,
     payment instruction, barcode), then collapsible Container, Measurements, Texts and documents, Attributes.
   - **Stops and legs**: the item's stops (waypoint type, relation + address (default address preselected), contact,
     date, time slot or window, duration, reference, remarks; add, remove, move) and below them the item's **legs**
     generated from the stops (origin → destination, dates, distance, CO₂, trip link, allocated cost/revenue;
     editable leg fields and the leg operations as before). Before Confirm the legs part says they are created on
     Confirm.
   - **Pricing**: the item's rate lines (kind badge, provenance, quantity, unit price, amount; add manual line,
     adjust unit price, remove manual) and the item total; the unsaved-change preview applies per item.
3. **Fast entry row** under the list (keyboard first): From (relation), To (relation), date/time, transport unit,
   quantity, weight, reference. The relation's default (or only) address is taken; From/To get the order type's first
   and last default waypoint type (Load/Unload); Tab moves on, **Enter adds the item** and keeps From, date and unit for
   the next one; an unknown relation or unit offers "Add new …" from the keyboard. Extra stops, cargo and container are
   edited in the item's detail. **Paste**: rows copied from Excel (tab-separated, columns in the entry row's order;
   relations and units matched on code or name) become items after a preview that marks unmatched cells.
4. **No stop pattern on the order.** Stops exist only per freight item. The order type's default waypoints only
   default the activities of a new item (entry row); the template keeps its stops per item as day offsets.
5. **New order opens the page directly** (no New dialog): order type = the default type, order date = today, focus on
   Customer; the financial defaults are copied when the customer is chosen (unchanged rule). Shortcuts: Ctrl/⌘+S
   save, Ctrl/⌘+Enter confirm (Draft), Esc closes the item detail.
6. **Totals** (read-only): freight items, order-level lines (fixed price, additional services, D&S), total, with
   `CalculatingIndicator`; the order-level rate lines (manual, additional service, fixed price, D&S) are managed in
   the Financial terms section.
7. **Tevens trip** (order-level section): as before, plus **which freight items go on the trip** (checkbox per item,
   default all; backend `tripRequest.lineIds`, null = all). Only the legs of the chosen items are put on the trip on
   Confirm; the other legs stay unplanned for the Planboard. The trip's stops follow from its legs.

The section "Order editor" below is superseded by this one; templates use the same page in template mode.

### Order editor (superseded, see "Order page, revised"; page with tabs, `useOpenRecord`; `EntityDialog size="xl"` for New)

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
