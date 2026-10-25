<!-- Appendix to docs/input/transport-rebuild-assessment.md and pricing-rebuild-assessment.md. Produced 2026-10-25 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 5f547cc6b); not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in that checkout. F = fact read in code, I = inferred. -->

# Legacy OpenTMS assessment: Transport file (vrachtdossier / trip)

Source root: `/Users/marinusbrink/dev/OpenTMS/aspnet-core/src`. Paths below are relative to it; `TFAS` = `FiLogic.Application/BusinessServices/TransportFile/TransportFileAppService.cs` (5,388 lines). Read-only research; nothing was built or run.

## 1. Entity model

**TransportFile** (`FiLogic.Core/BusinessEntities/TransportFile/TransportFile.cs`, table `tblTransportFile`, `FullAuditedFiLogicEntity` = Guid id + tenant + soft delete + audit):

| Group | Fields (line) |
|---|---|
| State flags | `IsArchived`, `IsCanceled`, `TransportUnitTransactionsProcessed` (12-16) — all `bool`, no state enum |
| Identity | `TransportFileTypeId` (20), `OrganizationUnitId` (long?, 23), `FileNumber` (string, 27), `FileDate` (31), `FileName` (33) |
| Departure | `DepartureRelationId`, `ScheduledTimeOfDeparture`, `ActualTimeOfDeparture` (36-40) |
| Arrival | `ArrivalRelationId`, `ScheduledTimeOfArrival`, `ActualTimeOfArrival` (43-47) |
| Distances | `ScheduledDistance`, `ActualDistance` (`double?`, 50-52) |
| CO₂ | `EstimatedCarbonFootprint`, `ActualCarbonFootprint` (55-57) |
| Loading performance | `EstimatedLoadingPerformanceIndicator`, `ActualLoadingPerformanceIndicator` (60-62; "LPI" in nl) |
| Carrier | `TransportCompanyId`, `TransportCompanyContactPersonId` (66-68) |
| Vehicles | `PrimaryVehicleId`, `SecondaryVehicleId`, `ThirdVehicleId` (71-75) — three fixed columns, not a child table |
| Route optimisation | `RecalculateOptimalRouteAutomaticly`, `RouteOptimizationMode` (77-79) |
| FMS | `DoNotSendToFmsBefore`, `DoNotSendToFmsAfter`, `KeepSynchronizedWithFms`, `FleetManagementSystemDeviceStatus` (81-89) |
| Texts | `Remarks`, `DriverInstructions`, `HighlightColor` (85-91) |
| Custom | `CustomProperty0..9` strings (95-123) |

**Drivers** are a child table `tblTransportFileDriver` (`TransportFileDriver.cs` 14-18): `TransportFileId`, `RelationId`; no order/role.

**TransportFileStop** (`TransportFileStop.cs`, `tblTransportFileStop`): `TransportFileId`, `OrderNumber` (int), `WaypointTypeId`, `RelationId?`, `TransportOrderFreightLegIdentifiers` (**required string**, `;`-joined GUIDs, 26), then three triplets: Calculated/Estimated/Actual × TimeOfArrival, TimeOfDeparture, CumulativeDistance (30-48), `Remarks` (52). Helpers `Get/SetTransportOrderFreightLegIdentifiers` parse and join the string (56-80). A stop with an empty identifier string is a "manual" stop.

**TransportFileAttributes** (`TransportFileAttributes.cs`, `tblTransportFileAttributes`): a denormalised one-row-per-file cache for the grid — `Cost`, `Drivers`, `NrOfLegs`, `TransportOrders`, `Customers`, `LoadFactor`, `LoadingMeters`, `Epq`, `Amount`, `Positions`, `Colli`, `NrOfStops`, `First/LastStopNames/Cities`, `StopNames`, `StopCities` (20-60). Maintained by SQL Server stored procedures and triggers (`FiLogic.EntityFrameworkCore/EntityFrameworkCore/Repositories/CustomSql/spTransportFileAttributes_*.sql`, `trigger_TransportFile*_SyncAttributes.sql`) called from `ReferencedPropertiesSynchronizer.SyncTransportFileAttributes` (lines 175-191).

**Questionnaires**: `TransportFileQuestionnaire` (6-21) is a tenant-level definition (`Name`, `Value`, `QuestionnaireResultsProcessingRules` as opaque strings); `TransportFileQuestionnaireResult` (10-30) stores FMS/board-computer answers per file/stop: `FleetManagementSystemDeviceId`, `VendorId`, `EventGenerationTime`, `Key`, `ValueType`, `Value`. Driver-questionnaire answers from the on-board unit, exported via `ExportQuestionnaireResult`.

**TransportFileType** (`TransportFileType/TransportFileType.cs`): what the trip reads — numbering (`FileNumberCreationOption`, `Prefix`, `Format`, `YearSuffix`, `MonthSuffix`, 26-40), defaults for carrier/departure/arrival relation (43-53, including `DefaultDepartureRelationIsFirstStop` / `DefaultArrivalRelationIsLastStop`), `DefaultScheduledDepartureTime`, `RecalculateOptimalRouteAutomaticly`, `DefaultRouteOptimizationMode`, `DefaultStopCreationMode`, `KeepSynchronizedWithFms`, `DefaultHighlightColor`, `DefaultOrganizationUnitId` (55-65).

