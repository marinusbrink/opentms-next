# Design 66: Planboard run (umbrella)

**PBI:** the PO's instruction of 2026-10-31 ("rebuild the current planboard app in OpenTMS NEXT … start building now and
don't stop until all functionality is addressed").
**Status:** approved. The PO accepted every recommendation D1–D21 of `docs/input/planboard-rebuild-assessment.md` on
2026-10-31; this run cites them as "PO decision D<n>".
**Date:** 2026-10-31
**Input:** `docs/input/planboard-rebuild-assessment.md` and its appendices `docs/input/planboard-assessment/scheduler.md`
(S-01…S-42), `panes.md` (P-01…P-52), `map-live-layout.md` (L-01…L-22). Those scenarios are the acceptance list.
**Part designs:** 67 (Platform: live record-type channel), 68 (Planning & Execution: Planboard reads and commands,
availability, route geometry, stop optimisation), 69 (Master Data: published resource query), 70 (Planboard app).

## Scope

The Planboard app at `/planboard` with eight panes in a dockable workspace (PO decision 2026-10-31):

| Pane | Reads from | Main actions |
|---|---|---|
| Transport trips | Planning | inline edits of name, remarks, driver instructions, carrier, vehicles, drivers; new trip; start, complete, cancel, archive; open the trip in the Transport app |
| Scheduler (several per user) | Planning (+ Master Data resources) | mode tractor, trailer or driver; move trips in time and between rows; drop legs on a row (new trip) or a bar (assign); new trip from an empty cell |
| Transport orders | Orders | read, open the order |
| Trip stops | Planning | of the selected trip: move a stop, remarks, record actuals |
| Legs | Planning | of the selected trip: unassign, carry out separately, references and remarks |
| Legs to schedule | Planning | paged unplanned legs, grouped; drag to a trip or a Scheduler row; split and return without a trip |
| Map | Planning | the selected trip's stops, route, toll, CO₂, window/lateness colours, ETAs; all trips of the range; unplanned legs nearby; two-way selection |
| Resources | Planning (+ Master Data drivers) | drivers with their availability and trips; manage unavailability and working times; drag a driver onto a trip |

Out of scope: Route42, route forecast, the FMS and driver-instruction wizards, e-mail from the trips pane, exchange
gateway, convert vehicle (D21); live vehicle positions (D17, AO-12 stays OPEN); team or shared layouts (D14);
importing absences from an HR system (D9).

## Architecture

- **Flows (`docs/architecture/02-flows.md`).** Flow 1 (command → fact → event): every Planboard action is a Planning
  command in one unit of work. Flow 2 (derived values → live update): totals, warnings, route, toll, CO₂ and geometry are
  stored derived values (AD-07, design 49), recalculated by `TripCalculator` after every change; the screens learn about
  changes through the record-updates hub. Flow 3 (reports from outside) is not extended.
- **Decisions relied on.** AD-07 (derived values, reads never calculate), AD-12 (routing behind one adapter, design 60),
  AD-20 "allocated values on legs" (design 59). AO-12 stays OPEN and is not touched (D17). AP-13 (Redis) stays PROPOSED:
  the API keeps one instance per revision (D5).
- **200 ms (D1).** p95 server time on a warm instance at 1,000 open trips, 5,000 unplanned legs, 300 vehicles and 300
  drivers per tenant, for every page read, row read and command commit. Follow-up work (calculation, routing,
  optimisation) runs in jobs. Exports and route previews are excluded. This is the Planboard's own budget, stricter than
  the department's 300/500 ms.
- **No Planning-owned read tables (PO 2026-10-31).** Reads are SQL projections over each module's own aggregate tables
  with indexes on the filter and sort columns; names owned by other modules come from one batched published lookup per
  page (in process) or from cached client lookups. Search/sort columns on the aggregate itself are allowed (D2): the
  trip's existing `SearchKey` stays. The route geometry is a derived value on the trip (D3).
- **Live updates (D4).** Design 67 adds a record-type channel to the hub; Planning notifies after every committed
  command, not only after calculations.
- **App mapping (D6).** The Planboard reads Planning & Execution, Orders and Master Data (read-only). `apps.config.ts`
  and the CLAUDE.md table change in this run.
- **Features (D7).** Every pane is a nav node and a feature: `Apps.Planboard.Trips`, `.Scheduler`, `.Orders`, `.Stops`,
  `.Legs`, `.LegsToSchedule`, `.Map`, `.Resources`, children of `Apps.Planboard`. Planning's trip and leg services accept
  the Transport **or** the Planboard feature. **Feature flags default ON** for this run (PO decision D7, an explicit
  deviation from constitution rule 4, stated in the PR).

## Components

- **Workspace:** dockview (MIT) (D14). Layout and per-pane settings in `platform."GridUserSettings"` under
  `planboard.layout` and `planboard.pane.<instanceId>`, per user.
- **Map:** MapLibre GL from npm, OpenFreeMap vector tiles with visible OSM attribution (D15); self-hosted PMTiles before
  go-live (rollout).
- **Scheduler component (D13).** The decision asked for a time-boxed comparison of Bryntum Scheduler Pro and DHTMLX
  Scheduler. Both trials need a vendor account (Bryntum's trial packages come from a private registry behind a sign-up;
  DHTMLX's free GPL edition has no timeline view), and this run may not create accounts. The build therefore uses the
  third candidate of GAP-11, **FullCalendar Scheduler (resource timeline, premium plugin)**, which runs fully in
  evaluation mode without an account (it shows a licence notice) and is behind one adapter component
  (`components/ui/resource-scheduler.tsx`), so that Bryntum or DHTMLX can replace it in one file after the PO's trial.
  **PO to confirm before go-live:** the component to buy; the evaluation licence does not allow production use.

## Test risk analysis

| Area | Risk class | Why |
|---|---|---|
| Platform record-type channel (67) | critical | Platform change; a wrong group name leaks another tenant's ids |
| Planning commands, availability, warnings (68) | high | tenant data, plan correctness |
| Master Data resource query (69) | high | tenant data, published contract |
| Planboard app (70) | medium | UI over tested endpoints |

Every new read gets a negative tenant-isolation test. The 200 ms budget gets a k6 script per endpoint
(`tests/load/planboard.js`) against the D1 volume; the nightly load run reports the p95.

## Flag & rollout plan

Features default ON (D7). Before go-live: buy the scheduler component (or swap it), self-host the PMTiles basemap,
run the k6 script on staging with the volume dataset.

## Cost & SLO impact

No new infrastructure. OpenFreeMap is free and keyless; self-hosted PMTiles cost Cloud Storage egress only. The record
channel sends ids only; at one instance per revision no backplane is needed.

## Assumptions

1. The trial component is acceptable for the PO's tests (PO 2026-10-31: "first implement so I can test properly").
2. A planner works in one tenant at a time; the channel never spans tenants.
