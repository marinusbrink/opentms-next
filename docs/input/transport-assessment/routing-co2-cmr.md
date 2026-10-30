<!-- Appendix to docs/input/transport-rebuild-assessment.md and pricing-rebuild-assessment.md. Produced 2026-10-25 by reading the OpenTMS source at ~/dev/OpenTMS (branch Development, commit 5f547cc6b); not available to a cloud session, which is why this copy lives in the repository. Citations are path:line in that checkout. F = fact read in code, I = inferred. -->

# Legacy OpenTMS assessment: Routing, CO₂, CMR

Read-only research of `~/dev/OpenTMS` (ASP.NET Boilerplate + EF Core, Angular), `~/dev/OpenTMS/osrm`, `~/dev/micro-services-valhalla` and `~/dev/OTM CO2 Profile`. Paths below are relative to `aspnet-core/src` unless stated; `angular/` paths are relative to `angular/src/app`. "Fact" = read in code; "inferred" = marked as such.

---

## 1. Routing

### 1.1 Two engine families, two factories

The legacy code splits routing in two interfaces, each with its own reflection-based static factory:

| Interface | Purpose | Factory |
|---|---|---|
| `IRouteCalculationService` (`IntegrationServices/RouteCalculationServices/IRouteCalculationService.cs:10-48`) | Stop-order optimisation of a trip (`OptimizeStops`) and ETA recalculation of a running trip (`CalculateEstimatedTimeOfArrivals`) | `RouteCalculationServiceFactory.cs` |
| `IWaypointDistanceService` (`IntegrationServices/WaypointDistanceServices/`) | Distance/duration (and polyline) between coordinates: leg distances, distance matrices for the optimiser, route geometry | `WaypointDistanceServiceFactory.cs` |

**Route-calculation engines (stop optimisation + ETA):**

| Class | Name / Id | Enabled | Default | Endpoint | State |
|---|---|---|---|---|---|
| `Vroom/VroomCalculationService.cs:28,70-73` | "OpenTMS Route Engine", `9049a02e-…` | true | **true** | `https://mvrp.services.opentms.nl` (hard-coded const) | live, 2054 lines |
| `PTV/PtvCalculationService.cs:54-59` | "PTV Route Engine", `229ac5b3-…` | true | false | `api.myptv.com` via NSwag clients (`PTV/Generated/PtvRoutingClient.cs:44`) | opt-in per tenant; `OptimizeStops` **delegates to Vroom** (`PtvCalculationService.cs` ~69-80); only ETA goes through PTV with `TrafficMode.REALISTIC` |
| `NextBillionAI/NextBillionAICalculationService.cs:97-103` | "FiLogic AI Route Calculation Engine" | **false** | — | `api.nextbillion.io/optimise-mvrp?key=…` with the API key in the URL (`:286,305`) | dead |
| `SmartRoute/SmartRouteCalculationService.cs:79-85` | "SmartRoute" | **false** | — | `filogic.smartroute.nl` (`:1118,1220`) | dead |
| `MapBox/MapBoxCalculationServiceV2.cs` | — | — | — | — | 737 of 996 lines commented out; token left in a comment (`:23`) |
| `RouteXl/RouteXlRouteCalculationService.cs` | — | — | — | — | 630 of 824 lines commented out |

**Waypoint-distance engines:** `Osrm/OsrmWaypointDistanceService.cs` (`IsEnabled() => true`, `:30-33`; `https://osrm.services.opentms.nl/route/v1/driving/…` `:326` and `/table/v1/driving/…` `:275`), `PTV/PtvWaypointDistanceService.cs` (profile `EUR_TRAILER_TRUCK`, Google-encoded polyline, API key from tenant setting `:52-60,103-110`), `Google/GoogleWaypointDistanceService.cs` (used only as fallback), `NextBillionAI/`, `SmartRoute/`.

### 1.2 How an engine is chosen

- Tenant setting `App.RouteOptimalization.DefaultRouteCalculationService` holds the GUID of the preferred `IRouteCalculationService`; default is empty (`FiLogic.Core/Configuration/AppSettingProvider.cs:198`). Saved from TMS settings (`Configuration/Tenants/TenantSettingsAppService.cs:557`); choosing PTV requires a probed PTV key (`:540-543`, `PtvApiKeyValidator.TryProbeAsync`).
- `RouteCalculationServiceFactory.Resolve()` (`:65-98`): read setting for `AbpSession.TenantId ?? 1` → match by id → else any `IsDefault` → else first enabled. Resolution uses `IocManager.Instance` and reflection over all implementations each call (`:20-29,37-50`).
- `WaypointDistanceServiceFactory.Resolve()` (`:27-42`): if tenant default == PTV GUID → `PtvWaypointDistanceService`, else first enabled implementation ("currently OSRM", `:33`).
- Both factories are static singletons that fall back to tenant 1 when there is no session (`RouteCalculationServiceFactory.cs:60-63,72`).

