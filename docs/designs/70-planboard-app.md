# Design 70: Planboard app — workspace, eight panes, map, Scheduler

**PBI:** design 66 (Planboard run).
**Status:** approved (PO 2026-10-31: D6, D7, D13–D19 and the appendix questions taken with their recommendation).
**Date:** 2026-10-31
**Depends on:** designs 6 (grid), 49/67 (live updates), 61 (Transport app, trip editor), 68, 69.
**UI rules:** `docs/conventions/ui/ui-rules/` (index, page, grid, dialog, actions, feedback, keyboard, tokens, bespoke,
permissions, format). The workspace, the Scheduler and the map are bespoke surfaces (`bespoke.md`); they use tokens only.

## App, routes, features

- `/planboard` (one page, the workspace). `apps.config.ts`: `domains: ["PlanningExecution", "Orders", "MasterData"]`
  (D6), `tenantOnly: true` (like Transport), `views` = the eight panes as nav nodes, each with its feature
  `Apps.Planboard.<Pane>`; a nav node opens (or focuses) that pane in the workspace instead of routing.
- Panes the tenant lacks the feature for are not offered; a stored layout containing one shows `FeatureUnavailable` in
  that panel.

## Workspace (D14)

- **dockview** (`dockview-react`), themed with tokens (a `planboard-dockview.css` mapping dockview's CSS variables to
  `opentms-theme.css` tokens).
- Toolbar (`AppCommandBar`): **Add pane** menu (eight types; Scheduler can be added many times, the others once),
  **Reset layout**, the board-wide **date range** (≤ 92 days, default today … today + 6) and **organisation units**
  filter; every pane follows them unless its own "Use board filter" toggle is off (then it has its own range/OUs).
- Default layout: Trips (left top), Scheduler tractor (left bottom), Legs to schedule (right top), Map (right bottom),
  Stops and Legs as tabs next to Trips.
- Persistence: dockview's `toJSON()` debounced (1 s) to `GridUserSettings` key `planboard.layout`; each pane instance's
  settings (mode, filters, columns, zoom, bar fields) under `planboard.pane.<instanceId>`. A corrupt or outdated layout
  falls back to the default.
- **Selection bus** (`PlanboardSelectionContext`): `selectedTripIds` (trips pane, Scheduler bars, map markers),
  `selectedLegIds`, `focusedTripId` (the last single selection). Stops, Legs and the map's detail layer follow
  `focusedTripId` when exactly one trip is selected (P:Q16).
- **Drag and drop** between panes with a typed payload (`application/x-opentms-legs`, `…-driver`, `…-trip`): legs from
  Legs to schedule / Legs onto a trips-pane row, a Scheduler row or bar, or the map's selected trip; a driver from
  Resources onto a trip row or a bar.

## Live updates

`usePlanboardLiveUpdates`: `useRecordTypeUpdates("PlanningExecution.TransportTrip")`, `…Leg`, `…ResourceAvailability`
(design 67). Each pane decides from the hints whether the id concerns it (date range overlap, OU, resource in its rows,
`removed`) and refetches only those ids (`?ids=` / `scheduler/bars?ids=`), patching its rows; a reconnect or window focus
refetches the visible page. Own commands update optimistically and reconcile with the returned stamp.

## Panes

### Transport trips
`OpenTmsGrid` (server-side) on `GET /api/planning/transport-trip` with the board filters; columns from
`TransportTripRowDto` (number, name, status, type, date, departure, arrival, carrier, vehicles, drivers, customers,
orders, first/last city, stops, legs, load factor, loading metres, distance, CO₂, cost, revenue, warnings). Inline edit
of name, remarks, driver instructions (`trips/{id}/field`), carrier, vehicles, drivers (existing set commands). Row
actions: open (trip page in the Transport app, `useOpenRecord`), start, complete (dialog with ATD and ATA), cancel,
archive, optimise stops, delete (`ConfirmDialog`). Toolbar: New trip (`NewTripDialog` from the Transport app moved to
`domains/planning-execution/components` so both apps share it). Drop target for legs (assign) and drivers (add).

