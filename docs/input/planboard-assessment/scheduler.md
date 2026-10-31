<!-- Appendix to the Planboard rebuild assessment. Produced 2026-10-31 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 2cf2c1558; copies under .claude/worktrees/ ignored) and the opentms-next repository at the same date. Not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in those checkouts. F = fact read in code, I = inferred. -->

# Legacy OpenTMS assessment: Vehicle planboard (to be rebuilt as the Scheduler) and the Resources pane

Legacy source root: `/Users/marinusbrink/dev/OpenTMS`. Abbreviations used below (all relative to that root):

| Short | Path |
|---|---|
| `G` | `angular/src/app/planboard/vehicle-planboard-gantt/vehicle-planboard-gantt.component.ts` (3,300 lines) |
| `G.html` | `angular/src/app/planboard/vehicle-planboard-gantt/vehicle-planboard-gantt.component.html` |
| `GCS` | `angular/src/app/planboard/vehicle-planboard-gantt/vehicle-planboard-gantt-color-settings.service.ts` |
| `O` | `angular/src/app/planboard/vehicle-outline/vehicle-outline.component.ts` (1,487 lines) |
| `OR` | `angular/src/app/planboard/vehicle-outline/vehicle-outline-row/vehicle-outline-row.component.{ts,html}` |
| `OSD` | `angular/src/app/planboard/vehicle-outline/set-vehicle-outline-settings-dialog/set-vehicle-outline-settings-dialog.component.ts` |
| `OS` | `angular/src/app/planboard/vehicle-outline/vehicle-outline.service.ts` |
| `PSS` | `angular/src/app/planboard/planboard-settings-panel/planboard-settings.service.ts` |
| `PB` | `angular/src/app/planboard/planboard.component.ts` |
| `FTS` | `angular/src/app/planboard/freight-to-schedule/freight-to-schedule.component.ts` (the "legs to schedule" grid, the drag source) |
| `TFC` | `angular/src/app/planboard/transport-files/transport-files.component.ts` (the trip grid pane) |
| `RES` | `angular/src/app/planboard/resources/resources.component.ts` |
| `VPAS` | `aspnet-core/src/FiLogic.Application/BusinessServices/VehiclePlanboard/VehiclePlanboardAppService.cs` |
| `PEB` | `aspnet-core/src/FiLogic.Application/BusinessServices/VehiclePlanboard/PlanboardEchoBroadcaster.cs` |
| `TFAS` | `aspnet-core/src/FiLogic.Application/BusinessServices/TransportFile/TransportFileAppService.cs` |
| `SEL` | `aspnet-core/src/FiLogic.Application/BusinessServices/TransportFile/GetAllTransportFileDtoQuerySelector.cs` |
| `CAL` | `aspnet-core/src/FiLogic.Application/BusinessServices/Calendar/CalendarAppService.cs` |

New-system paths are relative to `/Users/marinusbrink/dev/opentms-next`. Legacy "transport file" (TransportFile, TF) = new "transport trip"; legacy "leg" (TransportOrderFreightLeg) = new `Leg`. Read-only research; nothing was built or run.

**PO decisions this appendix starts from (not re-asked):** the legacy Vehicle planboard pane is rebuilt as the **Scheduler**; a user can open **several Scheduler panes** in one planboard, each in one of three modes: **tractor planning, trailer planning, driver planning**; the Resources pane is in scope; a paid scheduler component may be trialled, open source is fine too; every server call answers within **200 ms**; Planning keeps **no denormalised read tables** for the planboard.

## 1. The two implementations

### 1.1 Which is current

| | Vehicle outline | Vehicle planboard (gantt) |
|---|---|---|
| Component | `O` (selector `vehicle-outline`) | `G` (selector `vehicle-planboard-gantt`) |
| First commit | 2021-01-14 "created additional component for planboard" (F, `git log`) | 2024-03-27 "gantt initial version" (F); last change 2026-09-22 (F) |
| Tab id / label | `TabNames.vehiclePlanboard` (`angular/src/app/planboard/tabNames.ts:3`) | `TabNames.vehiclePlanboardGantt` (`tabNames.ts:10`), label `l('VehiclePlanboard')` (`PB:299`) |
| Permission | `Applications.OpenTmsApp.VehiclePlanboardApp` (`aspnet-core/src/FiLogic.Core/Authorization/AppPermissions.cs:71`, `PB:58`) | `Applications.OpenTmsApp.VehiclePlanboardGanttApp` (`AppPermissions.cs:72`), depends on feature `App.VehiclePlanboardGanttAppFeature`, **default false** (`FiLogic.Core/Features/AppFeatureProvider.cs:166`, `AppAuthorizationProvider.cs:129`) |
| Rendering | hand-built CSS grid of day × hour-step cells with Angular CDK drag-drop (`OR.html:3-53`) | commercial library **gantt-schedule-timeline-calendar 3.35.2** (GSTC, `angular/package.json:119`; licence key from `environment.gstcLicenseKey`, `G:2200`) with plugins TimelinePointer, Selection, ItemResizing, ItemMovement, CalendarScroll (`G:72-77`, `G:2279-2475`) |
| Live updates | none: every mutation refetches everything (F, `O:810`, `O:865`, `O:912`) | SignalR patches plus optimistic UI plus a 30 s safety net (§3.4) |

The two tabs are **mutually exclusive**: opening one closes the other (`PB:589-595`), and each tab id can be open once (the docking item id is the tab name, `PB:503-516`). The **gantt is the current implementation** (F: newest, actively maintained, carries the secondary mode, the optimistic and SignalR work, and the tab label "VehiclePlanboard"); the outline is kept for tenants without the gantt feature (I). A bug in the layout restore checks the outline tab twice: when the user lacks the gantt permission and the outline tab is open, it deletes the gantt tab id (`PB:146-151`) (F).

### 1.2 What the gantt shows