### 1.3 What production uses today

**Fact:** with the setting empty, every tenant gets **VROOM** (`mvrp.services.opentms.nl`) for stop optimisation/ETA and **OSRM** (`osrm.services.opentms.nl`) for all distances and matrices. The `osrm/Dockerfile` builds `osrm/osrm-backend` with `osrm-routed --algorithm mld /data/europe-latest.osrm`, downloading prebuilt Europe data from a DigitalOcean Spaces bucket (`osrm/download_files.sh:3`). The Vroom DTOs are the VROOM API (`shipments`, `vehicles`, `options.g`), i.e. VROOM backed by that OSRM (inferred from the DTOs; the VROOM container itself is not in the repo).

PTV is a recent opt-in path: the last 11 commits on the folder are PTV work (`13a2d3d65 tenant-aware RouteCalculationServiceFactory.Resolve()`, `91d605215 PTV-native CalculateEstimatedTimeOfArrivals with REALISTIC traffic`, `d34464fc1`, `4d0f32bfc`, `7bf8a9a46 …PtvWaypointDistanceService is disabled but resolves if PTV prefered by tenant`). It is per-tenant and off by default.

**Valhalla is wired, but not as a route-calculation engine.** It sits behind the Go Cloud Function `prod-filogic-services-route` (repo `filogic/micro-services-valhalla`, README: "Viatiq Routing Service … Valhalla + OSM", Cloud Run 16 GiB, Europe + Asia tiles rebuilt weekly). OpenTMS calls it in three places:

1. `BusinessServices/TransportFile/Calculators/TransportFileRouteCalculator.cs:65-66` (URL from `RouteFunction:Url`, falling back to the hard-coded `https://europe-west4-filogic-opentms.cloudfunctions.net/prod-filogic-services-route`) — route geometry, toll and CO₂ for a trip (`GetTransportFileRoute`, `:182`) or a leg (`:271`), shown on the planboard map. The vehicle spec comes from `RouteOptimalization/Dto/RouteVehicleSpecMapper.cs`.
2. `IntegrationServices/RouteFunctionServices/TollKilometersService.cs:54-55,104-131` — tolled kilometres for the price-per-km surcharge engine, always as a 40 t EURO VI truck.
3. `angular/services/route-function.service.ts:156` — the browser calls the function directly (bypassing interceptors) for the LOS app's missing polylines; the planboard gets consolidated data from the backend (`:145-149`).

No distance that is **stored** on a freight, leg, trip or stop comes from Valhalla; those come from OSRM/PTV. The grep for `valhalla|osrm` in `appsettings*.json` finds nothing: all engine hosts are in code.

### 1.4 Request/response shapes

- **OSRM route** (`OsrmWaypointDistanceService.cs:302-370`): GET `lon,lat;lon,lat…` (8 decimals) → `routes[0].legs[i].distance/duration`; distance is rounded to whole km (`:354`), duration rounded to the minute and stored as a `TimeSpan.ToString()` (`:355`). Table API gives a duration matrix for `FindMostDistancedWaypoint` (`:257-298`).
- **VROOM** (`VroomCalculationService.cs:1540-1682,1152-1172`): `Shipments[]` with `Pickup`/`Delivery` (`Location=[lon,lat]`, `Description="{stopId}|{legId}"`, `Service` seconds from product/transport-unit transaction times, `TimeSlots`, `Priority`), one `Vehicle` with `Start`/`End`, `Options { G = true }` (geometry). Response: `Code`, `Routes[].Steps`, `Unassigned`. If shipments are unassigned the time windows are widened twice, then dropped entirely (`:95-117`). Stationing stops of secondary vehicles are forced as a strict sequence in a second request (`:119-125`).
- **Route function** (`TransportFileRouteCalculator.cs:581-640`): POST `{origin, destination, waypoints[], vehicle{weight, euroClass, height, width, length, fuelType, fuelConsumption}}` → `{route{distance m, duration s, polyline}, toll{totalCost, totalDistance, segments[cost, ratePerKm, distance, geometry]}, carbonFootprint{totalKgCO2e, gCO2ePerTkm}}`. When truck routing fails (Valhalla 442 "no path"), it retries without a vehicle and keeps only the geometry (`:473-490`).
- **Result for stops** (`RouteCalculationResult.cs`): per stop `OrderNumber`, `TimeOfArrival`, `TimeOfDeparture`, `CumulativeDistance`; plus route-level `DelayedTimeOfDeparture`, `TimeOfCompletion`, `DistanceOfCompletion` duplicated on every element (`:13-16` "fixme").

### 1.5 The distance cache

