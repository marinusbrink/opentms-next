<!-- Appendix to the Planboard rebuild assessment. Produced 2026-10-31 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 2cf2c1558; stale copies under .claude/worktrees/ ignored) and opentms-next at branch feat/postmark-email-delivery. Citations are path:line in those checkouts. F = fact read in code, I = inferred. Read-only research; nothing was built or run. -->

# Legacy OpenTMS assessment: Planboard panes (trips, orders, stops, legs, legs to schedule)

Scope (PO): the panes **Transport trips** (`transport-files/`), **Transport orders** (`transport-orders/`), **Transport trip
stops** (tab `stops`), **Legs** (tab `legs`, `transport-file-legs/`), **Legs to schedule** (tab `legsToSchedule`,
`freight-to-schedule/`) and the dialogs they open (`manage-legs/`, `split-freight-item-dialog/`,
`transport-file-legs/generate-return-freight`, `transport-orders/generate-return-transport-order`, `complete-dialogs/`).
Route42, route forecast, the FMS sending wizard and the driver-instructions wizard are out of scope and named only where a
pane calls them. The trip and order entities, the assignment manager, stop composition, cost/revenue and warnings are
already described in `docs/input/transport-assessment/trips.md` (§1-§10) and `orders.md` (§1-§11); this file does not
repeat them and refers to their sections.

Path abbreviations:

| Short | Path |
|---|---|
| `PB/` | `~/dev/OpenTMS/angular/src/app/planboard/` |
| `FS` | `~/dev/OpenTMS/angular/src/app/transport/file-stops/file-stops.component.ts` (the Stops pane lives outside `planboard/`, `PB/planboard-tab-group/planboard-tab-group.component.ts:13`) |
| `GB` | `~/dev/OpenTMS/angular/src/app/global/filogic-grid-base/filogic-grid-base.ts` |
| `TF` | `PB/transport-files/transport-files.component.ts` |
| `TO` | `PB/transport-orders/transport-orders.component.ts` |
| `TFL` | `PB/transport-file-legs/transport-file-legs.component.ts` |
| `FTS` | `PB/freight-to-schedule/freight-to-schedule.component.ts` |
| `PBS` | `PB/planboard.service.ts` |
| `SRS` | `PB/planboard-signalr.service.ts` |
| `src/` | `~/dev/OpenTMS/aspnet-core/src/FiLogic.Application/BusinessServices/` |
| `TFAS` | `src/TransportFile/TransportFileAppService.cs` |
| `PAS` | `src/Planboard/PlanboardAppService.cs` |
| `TOAS` | `src/TransportOrder/TransportOrderAppService.cs` |
| `SEL` | `src/TransportFile/GetAllTransportFileDtoQuerySelector.cs` |
| `LSEL` | `src/TransportFile/GetAllLegsDtoQuerySelector.cs` |
| `next/` | `/Users/marinusbrink/dev/opentms-next/` |
| `TTAS` | `next/backend/modules/planning-execution/OpenTms.PlanningExecution.Application/TransportTrips/TransportTripAppService.cs` |
| `LAS` | `next/backend/modules/planning-execution/OpenTms.PlanningExecution.Application/Legs/LegAppService.cs` |
| `TOPS` | `next/backend/modules/planning-execution/OpenTms.PlanningExecution.Application/TransportTrips/TripOperations.cs` |

## 0. The board around the panes

- **Tabs are components in a docking layout** (`@smart-webcomponents-angular/dockinglayout`, `PB/planboard.module.ts:12`).
  `PB/planboard-tab-group/planboard-tab-group.component.html:8-48` mounts `transport-files` (selection `multiple`, flag
  filter, listens to board settings and the hub), `transport-file-legs` (input `selectedFile.id`), `file-stops` (input
  `selectedFile.fileNumber`), `transport-orders` and `freight-to-schedule`, each `@defer`red until idle/viewport (F).
- **Shared state is one root service** with ~60 `Subject`s (`PBS:24-182`): `selectedFile` (the single selected trip,
  47), `selectFile` event (51), drop-zone replay subjects per grid (58-67), optimistic schedule/unassign subjects
  (101-111), refresh subjects per pane (41-45, 76-77, 113), and **dirty flags** per tab (166-175) (F).
- **Tab activity**: `tabsActivity` signals (`PBS:138-163`) are set from the DOM (`document.querySelectorAll('.smart-tab-selected')`)
  in `PB/planboard.component.ts:210-221`; a pane that receives a change while hidden sets its dirty flag, and on tab
  activation the board fires that pane's refresh subject and clears the flag (`PB/planboard.component.ts:222-246`). The
  loop runs `i < selectedTabs.length - 1`, so the last selected tab in DOM order is never marked active (F, 214) — a bug
  that makes that pane fall back to "dirty, refresh later".
- **Board-level filter bar**: `PlanboardSettingsService` holds one date range and one organisation-unit set for the whole
  board, persisted as user settings `Filogic.User.Planboard.Settings.DateRange` / `.OrganizationUnits`
  (`PB/planboard-settings-panel/planboard-settings.service.ts:14-15`), date span capped at 92 days (19, 139). Each pane can
  either follow the board ("listen", when its own date-range/OU control is hidden) or use its own control
  (`TF:147-149, 275-335`; `FTS:71-73, 151-204`; `TO:84-86, 166-224`) (F).
- **Cross-window selection**: trips and legs-to-schedule mirror their selection to other browser windows through a
  `BroadcastChannel` (`TF:131, 404-407, 834-836`; `FTS:65, 214-217, 494-496`) (F) — the board can be spread over
  several windows/monitors (I).
- **Grid base**: every pane except Stops is an AG Grid on `FilogicGridBase`/`ServerSideGridBase` (`GB:1159`). Server-side
  blocks are 40 rows (`cacheBlockSize = 40`, `paginationPageSize = 40`), 4 concurrent block requests, 700 ms block-load
  debounce (`GB:83-88`) (F). Columns are not hard-coded: the user picks them from the DTO's attribute metadata
  (`getAllAttributes`/`getLegAttributes`, `GB:1098-1106`; `TFL:516-522`) and the column state is a user setting
  (`Filogic.User.<grid>.AgGrid.State`, `GB:1145-1153`); "apply to all users/tenants" exists (`GB:1114-1143`) (F).

## 1. Pane: Transport trips (`transport-files`)

### 1.1 Purpose and layout

The planner's list of trips in the board's date range; the anchor of the board: selecting one trip drives Legs, Stops
and the map (§6). Toolbar = `ag-preferences-panel` (search, refresh, column chooser, Excel export, date range, OU
filter, flag filter, apply-to-all) (`PB/transport-files/transport-files.component.html:3-29`). No "New" (`canAddNew=false`,
11) (F).

### 1.2 Columns

Any property of `GetAllTransportFileDto` (`src/TransportFile/Dto/GetAllTransportFileDto.cs:6-229`) can be a column; the
grid's special handling (`TF:1619-1710`):

| Column (field) | Source | Inline editable | Renderer / rule |
|---|---|---|---|
| `name` | `TransportFile.FileName` | yes, text (`TF:1646-1647`) | — |
| `remarks` | `TransportFile.Remarks` → plain text (`SEL:209`) | yes, text | — |
| `driverInstructions` | `TransportFile.DriverInstructions` → plain text | yes, text | — |
| `transportCompany` | Relation name via `TransportCompanyId` | yes, carrier picker scoped to the pane's OUs (`TF:1648-1654`) | — |
| `primaryVehicle`, `secondaryVehicle`, `thirdVehicle` | Vehicle name + licence plate from `vehiclesMetadata` (`TF:1712-1735`) | yes, vehicle picker with plates (`TF:1656-1688`) | — |
| `drivers` | `TransportFileAttributes.Drivers` (string) | yes, multi-driver picker (`TF:1689-1695`) | — |
| `taskStatus`, `loadFactor`, `fleetManagementSystemDeviceStatus`, `flags`, `emailDeliveryConfirmationStatus`, `warnings`, `exchangeGatewayTransmissions` | derived (§1.6) | no | custom cells (`TF:1626-1707`) |
| `revenue` | calculated on read | no, **not sortable** (`TF:1699-1701`) | — |
| `description`, `barcodes`, `containerNumbers`, `transportOrderReferences`, `invoiceReferences` | aggregated from freights | no; grouping blocked; sorting blocked also for `actualPrice`, `expectedPrice`, `weight` (`PB/../common/ag-grid-defaults/ag-column-restrictions.ts:6-9`) | truncated to 50 chars server-side (`TFAS:1746-1751`) |
| everything else (`fileNumber`, `fileDate`, `transportFileType`, `organizationUnit`, relations, scheduled/actual times, distances, CO₂, LPI, vehicle types, `nrOfStops`, `nrOfLegs`, `customers`, `transportOrders`, first/last/all stop names and cities, `loadingMeters`, `epq`, `amount`, `positions`, `colli`, `weight`, `cost`, `expectedPrice`, `actualPrice`, `sensorCodes`, `customProperty0..9`) | §1.6 | no | footer totals per attribute flag `displayTotalsInFooter` (`TF:1621-1625`) |