- **Rows** = vehicles of the current mode (F, `G:293-297`): *primary* mode lists `VehicleService.getList(isPrimaryVehicle: true, includeVehicleOrganizationUnits: true)`, *secondary* mode lists `getList(false, …)` (`G:3033-3034`). Legacy `Vehicle` has only the boolean `IsPrimaryVehicle` (`aspnet-core/src/FiLogic.Core/BusinessEntities/Vehicle/Vehicle.cs:41`): "secondary" means every non-primary vehicle (trailers and anything else), not specifically trailers (I).
- **Row label** (`G:1576-1598`): line 1 = name and licence plate (deduplicated), line 2 = last-seen location (`LastSeenLocation`, else lat/lon to 4 decimals, else "unknown location"); an online dot by CSS class `vop-row--online` (`G:1611`), online = `LastSeen` newer than a cut-off (`aspnet-core/src/FiLogic.Application/BusinessServices/Vehicle/VehicleAppService.cs:423`). Alternating row stripes (`G:1606`). Double-clicking a row label opens the vehicle edit dialog (`G:368-389`).
- **Synthetic "Unplanned" row** (filter `showUnplanned`, `G:435`, `G:1535-1559`): in primary mode it collects trips **without a secondary vehicle** ("unplanned trailers"), in secondary mode trips **without a primary vehicle** (`G:562-571`, labels `G:576-590`). A trip in that row is removed from its own vehicle row (`G:2131-2141`): a trip never appears twice in one scheduler (F).
- **Bars** = transport files whose row vehicle is the mode's slot: `PrimaryVehicleId` in primary mode, `SecondaryVehicleId` in secondary mode (`G:554-557`). The **third vehicle is never a row** (F). Bars sort by file number within the row (`G:2139`).
- **Bar time** = `actualTimeOfDeparture ?? scheduledTimeOfDeparture` to `actualTimeOfArrival ?? scheduledTimeOfArrival` (`G:1674-1676`); files without both ends are not drawn (`G:1676`); files outside the range ± 1 week are dropped because "GSTC OOMs on items beyond ±1 week" (`G:1669-1678`); bars cut at the range edge get `left_cut`/`right_cut` classes (`G:1690-1691`, slot `G:2520-2545`); minimum width 100 px (`G:1721`).
- **Bar content** = a user-configured grid of rows × columns of trip fields (`schedulerSettings.visibleColumns`, `G:2604-2700`), formatted by `getFormattedField` (task status localised, load factor as %, dates `ll`, `G:3231-3242`). Item height 24 px for one row, 48 px for more (`G:391-396`); when the bar shows more than two rows or more than one column a hover tooltip shows the full set (`G:1511-1516`, `G.html:273-285`). Warnings render as a red triangle when the `warnings` column is visible (`G:2680-2690`); FMS device status as an icon (`G:2664-2678`).
- **Colours.** A left stripe in the brand colour, or the trip's `HighlightColor` (hex or CSS name) which also widens the stripe and colours the border (`G:1702-1714`, `G:2615-2618`). Status colours come from `GCS`: twelve tenant settings `App.ColorSettings.TransportFile.OrderState.{Unknown,Requested,Accepted,Planned,InProgress,Completed}.{Background,FontColor}` plus `App.ColorSettings.VehiclePlanboard.TransportFile.Item.{Background,FontColor}` (`GCS:29-50`), exposed as CSS variables on the host (`G:236-240`, `GCS:57-85`) and applied **only to the task-status cell** when that column is shown (`VisibilityElementUtils.ts:18-46`, `vehicle-planboard-gantt.component.scss:1336-1359`); load factor cells colour red > 100 %, green ≥ 85 % (`VisibilityElementUtils.ts:26`). Task status is derived, not stored: Completed when both actuals are set and arrival ≤ now, InProgress when departed, else Planned (`aspnet-core/src/FiLogic.Application/BusinessServices/TransportFile/Dto/GetAllTransportFileDto.cs:9-15`) (F).
- **Time axis.** GSTC's own header is hidden (`headerHeight: 0`, `G:2204`); a custom CSS-grid header shows one column per day (`D dd`, "today" marked) with hour labels every `timeStep` hours (`G:869-892`, `G.html:187-214`). The range is pinned to local midnights (`G:2025-2033`). **Zoom is not user-controlled**: zoom = log2(range ms / chart width) so the whole range fits the pane width (`G:841-856`, `G:2059-2065`), recomputed on resize (`G:921-927`). Hourly grid cells (`G:2006-2018`).
- **Working hours** `workingDayStart`–`workingDayEnd` (HH:mm, falling back to integer `timeFrom`/`timeTill`) are shaded per hour cell (`G:1258-1284`, `G:2253-2265`).
- **Now-line**: a brand-coloured vertical line at the current time, repositioned every 60 s (`G:1223-1256`, `G.html:217-222`).
- **Info strip** above the chart (`G:726-764`, `G.html:146-171`): vehicle count, file count, in-transit count (task status 4), idle vehicles (no file in range), fleet utilisation % = Σ scheduled durations clipped to the range ÷ (vehicles × range hours). All computed client-side from loaded data (F).
- **Date range.** Follows the planboard-wide period from the settings panel (`PSS`, max 92 days, `PSS:19`) unless the pane "manages its own range" (`G:502-509`, `G.html:56-81`). The gantt renders **at most 14 days** (`VEHICLE_OUTLINE_MAX_SPAN_DAYS`, `G:128`); a wider planboard period makes the pane switch to its own range clamped to 14 days (`G:2977-3019`). Default own range: today + 6 days (`G:332-335`).
- **Filters** (toolbar `G.html:3-144`, filter menu `G.html:226-271`): vehicle multi-select (the active mode button doubles as the picker, `G.html:12-54`), relation groups (vehicle's `relationGroupId`, `G:1450-1461`), free-text search over row label fields and the visible bar fields of the vehicle's files (`G:1464-1508`), hide empty files (server-side `hideEmptyTransportFiles`, `G:1121-1128`), show unplanned, manage own date range, manage own organisation units. **Organisation units filter only the vehicle list client-side** (`G:299-306`); the trip query passes no organisation unit (`G:3110-3139`) (F).
- **Per-mode memory**: primary and secondary each keep their own filter snapshot, persisted as one JSON user setting `Filogic.User.Planboard.VehicleOutline.Scheduler.ModeFilters` together with the active mode (`G:163-190`, `G:604-715`). Other user settings: `…Scheduler.DateRange`, `…OrganizationUnits`, `…Settings` (bar layout, step, working day) (`G:163-175`, `G:3141-3229`).
- **Settings dialog** (`OSD`, shared with the outline): working day start/end (end after start, `OSD:12-21`), hour step 1/2/4/6/8 (`OSD:34`), and the bar layout editor: rows of columns chosen from `TransportFile.GetAllAttributes` (`OSD:132-152`, `TFAS:1139-1171`; default suggestions Warnings, FileNumber, FileDate, TransportCompany, PrimaryVehicle, SecondaryVehicle, SecondaryVehicleType, Drivers, scheduled times, ScheduledDistance, LoadingMeters, TaskStatus, EstimatedCarbonFootprint, `TFAS:1154-1156`), bold per cell, rows and columns reorderable by drag (`OSD:171-208`). The gantt **shares the storage key** `Filogic.User.Planboard.VehicleOutline.Scheduler.Settings` with the outline (`G:170`, `O:104`); the TODO to separate them is open (`G:160-161`) (F).
- **Planboard settings panel** (`planboard-settings-panel/`): organisation units and the planboard-wide period (date pill with tenant presets `App.DateRangeSelection.DefaultConfiguration`, cap 92 days, `planboard-settings-panel.component.html:1-38`, `PSS:18-22`); the panel is disabled while an active tab has not yet applied the latest change (`PSS:105-112`); a saved preset range is stored by name so it follows the calendar (`PSS:211-225`).

### 1.3 What the outline shows (and how it differs)

- Rows = **primary vehicles only** (`isPrimaryVehicle = true`, never toggled, `O:73`, `O:367`); no secondary mode, no unplanned row, no info strip, no now-line, no working-hours shading, no organisation unit filter (F).
- Time axis = per day only the hour columns from `timeFrom` to `timeTill` in steps of `timeStep` (default 0–20 h, step 4 h, `O:121-126`, `O:427-444`): **night hours outside that window are not drawn** (F). Bars snap to cells: position = cell of the departure hour plus a left padding percentage (`O:1327-1366`), width = number of cells (`O:1203-1318`).
- **Overlap stacking**: overlapping bars in one row are pushed to further levels and the row grows (`getAlignedRow`, `O:1071-1162`) (F). The gantt has no such code; whether GSTC stacks overlapping items by default was not verified (I).
- Bar background = `HighlightColor` (`OR.html:30`); bar content through `transport-file-view-details` with the same column settings (`OR.html:46-50`).
- Selection = a checkbox per bar, which only forwards **one** file at a time to the trip grid (`O:568-576`).
- Filters: vehicles, relation groups, name search plus visible bar fields, hide empty (`O:277-327`, `O:1369-1433`); settings keys `…VehicleOutline.Scheduler.{Settings,DateRange,VehicleFilter,RelationGroupFilter,EmptyFilesFilter}` (`O:104-108`).

### 1.4 Behaviour that exists in only one implementation

| Only in the gantt | Only in the outline |
|---|---|
| Secondary (trailer) mode with its own filters (`G:681-715`) | Overlap stacking in levels (`O:1071-1162`) |
| Unplanned row (`G:1535-1559`) | Resize from the **start** of a bar (`OR:124-153`, `O:690-730`) |
| Multi-select and bulk move/resize (`G:2281-2311`, `G:2414-2475`) | Day window `timeFrom`–`timeTill` hiding night hours (`O:427-444`) |
| Now-line, working-hours shading, info strip, row location/online dot | Leg removal handler `removeFreightsFromTransportFile` (`O:224-233`, `O:895-938`) |
| Optimistic placeholder and SignalR patching (§3.4) | Client-side incoming-invoice recalculation before save (`O:960-969`) |
| Trip type picker when dropping legs on an empty cell (`G:993-1023`) | |
| Vehicle edit dialog on row double-click (`G:368-389`) | |
| Own date range / own organisation units per pane, 14-day cap | |

## 2. Interactions

### 2.1 Gantt

| Interaction | What happens | Citation |
|---|---|---|
| Select | Click / marquee selects items (Selection plugin, cells off); exactly one selected → broadcast as the planboard's selected file (stops, legs, map panes follow); several → selection cleared for the other panes | `G:2281-2311` (F) |
| Move in time and/or to another row | ItemMovement; on drop every moved item (multi-select included) becomes `UpdateTransportFileScheduledVehicleInput { transportFileId, vehicleId = target row, scheduledTimeOfDeparture, scheduledTimeOfArrival }` → `MultipleUpdateTransportFileScheduledVehicle` (primary) or `…SecondaryVehicle` (secondary mode); moves into or out of the Unplanned row are ignored | `G:2414-2475`, `G:2835-2874` (F) |
| Resize | ItemResizing; start snap returns the raw time, end snap rounds to `interval = 900` on a millisecond value (≈ 0.9 s, effectively no snapping) | `G:2313-2358`, `G:2330-2333` (F; the unit mismatch is I) |
| Resize commit | Inputs for changed starts and changed ends are collected in one list; if **any** end changed, the whole list goes to `MultipleUpdateTransportFileScheduledTimeOfArrival`, so a start change in the same batch is written as an arrival | `G:2360-2410` (F) |
| Drop legs on a bar | From the "legs to schedule" grid (ag-Grid row drag, all selected rows): `assignFreightsToTransportFile` → legs removed optimistically from the source grid → `AssignLegsToVehicleOrTransportFile { legIds, transportFileId }` | `FTS:415-437`, `G:945-961`, `G:2876-2965` (F) |
| Drop legs on an empty cell | Blocked on the Unplanned row; optimistic grey "pending" bar from the drop time with a duration = Σ leg durations (TimeSpan strings) else 1 h; trip type: none → no type, one → that type, several → dialog (cancel rolls back); then `AssignLegsToVehicleOrTransportFile { legIds, vehicleId (primary) or secondaryVehicleId (secondary mode), cellStartTime, transportFileTypeId }` | `G:964-1052`, `G:1825-1892` (F) |
| Drop-target detection | Cells carry `data-vehicleid` / `data-leftglobal`; the cell drop zone checks whether the pointer is over a bar first (`transportFileDropZoneAtCoords`) and highlights bar or cell | `G:2253-2275`, `FTS:412-460` (F) |
| Unassign legs | Not on the gantt: legs are dragged from the trip's leg grid back onto "legs to schedule", which calls `assignTransportFileFreightLegs` with `transportFileId = null`; the gantt has no handler for `removeFreightsFromTransportFile` (only the outline listens) | `FTS:595-625`, `O:224-233` (F) |
| Open a trip | Double-click a bar → `TransportFileService.openModalEvent` (the global trip dialog) | `G:2622-2624`, `G:3252-3254` (F) |
| Context menu | Right-click → Edit, Complete (disabled when both actuals set), Delete, Flag, HighlightColor, EmailSend, EmailArchive (archive), PrintConsignmentNote, Split (convert), SendToVehicle (FMS send), RemoveFromVehicle (FMS withdraw) | `OS:14-82`, `G:3257-3299` (F) |
| Context menu execution | Only emits `doContextMenuActionEvent`; the handlers live in the **trip grid pane** (`TFC:453-467`, e.g. `sendToVehicle` `TFC:1246-1278`, `removeFromVehicle` `TFC:1279-1300`, `convertVehicle` `TFC:1302`) | F; I: with the trip grid pane closed the menu does nothing |
| Highlight colour | A local patch is merged into the cached file and the bar re-rendered at once | `G:1074-1085` (F) |
| Hover | Tooltip with all configured fields when the bar is too small | `G:1424-1435`, `G.html:273-285` (F) |
| Double-click row label | Vehicle edit dialog | `G:368-389` (F) |
| Mode switch | Saves the outgoing mode's filters, restores the incoming ones, no refetch (the loaded files serve both modes) | `G:681-715` (F) |

### 2.2 Outline

| Interaction | What happens | Citation |
|---|---|---|
| Move | CDK drag of a bar onto another cell → local move, then `GetSingle` (full edit DTO) → primary vehicle = target row, new departure from the cell (`"<day> <current year> <hour>"`), arrival = departure + duration → vehicle defaults copied client-side (carrier, secondary/third vehicle, stationing relation as departure and arrival, default drivers of all three vehicles) → `QueryIncomingInvoices` → `CreateOrUpdate` → refetch all | `O:647-688`, `O:777-827`, `O:940-958` (F) |
| Resize end / start | Mouse-move resize in whole cells; commit through `GetSingle` + `CreateOrUpdate` of the arrival (end) or departure (start) | `OR:92-153`, `O:690-769`, `O:999-1042` (F) |
| Drop legs on a cell | `GetSingle(undefined)` (new file with type defaults) → primary vehicle, file date, departure from the cell, vehicle defaults → invoices → `CreateOrUpdate` → `AssignTransportFileFreightLegs` → refetch: four sequential calls | `O:829-893` (F) |
| Drop legs on a bar | Same chain with the existing file id | `O:213-221`, `O:829-893` (F) |
| Remove legs | `GetSingle` → when no legs remain, arrival = departure + 2 h → `CreateOrUpdate` → refetch | `O:895-938` (F) |
| Select / open / context menu | Checkbox (one file), double-click opens the trip dialog, same context menu items | `OR.html:34-36`, `O:568-617` (F) |

The outline builds departure dates from the display string with the **current year** (`O:744`, `O:792`, `O:845`): a drop in a range that crosses New Year lands a year off (F).

### 2.3 Validation, conflicts and rules on these interactions

- **No overlap or double-booking check** anywhere: neither the frontend nor `VPAS` compares a vehicle's trips (F, `VPAS:130-286`). A vehicle can carry overlapping trips.
- **No availability check on drop.** Vehicle and driver calendar events, missing capabilities, incompatible temperature/product/unit combinations and leg dates outside the trip window are only **warnings** computed when the trip list is read (§3.3) and shown as a triangle on the bar (F, `SEL:519-593`, `TFAS:1598-1626`). Sensor values are shown, never checked (`SEL:599-613`).
- Permission: `VehiclePlanboardAppService` is `[AbpAuthorize]` only — any signed-in user of the tenant can call the mutations (`VPAS:27`) (F).
- Feature limits on create: `MaxTransportFilesPerDay` (`VPAS:461-464`, `VPAS:603-605`).
- Status: none of the planboard mutations checks cancelled, archived or completed; a completed trip can be dragged (F, `VPAS:130-286`). The context menu disables only Complete (`G:3288-3299`).
- **Primary vehicle change resets the trip** to the new tractor's defaults: carrier = vehicle's transport company, secondary and third vehicle = the vehicle's defaults (overwriting a trailer the planner chose), departure/arrival relation = stationing relation, drivers = default drivers of the three vehicles, incoming invoices re-queried (`VPAS:199-253`) (F).
- **Secondary vehicle change** writes only the secondary id and the times — no drivers, carrier or relations (`VPAS:263-286`). The same change through the Resources pane goes through `TFAS.UpdateSecondaryVehicle`, which **does** rebuild drivers from the vehicles' defaults (`TFAS:671-707`, `TFAS:692`): two paths, two outcomes (F).
- Time changes adjust the legs' dates and the template order (`VPAS:143`, `VPAS:154`, `VPAS:170`, `VPAS:181`); the vehicle move, which also changes the times, does **not** adjust leg dates (`VPAS:184-261`) (F).
- New trip from a cell: departure = cell start, arrival = departure + 2 h hard-coded, file date = cell date, type default or chosen, departure/arrival = type default or own company, stationing relation of the tractor overrides (`VPAS:459-589`). In secondary mode: trailer as secondary vehicle, no primary, no drivers, carrier = type default or own company (`VPAS:600-662`) (F).

## 3. Backend calls, data per bar, what is slow

### 3.1 Calls the gantt makes

| When | Call | Citation |
|---|---|---|
| Open | `VehicleService.getList(true, …, includeVehicleOrganizationUnits: true)`, `getList(false, …)`, `RelationGroupService.getList()` in parallel | `G:3029-3099` |
| Open / refresh / range change | `TransportFileService.getAll(executionDateRange = range, hideEmptyTransportFiles)` — **unpaged, no vehicle filter, no organisation unit filter** | `G:3110-3139`; proxy signature `angular/src/shared/service-proxies/service-proxies.ts:49280` |
| Move | `VehiclePlanboard.MultipleUpdateTransportFileScheduledVehicle` / `…SecondaryVehicle` | `G:2835-2874` |
| Resize | `VehiclePlanboard.MultipleUpdateTransportFileScheduledTimeOfDeparture` / `…Arrival` | `G:2787-2833` |
| Drop legs | `TransportFileType.getList()` then `VehiclePlanboard.AssignLegsToVehicleOrTransportFile` | `G:993-1045`, `G:2876-2965` |
| Settings dialog | `TransportFile.getAllAttributes()` | `OSD:132-152` |

`VPAS` methods (`IVehiclePlanboardAppService.cs:10-59`): the four `Multiple…` endpoints loop over the single-item methods inside one request, then **return an empty list** and queue the echo (`VPAS:88-128`); the frontend branch that applies returned DTOs is therefore dead and every move relies on the echo or the safety net (`G:2794-2801`) (F). `AssignLegsToVehicleOrTransportFile` creates the file if needed (transaction 1), assigns the legs with `DeferPostProcessing = true` (transaction 2), returns a **slim DTO** built from the entity (number, name, times, vehicles with names, highlight, flags, status flags; no derived data) and, for a new file only, pushes that slim DTO to the other clients at once (`VPAS:288-430`). The deferred chain (relations, ETAs, routes, invoices, prices, attribute sync, CO₂) runs in a `Task.Run` fire-and-forget after the response and ends with the echo (`TFAS:2206-2240`) — nothing survives a process restart (F).

### 3.2 Data per bar (legacy `GetAllTransportFileDto`)

`GetAll` delegates to `GetAll_Perf` (`TFAS:1237-1240`). Per trip it returns the entity columns (number, date, name, type, organisation unit, times, distances, CO₂, flags, highlight, remarks, driver instructions, custom properties, FMS status) **plus names resolved by joins in one database**: type, organisation unit, departure/arrival relation, carrier, the three vehicles and their types, creator/modifier (`SEL:63-96`, `SEL:155-244`); the **denormalised `tblTransportFileAttributes` row** (drivers text, number of legs, orders, customers, load factor, loading metres, EPQ, amount, positions, colli, stop count, first/last stop names and cities, maintained by SQL triggers and stored procedures — see `transport-assessment/trips.md` §1); and computed per request: revenue, cost, expected/actual price, load factor, warnings, barcodes, order references, invoice references, container numbers, sensor values, attachments count, e-mail delivery status (`TFAS:1528-1855`). Customer names on a bar come from that attributes row, vehicle names from the joins (F).

### 3.3 What the query does and why it is slow

The comment on the echo says the full DTO pipeline "takes seconds on large tenants" (`PEB:26-31`); `VPAS:69-77` says the old synchronous reload "held the response hostage to the multi-second GetAll pipeline". Reasons visible in code (F unless marked):

1. **Unbounded result**: the gantt loads every trip of the tenant whose scheduled window overlaps the range (`SEL:100`, `TransportFile/Queries/TransportFileQueries.cs:17`), up to 14 days, for all vehicles and all organisation units, although it shows only the filtered vehicles (`G:3110-3139`).
2. **About 27 SQL round trips per request** in `GetAll_Perf` (numbered `SQL - 1` … `SQL - 27`, `TFAS:1373-1516`).
3. **Whole tables loaded into memory on every call**: all capabilities, all freight capabilities, all relation capabilities, all vehicle capabilities, all vehicle-type capabilities (`TFAS:1475-1482`, with the TODO "once somebody starts using capabilities we should never get all in memory"), all discounts and surcharges (`TFAS:1429-1430`).
4. **No time bound on calendar events**: every event ever recorded for the trips' vehicles and drivers (`TFAS:1509-1516`; same in the echo path, `SEL:404-421`).
5. **No time bound on sensor measurements**: every measurement of every device of the trips' vehicles, then latest-per-sensor in memory (`TFAS:1686-1729`; grouped in SQL in the echo path, `SEL:428-465`).
6. **Money computed on read**: revenue, cost per km, cost and expected/actual price per trip on every list call (`TFAS:1528-1583`).
7. The **echo uses a different pipeline** (`GetAllTransportFileDtoQuerySelector.GetAll`, `PEB:103-107`) than the list (`GetAll_Perf`): the derived block is skipped entirely above 350 DTOs (`SEL:252-254`), `EstimatedLoadingPerformanceIndicator` is a random number (`SEL:496`), timings go to `Console.WriteLine` (`SEL:619`). A bar can look different after an echo than after a refresh (I).

### 3.4 Optimistic update, echo and the 30 s safety net

- After commit (`CurrentUnitOfWork.Completed`) `VPAS.QueueEchoAfterCommit` calls `PEB.Queue`, which builds the full DTOs in a new scope and unit of work under the caller's tenant and user and sends `onTransportFilesUpdates` to the SignalR group of the **whole tenant** (`VPAS:78-86`, `PEB:80-124`, hub group per tenant `aspnet-core/src/FiLogic.Application/BusinessServices/Planboard/PlanboardHub.cs:27-50`). Every planboard of the tenant receives every changed trip, whatever it displays (F).
- Client: `PlanboardSignalRService` (WebSockets only, negotiation skipped because the backend runs multi-instance without sticky sessions, reconnect back-off up to 30 s, `angular/src/app/planboard/planboard-signalr.service.ts:14-75`). The gantt applies each delivered file in place: removed when cancelled/archived, routed to the Unplanned row or the vehicle row, dropped when its vehicle is filtered out or it lies outside the window, kept in the local array for the info strip (`G:1737-1802`); single-item state path updates so GSTC re-renders one slot (`G:1807-1809`).
- Optimistic placeholder for a new trip: grey pulsing item; reconciled when an echoed trip has the same row vehicle and a departure within ± 10 min (`G:1811-1912`); removed on dialog cancel or error (`G:1030-1033`, `G:2957-2961`). Dropped legs leave the source grid at once and come back on rollback (`G:958-960`, `G:991-992`).
- **Safety net**: after every mutation response without DTOs, a 30 s timer runs a full `refresh()` unless any SignalR delivery (own or a colleague's) arrives first (`G:1191-1221`, `G:2798-2800`, `G:2949-2953`, `G:1743`) (F).

## 4. Resources pane

- An ag-Grid list (`RES:57-61`, columns: drag handle, resource name, availability, event details, `RES:418-446`) of one resource type at a time: **PrimaryVehicles**, **NonPrimaryVehicles** or **Drivers** (`RES:26-30`, `RES:63-64`, picker in the preferences panel `resources.component.html:18-24`), with organisation units, date range (own or planboard-wide, `RES:128-236`), flag filter, search and "only available" (`RES:397-401`).
- Data: `CalendarService.getAllResources(entityType, onlyAvailable, organizationUnitIds, category, dateRange)` (`RES:238-285`) → `CAL.GetAllResources` (`CAL:512-616`):
  - vehicles: non-archived vehicles with `IsPrimaryVehicle` = category, filtered by vehicle organisation units (`CAL:526-551`); name "Name (plate)" (`CAL:558-569`);
  - drivers: non-archived relations in a category flagged `IsDriverCategory`, filtered by relation organisation units (`CAL:572-610`) — a legacy driver is a `Relation`, not an own entity (F);
  - per resource the calendar events overlapping the range from `tblCalendar` (one calendar per `ResourceId`) / `tblCalendarEvent` (`CAL:640-669`, entities `aspnet-core/src/FiLogic.Core/BusinessEntities/Calendar/Calendar.cs:6-27`, `CalendarEvent.cs:7-52`);
  - for **vehicles only**, the trips they are scheduled on in the range are added as pseudo-events "`<file number>` scheduled" (primary: primary slot; non-primary: secondary or third slot) (`CAL:671-738`); **drivers' trips are not counted** (`CAL:598`, `includeTransportFiles: false`) (F);
  - availability = Available (no events), NotAvailable (events cover the whole range with gaps ≤ 60 s), else PartlyAvailable (`CAL:747-800`, enum `Calendar/Dto/ResourceAvailability.cs:3-8`); event details = "label: from – till" joined (`CAL:763-764`).
  - Recurring events (`CalendarEventRepeat`: frequency, interval, weekly days, until, `CalendarEventRepeat.cs:7-30`; exceptions on `CalendarEvent.cs:43-49`) are **not expanded**: only the stored event's own start/end is compared (I from `CAL:644-655`). There is no working-time pattern for drivers, only events (F: no such entity in `FiLogic.Core/BusinessEntities/Calendar/`).
- **Drag and drop**: rows are dragged onto the **trip grid pane** (drop zone propagated by `TFC:188`, `TFC:855`; handler `RES:288-349`), **not onto the gantt** (F: the gantt registers no resource drop zone).
  - primary vehicle → `TransportFile.UpdatePrimaryVehicle` (`RES:295-296`, `RES:351-383`; `TFAS:578-669`, which applies the vehicle defaults and drivers, `TFAS:649`);
  - non-primary vehicle → a context menu at the drop point: *Assign as secondary vehicle* / *Assign as third vehicle* / Cancel (`RES:297-326`) → `UpdateSecondaryVehicle` / `UpdateThirdVehicle` (`TFAS:671-743`, both rebuild the drivers from the vehicles' defaults, `TFAS:692`, `TFAS:729`);
  - driver → `TransportFile.UpdateDrivers({ drivers: [that driver] })` (`RES:327-347`), and `UpdateDrivers` **deletes all current drivers and inserts the given list** (`TFAS:758-772`): dropping a second driver replaces the first (F).
  - After each drop: resources refetched, trip grid refreshed (`RES:337-340`, `RES:376-377`); no availability check before assigning (F).

## 5. Driver and trailer planning today, and the gap to the three PO modes

| Need of the PO modes | Legacy today | Gap |
|---|---|---|
| **Tractor mode**: rows = motorised vehicles, bars = trips with that vehicle as tractor | Gantt primary mode, rows = `IsPrimaryVehicle` (F) | Rows must come from the vehicle **kind** (new Master Data: Tractor, Rigid, Van are motorised, design 48 `docs/designs/48-master-data-vehicles.md:99`, `:135`), not a boolean |
| **Trailer mode**: rows = trailers, bars = trips that use the trailer | Gantt secondary mode: rows = all non-primary vehicles, bars only by `SecondaryVehicleId`; third vehicle never shown (F, `G:554-557`) | A trip with two trailers (positions 2 and 3, or a combination with 3–4 members) must appear in **each** trailer's row; rows filtered to kind Trailer |
| **Driver mode**: rows = drivers, bars = trips the driver drives | **Does not exist.** Drivers are only a text column on bars and the Resources list (F) | Entire mode: rows from Master Data drivers (`docs/designs/43-master-data-relations-and-drivers.md:148-170`), bars by `TripDriver` (up to 3 per trip), drag between driver rows = replace that driver position, drop of legs on a driver row = new trip with that driver |
| **Several Scheduler panes at once** | One vehicle planboard tab at a time; outline and gantt exclusive (`PB:589-595`); tab id singleton (`PB:503-516`); settings keys are fixed per component (`G:163-175`) | Pane instances with their own id, mode, filters, range and bar layout; the same trip shown in several panes and kept consistent by one live-update stream |
| **A trip in several schedulers** | Within one scheduler a trip appears once (`G:2131-2141`) | Across panes: tractor pane, trailer pane(s), driver pane(s) all draw the same trip; a move in time in one must move it everywhere; a move between rows changes only that mode's resource |
| **Driver availability**: absences, working times | Calendar events per relation id, warnings on read (`SEL:582-592`, `TFAS:1509-1516`); availability column in Resources (`CAL:754-755`); no working-time pattern; recurrences not expanded (I) | Absences and working times as planning facts, drawn in the driver rows (and vehicle unavailability in vehicle rows), checked on assignment |
| **Vehicle availability**: maintenance, inspection | Calendar events per vehicle id (same mechanism) (F) | Same; design 48 moved "Calendar" out of Master Data to "Planning (availability)" (`docs/designs/48-master-data-vehicles.md:184`), nothing built |
| **Trailer coupling over time** | Default secondary/third vehicle on the tractor (`Vehicle.cs:43-45`), copied on assignment (`VPAS:207-208`, `VPAS:511-519`); "Unplanned trailers" row (F) | Standing combination exists (design 48 `VehicleCombination`); no notion of "trailer X is coupled to tractor Y from t1 to t2" across trips; a trailer swap inside a trip is only possible through Convert (`trips.md` §2 point 4) |
| **Third vehicle** | Only via the Resources drop menu or tractor defaults (`RES:307-309`) (F) | Covered by trailer mode if positions 2–3 are both trailer rows (see Q-12) |

## 6. Mapping to the new system

### 6.1 What Planning & Execution already has (design 59, built)

- `TransportTrip` with `PlannedDeparture`/`PlannedArrival`, `ActualDeparture`/`ActualArrival`, sortable UTC copies `PlannedDepartureUtc`/`PlannedArrivalUtc` (`backend/modules/planning-execution/OpenTms.PlanningExecution.Domain/TransportTrips/TransportTrip.cs:47-58`), `Status` (Planned, InProgress, Completed, Cancelled) and `IsArchived` (`:29-31`), `Execution` own fleet / subcontracted (`OpenTms.PlanningExecution.Domain.Shared/TransportTrips/TripExecution.cs:4-8`), `CarrierRelationId` (`:35`), **either** `VehicleCombinationId` **or** up to three positioned `TripVehicle` rows (`:64`, `:100`, `SetVehicles` `:227-243`, `MaxVehiclesPerTrip = 3`), up to three positioned `TripDriver` rows (`:102`, `SetDrivers` `:246-256`), stored derived values and coded warnings (`:81-95`, `TransportTripEnums.cs:36-49`).
- Commands usable by a Scheduler (`docs/designs/59-planning-legs-and-trips.md:82-95`, service `OpenTms.PlanningExecution.Application/TransportTrips/TransportTripAppService.cs`): `CreateAsync` (`:559`, type defaults fill empty groups, design 61), `UpdateAsync` (full header PUT with `ConcurrencyStamp`, `:569`; DTO `TransportTripDtos.cs:264-290`), `SetCarrierAsync` (`:621`), `SetVehiclesAsync` (`:633-649`, re-requests the legs' routes), `SetDriversAsync` (`:653-662`), `AssignLegsAsync` / `UnassignLegsAsync` / `MoveLegsAsync` (`:667-711`, `AllowOutsideWindow` flag `TransportTripDtos.cs:335`), `Start`/`Complete`/`Cancel`/`Archive`/`Delete` (`:783-865`). Refusals cover status (`TripNotAssignable`, `InvalidTransition`), legs on another trip and legs outside the window (design 59 `:93-95`, build note 1).
- Live updates: the Platform hub `/signalr-hubs/record-updates`, `Watch(type, ids[])` with at most 200 ids per call and per screen, push `{type, id, version, status}` only (ids, no data) (`docs/designs/49-derived-values-and-live-updates.md:34`, `:88`, `:116`, `:161-188`); Planning registers `PlanningExecution.TransportTrip` and `PlanningExecution.Leg` as calculator types (`OpenTms.PlanningExecution.Application/PlanningExecutionApplicationModule.cs:26-27`).
- Name resolution without read tables: the trip list composes each page from Planning rows plus batched published lookups — carriers, combinations, vehicles, drivers from `IMasterDataReferenceLookup`, orders and customers from Orders (`TransportTripAppService.cs:309-381`); the lookup surface is by id only (`backend/modules/master-data/OpenTms.MasterData.Application.Contracts/Published/IMasterDataReferenceLookup.cs:22-67`); the vehicle reference carries name, plate, type, **kind**, weights, dimensions, fuel, euro class (`:130-154`). This is the pattern the PO's "no denormalised read tables" rule leaves (I). The trip row does store a lower-cased `SearchKey` (build note 5, `TransportTrip.cs:98`) and the derived values (AD-07): columns on the fact row, not a read table (I).
- Frontend: the Planboard app is still a placeholder (`frontend/src/apps/planboard/index.tsx:1-6`), feature `Apps.Planboard` (`frontend/src/app/apps.config.ts:140-146`); UI library gaps GAP-07 (docking workspace) and GAP-11 (Gantt / resource scheduler) are open (`docs/conventions/ui/component-map.md:105`, `:108`); bespoke-screen rules apply (`docs/conventions/ui/ui-rules/bespoke.md:8-39`).

### 6.2 What is missing

| Missing | Why the Scheduler needs it | Notes |
|---|---|---|
| A **window query by resource** ("trips of these vehicles / drivers overlapping from–to") | Every Scheduler load | `TransportTripListRequestDto` has no vehicle or driver filter (`TransportTripDtos.cs:441-463`). Indexes: `(TenantId, Status, TripDate)` only (`OpenTms.PlanningExecution.EntityFrameworkCore/EntityFrameworkCore/PlanningExecutionDbContext.cs:141`); none on `PlannedDepartureUtc`/`PlannedArrivalUtc`; `TripVehicles` / `TripDrivers` indexed on `VehicleId` / `DriverId` without tenant (`:156`, `:164`) |
| Vehicles of a **combination** in the query | A trip planned with a `VehicleCombinationId` has **no** `TripVehicle` rows (`TransportTrip.cs:228-243`); "trips of trailer X" must first ask Master Data which combinations contain X | Either Planning stores the resolved vehicle ids of the combination on the trip (a fact copy, not a read table), or every load does a Master Data round trip first (I) |
| Narrow planboard commands | A drag must answer in ≤ 200 ms: "reschedule" (departure/arrival only), "move to vehicle in position n" / "replace driver n", atomically | Today a move to another row = `SetVehicles` (replaces the whole list, resolves names, requests leg routes) plus `UpdateAsync` (full header PUT) — two calls, not atomic, each returning the full `TransportTripDto` with stops (`TransportTripAppService.cs:633-649`, `:388`) |
| Vehicle defaults on assignment | Legacy copies carrier, trailers, stationing relation and default drivers from the tractor (`VPAS:199-253`) | Master Data has home depot, owner, default drivers and standing combinations (design 48 `:104`, `:124-135`, `:247`), but the published lookup returns none of them (`IMasterDataReferenceLookup.cs:130-154`); `SetVehicles` copies nothing (`TransportTrip.cs:228-243`) |
| **Resource availability** (driver absence, working times, vehicle unavailability) | Rows show it; drops warn or refuse on it; driver mode is built around it | Not in any design; design 48 assigns vehicle calendar to "Planning (availability)" (`:184`); no warning codes for it (`TransportTripEnums.cs:36-49`) |
| Double-booking warning | Overlap is the core conflict of a scheduler | No warning code; legacy has none either |
| Live updates for a **time window** | A Scheduler shows hundreds of trips and must learn about trips created or moved into its rows by others | Design 49 watches ≤ 200 known ids (`49:34`, `:165`); pushes are sent only when a calculation job finishes (`backend/modules/platform/OpenTms.Platform.Application/DerivedValues/DerivedValueCalculationJob.cs:30-39`); `SetDriversAsync` requests no calculation (`TransportTripAppService.cs:659`) so a driver change reaches nobody (F) |
| Feature gating | Planboard nodes are features (PO 2026-10-14) | The trip commands require `AppFeatures.Transport` + `Trips` (`TransportTripAppService.cs:384`, `:651`); a tenant with the Planboard but not the Transport app's Trips node could not plan (I) |
| Vehicle position / online state on the row | Legacy row label (`G:1576-1598`) | Depends on **AO-12 (OPEN)**: which domain owns vehicle positions (`docs/architecture/decisions.md:74`) |
| Pane workspace | Several Scheduler panes | GAP-07 open |

### 6.3 Cross-module data per bar and per row

| Item | Legacy source | New source |
|---|---|---|
| Trip number, name, times, status, execution, warnings, load factor, CO₂, cost/revenue | `tblTransportFile` + computed per read (`TFAS:1528-1626`) | Planning's own row (stored derived values, AD-07) |
| Trip type name | join (`SEL:70-72`) | Planning `TransportTripType` (own schema) |
| Vehicle names / plates / kind | joins on `tblVehicle` (`SEL:81-92`) | `IMasterDataReferenceLookup.GetVehiclesAsync` (batched per load) |
| Combination members | n/a (three slots) | `GetVehicleCombinationsAsync` |
| Driver names | `tblTransportFileAttributes.Drivers` text + `tblRelation` (`SEL:291-298`) | `GetDriversAsync` |
| Carrier name | join (`SEL:79-80`) | `GetRelationsAsync` |
| Customers, order numbers | `tblTransportFileAttributes.Customers` / `.TransportOrders` (triggers) | Orders' published order references, as the trip list does (`TransportTripAppService.cs:328-331`) |
| First/last stop city, stop count | attributes row | Planning `TripStop` (own schema) |
| Rows: vehicle list by kind / organisation unit / owner | `VehicleService.getList` (`G:3033-3034`) | A paged Master Data query by kind/OU; today only the Master Data app's own `VehicleAppService` list exists (design 48 `:197-204`); CLAUDE.md maps the Planboard app to **Planning & Execution only** (see Q-02) |
| Rows: drivers | relations in a driver category (`CAL:574-579`) | Master Data `Driver` aggregate (design 43) |
| Availability / absences | `tblCalendar` / `tblCalendarEvent` | Not built (see §6.2) |
| Last-seen location, online | `Vehicle.LastSeen*` (`Vehicle.cs:104-111`) | AO-12 open |

## 7. Functional scenarios a rebuild must pass

Mode names: T = tractor, R = trailer, D = driver. "Bar" = one trip drawn in one row.

- **S-01** Planner opens a Scheduler pane in mode T for a 7-day range → rows = active motorised vehicles of the selected organisation units, bars = their trips overlapping the range; first paint ≤ 200 ms server time (F legacy behaviour `G:3029-3139`; the budget is the PO's).
- **S-02** Planner opens a second pane in mode R and a third in mode D next to it → each pane keeps its own mode, filters, range and bar layout across reloads (PO; legacy keeps one pane, `PB:589-595`).
- **S-03** A trip with tractor A, trailer B and driver C appears once in pane T (row A), once in pane R (row B) and once in pane D (row C) (PO; legacy draws only by its mode's slot, `G:554-557`).
- **S-04** A trip with two trailers B1, B2 appears in both trailer rows in mode R (gap §5).
- **S-05** A trip with two drivers appears in both driver rows in mode D.
- **S-06** A trip planned on a standing combination (tractor + trailer) appears in the tractor's row in T and the trailer's row in R (design 48/59 combination).
- **S-07** Planner drags a bar later in time in pane T → planned departure and arrival shift by the same delta; panes R and D move the same bar without a manual refresh; the server call answers ≤ 200 ms (legacy `G:2414-2475`, `VPAS:184-261`).
- **S-08** Planner drags a bar to another tractor row → only the tractor changes (unless Q-06 decides otherwise); pane R and D keep the bar in their rows.
- **S-09** Planner drags a bar to another trailer row in pane R → only that trailer position changes; tractor, drivers and carrier unchanged (legacy `VPAS:263-286`).
- **S-10** Planner drags a bar to another driver row in pane D → that driver position is replaced; other drivers stay.
- **S-11** Planner resizes the end of a bar → planned arrival changes; resizing the start changes planned departure; a mixed multi-item resize writes each end to its own field (legacy bug `G:2360-2410` not repeated).
- **S-12** Planner selects several bars and drags them together → all move by the same delta in one call (legacy bulk endpoints `VPAS:88-128`).
- **S-13** Planner drops selected unplanned legs on an existing bar → legs assigned (`AssignLegs`), stops recomposed, bar re-drawn when the calculation finishes with the calculating indicator meanwhile (design 49).
- **S-14** Planner drops legs on an empty cell of tractor row A at 08:00 → a new trip with departure 08:00, tractor A, type = the only type or the chosen one; cancelling the type choice leaves nothing behind (legacy `G:964-1052`).
- **S-15** Same drop on a trailer row in mode R → new trip with that trailer and no tractor (legacy `VPAS:600-662`); on a driver row in mode D → new trip with that driver.
- **S-16** A drop of legs whose dates fall outside the trip window → refused with `LegOutsideTripWindow` unless the planner confirms "allow outside window" (design 59 `:93-95`).
- **S-17** Dragging a Completed, Cancelled or Archived trip → refused with a reason; such bars are visibly not draggable (legacy allows it, §2.3).
- **S-18** Moving a bar onto a vehicle that already has an overlapping trip → both bars stay visible, the trips get a double-booking warning (or are refused, Q-04).
- **S-19** Moving a bar onto a driver who is absent in that period → warning (or refusal, Q-05); the absence is drawn as a block in the driver's row.
- **S-20** A driver's working-time window is shaded in the driver row; a trip outside it gets a warning (Q-03).
- **S-21** A vehicle with a maintenance block → the block is drawn in its row; assigning a trip into it warns (Q-05).
- **S-22** Colleague B moves a trip that planner A sees → A's panes update within seconds without refetching everything; a trip B creates inside A's rows and range appears in A's panes (legacy SignalR `G:1066-1068`, `G:1737-1802`).
- **S-23** A move the server refuses → the bar snaps back and the error is shown (legacy `G:2957-2961`).
- **S-24** A pane loses its live connection → it shows that live updates are off and re-syncs the visible window on reconnect (legacy 30 s safety net `G:1191-1221`; design 49 re-watch on reconnect `49:184-188`).
- **S-25** Double-clicking a bar opens the trip editor dialog over the planboard (UI-DLG-007; legacy `G:2622-2624`).
- **S-26** Right-click on a bar → context menu with the trip actions the planner may perform; actions work without any other pane open (legacy depends on the trip grid pane, `TFC:453-467`).
- **S-27** Status colours: a bar shows the trip status (Planned, InProgress, Completed) in configurable tenant colours; the trip's highlight colour is shown as a stripe (legacy `GCS:29-50`, `G:1702-1714`).
- **S-28** The bar layout is configurable per pane: chosen fields in rows and columns, bold per field; a tooltip shows the full set when the bar is small (legacy `OSD`, `G:1511-1516`).
- **S-29** Warnings on a trip show an icon on the bar; hovering lists them localised (legacy `G:2680-2690`; new coded warnings).
- **S-30** Now-line at the current time, working-day shading, today's column marked (legacy `G:1223-1284`).
- **S-31** Range follows the planboard period unless the pane manages its own; a planboard period longer than the pane's maximum clamps the pane (legacy `G:2977-3019`, 14 days).
- **S-32** Filters per pane: resources (multi-select), owner/relation group, organisation units, free text over row labels and bar fields, hide empty trips, show an "unplanned" row (trips missing the other mode's resource) (legacy `G.html:3-271`).
- **S-33** Organisation unit filter restricts the trips fetched, not only the rows (legacy filters rows only, `G:299-306`, `G:3110-3139`).
- **S-34** Info strip per pane: resource count, trip count, in transit, idle resources, utilisation % (legacy `G:726-764`).
- **S-35** Tenant isolation: a planner of tenant A never sees rows, bars, absences or live pushes of tenant B, in every pane and on every push (constitution rule 1; legacy pushes per tenant group `PlanboardHub.cs:47-50`).
- **S-36** A user without the Scheduler feature or permission does not see the pane in the pane picker; a user with read but not update permission sees bars but cannot drag (PO feature model; legacy `[AbpAuthorize]` only, `VPAS:27`).
- **S-37** Resources pane: lists tractors, trailers or drivers with availability Available / Partly / Not available for the range, counting both calendar blocks and assigned trips for vehicles **and drivers** (legacy ignores drivers' trips, `CAL:598`).
- **S-38** Resources pane: dragging a driver onto a trip **adds** that driver (up to 3) instead of replacing all drivers (legacy replaces, `TFAS:758-772`; Q-15).
- **S-39** Resources pane: dragging a trailer onto a trip asks "position 2 or 3" when the trip already has a trailer (legacy menu `RES:297-326`); dragging a tractor sets position 1.
- **S-40** Resources pane: drop targets include Scheduler bars, not only the trip grid (legacy drop only on the trip grid, `TFC:188`).
- **S-41** A range crossing New Year places drops and moves in the right year (legacy outline bug `O:744`, `O:792`, `O:845`).
- **S-42** 500 rows × 14 days × ~3,000 bars scroll and drag smoothly in one pane (performance criterion for the component choice; legacy GSTC crashed beyond ± 1 week, `G:1669-1678`).

## 8. Open questions for the PO

Each with the recommendation of this appendix.

- **Q-01 Bar unit.** Is a bar always the **whole trip** in every mode (a trip in three panes is the same bar, moving it in time moves it everywhere), or does trailer/driver mode need sub-trip segments (trailer coupled for part of a trip, driver change mid-trip)? *Recommendation:* whole trip in all modes; mid-trip changes stay the Convert/split path (`trips.md` scenario 54).
- **Q-02 Where rows and bars are read.** CLAUDE.md maps the Planboard app to Planning & Execution only, but rows are Master Data vehicles and drivers. Does Planning publish one Scheduler endpoint that composes rows and bars itself (Planning rows + batched in-process lookups to Master Data and Orders, no read tables), or may the Planboard app read Master Data's vehicle and driver lists directly? *Recommendation:* one Planning endpoint per pane load (`GET /api/planning/scheduler?mode&from&to&resourceIds…`) plus a new published Master Data query "resources by kind / organisation unit, paged"; the app mapping stays unchanged.
- **Q-03 Owner of availability.** Driver absences, driver working times and vehicle unavailability: Planning & Execution (as design 48 `:184` already says for vehicle calendars) or Master Data? Imported from an HR/leave system later? *Recommendation:* Planning & Execution owns a `ResourceUnavailability` (resource kind + id, from–till, reason code, recurrence) and a driver working-time pattern; Master Data keeps the resources; an HR import is a later Integrations adapter.
- **Q-04 Double booking.** Allow an overlapping trip on the same vehicle/driver with a warning, or refuse it? *Recommendation:* allow with a stored warning `ResourceDoubleBooked` and an immediate visual conflict on the bar; no hard block in the first version.
- **Q-05 Availability and qualification on drop.** Warn or refuse when a driver is absent, outside working time, or lacks a required qualification (ADR, code 95 expiry, design 43), or a vehicle is blocked? *Recommendation:* warn (coded warnings, as capabilities today) and show a confirmation on drop; never refuse except for status rules.
- **Q-06 Tractor change and its defaults.** Legacy resets carrier, trailers, stationing relation and drivers to the new tractor's defaults (`VPAS:199-253`), overwriting the planner's choices. Keep that? *Recommendation:* fill only empty slots from the tractor's standing combination, default drivers and owner; never overwrite a trailer or driver the planner set.
- **Q-07 New trip from an empty cell in R and D mode.** Allowed (trip with only a trailer, or only a driver)? *Recommendation:* yes in all three modes, the trip type's defaults fill the rest (design 61 trip type defaults).
- **Q-08 Range and zoom.** Keep the 14-day maximum and the "fit range to width" zoom without user zoom? *Recommendation:* keep 14 days as the maximum per pane, add user zoom (hour/day/week presets) — a paid component gives it for free.
- **Q-09 Live updates for a window.** Design 49 watches at most 200 known record ids; a Scheduler needs "anything that enters or changes in my rows and range", including trips created by others and changes that run no calculation (`SetDrivers`). Extend Platform with a window/resource channel (push ids only, client refetches the affected bars), or have Planning notify per resource id? *Recommendation:* Planning sends a record-update notification after every committed trip command (not only after calculations), and the hub gets a group per tenant + resource (vehicle id / driver id) that a pane joins for its visible rows; this needs a Platform design change (critical risk class).
- **Q-10 Vehicle position on the row.** Legacy shows last-seen location and an online dot (`G:1576-1598`). This depends on **AO-12 (OPEN)**. *Recommendation:* leave it out of the first Scheduler design and list it under *Out of scope* until AO-12 is decided.
- **Q-11 Bar content.** Free choice of any trip field per pane (legacy) or a fixed template? Cost, revenue and customers per bar cost extra lookups per load. *Recommendation:* configurable from a whitelist of the fields `TransportTripRowDto` already carries (`TransportTripDtos.cs:13-48`), no new joins.
- **Q-12 Third vehicle and multi-trailer.** Is the "third vehicle" simply a second trailer row in trailer mode (both positions 2 and 3 shown), and does a dolly or a non-trailer third vehicle need its own mode? *Recommendation:* trailer mode shows every vehicle of kind Trailer in any position ≥ 2 or in the combination; no fourth mode.
- **Q-13 Combination resolution.** A trip planned on a standing combination stores only the combination id. For fast "trips of trailer X" queries, may Planning store the resolved member vehicle ids on the trip at assignment (a fact: which vehicles drive), refreshed when the planner changes it, not when Master Data edits the combination? *Recommendation:* yes; the trip's vehicles are Planning's fact (design 48 assumption 1, `48:304-306`).
- **Q-14 Feature gating.** Planboard nodes are features. Should the Scheduler's commands require the Planboard feature (`Apps.Planboard.Scheduler`, `Apps.Planboard.Resources`) instead of or next to `Apps.Transport.Trips`, which they require today? *Recommendation:* each trip command accepts either feature; the Planboard nodes get their own features in the same change.
- **Q-15 Resources pane driver drop.** Legacy replaces all drivers with the dropped one (`TFAS:758-772`). Add instead? *Recommendation:* add as the next driver position (max 3); replace with a modifier key or by dropping on a driver row in mode D.
- **Q-16 Component.** The PO allows a paid component under a trial. Which candidates go into the trial and on which criteria? *Recommendation:* time-boxed spike with Bryntum Scheduler Pro and DHTMLX Scheduler (Syncfusion as third), judged on: React 19 support, row virtualisation at S-42 volume, drag in from AG Grid (the legs list), several instances on one page, theming on `opentms-theme.css` tokens (UI-TOK-001), keyboard access, licence cost per developer and per deployment; the result closes GAP-11.

## 9. Known problems not to clone

1. Unpaged, unfiltered trip load for the whole tenant in the window; organisation units filter rows only (`G:3110-3139`, `G:299-306`).
2. ~27 queries per list call, whole capability and surcharge tables in memory, unbounded calendar and sensor history, money computed on read (`TFAS:1373-1730`).
3. Two DTO pipelines with different results (list vs echo, `PEB:103-107`, `SEL:252-254`), a random KPI (`SEL:496`).
4. Echo to every client of the tenant regardless of what it shows (`PEB:58-65`); post-processing in `Task.Run` that a restart loses (`TFAS:2206-2240`, `PEB:61`, `PEB:85`).
5. Mutation endpoints that return nothing while the client keeps a dead branch for returned DTOs (`VPAS:97`, `G:2794-2801`).
6. Resize batching that writes departures as arrivals (`G:2360-2410`); end-snap in milliseconds (`G:2330-2333`).
7. Tractor move overwriting trailer, drivers, carrier and relations (`VPAS:199-253`); secondary change with different side effects on two paths (`VPAS:263-286` vs `TFAS:671-707`).
8. Vehicle move without leg date adjustment while time moves adjust them (`VPAS:184-261` vs `VPAS:143`).
9. Context menu that works only while another pane is open (`TFC:453-467`).
10. Shared settings key between two components (`G:170`, `O:104`); singleton pane (`PB:503-516`, `PB:589-595`); layout-restore permission check on the wrong tab (`PB:146-151`).
11. Driver drop that replaces all drivers (`TFAS:758-772`); driver availability that ignores the driver's trips (`CAL:598`); recurring calendar events not expanded (I, `CAL:644-655`).
12. Current-year date parsing in the outline (`O:744`, `O:792`, `O:845`); hard-coded "+ 2 h" arrivals (`VPAS:507`, `VPAS:651`, `O:905`).
13. No authorization beyond "signed in" on planboard mutations (`VPAS:27`); no status guard on drags (§2.3).