**Legs**: `TransportOrderFreightLeg.TransportFileId` (`TransportOrder/TransportOrderFreighLeg.cs` 18) is the only link trip↔order; a leg carries `Distance` (double?) and `Duration` as a **string** parsed with `TimeSpan.TryParse` (25-36), `DepartureFromOrigin`/`ArrivalAtDestination` as `DateTime` (57, 85), origin/destination relation, waypoint type, time slot, reference, remarks. `TransportOrderFreightStop` (order-level stops) only supplies `OrderNumber` to seed file-stop ordering.

Types throughout: `double`/`double?` for money, distance and quantities; `DateTime` without kind or zone (UTC assumed, converted ad hoc with `TimeZoneCalculator`); lists stored as delimited strings (`TransportOrderFreightLegIdentifiers`, `SurchargesAndDiscounts` with `|`, `DoNotCombineWith`).

Indexes (`FiLogicDbContext.cs` 381-429): many single-purpose indexes on `tblTransportFile` (tenant/deleted × FileDate, Scheduled window, IsCanceled, IsArchived, TransportCompanyId, OrganizationUnitId, FMS status); unique `(TenantId, IsDeleted, FileNumber)` (404-405); stop and driver tables have only `TransportFileId` (424, 428). No FK constraints are declared in the model (ids are plain Guids); `FileNumber` uniqueness is per tenant+deleted flag, hence `Delete` renames the number (TFAS 4159).

## 2. Lifecycle

**Creation paths**
1. *Manual*: `GetSingle` with no id builds a new DTO from the type's defaults (TFAS 3936-4010): file date = today in tenant zone, departure time from type or tenant setting `DefaultDepartureTime`, arrival = departure + 2 h (hard-coded, 3985), carrier/departure/arrival default to the type or the "IsMe" relation, an outsourcing incoming invoice is pre-added when the carrier is not "me" (3998-4010). `CreateOrUpdate` (4104-4125) delegates to `TransportFileRepositoryHandler.ValidateCreateOrUpdate` + `CreateOrUpdate`, which validates foreign keys, the ABP features `MaxTransportFilesPerDay` and `MaxStopsInTransportFile`, forbids changing the year/month of `FileDate` when the number scheme embeds it, generates the number, inserts file + drivers + incoming invoices + attributes row, and on update diffs invoices/drivers, re-dates legs, syncs the template order and forwards for CO₂ (handler lines ~40-360).
2. *From legs* (planboard drag/drop): `AssignTransportFileFreightLegs` with an existing `TransportFileId` (§3).
3. *Along with an order*: an order flagged `CreateTransportFileAlongWithTemplate` creates/updates a transport file **with the same Guid as the order** (`TransportOrder/TransportOrderAppService.cs` 2923-2960, `TransportFile.Id = transportOrder.Id` at 2960), copying the order's `TemplateTransportFile*` fields, then assigns legs and mirrors the order's manual stops (`SynchronizeFreightStopsToTransportFile`). The reverse sync writes file changes back onto the order (`TransportFileRepositoryHandler.UpdateAssociatedTransportOrders`, template name, date, vehicles, drivers, buying price).
4. *Convert* (TFAS 4269-4484): splits a trip at a transfer point — creates a new file of another type, inserts a successive leg per source leg with waypoint types by SUTC code `TE1002`/`TE1003` (couple/decouple trailer), re-points the source leg's destination, assigns both sets, optionally optimises.
5. *FMS-sourced*: `SourceFromFleetManagementSystem` (1068-1096) rebuilds an order/file pair from board-computer activity.
6. A "generate transport files" dialog was not found in the backend (no `GenerateTransportFiles` symbol); templates exist only as order templates (`IsTemplate`).

**States**: no state machine. `IsCanceled`/`IsArchived` are independent booleans; `Archive`/`Unarchive`/`Cancel` (4173-4267) each reject already-cancelled or already-archived files but nothing else checks them (a cancelled trip can still receive legs). Execution state is derived from `ActualTimeOfDeparture`/`ActualTimeOfArrival` (in process = departed and not arrived, `TransportFileRouteCalculator.cs` 388). `FleetManagementSystemDeviceStatus` (`FleetManagementSystemDeviceTransitStatus.cs`: Unknown, NotPublished, Sent, Received, Confirmed, Completed, Error) tracks the on-board unit. `TransportUnitTransactionsProcessed` is set once the file's pallet/unit transactions were pushed to TrackOnline (`ThirdPartyApps/TrackOnline/TrackOnlineAppService.cs` 380).

**Completion**: `Complete` (5186-5211) sets ATA/ATD on the file and all stops where still null; `CompleteStop` (5168-5184) per stop. Both run in the ambient UoW with no validation.

**Delete** (4127-4171): unassigns legs (`TransportFileId = null`), hard-deletes stops, drivers, questionnaire results and incoming invoices via repository `DeleteAsync` (soft-delete filter applies), suffixes `FileNumber` with a Guid to free the unique index, soft-deletes the file.