A fixed checkbox column is first (`TF:383-388`); a stop-selection detail grid exists only in the master-detail variant
used by other screens (`TF:259-273, 605-674`), not on the board (I: the board passes no `masterDetail`).

### 1.3 Sort, filters, grouping, paging

- **Default sort**: none sent by the client; the server falls back to `FileName ASC` (`src/TransportFile/Queries/TransportFileQueries.cs:384-386`) —
  a non-unique key, so paging order is unstable (I). Unknown/derived sort columns are rejected by a blacklist (`TFAS:1270-1272`:
  Warnings, CreatorUser, VehicleTypes, EmailDeliveryConfirmationStatus, TaskStatus, NrOfAttachments, HasAttachments,
  Revenue, ProfitMargin).
- **Filters sent** (`TF:721`): `isCanceled=false`, `isArchived=false`, `isFmsSourced` (input, unset on the board),
  `transportCompanyId` (`selectedRelationId`, unset on the board), `organizationUnitIds` (multi), `flagFilterEnum`
  (bitmask, any-match `& > 0`, `SEL:107`), `dateRange` on **FileDate** (`SEL:99`), page size/number, free-text `filter`,
  sort column/order, `groupByColumns`/`groupByValues` (F).
- **Grouping**: AG Grid server-side row grouping on any column not blocked; the server answers group buckets with a count
  (`TFAS:1290-1361`: maps the 13-way join to the DTO, groups by the property, counts, sorts, pages groups), drill-down
  passes `groupByValues`; "Empty" maps to `empty` (`TF:714-719`) (F). Dropping a leg on a group row with more than one
  child is refused (`TF:985-996`).
- **Paging**: server-side row model, 40-row blocks (`TF:181`, `GB:85-88`).
- **Quick filter / search** box sends `filter` (free text over ~40 columns, `ToString().Contains` on dates and doubles,
  trips.md §9).

### 1.4 Selection, row actions, toolbar

- Multi-row selection with checkboxes (`PB/planboard-tab-group/planboard-tab-group.component.html:11`). One selected →
  `planboardService.selectedFile = row` and `selectFile` fires; more than one or none → `selectedFile = null`
  (`TF:1052-1085`). Legs and Stops therefore show only for exactly one trip; the map shows all selected (§6).
- Double-click opens the trip editor dialog (`TF:956-960`).
- Context menu (`TF:1335-1446`, right-click also selects the row): Edit; **Complete** (disabled when ATA and ATD exist,
  1448-1450) → `CompleteTransportFileDialog`; Delete (confirm → `TransportFile.delete`, then refresh trips, vehicle
  outline, legs, stops and routes, 1162-1198); Flag (set-flags dialog); Highlight colour (dialog, optimistic patch to the
  gantt, 1482-1510); E-mail; Archive (confirm → `archive`, 1200-1224); Print CMR (dialog); Split (= "convert vehicle"
  dialog, 1302-1333); **Send to vehicle** / **Remove from vehicle** (FMS, background job polled, 1246-1300; out of scope);
  an "Exchange gateway" sub-menu when gateways exist (1437-1443, 1452-1476).
- The same actions can be fired from the vehicle outline through `doContextMenuActionEvent` (`TF:453-470`).

### 1.5 Inline edits and their backend calls

The grid edits through `PlanboardServiceProxy` (`TF:1535-1592`); text cells auto-commit 1.5 s after the last keypress
(`TF:1512-1529`); pickers emit through board subjects and commit after 200 ms (carrier, vehicles) or 3 s (drivers)
(`TF:760-824`). Every call goes `PAS` → `TFAS` inside a **new** unit of work, then the PAS broadcasts the returned fat DTO
to the tenant's SignalR group (`PAS:170-186, 29-45`). The originating grid applies the returned DTO only when it is *not*
listening to the hub (`TF:1594-1610`); on the board it listens, so **its own edit appears only through the SignalR echo**
(F) — with the hub down the cell keeps the typed value but the row is not refreshed (I).

| Call | Input | Rules / side effects (TFAS) | Returns | Broadcast |
|---|---|---|---|---|
| `UpdateFileName` | `transportFileId`, `fileName` | `UpdateFileProperty`: load, set, save (520-543); then `UpdateAssociatedTransportOrder` (FMS-sourced order sync) | `GetAllTransportFileDto` via `QuerySingleTransportFileListDto` (4759-4765) = the **old selector with `SetDerivedProperties`** (revenue, cost, load capacity, capabilities, calendar, sensors, `SEL:250-622`) | `onTransportFilesUpdates` [dto] to tenant group (`PAS:39`) |
| `UpdateFileRemarks` | `transportFileId`, `fileRemarks` | same | same | same |
| `UpdateFileDriverInstructions` | `transportFileId`, `fileDriverInstructions` | same | same | same |
| `UpdateTransportCompany` | `transportFileId`, `transportCompanyId` | when changed: full `GetSingle` + re-query incoming invoices (outsourcing line) + full `CreateOrUpdate`, then `SyncTransportFileAttributes` (545-576) | same | same |
| `UpdatePrimaryVehicle` | `transportFileId`, `vehicleId?` | sets primary; copies vehicle's default secondary/third vehicle; departure **and** arrival = vehicle's stationing relation, else the type's defaults; carrier from vehicle (calls `UpdateTransportCompany`, which reloads the fat DTO a second time); drivers from vehicles; attribute sync; CO₂ forward (578-669) | same | same |
| `UpdateSecondaryVehicle` / `UpdateThirdVehicle` | `transportFileId`, `vehicleId?` | set; drivers from vehicles; order sync; attribute sync (671-743) | same | same |
| `UpdateDrivers` | `transportFileId`, `drivers[]` | delete all + insert per driver; attribute sync stored proc twice (745-793) | same | same |

No validation of state (a completed/archived trip can be edited), no concurrency token (F, none in the inputs).

### 1.6 Read path

`TransportFileServiceProxy.getAll(...)` (`TF:721`) → `TFAS.GetAll` which **returns `GetAll_Perf` immediately**; the old
body after the `return` is dead (`TFAS:1237-1260`). `GetAll_Perf` (`TFAS:1263-1859`):

1. Structured filters on `tblTransportFile` (+ attributes for hide-empty, + legs/freights/orders for FMS-sourced).
2. Group request → separate 13-way join + group-by + count (1290-1361).
3. Paging: `ToPageAsync` — when there is free text, a group drill-down, or a sort column that is not a file column (e.g.
   `customers`, `transportCompany`), it pages over the **13-way join** (users ×2, type, OU, relations ×3, vehicles ×3,
   vehicle types ×3, `TransportFileAttributes`, transport order) (`src/TransportFile/Queries/TransportFileQueryService.cs:69-127`); else over the bare file
   table; always a `COUNT` first.
