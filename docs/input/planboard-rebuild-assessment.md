<!-- Assessment for the Planboard run. Produced 2026-10-31 from the OpenTMS source at ~/dev/OpenTMS (branch Development,
commit 2cf2c1558) and from observing the production test tenant (app.opentms.nl, FiLogic demo, read-only). A cloud session
cannot read ~/dev/OpenTMS: the detailed facts live in the appendices in docs/input/planboard-assessment/. -->

# Planboard rebuild assessment

**Status:** decided. On 2026-10-31 the PO accepted every recommendation at D1–D21 (§3) as the PO's decision, including the
appendix questions §3 says are taken with their recommendation, and feature flags default ON for this run (D7; an
explicit PO deviation from constitution rule 4, to be stated in the PR). Nothing is designed or built yet. The designs
start after every open PR has been merged into `main`: umbrella design + part designs (§6) → build.

**Appendices** (path:line citations, F = fact read in code, I = inferred):

| File | Covers | Scenarios | Questions |
|---|---|---|---|
| [scheduler.md](planboard-assessment/scheduler.md) | Vehicle outline (2021) and vehicle Gantt (2024), interactions, backend, Resources pane, the three PO modes | S-01…S-42 | Q-01…Q-16 |
| [panes.md](planboard-assessment/panes.md) | Trips, Orders, Stops, Legs, Legs to schedule, the dialogs, coupling between panes | P-01…P-52 | Q1…Q17 |
| [map-live-layout.md](planboard-assessment/map-live-layout.md) | Map, live updates (SignalR), docking layout and settings, performance routes | L-01…L-22 | Q-01…Q-13 |

The appendices' own question ids are cited below as `S:Q-nn` (scheduler), `P:Qn` (panes) and `M:Q-nn` (map/live/layout).

## 1. What the run covers

### 1.1 PO decisions already taken (2026-10-31, in chat)

1. **Panes in scope:** Transport trips, Scheduler, Transport orders, Trip stops, Legs, Legs to schedule, Map, Resources
   (driver calendar). Out: Route42 average time per stop, route forecast, the FMS-sending and driver-instruction
   wizards.
2. **Scheduler.** The legacy "vehicle planboard" is renamed **Scheduler**. A user can open **several Scheduler panes**
   in one planboard; each runs in one of three modes: **tractor planning, trailer planning, driver planning**.
3. **Map.** When a trip is selected, the map shows at least what the legacy map shows, preferably more and smarter.
4. **Performance.** Every server call returns within **200 ms**. **Planning does not keep its own denormalised read
   tables** for the planboard: the PO calls that bad solution architecture.
5. **Components.** Paid UI components (Gantt/scheduler, docking) are allowed, but not bought now: the first build runs
   on a trial or free licence so the PO can test it; the purchase follows if it works well.
6. Standing decisions from earlier runs apply unchanged: the Planboard owns trip creation and vehicle/carrier
   assignment except "Trip with order" (PO 2026-10-25); design 56 assumption 2 says the Planboard reuses Planning's
   commands; designs 49 (derived values, live updates), 53 (trip types), 59 (legs, trips, stops), 60 (routing, CO₂) and
   61 (Transport app, incl. trip type defaults) are built.

### 1.2 What the legacy Planboard is

- A Smart WebComponents docking layout with one tab per pane; each pane can be open once; the layout is a user setting
  (`Filogic.User.Planboard.DockingProfile`). A board-wide date range (max. 92 days) and organisation-unit filter feed
  every pane ([panes.md §0](planboard-assessment/panes.md), [map-live-layout.md §3](planboard-assessment/map-live-layout.md)).
- **The vehicle Gantt is the current vehicle planboard** (2024, last changed 2026-09-22, library
  gantt-schedule-timeline-calendar 3.35.2, feature `App.VehiclePlanboardGanttAppFeature`). It switches between tractors
  and trailers, shows fleet KPIs (vehicles, trips, en route, empty, utilisation) and bars with trip number, date, driver,
  carrier, status and trailer. There is **no driver mode**. The outline (2021) is its predecessor; the two are mutually
  exclusive ([scheduler.md §1](planboard-assessment/scheduler.md)).