`tblWaypointDistanceCache` (`FiLogic.Core/BusinessEntities/WaypointDistanceCache/WaypointDistanceCache.cs`): `Origin`/`Destination` strings `"lat,lon"` with 5 decimals (`WaypointDistanceCacheService.cs:16-20`), `Distance` (km), `Duration` (string), `Route` (unused). Rows are always written with `TenantId = 1` (`:98-106`), i.e. a host-wide cache. There is **no TTL**. Hit logic: load every row whose origin *or* destination matches any coordinate, then `Find(origin,destination)` in memory (`:54-63,80-91`). **The read side is disabled**: `FindAsync` returns an empty matrix on its first line (`:82`), while `Update` still inserts a row for every OSRM leg on every call (`OsrmWaypointDistanceService.cs:206-211`) — a write-only table that grows without bound.

The toll-km result is cached separately in ABP's `ICacheManager`, key `"lat,lon|lat,lon"` at 3 decimals, absolute expiry 5 minutes (`TollKilometersService.cs:31-36,90-99`).

### 1.6 Where distances live

| Entity | Fields |
|---|---|
| `TransportOrderFreight` (`FiLogic.Core/BusinessEntities/TransportOrder/TransportOrderFreight.cs:56,68,76`) | `Duration` (string), `Distance`, `CarbonFootprint` |
| `TransportOrderFreightLeg` (`…/TransportOrderFreighLeg.cs:25,30,38,41`) | `Distance`, `Duration` (string), `EstimatedCarbonFootprint`, `ActualCarbonFootprint` |
| `TransportFile` (`…/TransportFile/TransportFile.cs:50-57`) | `ScheduledDistance`, `ActualDistance`, `EstimatedCarbonFootprint`, `ActualCarbonFootprint` |
| `TransportFileStop` (`…/TransportFile/TransportFileStop.cs:34-48`) | `CalculatedCumulativeDistance`, `EstimatedTimeOfArrival/Departure`, `EstimatedCumulativeDistance`, `ActualTimeOfArrival/Departure`, `ActualCumulativeDistance` |

Leg distances are (re)computed in the order service via `RelationAppService.QueryDistance` (`BusinessServices/Relation/RelationAppService.cs:1185-1215`): resolve waypoint service → OSRM; when the result is null or 0 km it silently falls back to **Google** (`:1193-1198`); the tenant speed-correction factor (default `80`, i.e. +20 % duration, `OsrmWaypointDistanceService.cs:59-85`) and optionally transaction times are added. Example writer: `TransportOrderAppService.cs:1017-1040` when a leg is removed from a chain. `TransportFile.ScheduledDistance` is set by `OptimizeStops`/`CalculateEtas` via `ExecuteUpdate` (`RouteOptimalization/RouteOptimalizationAppService.cs:351`); `ActualDistance` comes from the FMS handler (`IntegrationServices/FleetManagementSystems/FleetManagementSystemIntegrationTransportOrderHandler.cs:661`).

### 1.7 Ordering stops (optimisation modes) and triggers

`AppSettings.RouteOptimalization` (`FiLogic.Core/Configuration/AppSettings.cs:17-43`):

- `Mode`: `ShortestRouteToEmptyVehicle = 0`, `OptimizeRoundTrip = 1` (default; needs an arrival relation), `SetScheduledTimeOfArrivalToLatestTimeslot = 2`.
- `RouteCalculationOnLegAssignment`: `Disabled`, `CalculateEtas` (default), `OptimizeStops` — runs after legs are assigned to a trip (`TransportFile/TransportFileAppService.cs:2320-2345`) inside a `TransactionScopeOption.Suppress` unit of work; failures are logged and swallowed (`:2342-2345`).
- `DelayTransportFileStopArrivalBasedOnLegArrivalDate`, `AdjustTransportFileScheduledTimeOfArrival`, `AdjustTransportFileScheduledTimeOfDepartureBasedOnVehicleStationing`, `DefaultSpeedCorrectionFactor`, `PtvApiKey`.

`OptimizeStops` flow (`RouteOptimalization/RouteOptimalization.md:17-30`): filter relation-less and auto-generated stops, keep stationing stops in place, call the engine, merge back, update file distance/STA/STD, rewrite stop order numbers and ETAs.

### 1.8 ETA worker

`TransportFile/EtaRecalculationBackgroundWorker.cs`: every 10 minutes (`:64`), only for tenants with in-flight work (`:73-77`), selects files published to an FMS device (`Confirmed/Received/Sent`), departed within 3 days and not arrived (`:89-103`), resolves the tenant's engine and calls `CalculateEstimatedTimeOfArrivals`, persisting ETA/ETD/cumulative distance per stop (`:132-152`). The Vroom implementation (`VroomCalculationService.cs:859-980`) uses the latest FMS device position (≤15 min old) of the primary/secondary/third vehicle, else the last stop with an actual departure/arrival, else the departure relation; then a distance matrix from that point through the remaining stops.