4. Enrichment for the page — numbered "SQL - 3 … 27" in code (`TFAS:1384-1516`): file types, OUs, **`TransportFileAttributes`**
   (5), legs+freights+prices+orders via Dapper (6), order additional services (10), **all discount & surcharge rows of the
   tenant** (11), vehicles, vehicle types, incoming invoices, drivers, relations (+ the tenant's own `IsMe`), transport
   units, unit load factors, **all capabilities, all freight capabilities, all relation capabilities** (19-23, "todo"),
   vehicle/vehicle-type capabilities, calendars and calendar events, sensor measurements when FMS credentials exist.
5. In-memory calculation per row: revenue allocation, cost per km and cost, load capacity, warnings
   (`TFAS:1524-1601`; trips.md §6, §8), attachments and e-mail delivery status (`TFAS:1855-1856`).

**Columns from other domains**: customers and order numbers (string columns of the denormalised table
`tblTransportFileAttributes`, `FiLogic.Core/BusinessEntities/TransportFile/TransportFileAttributes.cs:11-61`: Cost, Drivers,
NrOfLegs, TransportOrders, Customers, LoadFactor, LoadingMeters, Epq, Amount, Positions, Colli, NrOfStops, First/Last/All stop
names and cities, filled by stored procedures and `SyncTransportFileAttributes`); carrier/relations/vehicles/types
(joined or looked up); revenue (orders' prices + D&S, recomputed), cost (incoming invoices + cost per km), expected/actual
price (incoming invoices), order references, barcodes, container numbers, invoice references (freights), sensor values
(FMS), exchange-gateway count (Integrations), e-mail status (mail log) (F).

**200 ms budget — what breaks it**

| # | Finding | Evidence |
|---|---|---|
| T-1 | ~27 round trips per page plus in-memory cost/revenue/warnings; whole-tenant loads of D&S and three capability tables | `TFAS:1384-1516` |
| T-2 | Free text, drill-down or a non-file sort switches paging to a 13-way join with `ToString().Contains` over ~40 columns; `COUNT` over the same | `TransportFileQueryService.cs:79-103`, trips.md §9 |
| T-3 | Every inline edit returns and broadcasts the **old** fat pipeline (`SetDerivedProperties` incl. cost calculators, capabilities, calendar, sensor group-by), the PlanboardEchoBroadcaster comment says it "takes seconds on large tenants" | `PAS:170-186`, `TFAS:4759-4781`, `src/VehiclePlanboard/PlanboardEchoBroadcaster.cs:21-37` |
| T-4 | `UpdatePrimaryVehicle` nests `UpdateTransportCompany` (full GetSingle + CreateOrUpdate + fat DTO) inside its own transaction, then builds the fat DTO again | `TFAS:578-669` |
| T-5 | The old selector still sets `EstimatedLoadingPerformanceIndicator = Random.Shared.Next(60, 100)` — every inline-edit echo carries a random LPI | `SEL:496` |
| T-6 | Selecting trips fetches a route per selected trip from the external route function (10 s timeout, nothing stored) | `TF:1087-1160`, trips.md §5 |

## 2. Pane: Transport orders (`transport-orders`)

### 2.1 Purpose, columns, filters

Read-only order list on the board for reference and order-level actions; it does **not** feed planning directly (no drag
source into trips; the grid is only an HTML5 drop target for row highlighting, `GB:940-960`) (F/I). Columns: any of
`GetAllTransportOrderDto` (orders.md §10 lists them: state, task status, customer, number, date, reference, stop
summaries, revenue, cost of revenue, margin, CO₂, trip numbers, vehicles, custom 0-9); renderers for `taskStatus`,
`orderState`, `flags`, `emailDeliveryConfirmationStatus` (`TO:559-586`); sorting blocked on CO₂ columns
(`ag-column-restrictions.ts:11-14`). The footer-total test is wrong — `findIndex(...)` is truthy for -1, so every column
but the first gets `aggFunc = 'sum'` (F, `TO:565-566`).

Filters (`TO:304`): `relationId` (input, unset on the board), `orderState` (input, unset), `isTemplate=false`,
`organizationUnitIds`, `flagFilterEnum`, `dateRange`, paging, free text, sort, grouping. Single selection
(`TO:65`). No inline edits. No SignalR subscription — the pane refreshes only on its refresh subject or a board-setting
change (`TO:112-121, 190-224`) (F).

### 2.2 Row actions

Context menu (`TO:395-460`): Edit (opens the order dialog, also on double-click, 357-361); Delete (confirm →
`TransportOrder.delete`, then legs-to-schedule refresh, 363-393); Flag; Copy (opens the order wizard pre-filled,
531-539); Print CMR; **Create return order** → loads `GetFreightsByTransportOrder` (`TOAS:725`) and opens
`GenerateReturnTransportOrderComponent` (§7.4) → `GenerateReturnTransportOrder` (`TOAS:1836`) (`TO:462-519`).

### 2.3 Read path

`TransportOrderAppService.GetAll` → `GetAllTransportOrderDtoQuerySelector` (`TOAS:615-621`) over the SQL view
**`vwTransportOrder`** (`src/TransportOrder/GetAllTransportOrderDtoQuerySelector.cs:51-52`), the base selector's late
materialisation and `COUNT` (`src/GetAllDtoQuerySelector.cs:254-347`), default sort `Name ASC` where `Name =
TemplateName ?? ""` (`GetAllDtoQuerySelector.cs:421-426`, `GetAllTransportOrderDtoQuerySelector.cs:57`) — effectively
unordered for normal orders (I). `SetDerivedProperties` (179-) computes cost of revenue with the **legacy** trip cost
calculator (227-256) — and decides whether to do so by **reading the user's saved grid state on the server** and checking
whether the cost/margin columns are visible (543-581); the client re-queries when those columns become visible
(`TO:146-164`) (F).

**200 ms budget**: O-1 the view-based list plus a cost calculation per page (legacy calculator, repository queries per
call; trips.md §6 "Legacy"); O-2 behaviour depends on a UI setting read server-side (do not clone).

## 3. Pane: Transport trip stops (`file-stops`)

### 3.1 Purpose and layout

The stops of the one selected trip as a **vertical card timeline**, not a grid (`~/dev/OpenTMS/angular/src/app/transport/file-stops/file-stops.component.html:111-167`).
Each card shows a time block (`stopTimePipe`), a warning icon by severity (comment/warning/error/fatal, `FS:261-273`)
and user-configurable rows of fields; defaults: relation (bold), waypoint type, order number (`FS:110-118`); the layout is
a user setting `Filogic.User.TransportFile.Stop.Visibility.Order` edited in a column-visibility dialog (`FS:109,
458-476, 576-585`) (F).

### 3.2 Toolbar and actions

Toolbar (`file-stops.component.html:2-109`), all disabled without a selected trip:

| Action | Call | After |
|---|---|---|
| Add stop ▾ (per waypoint type) | waypoint type with a stationing relation → `createOrUpdateStop` immediately at position 1; else opens `StopDetailsDialog` (`FS:341-414`) | refresh stops |
| Calculate ETA ▾ (per routing service) | `RouteOptimalization.calculateEtas(fileId, serviceId?, refreshCosts:true)` (`FS:416-434`) | refresh stops **and the whole trips grid** |
| Optimise stops ▾ (per routing service) | `RouteOptimalization.optimizeStops(...)` (`FS:436-456`) | same |
| Options: column visibility, Excel export (background job, `FS:488-535`) | | |
| Refresh | `getAllStops` | also signals map and gantt (`FS:318-333`) |

Card interactions: click selects (feeds Route42, out of scope, `FS:292-299`); double-click opens `StopDetailsDialog`;
**drag to reorder** (disabled for stops with ATA/ATD, `file-stops.component.html:115`) → `modifyStopOrderNumber` then
`calculateEtas` when the trip has `recalculateOptimalRouteAutomaticly`, then refresh stops and the trips grid
(`FS:537-574`). Context menu (`file-stops.component.html:143-156`): **Complete** (disabled when ATA and ATD exist) →
`CompleteTransportFileStopDialog`; **Delete** (refused client-side when the stop carries legs, else confirm →
`deleteTransportFileStop`, `FS:623-663`); **Outsource origin activity** → `OutsourceOriginActivityDialog` by stop
(`FS:599-621`; backend `TFAS:4988`, trips.md §7).

### 3.3 Read path and coupling

`getAllStops(fileId)` (`FS:301-316`) → `TFAS.GetAllStops` (2955-3045): file, stops, visiting address type, relations,
addresses, countries, waypoint types, time slots, vehicles (+ types), then per-stop DTO with legs, order numbers and
warnings (`EtaNotInTimeSlot`, `VehicleNotAbleToAccessLocation`, trips.md §8). About 10 queries, one trip, bounded (I: fits
200 ms when the stop count is small). Loads only when the Stops tab is active, else marks dirty (`FS:146-168`). **Not
connected to SignalR** (no hub subscription in `FS`); refreshes on `refreshStops`, `routeCalculationCompleted` and its
own actions (`FS:174-202`) (F). The leg-reference string on the stop (`TransportOrderFreightLegIdentifiers`) is used
client-side to forbid deleting non-manual stops (`FS:627`).

## 4. Pane: Legs (`transport-file-legs`)

### 4.1 Purpose, columns, selection

The legs (freight legs) assigned to the selected trip (or to one selected stop, `TFL:65-73, 187-191`). Client-side row
model with all legs of the trip (`TFL:37, 194-196`). Columns: any of `GetAllTransportFileLegListDto`
(`src/TransportFile/Dto/GetAllTransportFileLegListDto.cs:8-209`: order type/name/number/date/reference, customer and its
address, origin/destination/final relation with address, postal code, phone, e-mail, coordinates, geographic regions,
waypoint types and colours, time slots, references, remarks, dates, transport unit, product, product relation code,
temperature range, quantities (amount, positions, colli, volume, weight, EPQ, loading metres, load factor), distance,
duration, CO₂, dimensions, vehicle type, invoice reference, size/ISO, container number, pincode, barcode, `cost`
(purchase price), `isCarriedOutSeparately`, freight and order custom 0-9). Multi-select with header checkbox; holding
**Ctrl switches selection to single** (`TFL:661-671`, inverted from convention) (F). Double-click on a non-editable cell
opens the order (`TFL:586-596`).

**Inline-editable** (`TFL:524-584`): `transportOrderName`, `transportOrderReference`, `originReference`, `originRemarks`,
`destinationReference`, `destinationRemarks` (text); `originTimeSlotName`, `destinationTimeSlotName`,
`transportUnitName`, `productName`, `temperatureRangeName` (pickers, double-click to edit).

Context menu (`TFL:389-450`): Edit (open order); **Carry out separately** (disabled when already separate) →
`TransportFile.carryOutSeparately({transportFileId, transportFileLegId})`, then refresh legs and stops (`TFL:342-362`;
backend `TFAS:4918`); **Outsource origin activity** (by leg, `TFL:266-281`; `TFAS:5003`); **Create return order** →
`GenerateReturnFreightComponent` → `TransportFile.generateReturnFreight({legId, amount})` (`TFL:284-332`; `TFAS:5226`).
`deleteData` → `TransportOrder.deleteLegs` exists (`TFL:364-387`) but is not on the menu (F).

### 4.2 Inline edits

`PlanboardServiceProxy.updateLegProperty({legId, propertyName, value})` (`TFL:598-623`) → `PAS.UpdateLegProperty`
(`PAS:116-150`) → `TFAS.InlineUpdateLegProperty` (795-944):

| Property | Writes | Notes |
|---|---|---|
| `originReference`, `originRemarks`, `destinationReference`, `destinationRemarks` | leg field **and** the matching order waypoint (matched on freight, waypoint type, relation) | order aggregate edited from Planning |
| `originTimeSlotId`, `destinationTimeSlotId` | leg | — |
| `transportUnitId`, `productId`, `temperatureRangeId` | the **freight** (order line) | unit cannot be cleared (keeps old on parse failure) |
| `transportOrderName`, `transportOrderReference` | the **order** (`TemplateName`, `OrderReference`) | — |
| anything else | `UserFriendlyException` | — |

Returns `QuerySingleLegListDto` = the full legs selector for one leg (947-953). Broadcast `onLegPropertyUpdated` to the
tenant group with that leg, or — for the two order-level properties — **every leg of the order** (`PAS:110-150`). No
recalculation of prices, stops or warnings is triggered (F: none in 795-944). On error the grid reloads (`TFL:636-640`).

### 4.3 Read path

`getAllLegs(fileId)` → `TFAS.GetAllLegs` (2060-2067) → `LSEL.GetAll` (20-81), which ignores paging entirely: core query
legs ⋈ freight ⋈ order ⋈ order type ⋈ users (93-130), then final-destination properties, relation properties (relation ⋈
visiting address ⋈ country, 403-439), master data (waypoint types, time slots, units, products, product relation codes,
vehicle types, temperature ranges, 563-654), derived (stops of the files filtered with `LegIdentifiers.Contains(";")` —
a scan —, purchase prices per file, geographic regions, `isCarriedOutSeparately`, 290-345), attachments: ~15 queries.
Order and customer columns come from the Orders tables in the same query; master-data names by lookup; `cost` = Σ
purchase price lines (`LSEL:319-324`) (F). Bounded by the legs of one trip (I: OK for 200 ms at normal trip sizes; the
stop scan grows with the tenant).

## 5. Pane: Legs to schedule (`freight-to-schedule`)

### 5.1 Purpose, columns, filters

All **unassigned** legs whose origin *or* destination date is in the date range (`LSEL:119-121`; `TransportFileId ==
null` because the input carries none, 123-124), the planner's work queue. Same columns, inline edits and handlers as the
Legs pane (`FTS:909-1030`, identical lists). Toolbar: search, refresh, columns, export, **flag filter** (any of leg,
freight, order, order-type flags, `LSEL:115-116`), date range and OU filter (own or board-level) (`FTS:3-26`). Client-side
row model with client-side grouping (`FTS:41, 48`). Multi-select; selection mirrored to other windows and fetches a
route per selected leg from the route function for the map (`FTS:486-573`) (F).

Context menu (`FTS:791-849`): Open order; **Add or modify legs** (feature `App.AllowCreatingLegs`, else an information
dialog) → `ManageLegsModal` (§7.1) (`FTS:741-763`); **Remove leg** (confirm → `TransportOrder.deleteLegs([id])`, which
re-links the neighbours, §7.1) (`FTS:711-739`); **Split freight** → `SplitFreightIemComponent` (§7.2) →
`TransportFile.splitTransportFileFreightLeg({legId, newSourceAmount, newTargetAmount, transportFileId: undefined})`
(`FTS:664-704`; backend trips.md §3); **Create return order** → §7.3 (`FTS:851-899`). `deleteData` calls
`TransportFile.delete` **with a leg id** (a trip-delete endpoint) but is unreachable from the menu (F, `FTS:765-789`).

### 5.2 Read path

`getAllUnscheduledLegs(from, till, …, organizationUnitIds, flagsFilterValue, …, pageSize 0, pageNumber 0, …)`
(`FTS:507`) → `TFAS.GetAllUnscheduledLegs` (2137-2143) → the same `LSEL.GetAll` — **no paging, every unassigned leg of up
to a 92-day window** in one response, ~15 queries over the whole set, wildcard search as `Contains` over many columns
(`LSEL:132-`) (F).

**200 ms budget**: L-1 unpaged list; L-2 stop-identifier string scan in derived properties (`LSEL:297`); L-3 route call
per selected leg (`FTS:531-533`).

## 6. Cross-pane coupling

### 6.1 Selection

| Trigger | Effect | Evidence |
|---|---|---|
| Trips: exactly one row selected | `selectedFile` set, `selectFile` fires; Legs reloads (if tab active, else dirty); Stops reloads (if active, else dirty); map loads routes (if active, else dirty); push-to-vehicle disabled | `TF:1052-1085`, `TFL:54-63, 140-142`, `FS:147-159` |
| Trips: 0 or >1 selected | `selectedFile = null`; Legs and Stops empty; map shows every selected trip's route | `TF:1060-1062, 1087-1160`, `TFL:197-202`, `FS:155-159` |
| Vehicle outline / gantt selects a trip | trips grid selects the row without re-emitting (`eventSource: 'VehicleOutline'`) | `TF:429-451` |
| Legs to schedule: selection | routes of the selected legs added to the map | `FTS:517-573` |
| Board date range / OU change | each listening pane refreshes now if its tab is active, else sets its dirty flag | `TF:286-320`, `FTS:172-204`, `TO:190-224` |

### 6.2 Drag and drop

Drop zones are AG Grid row drop zones exchanged through replay subjects (`PBS:58-67`), group names in
`~/dev/OpenTMS/angular/src/app/common/dropZoneGroups.ts:1-4` (F):

| From → to | Client | Server | Optimistic UI |
|---|---|---|---|
| Legs to schedule → a trip row | `TF.onDropHandler` → `assignLegsToTransportFile` (`TF:962-1049`); drop on a group with >1 child refused | `PAS.AssignTransportFileFreightLegs` **forces `DeferPostProcessing = true`** (`PAS:47-58`) → assignment manager in one transaction (trips.md §3), response with `affectedTransportFiles`; post-processing (relations, ETA/optimise per tenant setting, routes, invoices, purchase prices, attribute sync) in a fire-and-forget `Task.Run`, ending with the fat echo (`TFAS:2164-2296`) | rows removed from legs-to-schedule at drop, rolled back only when the assignment (not the follow-up) fails (`TF:975-980, 1040-1047`; `FTS:227-232`) |
| Legs to schedule → Legs pane (the selected trip) | `TFL.onDropHandler` (`TFL:452-514`) | same | rows added to Legs and removed from legs-to-schedule at drop |
| Legs pane → another trip row | `TF.onDropHandler` (legs grid registers the trips grid as drop target, `TFL:116-124`); refused when the leg is already on the hovered selected trip (`TF:967-973`) | same (the manager unrefs the old trip's stops) | — |
| Legs pane → Legs to schedule (unassign) | `FTS.onDropHandler` with `transportFileId: null` (`FTS:592-662`) | same | leg cost set to 0 locally, rows removed from Legs (`legsOptimisticallyUnassigned`) and from the vehicle outline |
| Legs to schedule → vehicle outline/gantt cell or trip | outline/gantt subjects (`FTS:361-478`) | (vehicle planboard, out of scope) | — |

After a successful assign the client itself calls `optimizeStops` for affected trips that have
`recalculateOptimalRouteAutomaticly` (`TF:1010-1014`), **racing** the server's deferred post-processing which may also
calculate ETAs or optimise (I). In the Legs pane the filter is `filter(id => this._planboardService.selectedFile.id)` —
always truthy — so every affected trip is optimised regardless of its flag (F, `TFL:486`). In the unassign handler a
missing `selectedFile` throws on `selectedFile.fileNumber` (F, `FTS:623`). After assign the trips grid refreshes Legs,
Stops, legs-to-schedule and the map per their dirty flags (`TF:1015-1038`).

### 6.3 SignalR (`planboard-signalr.service.ts`)

- One hub `signalr-planboard`, WebSockets only with `skipNegotiation` (multi-instance without sticky sessions), manual
  reconnect ladder 0.5-30 s, 10 tries, "live updates unavailable" warning (`SRS:29-53, 111-158`) (F).
- The server adds each connection to `PlanboardHub_Tenant_<id>` (`src/Planboard/PlanboardHub.cs:27-52`); messages go to
  the whole tenant group, originator included:
  `onTransportFilesUpdates` (array of fat `GetAllTransportFileDto`) from PAS inline edits (`PAS:29-45`) and from
  `PlanboardEchoBroadcaster` (`Queue` builds DTOs off-request with the old selector; `BroadcastPrebuilt` for slim
  echoes, `src/VehiclePlanboard/PlanboardEchoBroadcaster.cs:54-124`) after deferred leg assignment (`TFAS:2286-2291`),
  highlight colour (`TFAS:383`) and vehicle-planboard moves; `onLegPropertyUpdated` (array of leg DTOs) from leg inline
  edits (`PAS:152-168`).
- **Patching per pane**:

| Pane | Handles | How |
|---|---|---|
| Trips | `onTransportFilesUpdates` | `applyServerSideTransaction({route, update:[file]})` for rows **already loaded**; new trips are never inserted; sets "please refresh" when a free-text filter is active, the sorted column's value changed or a grouped column's value changed (`TF:483-503, 537-574`) |
| Legs | `onLegPropertyUpdated` | `rowNode.setData` for loaded rows (`TFL:653-659`); assignments arrive only through the optimistic path or a refresh |
| Legs to schedule | `onLegPropertyUpdated` | same (`FTS:1061-1067`); legs created by another planner or by order intake never appear until refresh |
| Stops, Orders | — | not live |

Payloads carry full DTOs (names, prices, warnings) — data, not ids (F); constitution rule 5 forbids that shape in the
rebuild.

## 7. Dialogs

### 7.1 Manage legs (`PB/manage-legs/`)

Opened from legs-to-schedule (feature `App.AllowCreatingLegs`). Edits the whole leg chain of one freight item: rows with
origin/destination waypoint type and relation, departure/arrival dates and time slots, references, remarks, distance,
duration (`DD.HH:MM`), OU, flags; add row / delete row (keeps ≥1); first departure and last arrival read-only
(`PB/manage-legs/manage-legs.component.html:2-166`; `PB/manage-legs/manage-legs.component.ts:364-523`). Dates are checked
against neighbours (`434-445`); distance is queried per relation pair (`618-`); changes to price-sensitive fields trigger
a price preview (`172-190`). Save → `TransportOrder.createOrUpdateLegs` (`manage-legs.component.ts:547-572`) →
`TOAS.CreateOrUpdateLegs` (887-990): the chain validation runs **only when the freight is not found** and would then
dereference null (`TOAS:893-897`), so in practice no server validation happens (F); deletes/inserts/updates legs one by
one, deletes cached routes, does not touch trip stops of assigned legs (I: no stop handling in 887-990). Delete leg
(`TOAS:994-`): refuses the last leg of a freight, re-links previous → next leg and re-queries the distance.

### 7.2 Split freight (`PB/split-freight-item-dialog/`)

Shows unit, product, origin/destination, and two columns (origin vs. target) of amount, positions, weight, colli,
volume, loading metres, load factor, EPQ; typing either amount pro-rates all quantities to 2 decimals; OK enabled when
the target amount > 0 (`split-freight-item-dialog.component.ts:68-109`). Returns only the two amounts; the server
re-derives quantities (trips.md §3 "Splitting a freight item").

### 7.3 Generate return freight (`PB/transport-file-legs/generate-return-freight/`)

Shows the leg and its quantities; returns only `amount` (`generate-return-freight.component.ts:93-99`); the proportional
editing is commented out (101-129). Message differs for an assigned leg (72). Server: `TFAS.GenerateReturnFreight`
(5226-) clones the freight with origin/destination swapped and assigns it to the same trip (trips.md §3).

### 7.4 Generate return transport order (`PB/transport-orders/generate-return-transport-order/`)

Form: order date (required, default now), reference, read-only order number; one row per freight with read-only
details and an editable amount ≤ original (`generate-return-transport-order.component.ts:63-123`); rows removable;
confirm requires ≥1 row and no zero amount (129-138). Result `{baseOrderId, orderDate, orderReference,
freights[{freightId, amount}]}` → `TOAS.GenerateReturnTransportOrder` (1836).

### 7.5 Complete trip / complete stop (`PB/complete-dialogs/`)

- Trip: loads the fat DTO to prefill ATD/ATA from actual or scheduled times; both required; → `TransportFile.complete`;
  then either refreshes the whole trips grid (when sorted by an actual-time column) or re-reads the fat DTO and
  `setData` on the row, and refreshes stops (`complete-transport-file-dialog.component.ts:44-89`). Server
  `TFAS.Complete` (5186-5212) sets the trip's **and every stop's** missing ATA/ATD to the same two timestamps — no status
  change, no event, no broadcast (F).
- Stop: ATA/ATD default now; → `completeStop` (`TFAS:5168-5184`: `??=` on the stop only); refreshes stops.

## 8. Mapping to opentms-next

### 8.1 What exists

- **Entities** (design 59 `next/docs/designs/59-planning-legs-and-trips.md:15-75`): `Leg` with order/stop snapshots,
  stored derived route/CO₂/allocation values, quantities, `TripId`/`TripPosition`; `TransportTrip` with status machine,
  vehicles/combination, drivers, stored derived `LoadFactor`, `Warnings`, `CostAmount`, `RevenueAmount`, `Co2Kg`, child
  `TripStop` + `TripStopLeg` (a real link table replacing the `;`-string). Indexes for the unplanned list and the trip list
  (`next/backend/modules/planning-execution/OpenTms.PlanningExecution.EntityFrameworkCore/EntityFrameworkCore/PlanningExecutionDbContext.cs:100-106, 140-148`).
- **Trip list** `GET /api/planning/transport-trip` (`TTAS:67-79`): `GridRequest` paging (≤ 200 rows per block,
  `next/docs/designs/6-platform-grid-component.md:139`), filters status[], trip date from/to, execution window, carrier,
  **one** OU, hide empty, view (today/this week/open in tenant time zone), search on the stored lower-case `SearchKey`
  (trigram index), sort whitelist (`TTAS:211-307`). Rows: number, name, date, status, archived, execution, type, carrier,
  OU, planned/actual times, distances, duration, stop/leg counts, vehicles, drivers, customers, order numbers, first/last
  stop **city**, load factor, loading metres, CO₂, cost, revenue, warnings, `CalculatedAt` (`TTAS:309-388`).
- **Trip commands** (all return the full `TransportTripDto`): `UpdateAsync` (header incl. name, remarks, driver
  instructions), `SetCarrier`, `SetVehicles`, `SetDrivers`, `AssignLegs {legIds, afterStopId?, allowOutsideWindow}`,
  `UnassignLegs`, `MoveLegs {toTripId}`, `AddStop`, `UpdateStop` (move/remarks), `DeleteStop` (manual),
  `RecordStopActuals`, `Start`, `Complete`, `Cancel`, `Archive`/`Unarchive`, `Delete`, `SplitLine`, `ReturnLeg`,
  `CarryOutSeparately`, `OutsourceOrigin`, picker, export (`TTAS:559-960`). Each change refreshes the `SearchKey`,
  publishes an ETO through the outbox and requests the `TripCalculator` (design 49) (`TOPS:197-284`). Refusals per design
  59 (`TripNotAssignable`, `LegOnOtherTrip`, `LegOutsideTripWindow`, `StopHasActuals`, `InvalidTransition`, …).
- **Leg list** `GET /api/planning/leg` with `orderId`, `tripId` (default sort trip position) or neither = **unplanned
  list** (filters origin date from/to, customer, search over names/cities/references/remarks) (`LAS:65-112`); `PUT` leg
  (references, remarks, planned dates, time windows, concurrency stamp; requests the trip calculation when dates/windows
  change, `LAS:154-178`); `SplitAtAddress` / `UndoSplitAtAddress` (`LAS:185, 237`). `LegDto` carries order number,
  customer name/reference (Orders' published lookup), snapshots, trip number/status, route, CO₂, allocated cost/revenue,
  quantities and unit/product/temperature **ids** (`next/backend/modules/planning-execution/OpenTms.PlanningExecution.Application.Contracts/Legs/LegDtos.cs:13-97`).
- **Orders list** `GET /api/orders/transport-order` (status[], date, customer, one OU, source template, view)
  (`next/backend/modules/orders/OpenTms.Orders.Application/TransportOrders/TransportOrderAppService.cs:66`;
  `next/backend/modules/orders/OpenTms.Orders.Application.Contracts/TransportOrders/TransportOrderDtos.cs:17-32`), plus confirm, cancel, approve,
  bulk delete, export.
- **Live updates**: Platform hub `/signalr-hubs/record-updates`, watch per `(type, id)`, pushes `recordUpdated` with id,
  version and calculation status — never data (`next/backend/src/OpenTms.HttpApi.Host/RecordUpdates/RecordUpdatesHub.cs:11-48`,
  `next/backend/modules/platform/OpenTms.Platform.Domain/RecordUpdates/RecordUpdate.cs`). Today **only the derived-value job notifies**
  (`next/backend/modules/platform/OpenTms.Platform.Application/DerivedValues/DerivedValueCalculationJob.cs:92, 112`).
- **Frontend**: the Transport app has a trips list (`next/frontend/src/apps/transport/views/TripsView.tsx`, refetches on
  any record update of a visible row, 109-110) and a trip page with Legs and Stops tabs; hooks for unassign, carry out
  separately, return leg, split, outsource, stops (`next/frontend/src/domains/planning-execution/trips.ts:252-380`). **No hook
  for the unplanned list, assign or move** (no caller of `assign-legs`/`move-legs` in `domains/` or `apps/`). The Planboard
  app is a placeholder (`next/frontend/src/apps/planboard/index.tsx:1-6`), mapped to Planning & Execution only, feature
  `Apps.Planboard` default off (`next/frontend/src/app/apps.config.ts:140-147`;
  `next/backend/modules/platform/OpenTms.Platform.Application.Contracts/Features/PlatformFeatureDefinitionProvider.cs:21`).
  The Planboard workspace layout is library gap **GAP-07** (`next/docs/conventions/ui/component-map.md:104`).

### 8.2 Gaps per pane

**Trips pane**

| Legacy | Next | Gap |
|---|---|---|
| OU filter multi-select; flag filter; FMS-sourced; carrier filter | one OU; carrier; no flags concept (no `Flags` on trips/legs) | multi-OU; flags decision (Q7) |
| Server-side grouping with counts | `GridRequest.rowGroupCols` reserved, ignored (`6-platform-grid-component.md:99-105`) | grouping (Q3) |
| Columns: remarks, driver instructions, first/last stop **names**, stop names/cities lists, weight/amount/positions/colli/EPQ, barcodes, container numbers, invoice and order references, vehicle types, licence plates, FMS status, e-mail status, attachments, sensor values, exchange-gateway count, highlight colour, custom 0-9 | not in `TransportTripRowDto` (attributes jsonb exists on the trip, not in the row) | choose columns (Q9) |
| Inline edit name/remarks/driver instructions/carrier/vehicles/drivers in the grid | commands exist (`UpdateAsync` takes the whole header, so a one-field edit needs the row's full header + concurrency stamp) | narrow commands or the full header on the row (Q5) |
| Primary vehicle side effects (default trailers, stationing relation as departure/arrival, carrier from vehicle, drivers from vehicles) | `SetVehicles` sets ids only (`TTAS:633-651`) | Q6 |
| Context menu: complete, delete, archive, print CMR, split/convert, highlight, flag, e-mail, exchange gateway | complete, delete, archive, cancel exist; CMR is transport domain; no highlight/flag/convert | Q7, Q13 |
| Live patch of loaded rows, "please refresh" prompt | per-row record updates; TripsView refetches the block | §8.3 |
| Feature gate: Planboard | trip endpoints require `Apps.Transport` + `Apps.Transport.Trips` (`TTAS:60-66`) | Q15 |

**Orders pane**: list endpoint exists in Orders; the Planboard app mapping excludes Orders (Q1). Missing: flags filter,
multi-OU, "create return order" (no Orders command), copy wizard is a Transport-app view. Cost/margin columns come from
stored derived values (design 58) — no server-side grid-state gating needed.

**Stops pane**: `GET /{id}` returns stops with planned/estimated/actual times, cumulative distance, legs, remarks
(`TransportTripDtos.cs:97-`); commands add manual stop, move, remove manual, record actuals exist. Missing: "Optimise
stops" (design 59 assumption 1: VROOM in the Planboard run) and "Calculate ETA" as user actions (the calculator recomputes
on every change instead); stop-level warnings live on the trip's `Warnings` with `stopId`; outsource **by stop** (only by
leg exists); "complete stop" = `RecordStopActuals`; stop card layout setting.

**Legs pane**: `GET /api/planning/leg?tripId=` (paged ≤200, legacy loaded all), unassign/move/carry-out/return/outsource
exist. Missing: names for unit/product/temperature (ids only, frontend must resolve), addresses/postal code/phone,
geographic regions, final destination, custom properties, purchase price per leg (replaced by allocated cost); inline
edit of time slots, unit, product, temperature, order name/reference (Orders facts, Q5); leg list for a single stop.

**Legs-to-schedule pane**: unplanned list exists. Missing: OU filter, flags, date filter on origin **or** destination
(next filters origin only, `LAS:83-91`), all rows (legacy unpaged, client grouping; next ≤200 per block, Q2); split and
return on an **unplanned** leg (next requires the leg to be on the calling trip, `TTAS:868-914`; Q11); "manage legs"
free chain editing (next: split at address / undo only, Q12); "remove leg" = undo split only; new-row live insert (§8.3).

**Dialogs**: complete trip → `CompleteAsync(TripTimestampDto)` (one timestamp; legacy took ATD and ATA); complete stop →
`RecordStopActuals`; split freight → `SplitLine {legId, quantity, toTripId?}` (trip-bound); return freight →
`ReturnLeg` (trip-bound, no amount: the whole quantity); return order → none; manage legs → none.

### 8.3 The 200 ms target in the new design

| # | Path | Assessment |
|---|---|---|
| N-1 | Trip list: 2 `COUNT`s (total and filtered) + page with correlated stop count/first/last city + type names + Master Data relation names + vehicles/drivers with details + combinations + vehicles + drivers + legs of the page + Orders references (`TTAS:67-91, 309-388`): ~11 round trips, all keyed by the page's ids | I: fits 200 ms at 100-200 rows when every lookup is a keyed batch; verify the `SearchKey.Contains` translation uses the trigram index (Npgsql may emit `strpos`, which does not) |
| N-2 | Every trip command returns `ToDtoAsync` (~9 round trips incl. Master Data and Orders lookups, `TTAS:390-`) and runs `RefreshSearchKeyAsync` synchronously (3-4 more lookups, `TOPS:296-319`) | at risk for 200 ms on `AssignLegs` with many legs (also `EnsureOrdersPlannable`, source-trip recomposition) — consider returning the row DTO or 204 + record update |
| N-3 | Unplanned list: indexed partial index on `(TenantId, OriginPlannedDate) WHERE TripId IS NULL`; search is `ToLower().Contains` over 7 columns without an index (`LAS:97-104`) | fine with a date range; search over a large backlog needs a trigram/search column |
| N-4 | Derived values never on read (AD-07): load factor, warnings, cost, revenue, CO₂ are columns | meets the target by design; the planner sees `CalculatingIndicator` until the job pushes |
| N-5 | `SearchKey` copies carrier, vehicle, customer and order names onto the trip row | not a read table, but denormalised data from other domains that goes stale on a rename (design 59 build note 5; Master Data vehicle changes are not watched, design 59:125-127) — Q8 |
| N-6 | Live updates: per-record ids only; no notice for "a new unplanned leg", "a trip entered my date range", or a plain change without recalculation (`SetDrivers` requests none, `TTAS:653-664`) | the board needs collection-level notices and a notifier call per command (Q4) |

## 9. Scenario list

F = fact from legacy code, I = inferred intent. "Must" = the rebuild must pass it; where the legacy behaviour is a bug the
scenario states the corrected outcome.

**Board and filters**

- **P-01** Planner sets the board date range and OUs once — every listening pane (trips, orders, legs to schedule) reloads with them; a pane with its own control keeps its own (F, §0).
- **P-02** Planner sets a date range longer than 92 days — refused or clipped (F, settings service 19, 139).
- **P-03** Planner changes the board filter while a pane is hidden — that pane reloads when it is shown, not before (F, dirty flags).
- **P-04** Planner reopens the board — date range, OUs, column layout and pane layout are restored per user (F, `GB:1145-1153`).

**Trips pane**

- **P-05** Planner opens the board — trips with trip date in range, not archived, not cancelled, page by page, stable order (corrected: legacy sorts by non-unique name).
- **P-06** Planner filters by several OUs — only trips of those OUs (F, `SEL:106`).
- **P-07** Planner searches "rotterdam" — trips whose number, name, carrier, vehicle, customer, order number or stop city matches (F/I).
- **P-08** Planner groups by carrier — group rows with counts; expanding a group loads its trips (F, `TFAS:1290-1361`) (subject to Q3).
- **P-09** Planner sorts by a derived column (cost, load factor) — allowed, because the value is stored (corrected: legacy refuses revenue).
- **P-10** Planner selects exactly one trip — Legs and Stops show that trip; selecting a second trip empties them (F, `TF:1052-1062`).
- **P-11** Planner edits a trip's name/remarks/driver instructions inline — saved, row updated, other planners' boards show it within seconds (F, `PAS:60-76`).
- **P-12** Planner picks a carrier inline from a list scoped to the board's OUs — saved; the carrier's cost line recalculates in the background (F, `TFAS:545-576`, AD-07).
- **P-13** Planner picks a primary vehicle — trailers/defaults applied per Q6; drivers follow the vehicles when configured (F, `TFAS:578-669`).
- **P-14** Planner sets drivers inline (up to 3) — saved in order (F, `TFAS:745-793`; next ≤3).
- **P-15** Planner tries to edit a completed or archived trip inline — refused with a message (corrected: legacy allows).
- **P-16** Two planners edit the same trip — the second save is refused on the concurrency stamp, not silently overwritten (corrected).
- **P-17** Planner completes a trip with departure and arrival times — status Completed, missing stop actuals filled, row updated, Stops refreshed (F, `TFAS:5186-5212`; next status machine).
- **P-18** Planner deletes a Planned trip — confirm; its legs become unplanned and appear in Legs to schedule (F/I, design 59).
- **P-19** Planner archives a completed trip — it leaves the list (F, `TF:1200-1224`).
- **P-20** Another planner changes a trip that is on my screen — my row updates without a reload; if the change moves it out of my sort/filter/group I get a "refresh" hint (F, `TF:537-574`).
- **P-21** A trip is created elsewhere (order with "Tevens trip") in my date range — it appears on my board without a manual refresh (I; legacy does not).

**Orders pane**

- **P-22** Planner views orders in the board range, filters by OU, opens one by double-click (F, `TO:304, 357-361`).
- **P-23** Planner deletes an order from the board — confirm; its unplanned legs disappear from Legs to schedule (F, `TO:363-393`).
- **P-24** Planner creates a return order from an order — picks freight items and amounts ≤ original, a date and reference; the new order's legs appear unplanned (F, §7.4).

**Stops pane**

- **P-25** Planner views the stops of the selected trip in order with planned/estimated/actual times and warnings by severity (F, §3.1).
- **P-26** Planner drags a stop to another position — saved; times recalculate in the background; a stop with actuals cannot be moved (F, `FS:537-574`, `file-stops.component.html:115`).
- **P-27** Planner adds a manual stop of a waypoint type with a default (stationing) relation — inserted without a dialog (F, `FS:345-366`).
- **P-28** Planner deletes a manual stop — confirm, removed; deleting a stop that carries legs is refused (F, `FS:623-663`).
- **P-29** Planner records a stop's actual arrival/departure ("complete stop") — saved; the trip's progress follows (F, `TFAS:5168-5184`).
- **P-30** Planner asks to optimise the stop order (per Q10) — runs in the background; the board shows calculating and then the new order (I).
- **P-31** Planner outsources the origin activity of a stop to a hub on another trip (F, `FS:599-621`) — by stop or via its legs (Q).

**Legs pane**

- **P-32** Planner views the legs of the selected trip in stop order with order, customer, ends, dates, quantities, distance, allocated cost (F/I).
- **P-33** Planner edits a leg's origin/destination reference or remarks inline — saved; the order sees the planner's value without the leg being unassigned (F, `TFAS:795-944`; next build note 14).
- **P-34** Planner edits a leg's time window — saved; the trip recalculates; an outside-window result shows as a warning (next `LAS:154-178`).
- **P-35** Planner carries out a leg separately — the leg gets its own stop pair at the end; stops refresh (F, `TFL:342-362`).
- **P-36** Planner creates a return leg from an assigned leg — origin/destination swapped, on the same trip (F, `TFAS:5226`).

**Legs to schedule**

- **P-37** Planner sees every unplanned leg whose origin or destination date is in range, filterable by OU and customer (F, `LSEL:119-130`).
- **P-38** Planner drags one or several unplanned legs onto a trip row — they leave the list at once, appear on the trip, stops are composed per the trip's mode, the trip recalculates in the background (F, §6.2).
- **P-39** Planner drags legs onto the selected trip's Legs pane — same as P-38 (F, `TFL:452-514`).
- **P-40** Planner drags legs onto a group row with more than one trip — refused with "drop on a trip" (F, `TF:985-996`).
- **P-41** Planner drags legs onto a cancelled/completed/archived trip — refused, legs return to the list (corrected; next `TripNotAssignable`).
- **P-42** Planner drags a leg whose dates fall outside the trip window — refused unless confirmed ("allow outside window") (next design 59).
- **P-43** The server refuses an assignment — the optimistic change is rolled back in both panes and a message names the reason (F, `TF:1040-1047`).
- **P-44** Planner drags legs from a trip's Legs pane back to Legs to schedule — unassigned, emptied stops removed (F, `FTS:592-662`; trips.md §3).
- **P-45** Planner drags legs from the Legs pane onto another trip — moved; both trips recompose and recalculate (F, `TFL:116-124`; next `MoveLegs`).
- **P-46** Planner splits an unplanned leg's quantity (e.g. 33 of 34 pallets) — two legs, quantities pro-rated, both unplanned (F, `FTS:664-704`; Q11).
- **P-47** Planner removes an intermediate leg of a freight's chain — neighbours re-linked; the last leg cannot be removed (F, `TOAS:994-`; Q12).
- **P-48** A new order is confirmed elsewhere — its legs appear in Legs to schedule without a refresh (I; legacy does not).
- **P-49** A leg in the list is edited by another planner — the row updates (F, `FTS:1061-1067`).

**Tenancy, permissions, performance**

- **P-50** A planner of tenant A never sees, receives or can act on trips, legs or live notices of tenant B (constitution rule 1; legacy groups by tenant id, `PlanboardHub.cs:27-52`).
- **P-51** A user without the assign permission sees the panes but cannot drag (next `PlanningExecutionPermissions.TransportTrips.AssignLegs`).
- **P-52** Every pane read and every command answers within 200 ms p95 at the tenant volumes of the k6 profile; derived values arrive by push (PO target).

## 10. Open questions for the PO

| # | Question | Recommendation |
|---|---|---|
| Q1 | The Orders pane reads Orders, but the app mapping gives the Planboard only Planning & Execution (`apps.config.ts:140-147`, `/CLAUDE.md` apps table). Add Orders to the Planboard's mapping? | Yes: add Orders (read via its existing list endpoint and order page link); it is a PO-owned mapping change. |
| Q2 | Legs to schedule loads every unplanned leg of up to 92 days at once and groups client-side. Keep "all rows", or page it like every grid (≤200 per block)? | Page it server-side (constitution/query discipline); keep the 92-day cap; multi-select across pages via `GridSelectionDto`. |
| Q3 | Server-side grouping (legacy: any column, with counts) is reserved in `GridRequest` but not built. Needed in trips and legs-to-schedule? | Build grouping for a short whitelist of stored columns only (trip: status, date, carrier, type, OU; leg: origin date, customer, origin/destination city); no grouping on computed or foreign columns. |
| Q4 | Live updates: the legacy board pushes full DTOs to the whole tenant; next pushes ids per watched record and only after calculations. The board needs notices for new/removed rows (new unplanned legs, trips entering the range) and for plain changes. | Extend Platform record-updates (critical, design gate): every Planning command notifies its records after commit; add tenant collection groups (`PlanningExecution.UnplannedLegs`, `PlanningExecution.TransportTrip` by trip date) carrying ids only; the grid refetches those ids through a `?ids=` list filter. |
| Q5 | Inline edits: which fields stay editable in the panes? Legacy also edits order name/reference and freight unit/product/temperature from a leg (Orders facts). | Trips: name, remarks, driver instructions, carrier, vehicles, drivers via narrow commands with concurrency stamp. Legs: only Planning-owned fields (references, remarks, windows). Order/freight fields: open the order (no cross-domain writes). |
| Q6 | Choosing a primary vehicle in legacy also sets default trailers, departure/arrival = the vehicle's stationing relation, the carrier from the vehicle and drivers from the vehicles. Next does none of this. | Keep vehicle combinations for trailers; apply carrier-from-vehicle and the vehicle's stationing relation when the trip has no departure/arrival set; default drivers only when the trip has none. Confirm. |
| Q7 | Flags (filter on trips, legs, orders) and highlight colour per trip have no counterpart in next. | Drop flags; offer a highlight colour on the trip only if planners use it (check usage on production data first). |
| Q8 | `SearchKey` stores carrier, vehicle, customer and order names on the trip row (a denormalised copy of other domains' data, stale on rename). Acceptable under the "no own read tables" rule? | Keep it (it is one column on the aggregate, written in the same transaction) and refresh it on Master Data/Orders rename events; otherwise drop foreign names from trip search. |
| Q9 | Which legacy trip columns from other domains must the trip row carry (barcodes, container numbers, invoice references, quantities, FMS status, e-mail status, sensor values, exchange-gateway count, custom properties)? | Keep order numbers, customers, quantities (summed by the TripCalculator into stored columns), first/last stop name+city, remarks, attributes; drop sensor values and exchange-gateway count; e-mail/FMS status when those domains exist. |
| Q10 | "Optimise stops" and "Calculate ETA" buttons and the auto-optimise flag: in this run or the later routing run (design 59 assumption 1)? | In scope as a background command (`POST /{id}/optimize-stops`, derived-value job, result pushed); "Calculate ETA" disappears (the calculator recomputes on every change). |
| Q11 | Split and return on an **unplanned** leg (legacy: from Legs to schedule) — next only allows them on the calling trip. | Allow both on the Leg service without a trip; on a trip they keep the trip's checks. |
| Q12 | "Manage legs" (free editing of a freight's leg chain incl. manual distance/duration, OU, flags) and "Remove leg". Next has split-at-address/undo. | Split-at-address + undo replaces it; manual distance/duration goes (routing computes it); "remove leg" = undo split. |
| Q13 | Context-menu actions of the trips pane outside Planning (print CMR, e-mail, split/convert vehicle, exchange gateway, send/remove FMS): which belong in the Planboard rebuild? | Print CMR when the transport-domain CMR design lands (PO 2026-10-16); "convert vehicle" as a later Planboard PBI; e-mail and FMS out of this run. |
| Q14 | Workspace layout is GAP-07 (dockview, flexlayout-react, or fixed resizable panes with tabs). | Fixed `Resizable` layout with tabs per area (option c) for the first run; a docking library only if planners need free docking. |
| Q15 | The trip and leg endpoints require `Apps.Transport.Trips`; the Planboard has `Apps.Planboard`. A tenant with the Planboard but not Transport trips gets 403s. | Gate the Planboard's reads on `Apps.Planboard` OR `Apps.Transport.Trips`, and add Planboard nav-node features per pane (PO rule: every nav node is a feature). |
| Q16 | Multi-selection: legacy shows no legs/stops when more than one trip is selected (only the map shows all). Keep? | Keep: detail panes follow exactly one trip; multi-select serves bulk actions (archive, complete). |
| Q17 | Complete trip: legacy asks ATD and ATA and stamps both on every stop without actuals; next `Complete` takes one timestamp. | Ask both (departure defaulting to the planned or recorded start) and fill only missing stop actuals, as legacy. |
