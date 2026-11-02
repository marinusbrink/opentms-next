<!-- Appendix to the Planboard rebuild assessment. Produced 2026-10-31 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 2cf2c1558) and, for the route function, ~/dev/micro-services-valhalla; not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in those checkouts, or in this repository where the path starts with backend/, frontend/ or docs/. F = fact read in code, I = inferred. Nothing was built or run. -->

# Legacy OpenTMS assessment: Planboard map, live updates, layout and performance

Source roots: Angular `~/dev/OpenTMS/angular/src/app` (paths below start at `planboard/`, `map/`, `services/`, `toolbar/`, `global/`);
backend `~/dev/OpenTMS/aspnet-core/src` (paths start at `FiLogic.`). `TFAS` = `FiLogic.Application/BusinessServices/TransportFile/TransportFileAppService.cs`,
`PMS` = `services/planboard-map.service.ts`, `RC` = `FiLogic.Application/BusinessServices/TransportFile/Calculators/TransportFileRouteCalculator.cs`.

PO decisions this appendix works against: panes kept = Transport trips, Scheduler (the vehicle planboard, several
instances per user, modes tractor/trailer/driver), Transport orders, Trip stops, Legs, Legs to schedule, Map, Resources.
The map shows at least what legacy shows for a selected trip, "and maybe even more and smarter". Every server call
within 200 ms; Planning keeps no denormalised read tables of its own. Paid components allowed later; the first build
runs on trial/free.

---

## 1. Map pane

### 1.1 Library, tiles, keys and cost

| Item | Legacy | Line |
|---|---|---|
| Engine | **MapLibre GL JS 4.7.1**, loaded as a global script from unpkg (not an npm dependency); the code talks to `declare var maplibregl: any` | F `angular/src/index.html:35-36`, `PMS:9` |
| Basemap | **CARTO Positron** vector style `https://basemaps.cartocdn.com/gl/positron-gl-style/style.json`; no key | F `PMS:22`, `PMS:58-64` |
| Attribution | switched off (`attributionControl: false`) | F `PMS:63` |
| Other screens | Google Maps JS API with tenant key `App.GoogleCloud.MapsJavaScriptApiKey` (stops dialog, relations dialog, order request, the non-planboard "arrowline" map) | F `global/global-definitions.ts:34`, `services/scripts.service.ts:22`, `map/map.component.ts:170-174`, `PMS:16-17` |
| Map component | one shared `filogic-map` component, `[isPlanboard]="true"` selects the MapLibre engine | F `planboard/planboard-tab-group/planboard-tab-group.component.html:50-56`, `map/map.component.ts:167-174` |
| Route engine | the "route function": a Go Cloud Function in front of a Valhalla Cloud Run service (`POST {RouteFunction:Url}/api/v1/route`), default URL hard-coded to `europe-west4-filogic-opentms.cloudfunctions.net/prod-filogic-services-route` | F `RC:65-66`, `RC:560`; `~/dev/micro-services-valhalla/micro-services-valhalla/README.md:1-4,163-176` |