### 1.9 Failure handling, quotas, costs

- OSRM errors are logged and yield `null` → Google fallback (and a paid Google call). Vroom: no response → `UserFriendlyException("NoResponseFromService")`; `Code != 0` → `null` → empty result (`:1165-1170`). Route function: warn + `null` → the map draws a straight line (`angular/services/route-function.service.ts:173-176`); toll-km `null` → the toll surcharge line is dropped (`TollKilometersService.cs:140-144`).
- No per-call pricing is visible in code. PTV is metered per tenant key (`PTV/PTV.md` documents `results=TOLL_COSTS`/`MONETARY_COSTS`, no tariffs). NextBillion/MapBox keys are literals. The Valhalla micro-service note states the always-on Cloud Run instance costs about €150/month (`micro-services-valhalla/claude-code-prompt.md`, "Belangrijk").

### 1.10 Route forecast on the planboard (one line)

`GetTransportFileStopEtaForecast` (`TransportFileAppService.cs:2784-2870`) builds the "route forecast" bar from scheduled times plus `Abp.RandomHelper.GetRandom(0, 20)` minutes of delay per stop (`~:2857`) — the forecast is random filler, not an engine result.

---

## 2. CO₂

### 2.1 Formulas actually running

**Per freight (live):** `TransportOrder/CarbonFootprint/TransportOrderCarbonFootprintManager.cs:169-199`
`CarbonFootprint = CPI × transportUnitAmount × Σ great-circle distance between consecutive freight waypoints`, rounded to 2 decimals. Not road distance, not per fuel type, not per weight. CPI ("accounting carbon performance indicator") comes from tenant setting `TenantDefinedAccountingCPI`, else `AccountingCPI` fetched from the "Transport Performance Database" for the tenant's segment/sub-segment (`AppSettings.cs:271-290`; `BusinessServices/CarbonFootprinting/CarbonFootprintingAppService.cs:39-76`). Waypoints are collected through a `GroupBy` without ordering (`:89-119`), so the pairwise sum is order-undefined (fact: no `OrderBy`; effect inferred).

**Per trip (disabled):** `TransportFile/Forwarding/ITransportFileForwardJob.cs:35-115` posts an OTM trip to the Cloud Function `…/CarbonFootprintCalculator/CalculateTripCO2PerLeg/` (`:37-38`) and writes `TransportFile.EstimatedCarbonFootprint`, then distributes to legs by distance share, weight×distance, or the COFRET amount×distance allocation (`:181-315`, incl. `ActualCarbonFootprint` when the file has a manual actual). The job is enqueued after every save of a transport file (`TransportFileAppService.cs:664,2391`; `TransportFileRepositoryHandler.cs:350`) but `IsEnabled() => false` (`:57`), so it never runs. Note the `using (…) ;` no-op scopes (`:67-68`) and `.Wait()/.Result` blocking (`:79-82`).

**Per route for display:** the route function returns GLEC v3 / ISO 14083 WTW CO₂e (`micro-services-valhalla/README.md`, "CO₂ Methodology"; response `carbonFootprint.totalKgCO2e`, `gCO2ePerTkm`) which `TransportFileRouteCalculator` passes to the planboard; it is not persisted (inferred: only the DTO carries it). Vehicle inputs: `Vehicle.FuelType` (enum `FiLogic.Core/BusinessEntities/Vehicle/FuelType.cs`), `FuelConsumption` l/100 km, `GrossVehicleWeight`, `ExhaustEmissionLevel` (RDW text), `CO2Emission`/`CO2EmissionCode` from RDW/NDW (`Vehicle.cs:90-93`); `VehicleType.DefaultFuelType` (`VehicleType.cs:28`) seeds new vehicles.

### 2.2 Storage, estimated vs actual

Freight: `CarbonFootprint` (CPI estimate). Leg: `EstimatedCarbonFootprint`/`ActualCarbonFootprint` (only written by the disabled job). File: `EstimatedCarbonFootprint`/`ActualCarbonFootprint`, the latter editable by hand (`TransportFileRepositoryHandler.cs:229-236`). `FiLogic.Core/Shipments/Trip.cs` holds a generic allocation model ("total CO2 per file").

### 2.3 Recalculation

`TransportOrderCarbonFootprintReprocessBackgroundJob` (`CarbonFootprint/…ReprocessBackgroundJob.cs`) is enqueued when a tenant changes its CPI in TMS settings (`TenantSettingsAppService.cs:341-352`) and rewrites **every** freight of the tenant with raw SQL `UPDATE … CASE Id WHEN '{guid}'…` in batches of 500 (`Manager.cs:131-160`). There is no recalculation on waypoint change or on trip completion (fact: no other callers).

