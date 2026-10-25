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

## Security quickscan

Permission `Integrations.Routing` (read); outbound HTTPS only; keys from configuration/secret store; the status endpoint
never returns keys or full URLs; cache rows carry coordinates only (rounded), no relation ids; tenant-scoped.