**Number generation** (`TransportFileNumberGenerator.cs` 36-93): per type; options plain / year / year+month prefix; finds the last file by `OrderByDescending(FileNumber)` string sort and parses the suffix (95-123); an in-memory `_lastGeneratedFileNumber` only guards within one transient instance — no sequence, no lock, race-prone.

## 3. Leg assignment (`TransportOrderFreightLegAssignmentManager.cs`)

`AssignLegsToTransportFile(input)` (66-172), one transaction:
1. Loads the legs; every id must exist (80-83). Impacted files = files the legs were on + target (86-88).
2. Loads all legs of all impacted files, all their stops, waypoints and order-level freight stops (98-122). Records which stops are manual (empty identifiers) *before* mutation (112-115).
3. For legs moving away from another file: removes the leg id from that file's stops (125-131). Sets `TransportFileId` on every leg (133) — `null` unassigns.
4. If tenant setting `AdjustTransportOrderFreightLegDatesToTransportFile` is on, overwrites the legs' `DepartureFromOrigin`/`ArrivalAtDestination` and the freight waypoints' dates with the file's scheduled dates (136-147, 272-303).
5. `RedetermineStops` (174-200): per impacted file resolves the stop calculator from the **type's** `DefaultStopCreationMode` (the file's own mode is not consulted) and recalculates.
6. Unassigned legs: stripped from stops; stops left with no legs are deleted (152-161).
7. Audit stamps, insert new stops (`Id == Guid.Empty`), delete flagged stops (212-241).

**Validations**: existence only. No capacity, vehicle-type, date-window or cancelled/archived check at assignment time; those surface later as grid warnings (§8).

**Splitting a freight item** (`SplitTransportFileFreightLeg`, TFAS 2529-2683): amount must be set and `NewSource + NewTarget == Amount`; clones the freight, its waypoints and legs, pro-rates positions/colli/volume/weight/EPQ/loading meters/load factor by percentage (rounded to 5 decimals), then assigns the clones of the other legs back to their original files and the split leg's clone to the target file. `GenerateReturnFreight` (5226-5355) clones the freight with origin/destination swapped and assigns to the same file.

**Ordering**: legs are processed by `OrderNumber`; new stops get order numbers from the order-level freight stop when `ApplyFreightStopOrderNumbers` else 1,000,000+ so they land at the end, then `ApplyCorrectOrderNumbers` renumbers 1..n (calculator base 81-91).

**Post-processing** (`AssignTransportFileFreightLegs`, TFAS 2164-2269, `PostProcessLegAssignmentCore` 2301-2398), in separate `RequiresNew` transactions: (1) departure/arrival relation set to first/last stop when the type says so (2412-2455); (2) per tenant setting `RouteCalculationOnLegAssignment` → `CalculateEtas` or `OptimizeStops` (exceptions swallowed to a warning log, 2360-2364); (3) delete cached `Route` rows; (4) `IncomingInvoicesUpdateManager.UpdateTransportFileIncomingInvoices` (charter price lines); (5) purchase prices: clear for unassigned legs, recalc per file (`IFreightPriceCalculationService`); (6) attribute sync stored procs; (7) CO₂ forward job — whose `IsEnabled()` returns `false` (`Forwarding/ITransportFileForwardJob.cs` 57), so CO₂ is never recalculated. With `DeferPostProcessing` (planboard) the chain runs in a **fire-and-forget `Task.Run`** with a re-resolved app service (2233-2252), then echoes via SignalR `PlanboardEchoBroadcaster`.

## 4. Stop generation

`StopCreationMode` (0 `DistinctByLocationAndWaypoint` "op basis van lokatie en activiteit", 1 `DistinctByFreightItemWaypoint` "per vrachtregel stop (pendelritten)"). `TransportFileStopFactory.Resolve` (20-29) scans all `ITransportFileStopCalculator` implementations for `Supports(mode)`.