### 2.4 BigMile

`ThirdPartyApps/BigMile/` (commit `ca7f87afa`): the tenant connects with an API key, probed against `https://api.bigmile.eu/emissions/v2/calculate` (`BigMileClient.cs:32,81-113`) and stored in `App.ThirdPartyApps.BigMile.ApiKey/IsConnected` (`AppSettings.cs:612-618`). Publishing is **manual** from the BigMile page (`angular/bigmile/bigmile.component.ts`, date-range panel `:60-61`): for each transport file the `CarbonFootprintTripProfileMapper` builds an OTM 5.7 "Carbon Footprint — CO2 Calculation Input" trip (vehicle licence plate, stops with postal-code administrative references, ≥1 load and unload action, one `fuelConsumedEvent` with `amount = distance × consumption / 100`, `emissionFactorStandard = "GLEC"`; `IntegrationServices/OpenTripModel/Profiles/CarbonFootprintTripProfileMapper.cs:17-57,636-660`), validates it with `CarbonFootprintInputProfileValidator`, and POSTs each valid trip to `https://api.bigmile.eu/emissions/v2/otm/0.5/allocate` with `ApiKey` and tenant headers (`BigMileClient.cs:136-215`). Invalid trips are logged and skipped; publications are recorded as a JSON list in a tenant setting (`AppSettings.cs:617-618`). Nothing is read back into leg/file CO₂ fields (inferred from the store API: `GetDataPublications`, `DownloadPublicationDataFile`).

### 2.5 ISO 14083 / GLEC / OTM CO₂ profile

- GLEC v3 / ISO 14083 appear only in the Valhalla micro-service (`README.md` "CO₂ Methodology": WTW, Tier 1 GLEC defaults, Tier 2 `fuelConsumption` override) and as the `"GLEC"` token sent to BigMile.
- `~/dev/OTM CO2 Profile` is not a profile document but two libraries: `otm-library.net` (NuGet `OpenTripModel.Model/Profile/Serializer`: OTM v5 types, profile validation, OTM-style JSON) and `otm-library-java`, both with `CarbonFootprintInputProfileValidator`/`CarbonFootprintResultProfileValidator` for the **draft** OTM Carbon Footprint profile (`otm-library-java/README.md`, "Profile validators"). The .NET one is what the BigMile mapper uses.

---

## 3. CMR (consignment note)

### 3.1 Data model

There is **no `ConsignmentNote` entity**. A CMR is rendered on the fly **per `TransportOrderFreight`**: `GenerateConsignmentNoteByFieldOptions` takes `Identifiers` = freight ids (`BusinessServices/ConsignmentNote/Dto/GenerateConsignmentNoteByFieldOptionsInput.cs:22`; loaded at `ConsignmentNoteAppService.cs:485-490`) and produces one note (× `NrOfCopies`, 1–5) per freight (`:359-440,620-651`). `GroupByLeg` merges freights whose header fields are identical into one note, concatenating boxes 5, 13 and 19 and adding goods rows (`:652-681`). `Barcode` = freight id `N`-format; `SerialNumber` = its last 14 hex chars upper-cased (`:620-621`) — there is no sequential CMR number. Trips are printed by collecting the freights of their legs (`DataExporting/ExportFileGenerators/TransportOrder/ConsignmentNoteFileGenerator.cs:61-95`; Angular passes freight or order selections).

Persisted entities: `ConsignmentNoteProfile` (`FiLogic.Core/BusinessEntities/ConsignmentNote/ConsignmentNoteProfile.cs`: `Name`, `IsDefault`, `NrOfCopies`, `ShowBorders`, `ShowQrCode`, `GroupByLeg`, `Field1/2/3/4/16/17/21/22/23Option` GUID, `Field6/13/18Option` as `;`-delimited GUID strings) and `ConsignmentNoteFixedDescription` (`Name`, `Description`; CRUD in `BusinessServices/ConsignmentNoteFixedDescription/`). Tenant-wide defaults live in settings `App.ConsignmentNote.List.*` (`AppSettings.cs:387-402`), read with `;` splitting (`ConsignmentNoteAppService.cs:3092-3098`).

### 3.2 Box mapping (field options are hard-coded GUIDs, `ConsignmentNoteAppService.cs:42-79`)

