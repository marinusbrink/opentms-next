# Design 60: Integrations — routing and carbon-footprint adapters

**PBI:** design 56 (umbrella), Integrations part
**Status:** approved (PO 2026-10-25: Q4, Q10 decided)
**Date:** 2026-10-25
**Depends on:** design 56; AD-12. Input: `docs/input/transport-assessment/routing-co2-cmr.md` §1–2.

## Domain impact

**Module:** Integrations, schema `integrations`, folder `Routing/` and `CarbonFootprint/`. Migration `IntegrationsRouting`.

### Published contract (Application.Contracts, `[RemoteService(IsEnabled = false)]`)

```csharp
public interface IRoutingService
{
    Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, RoutingOptions options, CancellationToken ct);
    Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken ct);   // consecutive legs
    Task<RoutingStatus> GetStatusAsync(CancellationToken ct);                                                        // provider, reachable, cache figures
}
public sealed record RouteResult(int DistanceM, int DurationS, string? EncodedPolyline, int? TollDistanceM, decimal? TollAmount, string? TollCurrency, decimal? Co2Kg, string Provider, DateTimeOffset At);
public sealed record VehicleSpec(int GrossWeightKg, EuroClass? EuroClass, FuelType? FuelType, double? FuelConsumptionLPer100Km, int? HeightCm, int? WidthCm, int? LengthCm);
public sealed record RoutingOptions(bool IncludeGeometry = false, bool IncludeToll = false, bool IncludeCo2 = false);

public interface ICarbonFootprintCalculator
{
    Task<Co2Estimate> EstimateTripAsync(IReadOnlyList<GeoPoint> stops, VehicleSpec? vehicle, IReadOnlyList<LegLoad> legs, CancellationToken ct);
    Co2Estimate EstimateLegFallback(GeoPoint from, GeoPoint to, double? weightKg, decimal cpiGramPerTonneKm);   // great-circle, synchronous
}
public sealed record Co2Estimate(decimal TotalKg, IReadOnlyList<decimal> PerLegKg, string Method, string FactorSet);
```

### Providers

| Provider | Role | Endpoint (configuration) | Notes |
|---|---|---|---|
| `OsrmRoutingProvider` | distance, duration, geometry, matrix | `Integrations:Routing:Osrm:BaseUrl` | `/route/v1/driving/{lon,lat;…}?overview=false`, `/table/v1/driving/…`; metres and seconds as returned (no rounding); no vehicle profile |
| `ValhallaFunctionProvider` | toll (distance and amount per country), CO₂e (GLEC v3 / ISO 14083), geometry with truck profile | `Integrations:Routing:RouteFunction:BaseUrl`, `:ApiKey?` | POST `{ origin, destination, waypoints[], vehicle }` → `route`, `toll`, `carbonFootprint` (the OpenTMS Cloud Function contract, appendix §1.4); 10 s timeout; on 442/no path retry without vehicle and drop toll/CO₂ |
| `GreatCircleFallback` | last resort for CO₂ estimate | none | CPI (g/tkm) × tonnes × great-circle km |

Selection: tenant setting `Integrations:Routing:Provider` (`Osrm` default) for distance/matrix; toll and CO₂ always
through the function when `RouteFunction:BaseUrl` is configured, else null/fallback. PTV: not in this run (the
interface allows it). No static factories, no reflection: providers are registered in the module and resolved by name.

### Route cache

`RouteCacheEntry` (`integrations."RouteCache"`): `TenantId`, `Key` (from/to rounded to 4 decimals + vehicle class +
provider + options), `DistanceM`, `DurationS`, `TollDistanceM?`, `TollAmount?`, `Co2Kg?`, `Polyline?` (only when asked),
`CreatedAt`, `ExpiresAt` (30 days, setting `Integrations:Routing:CacheDays`). Read before every provider call; written
after; a Hangfire daily job purges expired rows. Unique `(TenantId, Key)`.

### Carbon footprint

