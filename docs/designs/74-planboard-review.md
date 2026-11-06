# Design 74: Planboard review — ten improvements after the first delivery

**PBI:** PO review of the Planboard run (design 66), 2026-11-05.
**Status:** approved (PO 2026-11-06; every choice below was put to the PO and is the PO's).
**Date:** 2026-11-06
**Depends on:** designs 49, 56, 58, 59, 61, 66–70.
**Input:** legacy OpenTMS read from code (`file-stops`, `stop-details-dialog`, `manage-legs`,
`split-freight-item-dialog`, `vehicle-planboard-gantt`).

## Domain impact

Modules: Planning & Execution, Orders (a new published interface and a narrow field update), Platform (Hangfire settings
only), frontend `apps/planboard`, `apps/transport`, shell. **No new tables.** Decisions: AD-07 (derived values), AD-16
(outbox). The save flow is **flow 2** (save without waiting); nothing here calculates in a read or a save.

The apps table changes by PO decision: the Planboard writes order and freight fields through Orders (point 6), so "Orders
read-only" (D6) no longer holds for those fields.

**Feature flags:** the PO decided that everything here is on at once, under the existing Planboard features
(`AppFeatures.PlanboardApp.*`, default on, D7). No new features. To be stated in the PR.

## 1. No left panel in the Planboard

`AppDefinition.hideNavPane?: boolean` in `apps.config.ts`, read in `AppShell`. The Planboard's `views` stay, because they
are the feature tree and the pane registry's source. Panes open through **Add pane**.

## 2. Stops as a timeline with a "now" marker

`StopsPane` shows a vertical timeline in place of the grid (no toggle; the legacy screen had none). Per stop:

- A time column: arrival above departure. One tier per stop, as legacy: **actual** (green check), else **estimated**
  (truck icon), else **planned** (no icon).
- A line with a marker. The part already driven is solid and brand-coloured, the part still to come dashed. Markers:
  done (filled), next (ring), later (empty).
- A card: relation (bold), activity, city, order numbers, the window, remarks, warnings (icon with tooltip, severity
  colour), and the state badge on time / at risk / late with the delay in minutes (the map's states, `stopState`).

**The now marker** (new; legacy has none) is a pure function in `panes/stops/model.ts`:

`nowPosition(stops, now)` returns where the trip is:
- before the first stop's arrival and no actuals: above the first stop ("not started");
- a stop with an actual arrival and no actual departure, or `now` between arrival and departure: **at** that stop;
- otherwise **between** the last stop with an actual departure (or, without actuals, the last whose planned/estimated
  departure has passed) and the next one, with a `fraction` 0..1 from the expected times for the marker's height;
- after the last stop's actual departure: past the end (no marker for a completed trip).

The marker shows the current time and the delay (expected against planned at the next stop). It re-renders once a minute.

Kept: drag to reorder (blocked once a stop has actuals), two-way selection with the map, Add stop, up/down, record actuals,
remove for manual stops, and live refresh. Click selects, double click opens the detail window, right click opens the menu.

## 3. Stop detail window

`StopDetailDialog` (an `EntityDialog`, in `domains/planning-execution/components/`, also opened from Transport's
`TripStopsTab`). Tabs:

- **General**, with a map on the right when the stop has coordinates.
  - Read-only: activity, address, contact details, planned and estimated times, distance.
  - Editable: **actual arrival and departure** (the existing `record-stop-actuals` rules), **remarks**, **planned duration
    in minutes**, and the **relation and address on a stop without legs**.
- **Legs**: the legs of the stop, read-only list with order, customer, origin, destination and quantity.

No Record tab (PO 2026-11-06): stops are rebuilt from the legs and have no audit of their own; the trip has its own.
Questionnaire and attachments are left out: they do not exist in this app (PO).

Backend: `UpdateStopDto` gains `RelationId?` and `RelationAddressId?`; changing them on a stop with legs is refused with a
business error. `TripStopDto` gains what the window needs (address and relation-level contact fields, through Master Data's
published address lookup, batched; additive). A change requests the recalculation; remarks alone do not.

## 4. Manage legs

`ManageLegsDialog` from Legs to schedule's row menu ("Add or modify legs"), permission `Legs.Update`. One block per leg of
the **order line**, in order, with a description "route from {first} to {last}".

Row fields: departure date, origin activity, origin relation and address, window, reference and remarks; the same for the
destination; distance and duration read-only (routing calculates them).

Rules (legacy made correct):
- The first row's origin and the last row's destination (activity, relation, address) are read-only.
- **A leg on a trip is read-only**, with a note naming the trip; so is a leg with actuals. Add and remove around them are
  refused when they would change that leg's ends.
- **Add** appends: the new row takes the old last row's destination, its origin type is the successor of that activity,
  its origin relation is empty, and the previous row's destination is cleared until a relation is chosen.
- **Remove** keeps at least one leg. First: the next takes its origin. Last: the previous takes its destination. Middle: the
  next takes its origin.
- **Auto-chaining:** a destination relation sets the next row's origin (relation, address, date, successor type); an origin
  relation sets the previous row's destination.
- Validated on save, on the server: the chain is contiguous (each origin equals the previous destination), dates do not
  run backwards, required activities and relations are present. Legacy only checked this in the client, and not properly.

Backend: `GET leg/line/{lineId}` and `PUT leg/line/{lineId}` (rows with `id` or none, and the line's concurrency: each
leg's stamp), one transaction. New legs and legs with another place or activity become `LegSource.Split` so the generator treats the line as reshaped
(design 59) and keeps them. Intermediate ends get synthetic stop ids (`Leg.SyntheticStopId`). Dates, windows, references and remarks do not reshape the line,
so it keeps following the order's quantities. Routing is requested per
new or changed-place leg through `IDerivedValueScheduler`.

## 5. Back to the Planboard after closing

`openRecord(type, id, { returnTo })`: a `returnTo` search parameter; `useCloseRecord` goes there instead of the list. Only
internal paths are honoured (starts with one `/`, not `//`), so it is no open redirect. The Planboard passes `/planboard`.
The selection survives the round trip: `PlanboardSelection` keeps `focusedTripId` and `selectedStopId` in `sessionStorage`
(wrapped in try/catch). The layout is already stored on the server.

## 6. Inline editing

| Pane | Columns | Through |
|---|---|---|
| Trips | carrier, vehicles, drivers (a popup lookup in the cell, in place of the dialog) next to name, remarks, driver instructions | existing resource commands |
| Legs, Legs to schedule | origin/destination reference, remarks, window from/till, planned dates | `PUT leg/{id}` |
| Legs, Legs to schedule | customer reference, transport unit, product, temperature (no order name: an order has none, PO 2026-11-06) | Orders, new |
| Orders | customer reference, internal remarks | Orders, new |

Orders gets `PUT /api/orders/planboard-order/{id}/field` and `PUT .../line/{lineId}/field` (a narrow field enum, concurrency
stamp, `Planboard` + `PlanboardApp.Orders` feature, `TransportOrders.Update` permission), publishing the normal facts
(`TransportOrderLinesChangedEto`). A leg on a completed or cancelled trip is read-only. Cells use the existing
`InlineTextCell` and a new popup lookup/date cell and patch the row from the response. Background jobs also move a stamp, so
on a 409 the cell refetches the row: if the field the user edited still has the value they saw, it retries once with the
fresh stamp; otherwise it shows the usual stale message.

`RefreshReshapedLine` in `LegGenerator` also takes over unit, product and temperature (not quantities), otherwise a freight
edit does not reach a reshaped line's legs.

## 7. Split line, as legacy

`SplitLineDialog` replaces `SplitLegLineDialog`: read-only unit, product, origin and destination; help text with the total;
two columns "Remaining" and "New line", each with an editable amount and read-only weight, colli, volume, loading meters,
load factor and EPQ, recalculating live (`splitQuantities` in a pure model, two decimals on screen). OK needs both amounts
above zero. (Legacy allowed a remaining amount of 0, which left an empty line: not copied.)

The server decides the quantities. `POST leg/{id}/split-order-line {quantity}` in Planning, one unit of work:

1. Orders' **new published interface** `IOrderLineSplitter.SplitAsync(orderId, lineId, quantity)`
   (`OpenTms.Orders.Published`): clones the line at the end of the order with new line and stop ids, pro-rata quantities and
   measurements (5 decimals, new = total − remaining, so the total stays exact), and publishes `TransportOrderLinesChangedEto`
   for both lines. Returns the new line id and the map old stop id → new stop id. This synchronous cross-module call is
   the documented exception of CLAUDE.md rule 3: it is what makes the split atomic.
2. The line's legs:
   - **Generator-owned line:** `OrderLegGeneration.RegenerateAsync` inside the same unit of work creates the new line's legs
     and updates the old legs' quantities (their ends are unchanged, so they stay on their trips). The new legs pair up with
     the old by position.
   - **Reshaped line:** every leg is cloned with its `Source`, ends mapped to the new stop ids (synthetic ends stay), quantities
     scaled; the originals scaled in place.
3. Every clone goes on its original's trip, **except the clone of the leg the planner chose**, which stays unplanned. Those
   trips are recomposed and recalculated. The clone also stays unplanned when the original is stuck in §8's sense (a
   completed or cancelled trip, or actuals on its stops).

Later delivery of the LinesChanged event changes nothing (idempotent). The Transport app's split (part of a leg, to another
trip) stays as it is.

## 8. Drag an order onto a trip

New; legacy had no such thing. Payload kind `order` (`application/x-opentms-order`, `orderId`) with a grip in the Orders
pane. Drop targets: a Trips row, a Scheduler bar, the map's trip card, and an empty Scheduler cell (a new trip, as point 10).

`GET planboard/{tripId}/assign-order-preview?orderId` returns the legs, `pulledFrom` (trips legs would leave, number and
count) and `stuck` (legs that stay: on a completed or cancelled trip, or with actuals on their stops). When `pulledFrom` is
not empty, one confirmation lists those trips; without it, no dialog. `POST planboard/{tripId}/assign-order {orderId,
concurrencyStamp, allowOutsideWindow}`: all legs except the stuck, leaving their old trips (recomposed), in one unit of
work. Returns the change plus `moved` and `stuck`. Stuck legs are named in a warning toast. An order with no movable leg is
a business error saying why. Empty cell: `create-trip` gains `orderId`.

## 9. The work after a save

Measured first (see *Measurements*). Changes:
- Hangfire: `PostgreSqlStorageOptions.QueuePollInterval` from the 15-second default to 1 second, configurable
  (`Hangfire:QueuePollInterval`), with `EnableLongPolling`; each hop in the chain waited on that poll.
- `SetFieldAsync` and other text-only edits (name, remarks, driver instructions) no longer recalculate the trip.
- `UnassignAsync` requests one calculation per affected trip, not one per leg.
- The frontend does not call `refreshBars` straight after a command with values it knows are old; it patches from the
  command's response and shows `CalculatingIndicator` until the notice arrives.

## 10. Legs dropped on the Scheduler

- On a bar: assign to that trip. On an empty cell: a new trip at that time on that resource. On an availability block: as
  an empty cell, after the existing warning.
- A bar the pane does not have in `barOf` is fetched by id instead of falling through to "create a trip".
- A drop that cannot proceed always says why (no permission, read-only trip, not over a row); no silent returns.
- More than one trip type: the type picker asks (legacy's `SelectTransportFileTypeDialog`), cancel undoes the drop. One type:
  no prompt.
- New trip's fields stay as design 68 (D11), plus arrival = departure + 2 hours when no route exists yet (legacy).
- A stale stamp on an assign (the calculator saved the trip meanwhile) is retried once with the fresh stamp: an assign does
  not overwrite anything the stamp protects. A 409 is only shown when the retry fails too.

## Measurements

To be filled in with before and after numbers from a local run (request end to the trip's `CalculatedAt` and the record
notice), in the PR.

## Out of scope

Questionnaire and attachments on stops; a stop's manual planned times (PO: read-only); inserting or reordering legs in Manage
legs (legacy appends only); renumbering of stops on a completed trip.

## Assumptions

No PROPOSED or OPEN architecture items are relied on.