| CMR box | Default source | Options |
|---|---|---|
| 1 Sender | customer of the order, postal address (`ComposeField1`, `:1575`) | customer, origin/destination location, "myself", executing carrier, shipper, departure/arrival relation of the trip, keep empty |
| 2 Consignee | destination location of the freight (`:1582`) | same relation list |
| 3 Place of delivery | destination city (`:1589`; full address for tenants 1361/2042, `:1596-1602`) | same |
| 4 Place/date of taking over | origin city + date of first waypoint (`:1608`) | same |
| 5 Documents attached | `AttachedDocuments` (`:627`) | — |
| 6–12 Goods table | per freight line; `Field6Option` toggles columns: container no., pincode, size/ISO, amount, transport unit, product code, product, colli, weight, L/W/H, volume, barcode, positions, temperature range, invoice ref, transaction table, column names/totals, fixed descriptions (`:718-838,934-1087,958-962`) | — |
| 13 Sender's instructions | `InstructionsForwarder` + chosen texts (`:1623`) | fixed descriptions etc. |
| 14 Franking | `FrankingOrder` | — |
| 16 Carrier | executing transport company (`:1646`) | relation list |
| 17 Successive carriers | derived from 16 (`:1656`) | relation list |
| 18 Carrier's remarks | toggles: vehicles, order number/reference, file numbers, freight/waypoint remarks and references, shipment and driver instructions, invoice ref, barcode, temperature, special agreements, shipping companies, forwarder instructions, origin/destination references (`:1741-1852`) | — |
| 19 Special agreements | `SpecialAgreements` (`:634`) | — |
| 21 Established in/on | relation + date (`:1965`) | relation list |
| 22/23/24 Signatures | sender/carrier/consignee; base64 stamps + remarks (`:639-647`) | relation list; signed flow fills 23/24 |

### 3.3 PDF generation and storage

The PDF is drawn programmatically with **iTextSharp** (`ConsignmentNoteAppService.cs:18-19,359-425`): A4, embedded Arial (`Fonts/`, `FontLoader`), static boxes and texts when `ShowBorders`, FiLogic logo + QR of the barcode when `ShowQrCode`, one page per copy in red/dark blue/green/slate/gray. No template engine and no report definition — `PdfGeneratorApi` (appsettings) is used for invoices/report definitions, not the CMR. The bytes are parked in the in-memory `TempFileCache` for 5 minutes under a `FileToken` (`:423`) and fetched by the download endpoint. The export generator `"ConsignmentNote"` (`ConsignmentNoteFileGenerator.cs:52-95`) uses the default profile for document distribution.

**Signed CMR:** `GenerateSignedConsignmentNote` (`:1139-1194`) takes a profile (or the default) and stamps; callers are the distribution portal (`BusinessServices/DistributionPortal/DistributionPortalAppService.cs:234-262`, saving one `BinaryObject` of the freight-document type per freight) and the driver-questionnaire processor `SignedConsignmentNoteCreator`/`SignedConsignmentNoteGenerator` (`BusinessServices/TransportFileQuestionnaire/`), enabled by `App.SignedConsignmentNoteCreator.*` settings. Only signed CMRs are stored; printed ones are not.

### 3.4 Print dialog and e-mail

`angular/transport/dialogs/print-cmr-dialog/print-cmr-dialog.component.ts:30-35,118-178`: choose a profile (`cmr-profile-ng-select`, with "add profile"), number of parts (1–5), show borders, QR code, group by leg, per-box option selects (`consignment-note-ng-select`), and tick freights/orders in a selectable table; buttons **Print** (download then `printCallback`), **Download**, **Send e-mail** (opens `EmailSendingDialogComponent` with the generated file; needs an `entityId`). Opened from the transport-order and transport-file wrappers, planboard lists and the distribution portal (`transport-order-wrapper.component.ts:443`, `transport-file-wrapper.component.ts:331`, `planboard/transport-files.component.ts:1237`, …). Saving the dialog also upserts the chosen profile (`ConsignmentNoteAppService.cs:452-470`).

---

## 4. Known problems — do not clone