- **DistinctByLocationAndWaypoint** (`DistinctByLocationAndWaypointTransportFileStopCalculator.cs` 29-106): per leg, origin and destination stops are matched on `(TransportFileId, RelationId, WaypointTypeId)`. Preference: an existing stop already referencing the leg; else an existing pair with no ATA/ATD (so a stop already visited is never reused, 45-73); else a freshly created stop in this pass; else new. Then `RemoveFreightReferencesOfFreightItem` strips references to legs no longer on the file and deletes emptied stops, sparing manual stops (base 18-61).
- **DistinctByFreightItemWaypoint** (`...FreightItem...` 28-73): always two new stops per leg unless a stop for that leg exists and is already in progress/completed.
- **Manual stops**: `CreateOrUpdateStop` (TFAS 3582-3676) inserts at position 1 and shifts the rest (3542-3580), deletes cached routes, re-applies first/last-stop relation rule; updating a stop's departure time shifts all successive stops' calculated/estimated/actual times by the same delta (3617-3652). `DeleteTransportFileStopAsync` (3513-3540) renumbers.
- **Time-based generator** (`TimeBasedTransportFileStopGenerator.cs` 12-79): inserts a waypoint-type stop at intervals over two days — `autoGenerateWaypointInMinutes = 0` and it is **never called** (grep finds only the constructor injection, TFAS ~210). Dead.
- **Via/depot stops**: no explicit concept. Departure/arrival relations are file fields, not stops; route optimisation can inject temporary "stationing" stops for secondary/third vehicle pick-up tagged `nonPrimaryVehicleLocationTag` and deletes/re-inserts them each run (`RouteOptimalization/RouteOptimalizationAppService.cs` 224-263, 358-364, 378-400). Convert adds couple/decouple legs by SUTC code.
- **Re-sequencing**: `ModifyStopOrderNumber` (TFAS 3678-3806) moves stops within one file, rejects stops with ATA, **nulls all calculated times/distances of every stop** (3722-3726), deletes cached routes, syncs attributes. `CarryOutSeparately` (4918-4986) pulls one leg out of shared stops into its own stop pair appended at the end.
- **How CTA/CTD/cumulative distance are filled**: only by `OptimizeOrCalculateStops` (same file 151-420). It requires departure relation and scheduled departure (288-291), all legs geocoded (292-307), asks the configured `IRouteCalculationService` (`RouteCalculationServiceFactory` 65-97; implementations PTV and SmartRoute, RouteXl commented out) to `OptimizeStops(file, stops, legs, skipOptimalization, …)`, then `ExecuteUpdate`s the file's `ScheduledDistance`, optionally `ScheduledTimeOfArrival` (setting `AdjustTransportFileScheduledTimeOfArrival`) and `ScheduledTimeOfDeparture` (vehicle-stationing setting) (348-356), and each stop's `OrderNumber`, `CalculatedTimeOfDeparture/Arrival`, `CalculatedCumulativeDistance` (404-411). Relation-less stops get times interpolated with waypoint-type transaction time (422-520).
- **RouteOptimizationMode** (`RouteOptimizationMode.cs`): `ShortestRouteToEmptyVehicle`, `OptimizeRoundTrip` (requires arrival relation, 216-221), `SetScheduledTimeOfArrivalToLatestTimeslot`. The mode actually used comes from a **tenant setting**, not the file's `RouteOptimizationMode` column (211-214).

## 5. Route calculation and ETA

`ITransportFileRouteCalculator` (`Calculators/ITransportFileRouteCalculator.cs`) is read-only presentation: `TransportFileRouteCalculator.GetTransportFileRoute` (182-268) loads file, stops with waypoint type, relation visiting-address coordinates, primary vehicle spec and (when in process) the live FMS device location or last-seen vehicle position (385-422), then POSTs origin/waypoints/destination plus vehicle weight (default 40 t), euro class (default EURO_VI), dimensions, fuel to the Google Cloud "route function" (`RouteFunction:Url`, Valhalla behind it) with a 10 s timeout (455-474, 556-578). Response: polyline, distance, duration, toll per country segment, CO₂ (`TotalKgCO2e`, `gCO2e/tkm`). On a truck-routing failure it retries without the vehicle profile and drops toll/CO₂ (476-502). **Nothing is stored**; it is called per planboard view (`GetRoute`, TFAS 1049-1058).

Stop ETAs: `EtaRecalculationBackgroundWorker.cs` runs every 10 min (33) as admin, over tenants that have in-flight files (`TransportFilesInProgessLookupService` 22-26: FMS status Sent/Received/Confirmed, departed <2 days ago, not arrived, raw SQL per tenant DB). For each file (departed in last 3 days, 56-66) with at least one unvisited stop it asks the routing service `CalculateEstimatedTimeOfArrivals` and writes `EstimatedTimeOfArrival/Departure` and `CalculatedCumulativeDistance` via raw SQL `TransportFileStopEtaRepository.UpdateETA` (110-124). ETA e-mail workers (`EtaNotificationsSendingBackgroundWorker`, `RelationConfigured…`) are out of scope. `GetTransportFileStopEtaForecast` (TFAS 2784-2947) is a demo: delays are `RandomHelper.GetRandom(0, 20)` (2865).

`ActualDistance` sources: FMS activity updates / manual edit only; `ActualCumulativeDistance` per stop comes from FMS (`UpdateATA/ATD`); `GetDurationAndDistanceOverStops` (988-1047) derives span between first and last actual values.

## 6. Cost and revenue

Two generations coexist.