- **Map:** MapLibre GL on the CARTO Positron basemap with attribution switched off. For a selected trip it draws
  numbered stops by waypoint type, departure and arrival, the Valhalla truck route split into toll and toll-free
  sections, and a summary chip (observed: "266 km · 2u 50m · €51,31 · 255 km tol (96%) · 258,4 kg CO₂") that expands to
  the toll cost per toll section. Overlays show low-emission zones, temporarily closed areas and window times. The truck
  is shown only while the trip runs. There are no ETAs, no lateness and no planned-vs-actual, and clicking a marker does
  nothing ([map-live-layout.md §1](planboard-assessment/map-live-layout.md)).
- **Live updates:** one `PlanboardHub`, one group per tenant, two messages carrying full DTOs (`onTransportFilesUpdates`,
  `onLegPropertyUpdated`), built fire-and-forget after commit. There is no version check (an older echo can overwrite a
  newer one) and no refetch after a reconnect; the Gantt refreshes everything after 30 s as a safety net. The Stops and
  Orders panes are never updated live.

### 1.3 Legacy performance (the baseline for "200 ms")

Observed on the production test tenant (18 trips in range, warm instance, browser resource timing):

| Call | Observed |
|---|---|
| `TransportFile/GetAll` (trips pane) | 1,255 ms |
| `TransportFile/GetAll` (Gantt, one week) | 915 ms |
| `TransportFile/GetRoute` (map) | 1,653 ms |
| `TransportFile/GetAllLegs` (legs/stops of one trip) | 341–818 ms |
| `TransportFile/GetAllUnscheduledLegs` (legs to schedule) | 701 ms |
| `Vehicle/GetList` (Gantt rows) | 390 ms |

Causes found in code: the trip list runs about 27 SQL queries per page, loads whole surcharge and capability tables,
reads all calendar and sensor history, and computes cost, revenue and warnings on every read. Legs to schedule is not
paged. The map makes 4–5 database queries and one or two calls to the route function per selection, with no caching
([panes.md §1, §5](planboard-assessment/panes.md), [scheduler.md §3](planboard-assessment/scheduler.md),
[map-live-layout.md §4](planboard-assessment/map-live-layout.md)). **A rebuild that keeps this shape cannot meet 200 ms.**

### 1.4 What opentms-next already has

- Planning & Execution (design 59): legs with origin/destination snapshots (name, city, coordinates, windows) and stored
  derived values (distance, toll distance, CO₂, allocated cost/revenue); trips with vehicles, drivers, carrier, stops;
  the commands assign/unassign/move legs, stops, start/complete/cancel, split line, return leg, carry out separately,
  outsource origin; the trip list and the unplanned-leg list.
- Routing and CO₂ (design 60) can return a polyline, toll sections and CO₂ with a 30-day route cache; no calculator asks
  for geometry yet.
- Live updates (design 49): the record-updates hub pushes ids and versions per watched record (max. 200 ids), re-watches
  after a reconnect, and is fed only by the derived-value job.
- `/planboard` is a placeholder; there is no frontend for assign/move or the unplanned list.

## 2. How 200 ms is reached without Planning-owned read tables

Proposed architecture for the designs; each line is evaluated against the module rules in the appendices
([map-live-layout.md §4](planboard-assessment/map-live-layout.md) P-1…P-13).

1. **Reads touch only the owning module's own aggregate tables**, as SQL projections (no entity loading), with keyset
   paging, one count, and indexes on exactly the filter and sort columns each pane uses. The leg snapshots design 59
   already stores are part of the leg aggregate (decided, not a read table).
2. **Nothing is computed on read.** Totals, warnings, distance, toll, CO₂ and the **route geometry** (polyline + toll
   sections) are stored derived values on Planning's own rows under AD-07/design 49 (§3 D3). The map then reads one row.
3. **Names owned by other modules** (vehicles, drivers, relations, carriers, customers) are resolved from ids by
   **cached client-side lookups** (small, tenant-wide lists through TanStack Query, refreshed on change notices) or, where
   a pane needs a server-side join, by **one batched published-interface call per page** (in-process, no cross-schema
   join). Sorting and searching on such names is the one hard case (§3 D2).