1. **Engines, hosts and secrets in code**: `osrm.services.opentms.nl` (`OsrmWaypointDistanceService.cs:275,326`), `mvrp.services.opentms.nl` (`VroomCalculationService.cs:28`), NextBillion key in the URL (`NextBillionAICalculationService.cs:286,305`), MapBox token in a comment (`MapBoxCalculationServiceV2.cs:23`), CO₂ Cloud Function URL const (`ITransportFileForwardJob.cs:37`), route-function URL fallback (`TransportFileRouteCalculator.cs:66`, `TollKilometersService.cs:55`, `route-function.service.ts:156`), PdfGeneratorApi key/secret in `FiLogic.Web.Host/appsettings.Development.json:26-27`.
2. **Write-only, cross-tenant distance cache**: reads short-circuited (`WaypointDistanceCacheService.cs:82`), writes to tenant 1 forever (`:98-106`), string keys, no TTL, LIKE-style `Contains` lookups.
3. **Durations and lists as strings**: `Duration` on freight/leg/cache (`TransportOrderFreight.cs:56-66`), `TransportFileStop.TransportOrderFreightLegIdentifiers` `;`-delimited (`TransportFileStop.cs:56-62`), Vroom step description `"{stopId}|{legId}"` parsed back (`VroomCalculationService.cs:1521`), settings `"<segmentId>;<subsegmentId>"` (`AppSettings.cs:282`) and `;`-joined GUID lists (`ConsignmentNoteAppService.cs:3098`), BigMile publications as JSON in a setting.
4. **Static service-locator factories** resolving `ISettingManager`/`IAbpSession` from `IocManager.Instance`, sync and async setting reads that must both be mocked (`RouteOptimalization.md:90-94`), fallback to tenant 1 (`RouteCalculationServiceFactory.cs:72`).
5. **Windows time zones**: `"W. Europe Standard Time"` literal (`IntegrationServices/Sftp/SftpDataImportServiceBackgroundWorker.cs:244`), `TimezoneHelper.GetWindowsTimeZoneIds` (`FiLogic.Core/TimeZoneCalculator.cs:5-21`, ArrowLine), server-local `.ToLocalTime()` in the forecast (`TransportFileAppService.cs:2828`).
6. **Random ETA forecast** (`TransportFileAppService.cs:~2857`).
7. **Guessing units by magnitude**: kg vs tonnes when `> 1000` (`RouteVehicleSpecMapper.cs:40`); euro class by substring sniffing where `"VI"` is tested before `"V"` and `"I"` matches almost anything (`:53-60`).
8. **Hard-coded tenant hack** in CMR box 3 (`ConsignmentNoteAppService.cs:1596-1602`); CMR field options as GUID constants (`:42-79`).
9. **CO₂ by great circle with unordered waypoints**, raw SQL built by string concatenation (`TransportOrderCarbonFootprintManager.cs:146-157`), a never-enabled job that still gets enqueued on every save (`ITransportFileForwardJob.cs:57`; callers `TransportFileAppService.cs:664,2391`), `using(…) ;` no-ops and `.Result/.Wait()` (`:67-82`).
10. **Silent semantic changes**: Vroom drops time windows after two widening retries (`VroomCalculationService.cs:95-117`); OSRM → Google fallback on 0 km (`RelationAppService.cs:1193-1198`); toll always computed as 40 t EURO VI (`TollKilometersService.cs:109`); auto route calculation exceptions swallowed (`TransportFileAppService.cs:2342`).
11. **Monoliths and dead code**: 3110-line CMR service, 2054-line Vroom service, two engines 70–90 % commented out, Google-encoded-polyline decoder duplicated in app service and interface (`IWaypointDistanceService.cs:73`).
12. **Generated PDF held in process memory** (`TempFileCache`, 5 min) — breaks on multiple instances; CMR is per freight with no number, so the same freight prints a different "serial" look-alike every time (the GUID tail) and nothing is archived unless signed.
13. **Distance rounding at the source**: OSRM km rounded to integers and minutes (`OsrmWaypointDistanceService.cs:354-355`) before any aggregation.

---

## 5. Functional scenario catalogue

Format: Role/trigger — expected outcome. (F) = fact in code, (I) = inferred.

**Routing**
1. Planner changes a leg's origin or destination — leg `Distance`/`Duration` recomputed from the relations' visiting addresses via the tenant's waypoint engine; speed-correction factor applied to duration. (F `TransportOrderAppService.cs:1017-1040`, `RelationAppService.cs:1185-1215`)
2. Planner re-queries a lane already computed — today a fresh OSRM call every time; the DB cache is only written. (F `WaypointDistanceCacheService.cs:82`)
3. OSRM returns an error or 0 km — distance falls back to Google Distance Matrix; a warning is logged. (F `RelationAppService.cs:1193-1198`)
4. Relation has no coordinates — `QueryDistanceOutput.Empty`; leg keeps null distance. (F `:1188-1189`)
5. Tenant selects PTV as default engine without a valid key — save is refused with `PtvApiKeyRequired`. (F `TenantSettingsAppService.cs:540-543`)
6. Tenant with PTV default asks for leg route geometry — PTV routing API with `EUR_TRAILER_TRUCK`; others get OSRM. (F `WaypointDistanceServiceFactory.cs:27-42`)
7. Pricing engine applies a toll-only surcharge — toll kilometres fetched from the route function as a 40 t EURO VI truck, cached 5 min; unreachable function → surcharge line omitted. (F `TollKilometersService.cs:104-144`)
8. Planner opens a trip on the planboard map — backend consolidates stops + route-function result (geometry, toll, CO₂); on truck-routing failure the car route is drawn without toll/CO₂; on total failure a straight line. (F `TransportFileRouteCalculator.cs:432-520`)
9. Legs are assigned to a trip and `RouteCalculationOnLegAssignment = CalculateEtas` — ETAs recalculated without reordering; `OptimizeStops` reorders by mode; `Disabled` does nothing; errors never block the assignment. (F `TransportFileAppService.cs:2320-2345`)
10. Planner clicks "Optimize route" in `OptimizeRoundTrip` mode without an arrival relation — user-friendly error; in `ShortestRouteToEmptyVehicle` the route ends at the last stop. (F `VroomCalculationService.cs:704-715`)
11. Optimiser cannot assign all shipments within time slots — windows widened twice, then dropped; the planner is not told. (F `:95-117`)
12. Trip has stationing stops for a secondary vehicle — they keep their position; remaining stops optimised around them. (F `RouteOptimalization.md:45`, `VroomCalculationService.cs:119-125`)
13. Stop without relation/coordinates — excluded from optimisation and merged back by time. (F `RouteOptimalization.md:21,28`)