### Scheduler (several instances)
`components/ui/resource-scheduler.tsx`: the adapter over FullCalendar resource timeline (design 66), API:
`resources`, `events`, `backgroundEvents`, `range`, `zoom`, `onEventMove(eventId, newStart, newEnd, fromResourceId,
toResourceId)`, `onEventClick`, `onExternalDrop(resourceId, start, payload)`, `onSelectEmpty(resourceId, start, end)`,
`eventContent`, `readOnly(event)`. Feature code never imports FullCalendar directly.
- Pane header: mode switch (Tractor / Trailer / Driver), range (≤ 14 days, presets day/3 days/week/2 weeks; zoom hour /
  day), search, OU filter (board or own), bar-fields menu (whitelist: number, name, status, carrier, drivers, trailer,
  first→last city, load factor, warnings), KPI strip (rows, trips, in progress, empty rows, utilisation).
- Bars coloured by status token; warning icon with the localised warning list; unavailability and outside-working-time
  as background blocks; a bar in conflict (overlap on the same row, or over a block) gets the conflict outline.
- Interactions: drag in time → `reschedule`; drag to another row → `resource` (mode-specific); both at once → two
  commands in order. Before committing a drop that creates a conflict or lands on a block, a `ConfirmDialog` names the
  conflict (D10: warn, never block). Completed/cancelled bars are read-only. Click selects; double-click opens the trip.
  Empty-cell select → create trip (type picker defaulting to the first type). External drop of legs on a row → create
  trip with those legs; on a bar → assign.

### Transport orders
`OpenTmsGrid` on Orders' existing list endpoint with the board date range (planned date) and OU; read-only; open the
order (`useOpenRecord` into the Transport app). Legs of a selected order show in Legs to schedule's filter "this order".

### Trip stops
Lines of the focused trip (from `GET transport-trip/{id}`): position, activity, name, city, window, planned, estimated,
actual arrival/departure, lateness badge, remarks. Drag to reorder (`PUT stops/{stopId}` move), edit remarks, record
actuals (dialog), add/remove manual stop.

### Legs
Legs of the focused trip (`GET leg?tripId=`): order, customer, origin → destination, dates, windows, quantities,
references, remarks, distance, allocated cost/revenue. Actions: unassign, carry out separately, edit references/remarks
(Planning fields only, D19), drag to another trip (move legs).

### Legs to schedule
`OpenTmsGrid` server-side on the unplanned list with the board range and OU; grouping on the D18 whitelist with counts;
multi-select; drag source; row actions split line, return leg (design 68 §7), split at address (existing), open order.

### Map
MapLibre GL (`react-map-gl/maplibre`), OpenFreeMap "positron" style, visible attribution.
- Detail layer (focused trip): route from `RouteSegments` (toll segments in a distinct token colour, toll-free in the
  primary), straight dashed lines when no geometry; numbered stop markers coloured by state (done, on time, at risk,
  late: estimated arrival vs window), departure/arrival markers; a summary card (distance, duration, toll amount and toll
  km %, CO₂, per-segment toll list on expand) like legacy; popups with stop name, window, ETA, actuals.
- All-trips layer (D16): the range's trips as first→last lines, clickable (selects the trip).
- Unplanned-near layer (D16): toggle; legs near the focused trip as markers; drag a marker onto the trip card → assign.
- Two-way selection: clicking a stop marker selects the stop row in Trip stops and vice versa.
- Legend; zero-emission zones and closed areas are the second iteration (D16) and not shown.

### Resources
Driver list (search, paged) with a compact availability strip for the board range (blocks, trips as dots); select a
driver → side panel with blocks and working pattern (create/edit/delete unavailability in an `EntityDialog`, weekly
pattern editor); drag a driver onto a trip row or Scheduler bar → `drivers/add`. A Vehicles tab lists vehicles with the
same availability management (Maintenance, Inspection).

## Localisation

All strings in `PlanningExecution` resource (nl, en), app and pane names in Platform's `App:` keys.

## Manual

`docs/manual/planbord.md` (Dutch): the workspace, each pane, the Scheduler modes, availability.

## Test risk analysis (medium)

Vitest: selection bus, live-update filtering by hints, layout persistence fallback, scheduler adapter event mapping,
conflict detection, drop payload parsing, map stop colouring. Browser check of the main flows on a local tenant.

## Assumptions

1. The trip page stays in the Transport app; the Planboard opens it in place (Transport is on for every tenant with
   the Planboard today). Without the Transport feature the Planboard shows the trip in a read-only side panel.