4. **Narrow commands with slim results.** Each Planboard action (move in time, change tractor, assign legs, set
   drivers) is one command that commits the fact, returns only what changed (ids + concurrency stamps + the changed
   fields) and leaves recalculation to the derived-value job. The client applies optimistic updates.
5. **Pushes carry ids, never DTOs.** After every committed Planning command (not only after a calculation) the
   affected records are notified; a pane refetches exactly those rows by id. Versions prevent an older update from
   overwriting a newer one; a reconnect or window focus triggers a refetch.
6. **Measured, not assumed.** Every Planboard endpoint gets a k6 budget at the agreed volume (§3 D1), run against
   PostgreSQL with the volume dataset in `opentms-next-testdata`, and a reviewer check on the query plan (no sequential
   scan, no N+1).

## 3. Decisions of the PO

The appendices ask 46 questions; most are detail the designs can settle with their recommendation. The ones below
change scope or architecture. Each carries the recommendation; the appendix ids point to the detail. **PO 2026-10-31:
every recommendation below is decided as written** (designs cite them as "PO decision D<n>").

### Performance and architecture

| # | Question | Recommendation |
|---|---|---|
| **D1** | **What exactly is "200 ms"?** p95 or every call; server time or the browser round trip; warm instance only (staging scales to zero, a cold start takes seconds); at which volume (open trips and legs per day, vehicles and drivers of the largest tenant)? Mutations too (department budget 500 ms), exports, the first load of a pane over 92 days? (`M:Q-06`) | p95 server time on a warm instance at a named volume (proposal: 1,000 open trips, 5,000 unplanned legs, 300 vehicles, 300 drivers per tenant) for every page read, row read and command commit; follow-up work async; exports and route previews excluded. Recorded as the Planboard's own budget (stricter than the department's). |
| **D2** | **Do search/sort columns on Planning's own rows count as the forbidden read tables?** The trip already has a `SearchKey` holding carrier, vehicle, customer and order names (design 59), written in the same transaction. Sorting the trips pane by carrier name, or searching for a customer, needs such a column or a cross-module call per keystroke. (`M:Q-03`, `P:Q8`) | Allow a small named set of search/sort columns on the aggregate itself, refreshed on rename events; never a separate table; everything that is only displayed resolves through lookups (§2.3). |
| **D3** | **Store the route geometry on the trip** (polyline and toll sections as a derived value written by the trip calculator) so the map opens in one read? (`M:Q-02`) | Yes: a derived value on Planning's own row under AD-07. |
| **D4** | **Live channel.** The record-updates hub needs (a) notices after every Planning command, not only after calculations, and (b) a channel for rows that **enter** a pane: new trips, newly unplanned legs, a trip moved onto a row of my Scheduler. This is a Platform change (risk class critical). (`S:Q-09`, `P:Q4`, `M:Q-05`) | Yes: tenant groups per record type with routing hints (date, org unit, resource id), ids only; the client filters and refetches by id. |
| **D5** | **Multi-instance.** Production runs one API instance and has no Redis (AP-13 PROPOSED). Accept that for the first Planboard, or bring AP-13 in? (`M:Q-13`) | One instance plus reconnect/focus refetch for the first run; AP-13 before scaling out. |
| **D6** | **App mapping.** The Orders pane reads Orders and the Scheduler rows come from Master Data, but `apps.config.ts` maps the Planboard to Planning & Execution only. (`P:Q1`, `S:Q-02`) | Add Orders and Master Data (read-only) to the Planboard's mapping. The Scheduler reads through one Planning endpoint that composes rows and bars, plus a new published Master Data query "resources by kind and organisation unit, paged". |
| **D7** | **Feature gating.** Trip and leg endpoints require `Apps.Transport.Trips`; a tenant with the Planboard but without Transport trips gets 403s. Every Planboard pane is a nav node, and so a feature. (`P:Q15`, `S:Q-14`) | Each Planboard read and command accepts the Planboard feature **or** the Transport one; the eight panes get their own features (`Apps.Planboard.Trips`, `.Scheduler`, `.Orders`, `.Stops`, `.Legs`, `.LegsToSchedule`, `.Map`, `.Resources`) in the same change. Feature flags default ON as in the Transport run? (constitution rule 4 deviation, PO to confirm) |