*New (pure, stateless) calculators* in `Calculators/`, used by `GetAll_Perf` (TFAS 1524-1601, assigned 1845-1850):
- `ITransportFileCostCalculator.CalculateCostPerKm` (63-110): own fleet only (file's `TransportCompanyId == IsMe`): sum of `Vehicle.CostPricePerKm` else `VehicleType.CostPricePerKm` over the three vehicles, fallback tenant setting `CostPricePerKm`. `Calculate` (37-61): cost = Σ incoming invoices (`ActualPrice ?? ExpectedPrice`) + costPerKm × (`ActualDistance ?? ScheduledDistance`). Charter buying price = the incoming invoice lines (`IncomingInvoicesUpdateManager`, outsourcing invoice type). No per-hour cost. D&S on the trip: only the order-template `TemplateTransportFileDiscountAndSurchargeId` applied to buying price in the along-with-order sync.
- `ITransportFileRevenueAllocator.Allocate` (29-97): per leg `leg.Distance × order revenue-per-km`; if that yields 0 and the file holds all freights of exactly one order, the full order revenue (fallback with a `todo` about two orders).
- `ITransportFileLoadCapacityCalculator` (23-109): "vehicle equivalent" = Σ over freights of `Amount / Σ MaxCapacity(unit, vehicle types)` × 100; exposed as `LoadFactor`.
- `CalculateLoadingMeters` / `CalculateLoadCapacity` (TFAS 2685-2782, input `CalculateLoadRateInput` = legs + three vehicle ids) return max loading meters vs. Σ freight loading meters, and positions-based capacity %, for the planboard tooltip.

*Legacy* `TransportFileCostCalculator.cs` (parent folder, 513 lines) does the same with repository queries per call (`CalculateCostPerAssignedLegKM`, `CalculateTransportFileCost`, `CalculateTransportFileRevenue` incl. D&S string parsing with `|`), still used by the dashboard and order grid.

*Shipments* (`FiLogic.Core/Shipments/`): **live, not dead** — `Cofret.Allocate` distributes a trip total (distance/duration/CO₂) over legs by `units × great-circle distance`, used by `PriceList/FreightPrices/Calculators/FreightPurchasePriceCalculationInputBuilder.cs` 213, 260 for purchase prices.

**Stored vs. computed**: `Cost`, `Revenue`, `LoadFactor`, `Warnings` are computed on every grid read; `TransportFileAttributes` caches Cost/LoadFactor etc. via stored procs but the grid overwrites them. Loading performance indicator columns exist but no writer was found. Purchase prices per leg are stored (`TransportOrderFreightPrice`) by `UpdatePurchasePricesByFileIds`.

## 7. ITransportFileAppService operations (`ITransportFileAppService.cs`)

Service-level `[AbpAuthorize()]` only (TFAS 60); no per-operation permission.

**Read/grid**: `GetAll` (→ `GetAll_Perf`, filters §9, enriches cost/revenue/load/warnings), `GetAllAttributes`/`GetLegAttributes`/`GetStopAttributes`/`GetUnscheduledLegAttributes` (column metadata), `GetList`, `GetTransportFileSelectionList`, `GetSingle` (edit DTO: file, stops, legs, invoices, drivers, custom props), `GetSingleTransportFileOutputDto`, `QuerySingleTransportFileListDto`, `QueryTransportFileListDtos`, `QueryLegListDtos`, `GetLegIdsByTransportOrderId`, `GetAllLegs`, `GetAllUnscheduledLegs`, `GetAllStops`, `GetSingleStop`, `GetEtasByLeg`, `GetTransportFileStopEtaForecast` (random), `GetDurationAndDistanceOverStops`, `GetRoute`, `GetTransportOrderFreightLegRoute`, `GetRouteOptimizationModeList`, `GetStopCreationModeList`, `GetAllEtaRecipients`, `GetPrintEmailTransportFileSelection`.

**Create/update file**: `CreateOrUpdate` (validate + persist, see §2); inline `UpdateFileName`/`UpdateFileRemarks`/`UpdateFileDriverInstructions` (520-543; also syncs template order); `UpdateTransportCompany` (545-576: re-queries incoming invoices, full CreateOrUpdate, attribute sync); `UpdatePrimaryVehicle` (578-669: also sets default secondary/third vehicle, departure/arrival to the vehicle's stationing relation or type defaults, carrier from vehicle, default drivers, CO₂ forward); `UpdateSecondaryVehicle`/`UpdateThirdVehicle` (drivers from vehicle); `UpdateDrivers` (delete-all/insert); `SetHighlightColor` (planboard echo); `SetFlags`; `Convert`.

**Legs**: `AssignTransportFileFreightLegs`, `SplitTransportFileFreightLeg`, `GenerateReturnFreight`, `CarryOutSeparately`, `OutsourceOriginActivityByStop`/`ByLeg` (5003-5148: inserts a new leg to a hub on another file, re-points the old leg, creates waypoint with a random reference, queries distances, creates stops directly — bypasses the assignment manager), `InlineUpdateLegProperty` (795-944: references, remarks, time slots, unit, product, temperature, order name/reference — writes into order aggregates), `SetTransportOrderFreightLegFlags`, `AdjustTransportOrderFreightLegDatesToTransportFile`, `PostProcessLegAssignment`.

**Stops**: `CreateOrUpdateStop`, `DeleteTransportFileStopAsync`, `ModifyStopOrderNumber`, `CompleteStop`, `Complete`.

**Lifecycle**: `Delete`, `Archive`, `Unarchive` (not on interface), `Cancel`.

**FMS**: `CanSendToFleetManagementSystem` (which files have a device via vehicle), `SendToFleetManagementSystem` (enqueues `FleetManagementSenderBackgroundJob`, which calls the vendor's `CreateOrUpdateTrip` and sets status `Sent`, job 155-164), `RedrawFromFleetManagementSystem` (`DeleteTrip`, status `NotPublished`), `SourceFromFleetManagementSystem`, `ExportQuestionnaireResult`.

**Finance/other**: `CalculateLoadingMeters`, `CalculateLoadCapacity`, `UpdateAssociatedTransportOrder`, `SendEtaNotification` (also persists wizard margin as tenant setting), `ExportAllToExcel`/`ExportAllLegsToExcel`/`ExportAllUnscheduledLegsToExcel`/`ExportAllStopsToExcel` (Hangfire jobs in `ExcelExport/`), `FixERR` (re-runs all attribute stored procs).

## 8. Warnings

File-level (`Calculators/ITransportFileWarningBuilder.cs` 48-267), computed per grid row:
1. `TransportFileRequiresCapability {name}` — a freight requires a capability no vehicle, vehicle type or driver of the file has (102-108).
2. `InvalidTemperatureRangeCombination {a}; {b}` (111-135), `InvalidProductCombination` (138-161), `InvalidTransportUnitCombination` (164-186) — `DoNotCombineWith` pairs present on one trip.
3. `DepartureFromOriginNotWithinScheduledTransportFileExecutionTime` / `ArrivalAtDestination…` — leg dates outside [scheduled departure date, arrival date+1) (189-211; the arrival branch checks the wrong key at 205, marked "intentionally … to match prod").
4. Unlocalised English strings "Vehicle {x} has a calendar event from … till …" / "Driver … has a calendar event …" (236-257; vehicle name prints the entity's `ToString()`).

Stop-level (TFAS 3233-3257): `EtaNotInRequiredTimeSlot` (severity Error when `MustBeMet`) / `EtaNotInTimeSlot`, `VehicleNotAbleToAccessLocation {types}`.

## 9. Querying

Grid DTO `GetAllTransportFileDto`: all file columns plus names (type, OU, relations, vehicles, vehicle types), `DriverRelations`, `VehiclesMetadata`, `IsEnvironmentalFriendlyVehicleType`, `IsPublishedToFleetManagementSystem`, attribute columns (`NrOfStops`, `NrOfLegs`, `Customers`, `TransportOrders`, stop names/cities, load values), aggregated `TransportOrderReferences`, `Barcodes`, `InvoiceReferences`, `ContainerNumbers`, `SensorCodes`, `ExchangeGatewayTransmissions`, `Warnings`, `Revenue`, `Cost`, `ExpectedPrice`, `ActualPrice`, `EmailDeliveryConfirmationStatus`, custom properties.

Filters (`Queries/TransportFileQueries.cs` 10-78, input `GetAllTransportFilesInput`): `DateRange` on FileDate, `ExecutionDateRange` on the scheduled window, `IsCanceled`, `IsArchived`, `TransportCompanyId(s)`, `OrganizationUnitId(s)`, `FlagFilterEnum` bitmask, `HideEmptyTransportFiles` (attributes.NrOfStops ≠ 0), `IsFmsSourced` (order `TransportFileFillsTransportOrder`), `TransportFileId(s)`; free text over 39 columns via `ToLower().Contains` incl. `ToString()` of dates and doubles; group-by aggregation; sorting with an "unsupported columns" blacklist (1271). Menu permissions `TransportFilesToday/ThisWeek/Open/All` (`Authorization/AppPermissions.cs` 84-88) are the UI's date presets; the "in progress/completed lookup services" are tenant-DB scanners for background workers, not grid filters.

Legs grid: `GetAllLegsDtoQuerySelector` (`GetAllTransportFileLegListDto`: order, relation, origin/destination addresses, time slots, freight quantities, `Cost` from purchase price lines, `IsCarriedOutWithSeparateStop`), filtered by file, date range, OU, flags; "unscheduled legs" = same selector without a file. Excel exports run as Hangfire jobs with the user's selected `Fields`.

## 10. Known problems not to clone

1. **Leg references as `;`-delimited string** on the stop, searched with `Contains(guid.ToString())` (`TransportFileStop.cs` 26; manager 127, 154; TFAS 2105, 4926) — no FK, no index, substring false-positives impossible only because GUIDs are fixed-width.
2. **Derived values computed on every read**: cost, revenue, load factor, warnings in `GetAll_Perf` (TFAS 1524-1601) and the legacy calculator for dashboards; plus a parallel stored-procedure cache (`spTransportFileAttributes_*`, triggers) that the read path partly ignores. Two cost/revenue implementations drift.
3. **Doubles for money and quantities** everywhere; rounding sprinkled (`Round(2)`, `Round(5)`, `Round(4)`).
4. **No time zones**: `DateTime` columns, `DateTime.Now` on stop creation (TFAS 3549) next to `UtcNow` elsewhere, `ToLocalTime()` on the server (2812), `TimeZoneCalculator` conversions in business logic (manager 274-277).
5. **Missing constraints**: no FKs; stop/driver tables indexed only by file id; `FileNumber` unique only per `(Tenant, IsDeleted)` requiring the delete-rename hack (TFAS 4159); number generator races (`TransportFileNumberGenerator.cs` 53-55).
6. **Business rules in settings and types rather than the aggregate**: stop mode and route mode come from the type/tenant setting, the file's own `RouteOptimizationMode` is ignored (`RouteOptimalizationAppService.cs` 211-214); cancelled/archived files accept legs.
7. **Fire-and-forget post-processing** (`Task.Run`, TFAS 2233) and swallowed exceptions (2360, 5160) — ETA/price/invoice updates can silently not happen; nested `RequiresNew` UoW gymnastics documented in comments (2177-2201).
8. **N+1 / full-table loads**: `GetTransportFileStopEtaForecast` loads all relations (2795), `RedrawFromFleetManagementSystem` loads all relations (4546), `SummarizeTransportUnitLoadFactors` loads all load factors (4699), `ModifyStopOrderNumber` updates stops one by one (3792-3793), `Convert` queries vehicles/relations one at a time, `Thread.Sleep(1)` "to ensure order of saving" (`TransportFileRepositoryHandler.cs` 309, 325).
9. **Bugs**: `Convert` looks up the target carrier by the *departure relation id* (TFAS 4308) and updates the wrong leg when renumbering (4441 updates `sourceTransportOrderFreightLeg`); warning builder arrival check keyed on departure string (205); `Complete`/`CompleteStop` take non-nullable dates but use `??=`; `TransportFileCostCalculator.GetEffectiveVehicleCost` catches NREs with `Console.WriteLine` (new calculator 126-127).
10. **Dead/demo code**: `TimeBasedTransportFileStopGenerator` (unused, would loop with 0-minute step), `GetAll` legacy branch after an unconditional `return` (TFAS 1239-1261), commented-out `UpdateTransportFileIncomingInvoices` block (2457-2527), random ETA forecast, CO₂ forward job permanently disabled, `RouteXl` service commented out. `FiLogic.Core/Shipments` is **live** (Cofret allocation) despite the odd placement.
11. **Side effects into other aggregates**: `InlineUpdateLegProperty` edits order name/reference and freight unit/product; FMS sourcing rebuilds orders; triggers (`trigger_TransportFile_CompanyOrVehicleChanged.sql`) push file properties into order attribute tables.
12. **Permissions**: one `[AbpAuthorize()]` on the service; grid presets are the only permission nodes (`AppAuthorizationProvider.cs` 142-146).
13. **Tests**: 14 facts in `test/FiLogic.Tests/Integration/TransportFileAppService/TransportFileAppService_Tests.cs`, all on grid/cost/revenue/load-capacity; none on stop calculators, assignment manager, numbering or lifecycle.

## 11. Functional scenario catalogue

F = fact from code, I = inferred intent.

1. Planner — opens "new trip" — gets a trip pre-filled with today's date, type/tenant default departure time, arrival +2 h, default carrier/departure/arrival relations (F, TFAS 3936-3985).
2. Planner — saves a new trip whose carrier is not own company — an outsourcing incoming-invoice line is created (F, 3998-4010).
3. Planner — saves a trip — number is generated per type scheme (prefix/year/month/sequence) (F, NumberGenerator 36-93).
4. Planner — changes FileDate to another year/month under a year/month scheme — rejected (F, handler `ValidateDateChange`).
5. Tenant — exceeds `MaxTransportFilesPerDay` or `MaxStopsInTransportFile` feature — save rejected with upgrade hint (F).
6. Planner — saves a trip referencing a missing vehicle/relation/type — rejected with not-found (F, `ValidateForeignKeyReferences`).
7. Planner — drags a leg onto a trip — leg gets `TransportFileId`; origin and destination stops are created or merged per stop-creation mode (F, manager 66-172).
8. Planner — drags a leg from trip A to trip B — A's stops lose the reference, emptied stops are deleted, B gets stops (F, 125-131, 152-161).
9. Planner — unassigns a leg — leg returns to the unscheduled list; its stops shrink or disappear; manual stops survive (F, 109-115).
10. Planner — assigns a leg while setting "adjust leg dates to file" is on — leg and waypoint dates are overwritten with the trip's scheduled dates (F, 136-147).
11. Planner — assigns legs with location+activity mode — two legs loading at the same relation share one stop unless that stop already has an ATA (F, calculator 45-73).
12. Planner — assigns legs with freight-item mode (shuttle) — every leg gets its own two stops (F).
13. Planner — assigns a leg from an order that has order-level stops with `ApplyFreightStopOrderNumbers` — file stops inherit the order's stop numbers (F, base 64-78).
14. System — after any assignment — stops are renumbered 1..n without gaps (F, base 81-91).
15. System — after assignment with type rule "departure = first stop" — trip departure/arrival relation follow first/last stop (F, TFAS 2412-2455).
16. System — after assignment with tenant setting CalculateEtas — calculated times/distances per stop and trip scheduled distance/arrival are filled by the routing engine (F, RouteOpt 151-420).
17. System — after assignment with setting OptimizeStops — stop order is re-sequenced by the routing engine (F).
18. System — after assignment — cached routes deleted, incoming invoices and purchase prices refreshed, attribute cache synced (F, TFAS 2365-2390).
19. Planner (planboard) — assignment with deferred post-processing — gets an immediate response; derived data arrives via SignalR echo later (F, 2208-2252).
20. Planner — splits a freight item 60/40 — a cloned freight with pro-rated quantities and legs appears; clone of the split leg lands on the target trip (F, 2529-2683).
21. Planner — split with amounts not summing to the original — rejected (F, 2541-2543).
22. Planner — generates return freight for a leg — mirrored freight/leg created and assigned to the same trip (F, 5226-5355).
23. Planner — "carry out separately" a leg in a shared stop — leg gets its own stop pair at the end (F, 4918-4986).
24. Planner — outsources the origin activity to a hub trip — new leg to hub on the other trip, original leg now starts at hub (F, 5003-5148).
25. Planner — adds a manual stop — inserted at position 1, others shift down, route cache cleared (F, 3542-3580) (I: UI then re-orders).
26. Planner — edits a stop's calculated departure — all later stops shift by the same delta (F, 3617-3652).
27. Planner — reorders stops — allowed only within one trip and only for stops without ATA; calculated times are cleared (F, 3678-3806).
28. Planner — deletes a stop — remaining stops renumbered, departure/arrival rule reapplied (F, 3513-3540).
29. Planner — opens trip route on the map — geometry, toll, CO₂ from the route function; schematic fallback when geocoding missing (F, RouteCalculator 182-268, 443-453).
30. Planner — views an in-progress trip — current vehicle position from FMS device or last-seen (F, 385-422).
31. System — every 10 min — ETAs of stops on departed, unfinished, FMS-published trips are recalculated (F, EtaWorker 33, 56-124).
32. Planner — optimises a round trip without arrival relation — rejected (F, RouteOpt 216-221).
33. Planner — optimises with an ungeocoded address — rejected naming the address (F, 292-307).
34. Planner — sets primary vehicle — secondary/third defaults, stationing relation as departure/arrival, carrier, default drivers follow (F, TFAS 578-669).
35. Planner — changes carrier — incoming invoices re-queried from price lists and purchase prices refreshed (F, 545-576; handler `GetFilesToUpdatePurchasePrices`).
36. Planner — sets drivers — replaced wholesale; attribute "Drivers" text updated (F, 745-793).
37. Planner — edits name/remarks/driver instructions inline — saved and mirrored to the along-with-order template (F, 490-543).
38. Planner — sets highlight colour on several trips — saved and broadcast to other planboards (F, 363-387).
39. Grid — own-fleet trip — cost = invoices + (vehicle or type cost/km, else tenant cost/km) × (actual or scheduled distance) (F, new calculator 37-110).
40. Grid — charter trip — cost = incoming invoice lines only (F).
41. Grid — revenue = Σ leg distance × order revenue/km; full order revenue when the trip holds the whole single order (F, allocator 29-97).
42. Grid — load factor = Σ freight amount / unit capacity of the trip's vehicle types (F).
43. Planner (planboard tooltip) — load meters and capacity % for a candidate vehicle/leg set (F, 2685-2782).
44. Grid — missing capability, forbidden temperature/product/unit combination, leg dates outside execution window, calendar conflicts → warnings (F, warning builder).
45. Stop list — ETA outside time slot → warning or error when the slot must be met; vehicle type cannot access location → warning (F, 3233-3257).
46. Planner — cancels a trip — flag set; rejected if already cancelled/archived (F, 4236-4267). Legs stay assigned (F).
47. Planner — archives / unarchives — flags toggled with the same guards (F, 4173-4234).
48. Planner — deletes a trip — legs unassigned, children deleted, number suffixed, soft-deleted (F, 4127-4171).
49. Driver/FMS — completes a stop or trip — actual times set where empty (F, 5168-5211).
50. Planner — sends to FMS — only trips with a configured device; job sends trip, status Sent; optional "not before" delay (F, 4486-4661, job 155-164).
51. Planner — withdraws from FMS — vendor trip deleted, status NotPublished (F, 4527-4620).
52. Office — order saved with "create transport file along with" — a trip with the order's id is created/updated from template fields; manual order stops mirrored (F, OrderAppService 2923-3015).
53. Office — edits that trip — template fields on the order are updated back (F, handler `UpdateAssociatedTransportOrders`).
54. Planner — converts a trip at a transfer point — new trip of another type, couple/decouple legs, both re-stopped, optional optimisation (F, 4269-4484).
55. Planner — convert with earlier date/departure than source — rejected (F, 4295-4303).
56. Planner — filters grid by date, execution window, cancelled, archived, carrier, OU, flags, empty, FMS-sourced, free text (F, Queries 10-78).
57. Planner — exports trips/legs/stops to Excel — background job with chosen columns (F).
58. Planner — selects ETA recipients per stop and sends ETA mail (F, 421-488).
59. Planner — inline-edits a leg's reference/remarks/time slot/unit/product/temperature/order name (F, 795-944).
60. Back office — TrackOnline push marks `TransportUnitTransactionsProcessed` (F, TrackOnline 380).
61. System — trip whose vehicle changed — SQL trigger pushes vehicle names into order attributes (F, trigger_TransportFile_CompanyOrVehicleChanged.sql).
62. Planner — requests stop ETA forecast — receives randomised delays (F; I: placeholder never finished).