`CarbonFootprintCalculator`: tries the function's `carbonFootprint.totalKgCO2e` for the trip route with the vehicle spec
(Tier 2 when fuel consumption known, else Tier 1 GLEC defaults by vehicle class); allocates per leg by tonne-km
(`weightKg × legDistanceM`), equal shares when weights are unknown (COFRET as OpenTMS intended); when the function is
unavailable, falls back to great-circle × tenant CPI (`Integrations:CarbonFootprint:CpiGramPerTonneKm`, default 62 as
OpenTMS's default accounting CPI) and marks `Method = "GreatCircleCpi"`.

### Vehicle spec source

Planning builds `VehicleSpec` from Master Data (design 48): `GrossVehicleWeightKg`, `EuroClass`, `FuelType`,
dimensions; fuel consumption null until the RDW block exists. Default when no vehicle: 40 t, Euro VI, diesel (OpenTMS
toll default).

## API contract

`RoutingStatusAppService` `GET /api/integrations/routing/status` (permission `Integrations.Routing`) → provider, base
URLs' hosts only, reachable (a cached probe), cache rows, hit ratio of the last hour; shown in the host Maintenance →
Health later (this run: endpoint and health check `routing` registered with the existing health checks).

## Migration strategy

Expand only: `integrations."RouteCache"` with the unique key and an index on `ExpiresAt`.

## UI design

None of its own; the Maintenance health list shows the `routing` check.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| OSRM provider: request shape, parsing, metres/seconds exact, error → null | **High** | unit tests with a fake `HttpMessageHandler` and recorded responses |
| Function provider: contract, retry without vehicle, toll/CO₂ parsing | **High** | unit with recorded responses (from the appendix §1.4 shape) |
| Cache: hit, miss, expiry, per tenant | **Critical** (tenant) | PostgreSQL |
| CO₂ allocation by tonne-km and fallback | **High** | unit |
| Configuration: no literal hosts (architecture test greps the module for `opentms.nl`/`cloudfunctions.net`) | **High** | architecture test |
| Health check and status endpoint | **Medium** | SQLite |

## Flag & rollout plan

No feature (infrastructure); `Integrations:Routing:Osrm:BaseUrl` unset → every routing call returns null (legs stay
without distance, warning `RoutingNotConfigured` on the trip). Rollback: unset.

## Cost & SLO impact

OSRM self-hosted: no per-call cost; the function ≈ €150/month always-on (existing); cache hit ratio expected > 60 % on
recurring lanes; every call from a job, never a request.

## Assumptions

1. The OpenTMS OSRM instance and Cloud Function stay reachable; their URLs are entered in configuration per environment.
2. The Valhalla function's response contract is the one `TransportFileRouteCalculator.cs:581-640` consumes today.
3. PTV and VROOM are out of this run.

### Build notes (implementation of 2026-10-25)

What was built as designed, and where the implementation had to choose:

- **Configuration keys** (bound in `IntegrationsApplicationModule`, no literal host anywhere; the architecture test
  `IntegrationsConfigurationTests` greps the module for `opentms.nl` / `cloudfunctions.net`, allowing only the
  `@opentms.nl` workspace account names of design 55): `Integrations:Routing:Provider` (`Osrm`; or `RouteFunction`),
  `Integrations:Routing:Osrm:BaseUrl`, `Integrations:Routing:RouteFunction:BaseUrl`, `Integrations:Routing:RouteFunction:ApiKey`
  (secret store), `Integrations:Routing:RouteFunction:ApiKeyHeader` (`X-API-Key`), `Integrations:Routing:CacheDays` (30),
  `Integrations:Routing:TimeoutSeconds` (10), `Integrations:CarbonFootprint:CpiGramPerTonneKm` (62).
- **Route-function request shape** (assumption 2 made concrete; verify against the legacy mapper): coordinates as
  `{ lat, lon }` objects; `vehicle.weight` in kilograms; `vehicle.euroClass` as its number (Euro 0–6; EEV sent as 5,
  zero emission as 6); `height`/`width`/`length` in metres; `fuelType` as the lower-case enum name (`diesel`, `lng`, …);
  `fuelConsumption` in l/100 km, left out when unknown. The retry without the vehicle fires on HTTP 442 or on a 4xx whose
  body contains "no path".
- **Status endpoint route**: the service is `RoutingAppService.GetStatusAsync`, which the conventional controller exposes
  as `GET /api/integrations/routing/status` (a class named `RoutingStatusAppService` would have become
  `/routing-status/status`). The hit ratio of the last hour is not in the status (the contract has no field for it; the
  cache row count and the probe are). The probe is cached for 60 s in process.
- **Purge**: an ABP periodic worker (`RouteCachePurgeWorker`, every 24 h, first run one period after start) registered by
  the Integrations application module, not a Hangfire job; it visits the host and then every tenant of the tenant store
  under `ICurrentTenant.Change`, so the tenant filter scopes each delete and no filter is disabled (a tenant with its own
  database is visited there, a shared-tier tenant inside the host database). A failing tenant is logged and skipped. The
  host may later move the registration behind a flag like `Outbox:DispatchEnabled`.
- **Cache rows**: `CreatedAt`/`ExpiresAt` are UTC `DateTime`s (as Platform's technical tables), not `DateTimeOffset`, so
  the expiry comparison also runs on the SQLite test provider. An expired row that the purge has not removed yet is
  refreshed in place by the next answer (the unique key stays). Rows are written in a unit of work of their own: a
  calculation job's transaction never fails on a cache write; two jobs racing on one lane lose the second write quietly.
  A row is written only when every asked part is in hand: a function outage does not pin "no toll" for 30 days.
- **CO₂ fallback without a weight**: a leg without a weight counts as one tonne (CPI × kilometres), so the figure is
  never silently zero; with weights known, a weightless leg gets no share of the function's total by tonne-km, and equal
  shares apply only when no leg has a weight. The route function is asked with `VehicleSpec.Default` when the trip has no
  vehicle. `FactorSet` reads `GLEC v3 / ISO 14083` or `CPI <value> g/tkm`.
- **Error codes**: `IntegrationsErrorCodes` exists with empty `ConflictCodes`/`BadRequestCodes` (the routing service
  answers null, never throws); the host mapping is wired at integration.

## Security quickscan

Permission `Integrations.Routing` (read); outbound HTTPS only; keys from configuration/secret store; the status endpoint
never returns keys or full URLs; cache rows carry coordinates only (rounded), no relation ids; tenant-scoped.

- **2026-10-25, integration:** the route-function vehicle block was verified against the legacy `TransportFileRouteCalculator.cs:455-464` and `RouteVehicleSpecMapper.cs`: weight in tonnes, Euro class `EURO_0`…`EURO_VI`, fuel as the GLEC names `Diesel`/`Petrol`/`Electric`/`CNG` (others omitted), dimensions in metres, consumption in litres per kilometre. The adapter was corrected accordingly (open question 1 of the build notes is closed).