### Scheduler

| # | Question | Recommendation |
|---|---|---|
| **D8** | **Bar unit.** Is a bar always the whole trip in every mode (moving it in time in one pane moves it in every pane), or do trailer/driver modes need part-of-trip segments (trailer coupled for part of a trip, driver change mid-trip)? (`S:Q-01`) | Whole trip in every mode; mid-trip changes stay the split/convert path. |
| **D9** | **Who owns availability?** Driver absences, working times and vehicle unavailability do not exist in the rebuild. Planning & Execution (design 48 already says so for vehicle calendars) or Master Data; an HR/leave import later? (`S:Q-03`) | Planning & Execution owns a `ResourceUnavailability` (resource kind + id, from–till, reason, recurrence) and a driver working-time pattern; Master Data keeps the resources; HR import later via Integrations. The Resources pane reads it. |
| **D10** | **Warn or block** on double booking, absence, working time, missing qualification (ADR, code 95), a blocked vehicle? Legacy checks nothing on drop. (`S:Q-04`, `S:Q-05`) | Warn: coded warnings on the trip, a visible conflict on the bar and a confirmation on drop; block only on status rules (no moving a completed trip). |
| **D11** | **Tractor change.** Legacy resets carrier, trailers, stationing relation and drivers to the new tractor's defaults, overwriting the planner's choices. (`S:Q-06`, `P:Q6`) | Fill only empty slots from the tractor's standing combination, default drivers and owner; never overwrite what the planner set. |
| **D12** | **Combinations.** A trip planned on a standing combination stores only the combination id. May Planning store the resolved vehicle ids on the trip (the fact "which vehicles drive"), so "trips of trailer X" is one indexed query? (`S:Q-13`, `S:Q-12`) | Yes. Trailer mode shows every vehicle of kind Trailer in any position ≥ 2; no fourth mode. |
| **D13** | **Scheduler component trial.** Which candidates and criteria? (`S:Q-16`) | A time-boxed spike with Bryntum Scheduler Pro and DHTMLX Scheduler (Syncfusion third) on: React 19, row virtualisation at the D1 volume, drag-in from the legs grid, several instances on one page, theming on `opentms-theme.css` tokens, keyboard access, licence cost. The build uses the winner under its trial licence; the trial terms forbid production use, so the purchase decision comes before go-live. |

### Panes, map and layout

| # | Question | Recommendation |
|---|---|---|
| **D14** | **Docking.** A user opens several Scheduler panes, so the layout must hold several instances of one pane type with their own settings. dockview (MIT), FlexLayout (MIT), or fixed resizable areas with tabs? (`M:Q-07`, `P:Q14`) | dockview: free, drag-docking, JSON layout, several Scheduler instances as plain panels. Layout and per-pane settings stored in the existing `platform."GridUserSettings"` under `planboard.layout` and `planboard.pane.<instanceId>`; per user only (`M:Q-08`). |
| **D15** | **Map tiles.** CARTO (legacy; terms to verify, attribution hidden), MapTiler/Stadia (free tier, paid above), OpenFreeMap (free, no key), or self-hosted Protomaps PMTiles? (`M:Q-01`) | OpenFreeMap for the first build, self-hosted PMTiles in Cloud Storage before go-live; visible OSM attribution; MapLibre GL from npm. |
| **D16** | **"Smarter map", first run.** Candidates ([map-live-layout.md §1](planboard-assessment/map-live-layout.md) M-1…M-12): two-way selection (marker ↔ stop row), colouring stops by window and lateness, planned ETA per stop, all trips of the range as a layer, unplanned legs near the selected trip (drop them on the trip), drag to reorder stops, zero-emission-zone warnings. (`M:Q-10`) | First run: two-way selection, window/lateness colouring, ETA per stop, all-trips layer, unplanned legs nearby. Second iteration: drag to reorder, zone warnings. |
| **D17** | **Vehicle positions (AO-12, OPEN).** The live truck on the map and the last-seen location on a Scheduler row need an owner for positions. (`M:Q-04`, `S:Q-10`) | Decide AO-12 for Planning & Execution ("realtime status, track & trace" is in its domain row). First run: no live positions; the Planboard designs list them under *Out of scope*. |
| **D18** | **Legs to schedule paging.** Legacy loads every unplanned leg of 92 days in one response and groups on the client. (`P:Q2`, `P:Q3`) | Page it server-side; grouping only on a whitelist of stored columns (origin date, customer, origin/destination city) with counts. |
| **D19** | **Inline edits from another domain.** Legacy edits order name/reference and freight unit/product/temperature from a leg row. (`P:Q5`) | Planboard panes edit only Planning-owned fields (trip: name, remarks, driver instructions, carrier, vehicles, drivers; leg: references, remarks, windows); Orders facts are edited on the order. |
| **D20** | **Stop optimisation** (VROOM, design 59 assumption 1): in this run, or the later routing run? (`P:Q10`) | In this run, as a background command whose result is pushed; "Calculate ETA" disappears because the calculator recomputes on every change. |
| **D21** | **Trip actions outside Planning** in the trips pane's context menu: print CMR, e-mail, convert vehicle, exchange gateway, FMS. (`P:Q13`) | Print CMR only if the transport-domain CMR design has landed; the rest stays out of this run. |