**ETA**
14. Driver departs (FMS status Sent/Received/Confirmed, `ActualTimeOfDeparture` set) — within 10 minutes the worker recalculates ETAs for all open stops from the live vehicle position. (F `EtaRecalculationBackgroundWorker.cs:64,89-114`)
15. No FMS position in the last 15 minutes — ETA from the last stop with actual departure/arrival, else from the departure address. (F `VroomCalculationService.cs:890-935`)
16. Stop gets an actual arrival — it leaves the ETA set; estimated values stay as history next to actual ones. (F `:871-879`, `TransportFileStop.cs:37-48`)
17. Trip arrives (`ActualTimeOfArrival` set) or departed more than 3 days ago — no further ETA work. (F `EtaRecalculationBackgroundWorker.cs:96-103`)
18. Tenant default is PTV — ETA matrix uses PTV with live traffic (`REALISTIC`); planning uses average speeds. (F `PtvCalculationService.cs` ~84-90)
19. Planner opens the route-forecast bar — shows per-departure-time ETAs with random 0–20 min delays. (F `TransportFileAppService.cs:~2857`) Do not reproduce.

**CO₂**
20. Order with ≥2 geocoded waypoints is created/edited — freight `CarbonFootprint` = CPI × amount × great-circle km (via `EstimateTransportationEmissions` when the UI asks). (F `TransportOrderCarbonFootprintManager.cs:169-199`, `CarbonFootprintingAppService.cs:61-76`)
21. Tenant changes its CPI or segment — background job rewrites the footprint of every freight of the tenant. (F `TenantSettingsAppService.cs:341-352`)
22. Trip is planned/saved — a CO₂ trip job is enqueued but does nothing (disabled); leg `EstimatedCarbonFootprint` stays null. (F `ITransportFileForwardJob.cs:57`)
23. Trip completed with manual `ActualCarbonFootprint` — stored on the file only; leg allocation would need the disabled job. (F `TransportFileRepositoryHandler.cs:229-236`)
24. Planner views a trip route — GLEC/ISO 14083 WTW CO₂e from the route function displayed, not stored. (F `TransportFileRouteCalculator.cs:182`; I: not persisted)
25. Tenant connects BigMile with a key — key probed against the calculate endpoint; invalid key clears the connection. (F `BigMileClient.cs:81-113`)
26. Tenant publishes a date range to BigMile — one OTM carbon-footprint trip per transport file; files lacking licence plate, postal-code stop addresses or load/unload actions are skipped with a logged reason; publication recorded. (F `BigMileClient.cs:136-215`)

**CMR**
27. Planner prints a CMR for one freight — PDF with N copies (1–5) in different colours, borders and QR per profile/tenant defaults. (F `ConsignmentNoteAppService.cs:359-425`)
28. Planner prints CMRs for a trip — all freights of the trip's legs; with `GroupByLeg` freights sharing the same parties are merged onto one note with combined goods rows and boxes 5/13/19. (F `ConsignmentNoteFileGenerator.cs:61-95`, `ConsignmentNoteAppService.cs:652-681`)
29. Planner picks a non-default profile and changes a box option — the profile is updated and the PDF uses the new options. (F `:452-470`)
30. Fixed texts selected in box 6/13 options — appended to the goods description. (F `:958-962`)
31. Download vs Print — same generation; Print triggers the browser print callback, Download saves the file. (F `print-cmr-dialog.component.ts:118-146`)
32. Send e-mail — opens the e-mail dialog with the generated PDF attached; requires an entity context. (F `:150-160`)
33. Driver signs on delivery via the distribution portal or questionnaire — signed CMR generated with stamps in boxes 23/24 and stored as a freight-document `BinaryObject`. (F `DistributionPortalAppService.cs:234-262`, `SignedConsignmentNoteGenerator.cs`)
34. Reprint later — regenerated from current data; no numbered, immutable CMR exists. (I from absence of an entity)