Cost and licence notes:
- The route function costs about €150/month always-on (Valhalla `min-instances=1`, 16 GiB) (F `micro-services-valhalla/README.md:171-176`; design 60 `docs/designs/60-integrations-routing-and-co2.md:98`).
- CARTO basemaps carry no key but have usage terms that require attribution and restrict commercial use without a CARTO
  licence (I: from CARTO's published terms, verify before reuse). Legacy hides the attribution (`PMS:63`), which OSM's
  ODbL also requires: a licence risk the rebuild must not copy (I).
- Environmental zones are a static GeoJSON asset bundled with the app (`assets/GeoJson/environmentalZones.json`) (F `PMS:542`).

### 1.2 What is drawn when a trip (transport file) is selected

The selection in the Transport files grid drives the map; the vehicle planboard selection also lands there through
`selectFile` (F `planboard/planboard.service.ts:49-51`). For each newly selected file the grid calls
`GET TransportFile/GetRoute(id)` and the map renders the returned route list (F `planboard/transport-files/transport-files.component.ts:1074-1085,1087-1160`;
`map/map.component.ts:127-134,235-247`). Rendering is synchronous; nothing is fetched by the map itself.

| Element | How it is drawn | Line |
|---|---|---|
| Route geometry (normal) | the route function's encoded polyline (precision 6), decoded client-side; with the toll overlay on: the whole route as a **green** line (3 px, 0.8 opacity), toll sections on top in the route colour (4 px) with repeating **€ glyphs** and a numbered badge at the start of each toll section; with the toll overlay off: one line in the route colour (4 px) | F `PMS:165-166,220-267,279-302` |
| Route geometry (fallback 1) | `pathDto.geometry` (backend OSRM geometry) drawn solid — but the planboard builds every route with `pathDto: null`, so this branch is dead on the planboard | F `PMS:167-169,317-333`; `transport-files.component.ts:1132` |
| Route geometry (fallback 2) | schematic **dashed straight lines** departure → stops → arrival when no geometry came back (route function down, < 2 known points) | F `PMS:170-173,364-387` |
| Truck fallback | Valhalla "no path" (442) for the truck profile → retry without vehicle; geometry kept, toll and CO₂ dropped | F `RC:476-501` |
| Departure | plain round marker `pb-marker-departure`, tooltip "Departure" | F `PMS:390-394,425-430` |
| Stops | round marker numbered **1..n in stop order**, background = the waypoint type's `DisplayColor` (default `#9c36b5`), tooltip "Stop n" | F `PMS:396-403,440-447`; stop order `RC:98-112` (`orderby stop.OrderNumber`) |
| Arrival | `place` material icon, bottom-anchored | F `PMS:405-409,432-438` |
| Vehicle position | pulsing truck marker with tooltip "name · speed km/h · location"; **only while the file is in process** (actual departure set, actual arrival not); newest FMS device location, else the vehicle's last-seen coordinates | F `PMS:411-417,449-464`; `RC:385-422` |
| Colours | per route from a palette starting with the tenant theme colour (`--filogic-base-color`), then six fixed colours; the backend's own leg colour is ignored on purpose | F `PMS:116-144` |
| Hover popups | route line → distance, duration, toll km, toll €, CO₂ (toll/CO₂ rows follow the overlay toggles); toll section → cost, distance, duration, rate/km; zone → type, town, valid from | F `PMS:353-362,621-672` |
| Route info strip | glass strip under the map: one route → km, duration, toll € and toll km (%), CO₂ kg; several routes → totals; expandable sheet per route with vehicle name and a bar per toll section; "route function unavailable" warning | F `map/map.component.html:14-109`, `map/map.component.ts:249-282` |
| Legend | departure / waypoint / arrival, environmental zone, temporary restricted areas, window times; visibility saved per user (`Filogic.User.Planboard.Map.Legend.Visibility`) | F `map/map.component.html:117-126`, `map/map.component.ts:176-178,303-319` |
| Overlays (toolbar "traffic layers") | toll costs, carbon footprint, zero-emission/environmental zones (fixed ids), plus route constraints (road works, restricted areas, as lines or filled polygons) fetched for the trip date range | F `map/map.component.ts:69-72,180-214,322-361`; `PMS:466-581` |
| Fit | `fitBounds` over everything drawn, max zoom 12 | F `PMS:178-180` |

What is **not** drawn: no ETA per stop, no time windows, no lateness, no planned-vs-actual track, no legs of the trip as
separate lines, no unplanned legs nearby, no other vehicles (F: none of these appear in `PMS` or `map.component.*`). The
`route-forecast` ETA bar is wired only into the legacy Google flow (`map/map.component.ts:173`), so on the planboard it
never fills and stays hidden (I, from `map/map.component.html:128-131` and `PMS` having no forecast reference).

### 1.3 Interactions

| Interaction | Behaviour | Line |
|---|---|---|
| Several trips | multi-select in the grid → one route per file, each its own palette colour, totals in the strip | F `transport-files.component.ts:1052-1085`; `map/map.component.ts:92-107` |
| Legs to schedule on the map | yes: selecting rows in Legs to schedule adds a route per leg (origin → destination, default 40 t Euro VI truck, toll and CO₂) next to the trip routes | F `planboard/freight-to-schedule/freight-to-schedule.component.ts:517-573`; `RC:271-307,455-459` |
| Click a marker | nothing: markers have a `title` tooltip and no click handler; the map never selects anything back | F `PMS:420-423` |
| Stop selected elsewhere → bounce | `highlightSelectedStop` is subscribed by the map but **never emitted** anywhere: dead feature | F `map/map.component.ts:136-138`; grep: no `.next` call |
| Drag / reorder on the map | none | F |
| Expand / maximise | toggle saved as `Filogic.User.Planboard.Map.ShowMaximized` | F `map/map.component.ts:145,298-301` |
| Hidden map tab | selection while the map tab is not active only sets `isMapTabDirty`; activating the tab replays | F `transport-files.component.ts:1076-1079`; `planboard/planboard.component.ts:262-271` |
| Refresh after an edit | only when the Stops tab reports `transportFileStopsRefreshed` (re-fetch of every selected route); the SignalR echo does **not** refresh the map, so a vehicle position or a stop change by a colleague stays stale until reselect | F `transport-files.component.ts:782-790`; `transport/file-stops/file-stops.component.ts:324`; map has no hub subscription |

### 1.4 Data calls and their cost

`GetRoute(transportFileId)` (`TFAS:1043-1053`, `RC:182-268`), non-transactional unit of work:

1. trip row (compiled query, indexed by id) — `RC:83-96,189`;
2. stops with waypoint type, in order (compiled) — `RC:98-112,196-199`;
3. name + visiting-address coordinates of every involved relation in one set-based query (not compilable) — `RC:325-359`;
4. vehicle row (compiled) and, when in process, the newest FMS device location (compiled, `ORDER BY Time DESC LIMIT 1`) — `RC:114-143,241-253,385-422`;
5. **one route-function call** (10 s timeout), **a second one** on a truck "no path" — `RC:47,474-488,556-578`.

Stage timings `t1..t6` are returned to the browser in `Diagnostics` (F `RC:265`), but no measured numbers are recorded in
code. **Nothing is cached**: every selection of a file recomputes the Valhalla route, toll and CO₂ (F: no cache in `RC`);
client-side the route objects are kept only for the current selection (F `transport-files.component.ts:1089-1095`). The
grid fires one `GetRoute` per newly selected id and joins them with `zip`, so the map waits for the slowest (F
`transport-files.component.ts:1096-1110`). Legs to schedule: `GetTransportOrderFreightLegRoute(legId)`, same pattern
(F `TFAS:1056-1065`, `RC:271-307`). Route constraints: `RouteConstraint/GetAll(layerIds, from, till)` once per layer change
(F `map/map.component.ts:353-361`). I: the DB stages are single-digit milliseconds each (compiled, indexed); the route
function dominates (hundreds of ms warm, seconds cold, 10 s worst case), so the legacy map is **not** inside 200 ms.

### 1.5 What the new system has today

- Trip stops already carry `Latitude/Longitude/TimeZoneId` on Planning's own `TripStop` (F `docs/designs/59-planning-legs-and-trips.md:213`),
  so markers need no Master Data call.
- Integrations' `IRoutingService.GetRouteAsync` can return an `EncodedPolyline`, toll and CO₂ (`RoutingOptions(IncludeGeometry, IncludeToll, IncludeCo2)`),
  with a tenant-scoped 30-day `RouteCache` that stores the polyline only when asked (F `docs/designs/60-integrations-routing-and-co2.md:14-23,45-50`).
- No calculator asks for geometry yet: legs ask toll only, trips a distance matrix and a CO₂ estimate (F
  `backend/modules/planning-execution/OpenTms.PlanningExecution.Application/Legs/LegRouteCalculator.cs:82`,
  `.../TransportTrips/TripCalculator.cs:158,317`). No map component, no MapLibre dependency in `frontend/package.json` (F).
- Vehicle positions do not exist; which domain owns them is **OPEN (AO-12)** (F `docs/architecture/decisions.md:74`, `docs/architecture/07-ingest.md:55-68`).
- Component map GAP-12: "keep MapLibre GL via npm (`react-map-gl`); a decision for the planboard design" (F `docs/conventions/ui/component-map.md:109`).

### 1.6 Smarter map — options

Parity first: markers numbered in stop order coloured by waypoint type, departure/arrival, truck-routed geometry, toll
sections, CO₂, zones, road-work constraints, multi-trip colours, unplanned legs as routes, route strip. Then:

| # | Improvement | What it needs | Decision / owner |
|---|---|---|---|
| M-1 | **Stored trip geometry** so the map opens in < 200 ms: `TripCalculator` asks `IncludeGeometry` and stores the polyline (and toll sections) on the trip as a derived value (AD-07), refreshed by the existing derived-value job; the map reads it with the trip | one `jsonb`/text column on Planning's trip (its own fact-derived value, not a read table); calculator change; geometry ≈ 5–30 KB per trip (I) | Planning design; AD-07 already decided |
| M-2 | **Click marker or route → select** the stop in Trip stops / the trip in the grid (two-way selection, legacy has none) | a shared selection store in the planboard shell | Planboard design |
| M-3 | **Time-window and lateness colouring** of stop markers: green on time, amber within margin, red outside window / late; window in the popup | stop `WindowFrom/Till` come from the leg snapshots (F `59…md:22`), `EstimatedArrival` on `TripStop` (F `59…md:75`); until the ingest run ETA = planned (F `59…md:187`) | margin per tenant setting (PO) |
| M-4 | **ETA per stop and remaining km** in the popup and strip | `Estimated*` and `PlannedCumulativeDistanceM` on `TripStop` (F `59…md:75`) | none for planned; live ETA needs positions (**AO-12**) |
| M-5 | **Live vehicle position** of the selected trip (and optional fleet layer), moving without reselect | a position store and push (one message per vehicle per n seconds, thinned); positions are personal data when one driver drives (F `07-ingest.md:55`) | **AO-12** (owner), ingest role (AD-11), privacy answer |
| M-6 | **Planned vs actual**: planned polyline next to the driven track (breadcrumb, map-matched with Valhalla `trace_route`) and actual arrival times per stop | positions history (**AO-12**); `Actual*` on `TripStop` (F `59…md:75`); Valhalla map matching in the route function (I) | AO-12; Integrations contract extension |
| M-7 | **Unplanned legs nearby**: show Legs to schedule within x km of the trip's corridor or end point, clustered, drag a leg onto the trip | unplanned legs carry origin/destination coordinates in Planning's leg snapshots (F `59…md:22`); a bounding-box query on Planning's own leg table (index on coordinates or PostGIS) | Planboard design; assignment command exists (design 59) |
| M-8 | **Clustering and many trips**: show every trip of the date range as light lines/markers, cluster at low zoom | MapLibre GeoJSON source clustering (built in) or deck.gl (MIT) for > 10k features (I) | none |
| M-9 | **Drag to reorder stops** on the map (or drag a stop number) → `MoveStop` | `MoveStop { stopId, position }` exists and refuses stops with actuals (F `59…md:88`); route refresh through the derived-value job, `CalculatingIndicator` meanwhile (design 49) | Planboard design |
| M-10 | **What-if route preview**: before dropping a leg, show the added km/duration/toll | a synchronous `IRoutingService` call is a request-path exception to "every call from a job" (F `60…md:98-99`) and to 200 ms; use the cached matrix only | PO (200 ms vs live preview) |
| M-11 | **Zero-emission zone check**: warn when a stop lies in a ZE zone and the vehicle is not zero-emission | zone polygons as data (legacy: static asset `PMS:542`), vehicle fuel type from Master Data (design 48) | who maintains the zone set (PO) |
| M-12 | **Live push for the map**: re-render on "trip changed" from the record-updates hub instead of only on reselect | design 49 hub, see §2.5 | none |

---

## 2. Live updates

### 2.1 Legacy PlanboardHub

| Item | Fact | Line |
|---|---|---|
| Hub | `PlanboardHub : OnlineClientHubBase`, `[AbpAuthorize(PlanboardApp)]`, mapped at `~/signalr-planboard` | F `FiLogic.Application/BusinessServices/Planboard/PlanboardHub.cs:16-22`; `FiLogic.Web.Host/Startup/Startup.cs:399` |
| Groups | one group per tenant, `PlanboardHub_Tenant_{tenantId}`, joined in `OnConnectedAsync`; a host user (null tenant) joins nothing | F `PlanboardHub.cs:27-32,34-53` |
| Messages | `onTransportFilesUpdates(GetAllTransportFileDto[])` and `onLegPropertyUpdated(GetAllTransportFileLegListDto[])` — **full DTOs**, not ids | F `planboard/planboard-signalr.service.ts:9-12,70-81` |
| Redis backplane | when `Abp:RedisCache:IsEnabled`: `AddSignalR().AddStackExchangeRedis(...)` with channel prefix `FiLogic-SignalR`; else in-memory | F `Startup.cs:122-175` |
| Transport | WebSockets only, `skipNegotiation: true`, because Cloud Run multi-instance has no sticky sessions; token in the query string (`enc_auth_token`) | F `planboard-signalr.service.ts:34-45,60-68` |
| Reconnect | automatic reconnect with delays 0.5 s … 30 s (10 tries); a start loop with the same delays; on a 404 (stale connection id) up to 3 fresh connections; then a "live updates unavailable" toast | F `planboard-signalr.service.ts:14-25,46-53,111-158` |
| After reconnect | **no refetch**: there is no `onreconnected` handler, so everything broadcast while disconnected is lost until a manual refresh (or the gantt's safety net, which only runs after an own mutation) | F `planboard-signalr.service.ts` (no handler) |
| Metrics | an up/down counter of active SignalR connections per hub with tenant/user tags | F `FiLogic.Application/Metrics/ApplicationMetrics.cs:40-75` |

### 2.2 Who sends what: slim and fat echo

The commit `8b000ac67` "perf: planboard-mutaties optimistisch + zware naketen naar achtergrond" (2026-07-21) made
mutation endpoints answer right after the core commit; the heavy follow-up chain (ETAs, routes, invoices, purchase
prices, attribute sync, carbon) runs deferred and ends with a SignalR echo (F `git show 8b000ac67`).

| Sender | Payload | When | Line |
|---|---|---|---|
| `PlanboardEchoBroadcaster.Queue` (**fat**) | rebuilds `GetAllTransportFileDto` through the full `GetAllTransportFileDtoQuerySelector` in a fresh scope and unit of work, then sends to the tenant group (originator included) | registered on `CurrentUnitOfWork.Completed` (after commit), fire-and-forget `Task.Run` | F `FiLogic.Application/BusinessServices/VehiclePlanboard/PlanboardEchoBroadcaster.cs:21-37,80-124` |
| `PlanboardEchoBroadcaster.BroadcastPrebuilt` (**slim**) | an already built slim DTO (number, name, times, vehicles) | only for a **new** file created by a drop on the gantt; skipped for existing files because a slim DTO "would temporarily blank the enriched fields" | F `PlanboardEchoBroadcaster.cs:49-72`; `FiLogic.Application/BusinessServices/VehiclePlanboard/VehiclePlanboardAppService.cs:355-374` |
| Time/vehicle changes from the gantt | fat echo after commit, the endpoint returns an empty list | F `VehiclePlanboardAppService.cs:69-98` |
| Leg (un)assignment | `DeferPostProcessing = true`; `PostProcessLegAssignment` ends with the fat echo | F `FiLogic.Application/BusinessServices/Planboard/PlanboardAppService.cs:47-58`; `TFAS:2270-2292` |
| Highlight colour | fat echo after commit; the originator already patched locally (`transportFileLocalPatch`) | F `TFAS:365-384`; `planboard/planboard.service.ts:83-91` |
| File property edits (name, remarks, driver instructions, carrier, drivers, vehicles) | synchronous: own `RequiresNew` unit of work, then the returned DTO is broadcast | F `PlanboardAppService.cs:60-106,170-186` |
| Leg inline edit | `onLegPropertyUpdated` with the leg, or every leg of the order when an order-level property changed | F `PlanboardAppService.cs:110-150,152-168` |

The fat DTO "takes seconds on large tenants" (F comment `PlanboardEchoBroadcaster.cs:26-29`). It is also capped: above
350 rows the derived properties (revenue/cost, load capacity, drivers, …) are not computed at all (F
`GetAllTransportFileDtoQuerySelector.cs:250-254`).

### 2.3 What each pane does on a message

| Pane | Reaction | Line |
|---|---|---|
| Vehicle planboard (gantt) | patches its cached file list and the one GSTC item in place (add / update / remove when cancelled, archived or moved off a visible vehicle); reconciles optimistic placeholders by vehicle + overlapping start; any delivery cancels the safety net | F `planboard/vehicle-planboard-gantt/vehicle-planboard-gantt.component.ts:1062-1068,1737-1802,1811-1823` |
| Safety net | after an own mutation, a 30 s timer runs a full `refresh()` unless any SignalR delivery arrives first (a colleague's broadcast counts) | F `vehicle-planboard-gantt.component.ts:1191-1221` |
| Transport files grid | `applyServerSideTransaction` update of rows **already loaded**; a file not in the grid is ignored (new files never appear); when a filter is active, or the sorted/grouped column changed, it sets "prompt for manual refresh" instead of moving the row | F `transport-files.component.ts:358-363,483-503,537-562` |
| Legs (of the selected trip), Legs to schedule | update the row from the leg DTO (`onLegPropertyUpdated` only) | F `planboard/transport-file-legs/transport-file-legs.component.ts:144-146`; `freight-to-schedule.component.ts:219-221,1061-1067` |
| Legs optimistic flows | rows removed at drop time, restored on backend rejection (`legsOptimisticallyScheduled/Unassigned` + rollback) | F `planboard.service.ts:93-111`; `freight-to-schedule.component.ts:223-232`; `transport-file-legs.component.ts:148-157` |
| Transport orders, Stops, Map, Resources | no hub subscription (F: grep `updatedTransportFiles`/`updatedLegs` finds only the panes above) |

### 2.4 Races and lost updates

- **No version on the echo.** Echoes are applied in arrival order; nothing compares `LastModificationTime` or a version
  (F `vehicle-planboard-gantt.component.ts:1748-1767`, a commented-out sort at `:2138`). Two fat echoes built in parallel
  `Task.Run`s for the same file can arrive out of order, so an older state can overwrite a newer one (I).
- **Fire-and-forget after commit.** A crash between commit and `Task.Run`, or a build exception, loses the echo silently
  (F `PlanboardEchoBroadcaster.cs:61-71,120-123`); only the gantt self-heals, and only after an own mutation (F
  `vehicle-planboard-gantt.component.ts:1200-1214`).
- **Reconnect gap.** No refetch after reconnect (F §2.1).
- **Fan-out to every planner of the tenant** with full DTOs whatever their date range or organisation unit: a planner of
  unit A receives unit B's files (F: one tenant group, `PlanboardHub.cs:47-50`). Rows outside the grid are ignored on
  the client (F `transport-files.component.ts:487-488`), so the cost is bandwidth and DTO building, not visible data.
  Organisation-unit visibility is not re-checked on the push (I).
- **Selection race** in the trips view was fixed in the same perf commit (synchronous `selectFile`, re-entrant refresh)
  (F `git show 8b000ac67`; `transport-file-legs.component.ts:168-175`).
- **Route list race**: late `GetRoute` answers for a deselected file are dropped by re-checking the current selection
  (F `transport-files.component.ts:1092-1094,1112-1114`).

### 2.5 Comparison with opentms-next's record-updates hub (design 49)

What exists (BUILT on this branch):
- Hub `RecordUpdatesHub` at `/signalr-hubs/record-updates`, `Watch(type, ids[])` / `Unwatch`, groups
  `<tenantId|host>:<type>:<id>`, the type's watch permission checked, ≤ 200 ids per call (F
  `backend/src/OpenTms.HttpApi.Host/RecordUpdates/RecordUpdatesHub.cs:17-47`; `docs/designs/49-derived-values-and-live-updates.md:124-127,161-166`).
- Payload `recordUpdated { targetType, targetId, version, status }` — **ids and versions only** (constitution rule 5) (F `frontend/src/lib/realtime/record-updates.ts:17-23`).
- Client: one connection per tab, reference-counted watches, re-watch and refetch after reconnect (fixes the legacy gap)
  (F `record-updates.ts:38-142`; `frontend/src/lib/realtime/use-record-updates.ts:27-59`).
- Sender: today only the derived-value job after a calculation (F `backend/modules/platform/OpenTms.Platform.Application/DerivedValues/DerivedValueCalculationJob.cs:92,112`);
  `IRecordUpdateNotifier.NotifyAsync(..., status: null)` exists for "changed by someone else" but no app service calls it yet (F grep; `49…md:125`).
- Transport app use: the trips and orders views watch the visible ids (≤ 400) and on any push refetch the page (F
  `frontend/src/apps/transport/views/TripsView.tsx:100-110`; `OrdersView.tsx:107`).
- The version on the push lets a client drop an older message — the out-of-order problem of §2.4 has an answer (I).

What a planboard needs that it lacks:

| Gap | Why | Option |
|---|---|---|
| **New records** | per-record groups only reach ids a screen already watches; a trip created by a colleague, or a leg that becomes unplanned, never reaches Legs to schedule or the Scheduler | a **per-type tenant channel**: `WatchType("PlanningExecution.TransportTrip")` → group `<tenant>:<type>`; push `{type, id, version, change: Created/Updated/Deleted, hints}` |
| **Filtering by date range / organisation unit** | 200 planners × all trips of the tenant is noisy; the legacy fans out everything | either push routing hints (planned date, org unit id — ids and dates, no personal data) and let the client filter, or a group per `<tenant>:<type>:<orgUnit>:<day>` (I: hints are simpler and keep the hub dumb) |
| **Plain change notices** | the push today means "recalculated"; a vehicle change is pushed only after the calculation job (outbox → Hangfire → calculator), seconds later (I) | call `NotifyAsync(type, id, version, null)` after commit from every Planning command, then the derived-value push follows |
| **Row delta instead of page refetch** | `TripsView` refetches whole pages on any push (F `TripsView.tsx:107-110`); on a planboard with 8 panes that multiplies | client re-fetches **only the changed rows** by id (`GET …/rows?ids=`) and patches AG Grid with `applyServerSideTransaction` / `applyTransaction` |
| **Watch limits** | ≤ 200 ids per call, design volume 200 watched records per connection (F `49…md:34`); a scheduler over a week can show 1,000+ trips | the type channel removes per-id groups for list panes; per-id watch stays for detail panes |
| **Several messages per save** | a save that touches trip + legs + stops | coalesce per transaction: one message per record, debounce client-side 100–250 ms |

**AP-13 and one instance.** The API runs as **one instance per revision** (`max 1`), deliberately without Redis (F
`docs/designs/gcp-infrastructure.md:89-94,209-212`); AP-13 is PROPOSED (F `docs/architecture/decisions.md:52`;
`docs/architecture/05-runtime-and-deployment.md:55-67`). During a canary two revisions run side by side and a push
computed on the other revision can be missed (F `gcp-infrastructure.md:209-212`); session affinity keeps a browser on
one revision (F `gcp-infrastructure.md:139`). Consequences for the planboard: in-process delivery is correct today; the
reconnect refetch and a client safety net (refetch on tab focus / every few minutes while idle) cover the canary gap;
splitting the Jobs role or a second API instance needs AP-13 first (F `49…md:130-133,218`). Unlike legacy, opentms-next
negotiates normally (`accessTokenFactory`, no `skipNegotiation`) (F `record-updates.ts:144-150`), which is correct only
while affinity or a single instance holds (I).

---

## 3. Layout and settings

### 3.1 Docking layout

| Item | Fact | Line |
|---|---|---|
| Library | Smart HTML Elements `@smart-webcomponents-angular/dockinglayout` ^19.0.8 and `tabs` (commercial licence) | F `angular/package.json:83,196` |
| Host | `<smart-docking-layout snapMode="simple" floatable=false animation="none" autoLoadState=false liveResize>` | F `planboard/planboard.component.html:9-22` |
| Panes | each tab is a `PlanboardTabGroupComponent` created dynamically and injected as the panel's content; lazy `@defer (on viewport)` per pane | F `planboard/planboard.component.ts:433-461,503-549`; `planboard-tab-group.component.html:1-104` |
| **One instance per pane** | the tab id is the `TabNames` value, so each pane exists at most once; the two vehicle planboards are mutually exclusive | F `planboard/tabNames.ts:1-12`; `planboard/PlanboardTabInstanceLookup.ts:4-31`; `planboard.component.ts:585-599` |
| Default | an empty horizontal group with one panel `tabPanel`; the user adds panes from the toolbar | F `planboard.component.ts:188-208` |
| Saved layout | `docking.getJSONStructure()` serialised into the **user setting `Filogic.User.Planboard.DockingProfile`** on every layout change (re-reads the setting first, saves only when different and not empty) | F `planboard.component.ts:62,304-326,464-478` |
| Load | parse, drop unknown tab names and empty panels, relabel, create instances, `loadState`; non-granted panes removed after load | F `planboard.component.ts:135-173,329-345,366-387` |
| Tab activity | which tabs are visible drives the "dirty" replay per pane when it becomes active | F `planboard.component.ts:210-279`; `planboard.service.ts:138-175` |

### 3.2 Per-pane settings, global filters and toolbar

- **Global filters**: one date range (picker capped at **92 days**, quick picks from the tenant setting
  `App.DateRangeSelection.DefaultConfiguration`, default today + tomorrow) and organisation units, saved per user as
  `Filogic.User.Planboard.Settings.DateRange` (a preset is stored by name so it rolls with the date) and
  `…Settings.OrganizationUnits` (F `planboard/planboard-settings-panel/planboard-settings.service.ts:13-22,114-169,187-225`;
  `planboard-settings-panel.component.html:1-38`).
- **Sharing**: panes register as listeners; a change marks every listener stale and broadcasts
  `$planboardSettingsUpdate` (`DateRange` / `OrganizationUnits` / `All`); the panel is **locked while any active pane has
  not applied the latest settings** (F `planboard-settings.service.ts:72-112,183-209`). Listeners: transport files,
  transport orders, legs to schedule, resources, gantt (F grep). The map is not a listener; its road-work layer still
  reads the older per-pane `transportFileRange` (F `map/map.component.ts:358`; `planboard.component.ts:181-186`).
- **Opt-out per pane**: the gantt can manage its own date range and units (`manageDateRange`,
  `manageOrganizationUnits`) (F `vehicle-planboard-gantt.component.ts:1129-1153`); grids can show their own date/unit
  controls per `…Preferences.ControlsVisibility` user setting (`PreferencesVisibilityManager`) (F
  `planboard/preferences-visibility-manager.ts:1-49`; use `freight-to-schedule.component.ts:243-249`).
- **Grid state**: AG Grid column state per grid in user setting `Filogic.User.<grid>.AgGrid.State`, optionally one state
  per organisation-unit set (F `global/filogic-grid-base/filogic-grid-base.ts:345-381,550-568`;
  `transport-files.component.ts:339`).
- **Gantt settings**: nine keys under `Filogic.User.Planboard.VehicleOutline.Scheduler.*` (date range, units, vehicle
  filter, relation-group filter, empty files, unplanned, settings, controls visibility, mode filters) and per-mode
  snapshots for **primary (tractor) / secondary (trailer)** — there is no driver mode (F
  `vehicle-planboard-gantt.component.ts:164-176,670-690`). Because the keys are per user, a second scheduler instance
  would overwrite the first's settings (I).
- **Map settings**: legend and maximised per user; toll and CO₂ overlays are **tenant-wide** (`setMapOverlay`), the
  enabled traffic layers a setting `App.RouteConstraint.EnabledTrafficLayers` (F `toolbar/toolbar.component.ts:413-442`;
  `planboard.service.ts:18-22,134-136`).
- **Toolbar**: opens/closes panes, saves the layout, hosts the traffic-layer menu and the settings panel (F
  `toolbar/toolbar.component.html:139-151`; `planboard.component.html:2-7`).

### 3.3 What opentms-next already has

- `platform."GridUserSettings"`: one row per (tenant, user, grid id), `SettingsJson text` without length limit, upsert
  under a per-grid lock, tenant via the ABP data filter (F `docs/designs/grid-settings-storage.md:39-79`). It removed the
  2,048-character ABP setting limit that a docking profile JSON would also hit (F `grid-settings-storage.md:16-29`).
  A planboard layout and per-pane settings fit the same pattern: a `gridId`-like key such as
  `planboard.layout`, `planboard.scheduler.<instanceId>`, `planboard.filters` (I; a generic name — "user workspace
  setting" — would need a Platform design, risk class critical).
- Component map **GAP-07**: docking workspace, options dockview / flexlayout-react / fixed `Resizable` with tabs, needs a
  PO choice (F `docs/conventions/ui/component-map.md:28,104`). **GAP-11** (gantt/scheduler) is a separate choice (F `:108`).
- AG Grid Enterprise 33 is licensed and in use (F `frontend/package.json:27-28`).

### 3.4 Docking library candidates

| Library | Licence | Fit | Notes |
|---|---|---|---|
| **dockview** (React) | MIT | high | tabs, splits, floating groups, pop-out windows, `toJSON()`/`fromJSON()` layout, zero dependencies; panes can be any React component, so several Scheduler instances are just several panels with their own id (I) |
| **FlexLayout** (`flexlayout-react`) | MIT | high | JSON model with tabsets and border panels, pop-outs; mature (Caplin) |
| rc-dock | Apache-2.0 | medium | React dock layout with float/maximise; smaller community |
| Golden Layout 2 | MIT | low | framework-agnostic, React binding is manual; slow maintenance |
| react-mosaic | Apache-2.0 | low | tiling windows, no tab stacks of its own |
| shadcn `Resizable` (react-resizable-panels) + tabs | MIT | low–medium | GAP-07 option (c): fixed splits, no drag-docking; simplest, already in the stack |
| Infragistics Ignite UI **Dock Manager** | commercial | high | web component with React wrapper, rich docking; paid per developer |
| Smart HTML Elements docking layout (legacy) | commercial | medium | web component; React wrapper exists; keeps the legacy model |
| Syncfusion Dashboard Layout / Docking | commercial, community licence for small companies | medium | tile-dashboard rather than IDE-style docking (I) |

I: dockview satisfies the first build under a free licence and the "several instances per user" decision without
special code; FlexLayout is the closest alternative. Licence terms are from the projects' published licences; verify
versions at design time.

---

## 4. Performance

### 4.1 Legacy read paths behind the panes

| Pane | Call | Shape | Line |
|---|---|---|---|
| Transport files | `TransportFile/GetAll(… dateRange, orgUnits, flags, page, sort, groupBy)` | AG Grid server-side row model; `GetAllTransportFileDtoQuerySelector` with **20 joins** (users, type, org unit, relations, carrier, …), offset paging (`Skip/Take`), a `COUNT` unless the first page holds everything, then `SetDerivedProperties` (revenue, cost, load capacity, drivers, incompatibilities) — skipped above 350 rows | F `transport-files.component.ts:721`; `GetAllTransportFileDtoQuerySelector.cs:43-80,250-300`; `FiLogic.Application/BusinessServices/GetAllDtoQuerySelector.cs:248-349` |
| Trip stops (master/detail) | `GetAllStops(fileId)` per expanded row | F `transport-files.component.ts:662-669` |
| Transport orders | `TransportOrder/GetAll(…)` server-side, same selector pattern | F `planboard/transport-orders/transport-orders.component.ts:304` |
| Legs to schedule | `GetAllUnscheduledLegs(dateRange, orgUnits, flags, …)` → `GetAllLegsDtoQuerySelector` | F `freight-to-schedule.component.ts:507`; `TFAS:2137-2143` |
| Legs (selected trip) | `GetAllLegs(fileId)` | F `transport-file-legs.component.ts:194` |
| Gantt | vehicles ×2, relation groups, trip types, then `TransportFile/GetAll` for the range | F `vehicle-planboard-gantt.component.ts:993,3033-3035,3111` |
| Resources | `Calendar/GetAllResources(…)` | F `planboard/resources/resources.component.ts:256` |
| Map | `GetRoute` per selected file (§1.4) | |

Measured or claimed latencies found in the legacy source:
- "the full DTO pipeline … takes seconds on large tenants" (F `PlanboardEchoBroadcaster.cs:26-29`).
- Mutation timings are logged (`AssignLegsToVehicleOrTransportFile responded in {ms}`, `core committed in {ms}`) but not
  recorded in the repository (F `VehiclePlanboardAppService.cs:371-372`; `TFAS:2251-2252`).
- Per-stage stopwatches returned to the client: `GetRoute` `t1..t6` (F `RC:184-265`), assign `t1..t5` (F `TFAS:2254`).
- Commit history: "save transport order within 300 ms" (PR `c51c09870`), arrowline query via IQueryable projection
  (`203d397ab`), split-query experiment (`3273defe4`), `getall_perf` paging (`65ea4583d`), db calls in a loop
  (`c6e3f41b1`), the optimistic-mutation commit `8b000ac67` (F `git log --grep perf`). No dashboard numbers in code.
- `FiLogic.Metrics` holds only shutdowns, instance-up and SignalR connection counts — no request-latency histogram of
  its own (F `ApplicationMetrics.cs:18-75`).

I: the legacy reaches "feels fast" through optimistic UI and moving work off the request path, not through fast reads;
the GetAll pipeline on large tenants is well beyond 200 ms.

### 4.2 Where opentms-next stands

`TransportTripAppService.GetListAsync` already avoids cross-schema joins: Planning's own trips, filtered and sorted in
SQL with offset paging and two `COUNT`s (total, filtered), then **one batch of published lookups per page** for carrier,
combinations, vehicles, drivers and order references (F
`backend/modules/planning-execution/OpenTms.PlanningExecution.Application/TransportTrips/TransportTripAppService.cs:67-91,310-376`).
Search runs on a lower-cased `SearchKey` with a trigram index on the trip row (F `59…md:213-216`). Budgets: interactive
reads p95 < 300 ms, mutations p95 < 500 ms (F `.dev-department/conventions/performance-budgets.md:9-10`) — the PO's
200 ms is stricter than both.

### 4.3 How 200 ms can be reached without Planning-owned read tables

Rules that bind every option: schema-per-module and no cross-schema joins (CLAUDE.md "Module boundary rules" 1),
cross-module only via events or published interfaces (rule 2), events carry ids (constitution rule 5), derived values
stored and never calculated on read (AD-07).

| # | Option | Effect | Against the rules | Verdict |
|---|---|---|---|---|
| P-1 | **Well-indexed queries on Planning's own tables only**: composite indexes per pane filter (tenant DB, `IsArchived`, `PlannedDepartureUtc`, org unit), partial index for unplanned legs (`TripId IS NULL`), covering columns for the list | pane queries in single-digit to tens of ms on 100k trips (I) | fully inside | **do** — the base of everything |
| P-2 | **Projection in SQL, not entity loading**: `Select` into row DTOs; today `RowSource` carries the whole `Trip` entity (F `TransportTripAppService.cs:85-91`) | less data, no change tracking | inside | **do** |
| P-3 | **Display fields of other modules from cached client lookups** (vehicles, drivers, relations, carriers, org units: small, slowly changing lists per tenant) via TanStack Query with long `staleTime`, refreshed by a change push | list call returns ids only; no lookup on the hot path | inside (generated client, app reads its mapped domains); master data is a view in the Master data app | **do for small lists**; relations can be large (thousands) → page the lookup or use P-4 |
| P-4 | **Batched published interfaces** per page (what `GetListAsync` already does) with an in-process per-tenant cache in the owning module | one extra round trip per page, bounded | inside (rule 2); the cache lives in the owning module; multi-instance needs AP-13 for invalidation | **keep**; add caching in Master Data's lookup |
| P-5 | **Ids-only deltas over SignalR, client re-fetches only that row** (`GET …/rows?ids=`), patched into AG Grid | a colleague's change costs one small indexed read per planner who shows it | inside; matches design 49 and rule 5 | **do**; needs the type channel of §2.5 |
| P-6 | **Keyset paging** (`WHERE (PlannedDepartureUtc, Id) > (@a, @b) ORDER BY … LIMIT n`) instead of `Skip/Take` | constant cost for deep pages; AG Grid server-side model supports it with a cursor in the block request (I) | inside | **do** for the large panes (trips, legs to schedule, orders) |
| P-7 | **Avoid two `COUNT`s per page**: count once per filter change, or an estimate, or only for the first block | removes the most expensive statement on big filters (I) | inside | **do** |
| P-8 | **Compiled queries** (`EF.CompileAsyncQuery`) for the hot single-row reads (trip by id, stops of a trip), as legacy `RC:70-143` does | saves LINQ translation per call (cold start mostly) | inside | do where measured; CLAUDE.md enables compiled models at > 4 s cold start |
| P-9 | **HTTP caching / ETags** on lookups and on trip detail (`ETag` = version / `ConcurrencyStamp`); `304` when unchanged | near-zero server time for unchanged data | inside; needs per-tenant, per-user keys and `Vary: Authorization` (I) | **do for lookups and geometry**; lists change too often |
| P-10 | **Stored derived values instead of compute on read**: route geometry, toll, CO₂, load factor, cost totals on Planning's/Financial's own rows via the derived-value job (M-1) | the map and lists read stored columns | inside: AD-07 is exactly this; a stored derived value on the owner's row is not a read table | **do**; the map cannot meet 200 ms otherwise |
| P-11 | **Optimistic mutations + background follow-up** (legacy `8b000ac67`) | perceived ~0 ms; the API answers after the core commit | inside; performance budget asks for it (F `performance-budgets.md:10`) | **do** |
| P-12 | Snapshot columns on Planning's rows (names copied from other modules and refreshed by events) | removes lookups | a gray zone: Planning already stores snapshots (stop name/city, `SearchKey`, F `59…md:22,213-216`); a set of such columns maintained by events **is** a denormalised read model in all but name | **ask the PO** (Q-03) — the decision rejects read tables; whether search/sort snapshots on the row count is not stated |
| P-13 | A cross-module read model or materialised view joining schemas | fast | **violates** rule 1 and the PO decision | reject |

Sorting and filtering on a field another module owns (carrier name, driver name, customer name) is the hard case: with
P-3/P-4 the database cannot sort by it. Options: sort by `SearchKey`-like snapshot (P-12, PO question), restrict sort to
Planning's own columns, or sort client-side within a loaded window (I).

---

## 5. Scenarios and open questions

### 5.1 Scenario list

| ID | Scenario | Legacy behaviour (line) |
|---|---|---|
| L-01 | Select one trip with 8 geocoded stops: map shows departure, stops 1–8 coloured by waypoint type, arrival, truck route, toll sections with €-glyphs and numbered badges, strip with km/duration/toll/CO₂ | `PMS:148-182,220-267,389-418`; `map/map.component.html:14-50` |
| L-02 | Select three trips: three colours (theme first), totals in the strip, sheet per route | `PMS:125-144`; `map/map.component.ts:92-107` |
| L-03 | Trip in process with an FMS device: pulsing truck at the newest device location; without a device: last-seen location; not in process: no truck | `RC:385-422`; `PMS:411-417` |
| L-04 | Route function down: schematic dashed lines, "route function unavailable" in the strip | `RC:572-577`; `PMS:170-173`; `map/map.component.html:33,65-68` |
| L-05 | Truck routing finds no path (442): car-profile geometry, toll and CO₂ omitted | `RC:476-501` |
| L-06 | A stop without coordinates: skipped in markers and in the route; < 2 known points → no route | `RC:439-453`; `PMS:396-399` |
| L-07 | Select rows in Legs to schedule: each leg drawn origin → destination with the default truck next to the selected trips | `freight-to-schedule.component.ts:517-573` |
| L-08 | Toggle toll overlay off: route redrawn in its colour without toll sections, from cached data (no refetch); setting is tenant-wide | `map/map.component.ts:152-162`; `toolbar/toolbar.component.ts:422-428` |
| L-09 | Toggle road works: constraints for the trip date range drawn as lines/areas with hover district | `map/map.component.ts:180-214,322-361` |
| L-10 | Map tab hidden while selecting: route fetched when the tab becomes active | `transport-files.component.ts:1076-1079`; `planboard.component.ts:262-271` |
| L-11 | Click a stop marker: nothing happens (rebuild: select the stop, M-2) | `PMS:420-423` |
| L-12 | Colleague changes a trip's vehicle: gantt patches the slot, trips grid updates the loaded row, map stays stale | `vehicle-planboard-gantt.component.ts:1737-1802`; `transport-files.component.ts:483-503` |
| L-13 | Colleague creates a trip by dropping a leg: other gantts get a slim DTO at once, the fat DTO later; the trips grid does not show the new trip until refresh | `VehiclePlanboardAppService.cs:355-369`; `transport-files.component.ts:487-488` |
| L-14 | Own mutation, echo lost: gantt refreshes after 30 s; other panes stay stale | `vehicle-planboard-gantt.component.ts:1191-1221` |
| L-15 | Trips grid filtered or sorted on a changed column: row updated in place plus a "refresh" prompt | `transport-files.component.ts:537-562` |
| L-16 | Network drop and automatic reconnect: no refetch; updates during the gap are missing | `planboard-signalr.service.ts:46-53` (no `onreconnected`) |
| L-17 | WebSocket blocked: 3 fresh attempts on 404 / 10 retries, then a "live updates unavailable" toast | `planboard-signalr.service.ts:111-158` |
| L-18 | Change the global date range: every listening pane goes stale, panel locked until each active pane has reloaded | `planboard-settings.service.ts:105-112,187-209` |
| L-19 | Range wider than 92 days saved earlier: reset to the first configured preset on load | `planboard-settings.service.ts:121-144` |
| L-20 | Open a pane, move it, close the planboard, reopen: layout restored from `Filogic.User.Planboard.DockingProfile`; panes the user lost permission for are removed | `planboard.component.ts:135-173,304-345` |
| L-21 | Try to open both vehicle planboards: the other one closes (one instance per pane) | `planboard.component.ts:585-599` |
| L-22 | Gantt mode switch tractor ↔ trailer: per-mode filter snapshot restored, no reload | `vehicle-planboard-gantt.component.ts:676-690` |

### 5.2 Open PO questions with recommendations

| ID | Question | Recommendation |
|---|---|---|
| Q-01 | **Map tiles**: CARTO (legacy, no key, terms to verify, attribution hidden), MapTiler/Stadia (free tier, paid above), OpenFreeMap (free OSM vector tiles, no key), or self-hosted Protomaps PMTiles in Cloud Storage? | Self-hosted PMTiles (or OpenFreeMap for the first build) with visible OSM attribution: no key, no per-view cost, fits "costs nothing while idle". MapLibre GL via npm (GAP-12). |
| Q-02 | **Stored trip geometry** (M-1): may the trip calculator store the polyline and toll sections on the trip so the map reads in < 200 ms? | Yes: a derived value under AD-07, on Planning's own row; not a read table. |
| Q-03 | Do **snapshot columns** on Planning's own rows (carrier name, driver names for sort/search; `SearchKey` exists already) fall under "no denormalised read tables"? | Allow a small, named set for search and sort only, refreshed by events; everything displayed resolves through lookups (P-3/P-4). |
| Q-04 | **Vehicle positions (AO-12)**: which domain owns them, and is a live truck on the map (M-5) and planned-vs-actual (M-6) in the first Planboard run? | Decide AO-12 for Planning & Execution ("realtime status, track & trace" in its map); first run: last known position from the trip's actuals only, live positions with the ingest run. |
| Q-05 | **Live channel**: may the hub get a per-type tenant channel with routing hints (planned date, org unit) for list panes (§2.5)? It is a Platform change, risk class critical. | Yes; ids + date + org-unit id only, client filters, rows re-fetched by id (P-5). |
| Q-06 | **200 ms scope**: does it apply to every call, including mutations (budget 500 ms), exports and the first load of a pane over a 92-day range? | 200 ms p95 for reads of a page/row and for a mutation's core commit; follow-up work async; exports and route previews excluded. |
| Q-07 | **Docking library (GAP-07)**: dockview, FlexLayout, or a fixed resizable layout first? | dockview (MIT): drag-docking, pop-outs, JSON layout, several Scheduler instances as plain panels. |
| Q-08 | **Layout storage**: reuse `platform."GridUserSettings"` with keys like `planboard.layout` and `planboard.scheduler.<instanceId>`, or a named "workspace setting"? Shared/team layouts? | Reuse the table with a key convention; per-user only in the first run; team templates later. |
| Q-09 | **Global filters**: keep one shared date range + organisation units with a per-pane opt-out (legacy), and should the map follow it (show all trips of the range, M-8)? | Keep; map follows the global filters for the "all trips" layer, the selection for the detail layer. |
| Q-10 | **Map interactions in the first run**: two-way selection (M-2), lateness colouring (M-3), unplanned legs nearby (M-7), drag to reorder (M-9) — which are in? | M-2 and M-3 in (data exists); M-7 and M-9 in the second iteration. |
| Q-11 | **Toll/CO₂ overlay setting**: tenant-wide (legacy) or per user? | Per user (a view preference), default from a tenant setting. |
| Q-12 | **Zero-emission zone data**: who maintains the polygon set the map and the M-11 check use? | Integrations imports a published dataset on a schedule; legacy's static asset as the first seed. |
| Q-13 | **Multi-instance**: is AP-13 (Redis) in scope before the planboard goes live with more than one API instance, or does the planboard accept the single-instance limit plus a refetch safety net? | Single instance plus reconnect/focus refetch for the first run; AP-13 before scaling out. |