All other appendix questions (`S:Q-07/08/11/15`, `P:Q7/9/11/12/16/17`, `M:Q-09/11/12`) are taken with their
recommendation unless the PO says otherwise.

## 4. Requirements the designs carry (summary)

- **Scheduler:** rows by vehicle kind (tractor: Tractor/Rigid/Van; trailer: Trailer) or by driver; bars = trips by the
  mode's resource; a trip appears in every pane and row it uses; drag in time, between rows (changes only that mode's
  resource), drop legs from Legs to schedule onto a row (new trip) or onto a bar (assign); create from an empty cell with
  the trip type's defaults; per-pane range, zoom, filters and bar template; KPIs per pane; conflict and availability
  warnings; one consistent live stream ([scheduler.md §2, §5, §7](planboard-assessment/scheduler.md)).
- **Grids:** the five list panes on `OpenTmsGrid` with server-side paging, sorting and filters; the board-wide date range
  and organisation units with a per-pane opt-out; selection of one trip drives Stops, Legs and the map; multi-select is
  for bulk actions ([panes.md §6, §8](planboard-assessment/panes.md)).
- **Map:** everything in §1.2 plus the D16 additions, from stored geometry; OSM attribution visible.
- **Resources:** drivers (and non-primary vehicles with a role) with their availability; dropping a driver on a trip adds
  a driver position (max. 3) instead of replacing all drivers (`S:Q-15`).
- **Do not clone** the legacy bugs listed in [scheduler.md §9](planboard-assessment/scheduler.md) and
  [panes.md §8](planboard-assessment/panes.md), e.g. drag of completed trips, unauthorised mutations, the random
  loading-performance indicator, the unassign crash, the always-true optimise filter.

## 5. Dependencies and housekeeping

1. All open PRs merged into `main` before the designs start (PO).
2. `docs/architecture/decisions.md` uses the id **AD-20 twice** (e-mail send log, 2026-10-30; allocated values on legs,
   2026-10-25). One needs a new id in a PO-owned change.
3. A volume dataset for the D1 load test in `opentms-next-testdata` (trips, legs, vehicles, drivers at the agreed scale).
4. The full EF test suite already takes about 5 hours on the primary dev machine (transport run); Planboard query tests
   should run on the PostgreSQL module only.

## 6. Shape of the briefs (proposal)

Numbering continues after design 65 (tenant test datasets took 65).

| Design | Content |
|---|---|
| 66 | Planboard run umbrella: scope, D1–D21 as decided, flows, budgets, rollout |
| 67 | Platform: record-type channel on the record-updates hub (D4), risk class critical |
| 68 | Planning & Execution: Scheduler read endpoint, narrow commands, stored geometry, resource unavailability, combination resolution, stop optimisation |
| 69 | Master Data: published resources query (vehicles by kind, drivers, by organisation unit, paged) |
| 70 | Planboard app: workspace (dockview), the eight panes, map, Scheduler component |
