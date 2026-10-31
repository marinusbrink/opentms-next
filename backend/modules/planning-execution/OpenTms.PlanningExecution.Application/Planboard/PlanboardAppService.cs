using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.Planboard;

/// <summary>
/// Design 68 §6 and "API contract — reads" (PO decisions D1, D11, D19): the Scheduler reads and the Planboard's narrow
/// commands. Reads are SQL projections over Planning's own tables plus one batched Master Data read per kind (rows from
/// the published resource query of design 69, names from the reference lookup); nothing is calculated but the page's
/// figures. Every command is one unit of work on one trip, takes the trip's concurrency stamp (a stale one is refused
/// with 409), reuses <see cref="TripOperations"/> and <see cref="LegAssignmentService"/>, requests the trip's calculation
/// and notifies the trip (before and after) and the legs it touched on the record channel. Feature: the Planboard app.
/// </summary>
[RequiresFeature(AppFeatures.Planboard)]
public class PlanboardAppService : PlanningExecutionAppServiceBase, IPlanboardAppService
{
    private static readonly VehicleTypeKind[] MotorKinds = [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van];

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<TripVehicle> _tripVehicles;
    private readonly IRepository<TripDriver> _tripDrivers;
    private readonly IRepository<TransportTripType, Guid> _types;
    private readonly TripOperations _ops;
    private readonly MasterDataReferences _masterData;
    private readonly IMasterDataResourceQuery _resources;
    private readonly IRepository<TripStop, Guid> _stops;
    private readonly IRepository<TripStopLeg> _stopLegs;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly AvailabilityReader _availability;

    public PlanboardAppService(
        IRepository<TransportTrip, Guid> trips,
        IRepository<TripVehicle> tripVehicles,
        IRepository<TripDriver> tripDrivers,
        IRepository<TransportTripType, Guid> types,
        TripOperations ops,
        MasterDataReferences masterData,
        IMasterDataResourceQuery resources,
        IRepository<TripStop, Guid> stops,
        IRepository<TripStopLeg> stopLegs,
        IRepository<Leg, Guid> legs,
        AvailabilityReader availability)
    {
        _trips = trips;
        _tripVehicles = tripVehicles;
        _tripDrivers = tripDrivers;
        _types = types;
        _ops = ops;
        _masterData = masterData;
        _resources = resources;
        _stops = stops;
        _stopLegs = stopLegs;
        _legs = legs;
        _availability = availability;
    }

    /* Reads */

    /// <summary>
    /// A page of resource rows of the mode (≤ 200, from Master Data) and the bars of every trip that uses one of them in
    /// the window: neither cancelled nor archived, planned departure before the window's end and planned arrival (else
    /// departure) at or after its start. Statements: the rows (Master Data), the matching trip ids, the bars, their vehicles,
    /// their drivers, one name lookup per kind (vehicles, drivers, carriers), and the rows' availability (unavailabilities
    /// and working patterns, design 68 §2).
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<PlanboardSchedulerDto> GetSchedulerAsync(PlanboardSchedulerRequestDto input)
    {
        if (input.To <= input.From || input.To - input.From > TimeSpan.FromDays(PlanningExecutionConsts.MaxSchedulerWindowDays))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidSchedulerWindow).WithData("max", PlanningExecutionConsts.MaxSchedulerWindowDays);
        }

        var take = Math.Clamp(input.Take, 1, PlanningExecutionConsts.MaxPlanboardPageSize);
        var (rows, totalRows) = await RowsAsync(input, take);
        var rowIds = rows.Select(r => r.Id).ToList();

        var fromUtc = input.From.UtcDateTime;
        var toUtc = input.To.UtcDateTime;
        var bars = new List<PlanboardBarDto>();
        var truncated = false;
        if (rowIds.Count > 0)
        {
            var trips = Live(await _trips.GetQueryableAsync())
                .Where(t => t.PlannedDepartureUtc < toUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) >= fromUtc);
            var matches = input.Mode == PlanboardResourceMode.Driver
                ? from d in await _tripDrivers.GetQueryableAsync()
                  where rowIds.Contains(d.DriverId)
                  join t in trips on d.TripId equals t.Id
                  select new { t.Id, t.PlannedDepartureUtc }
                : from v in await _tripVehicles.GetQueryableAsync()
                  where rowIds.Contains(v.VehicleId)
                  join t in trips on v.TripId equals t.Id
                  select new { t.Id, t.PlannedDepartureUtc };
            var tripIds = await AsyncExecuter.ToListAsync(matches.Distinct()
                .OrderBy(m => m.PlannedDepartureUtc).ThenBy(m => m.Id)
                .Select(m => m.Id)
                .Take(PlanningExecutionConsts.MaxSchedulerBars + 1));
            truncated = tripIds.Count > PlanningExecutionConsts.MaxSchedulerBars;
            bars = await BarsAsync(tripIds.Take(PlanningExecutionConsts.MaxSchedulerBars).ToList());
        }

        return new PlanboardSchedulerDto
        {
            Rows = rows,
            Bars = bars,
            Blocks = await BlocksAsync(input.Mode == PlanboardResourceMode.Driver ? ResourceKind.Driver : ResourceKind.Vehicle, rowIds, input.From, input.To),
            Names = await NamesAsync(bars),
            Kpis = Kpis(rows, bars, input.Mode, input.From, input.To),
            TotalRows = totalRows,
            BarsTruncated = truncated
        };
    }

    /// <summary>The bars of given trips (live-update refetch, ≤ 200): a cancelled, archived or deleted trip is absent.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<PlanboardBarsDto> GetSchedulerBarsAsync(PlanboardBarsRequestDto input)
    {
        if (input.Ids.Count > PlanningExecutionConsts.MaxIdsPerQuery)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyIds).WithData("max", PlanningExecutionConsts.MaxIdsPerQuery);
        }

        var bars = await BarsAsync(input.Ids.Distinct().ToList());
        return new PlanboardBarsDto { Bars = bars, Names = await NamesAsync(bars) };
    }

    /* Map reads (design 68 "API contract — reads", PO decisions D3, D16). */

    /// <summary>
    /// The trip on the map: stops with their stored times, the window of their legs and the lateness (estimated arrival
    /// minus window end, a subtraction of stored values), the stored route segments and figures. Statements: the trip, its
    /// stops, the stops' legs with their windows, one address lookup for a departure or arrival that is not a stop.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    [RequiresFeature(AppFeatures.PlanboardApp.Map)]
    public virtual async Task<PlanboardTripMapDto> GetMapAsync(Guid id)
    {
        var trip = await AsyncExecuter.FirstOrDefaultAsync((await _trips.GetQueryableAsync())
                       .Where(t => t.Id == id)
                       .Select(t => new
                       {
                           t.Id,
                           t.Number,
                           t.Status,
                           t.PlannedDeparture,
                           t.PlannedArrival,
                           t.FirstLastStopRule,
                           t.DepartureAddressId,
                           t.ArrivalAddressId,
                           t.RouteSegments,
                           t.TollAmount,
                           t.TollCurrencyCode,
                           t.TollDistanceM,
                           t.PlannedDistanceM,
                           t.PlannedDurationS,
                           t.Co2Kg,
                           t.RouteProvider,
                           t.RoutedAt,
                           t.CalculatedAt,
                           t.ConcurrencyStamp
                       }))
                   ?? throw new EntityNotFoundException(typeof(TransportTrip), id);

        var stops = await AsyncExecuter.ToListAsync((await _stops.GetQueryableAsync())
            .Where(s => s.TripId == id)
            .OrderBy(s => s.Position)
            .Select(s => new
            {
                s.Id,
                s.Position,
                s.Name,
                s.City,
                s.Latitude,
                s.Longitude,
                s.WaypointTypeId,
                s.IsManual,
                s.PlannedArrival,
                s.PlannedDeparture,
                s.EstimatedArrival,
                s.EstimatedDeparture,
                s.ActualArrival,
                s.ActualDeparture
            }));
        var links = (await AsyncExecuter.ToListAsync(
                from link in await _stopLegs.GetQueryableAsync()
                join stop in await _stops.GetQueryableAsync() on link.TripStopId equals stop.Id
                join leg in await _legs.GetQueryableAsync() on link.LegId equals leg.Id
                where stop.TripId == id
                select new
                {
                    link.TripStopId,
                    link.LegId,
                    link.Role,
                    leg.OriginWindowFrom,
                    leg.OriginWindowTill,
                    leg.DestinationWindowFrom,
                    leg.DestinationWindowTill
                }))
            .ToLookup(l => l.TripStopId);

        var departureId = trip.FirstLastStopRule.HasFlag(FirstLastStopRule.DepartureIsFirstStop) ? null : trip.DepartureAddressId;
        var arrivalId = trip.FirstLastStopRule.HasFlag(FirstLastStopRule.ArrivalIsLastStop) ? null : trip.ArrivalAddressId;
        var addresses = departureId == null && arrivalId == null
            ? new Dictionary<Guid, MasterDataAddressReferenceDto>()
            : await _masterData.AddressesAsync([departureId, arrivalId]);

        return new PlanboardTripMapDto
        {
            Id = trip.Id,
            Number = trip.Number,
            Status = trip.Status,
            PlannedDeparture = trip.PlannedDeparture,
            PlannedArrival = trip.PlannedArrival,
            DeparturePoint = Point(addresses, departureId),
            ArrivalPoint = Point(addresses, arrivalId),
            Stops = stops.Select(s =>
            {
                var windows = links[s.Id].Select(l => l.Role == StopLegRole.Origin
                    ? (From: l.OriginWindowFrom, Till: l.OriginWindowTill)
                    : (From: l.DestinationWindowFrom, Till: l.DestinationWindowTill)).ToList();
                var windowFrom = windows.Where(w => w.From != null).Select(w => w.From!.Value).DefaultIfEmpty().Max();
                var windowTill = windows.Where(w => w.Till != null).Select(w => w.Till!.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
                DateTimeOffset? from = windows.Any(w => w.From != null) ? windowFrom : null;
                DateTimeOffset? till = windows.Any(w => w.Till != null) ? windowTill : null;
                return new PlanboardMapStopDto
                {
                    Id = s.Id,
                    Position = s.Position,
                    Name = s.Name,
                    City = s.City,
                    Latitude = s.Latitude,
                    Longitude = s.Longitude,
                    WaypointTypeId = s.WaypointTypeId,
                    IsManual = s.IsManual,
                    PlannedArrival = s.PlannedArrival,
                    PlannedDeparture = s.PlannedDeparture,
                    EstimatedArrival = s.EstimatedArrival,
                    EstimatedDeparture = s.EstimatedDeparture,
                    ActualArrival = s.ActualArrival,
                    ActualDeparture = s.ActualDeparture,
                    WindowFrom = from,
                    WindowTill = till,
                    LatenessMinutes = s.EstimatedArrival == null || till == null
                        ? null
                        : Math.Max(0, (int)Math.Ceiling((s.EstimatedArrival.Value - till.Value).TotalMinutes)),
                    LegIds = links[s.Id].Select(l => l.LegId).Distinct().ToList()
                };
            }).ToList(),
            Segments = (trip.RouteSegments ?? []).Select(g => new PlanboardRouteSegmentDto
            {
                FromStopId = g.FromStopId,
                ToStopId = g.ToStopId,
                EncodedPolyline = g.EncodedPolyline,
                DistanceM = g.DistanceM,
                DurationS = g.DurationS,
                TollDistanceM = g.TollDistanceM,
                TollAmount = g.TollAmount
            }).ToList(),
            TollAmount = trip.TollAmount,
            TollCurrencyCode = trip.TollCurrencyCode,
            TollDistanceM = trip.TollDistanceM,
            PlannedDistanceM = trip.PlannedDistanceM,
            PlannedDurationS = trip.PlannedDurationS,
            Co2Kg = trip.Co2Kg,
            RouteProvider = trip.RouteProvider,
            RoutedAt = trip.RoutedAt,
            CalculatedAt = trip.CalculatedAt,
            ConcurrencyStamp = trip.ConcurrencyStamp
        };
    }

    /// <summary>The "all trips" layer (D16): live trips overlapping the window (≤ 14 days), at most 500, earliest first. One statement.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    [RequiresFeature(AppFeatures.PlanboardApp.Map)]
    public virtual async Task<PlanboardMapTripsDto> GetMapTripsAsync(PlanboardMapTripsRequestDto input)
    {
        EnsureWindow(input.From, input.To);
        var fromUtc = input.From.UtcDateTime;
        var toUtc = input.To.UtcDateTime;
        var query = Live(await _trips.GetQueryableAsync())
            .Where(t => t.PlannedDepartureUtc < toUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) >= fromUtc);
        var units = input.OrganizationUnitIds.Distinct().ToList();
        if (units.Count > 0)
        {
            query = query.Where(t => t.OrganizationUnitId != null && units.Contains(t.OrganizationUnitId.Value));
        }

        var rows = await AsyncExecuter.ToListAsync(query
            .OrderBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Id)
            .Take(PlanningExecutionConsts.MaxMapTrips + 1)
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Status,
                t.PlannedDeparture,
                t.PlannedArrival,
                t.Warnings,
                StopCount = t.Stops.Count,
                FirstLatitude = t.Stops.OrderBy(s => s.Position).Select(s => s.Latitude).FirstOrDefault(),
                FirstLongitude = t.Stops.OrderBy(s => s.Position).Select(s => s.Longitude).FirstOrDefault(),
                LastLatitude = t.Stops.OrderByDescending(s => s.Position).Select(s => s.Latitude).FirstOrDefault(),
                LastLongitude = t.Stops.OrderByDescending(s => s.Position).Select(s => s.Longitude).FirstOrDefault()
            }));

        return new PlanboardMapTripsDto
        {
            Truncated = rows.Count > PlanningExecutionConsts.MaxMapTrips,
            Trips = rows.Take(PlanningExecutionConsts.MaxMapTrips).Select(t => new PlanboardMapTripDto
            {
                Id = t.Id,
                Number = t.Number,
                Status = t.Status,
                PlannedDeparture = t.PlannedDeparture,
                PlannedArrival = t.PlannedArrival,
                FirstLatitude = t.FirstLatitude,
                FirstLongitude = t.FirstLongitude,
                LastLatitude = t.LastLatitude,
                LastLongitude = t.LastLongitude,
                StopCount = t.StopCount,
                WarningCount = t.Warnings.Count
            }).ToList()
        };
    }

    /// <summary>
    /// Unplanned legs whose origin or destination lies within the radius of a stop of the trip: a bounding box around the
    /// stops in SQL (on the legs' snapshot coordinates, origin date in range), then the great-circle distance in memory;
    /// at most 200, nearest first. Statements: the stops, the candidates, one customer name lookup.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    [RequiresFeature(AppFeatures.PlanboardApp.Map)]
    public virtual async Task<PlanboardUnplannedNearDto> GetUnplannedNearAsync(PlanboardUnplannedNearRequestDto input)
    {
        if (input.RadiusKm is < 1 or > PlanningExecutionConsts.MaxUnplannedNearRadiusKm || input.To < input.From
            || input.To.DayNumber - input.From.DayNumber > PlanningExecutionConsts.MaxUnplannedNearDays)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidNearbySearch)
                .WithData("max", PlanningExecutionConsts.MaxUnplannedNearRadiusKm).WithData("days", PlanningExecutionConsts.MaxUnplannedNearDays);
        }

        var points = await AsyncExecuter.ToListAsync((await _stops.GetQueryableAsync())
            .Where(s => s.TripId == input.TripId && s.Latitude != null && s.Longitude != null)
            .Select(s => new { Latitude = s.Latitude!.Value, Longitude = s.Longitude!.Value }));
        if (points.Count == 0)
        {
            if (!await _trips.AnyAsync(t => t.Id == input.TripId))
            {
                throw new EntityNotFoundException(typeof(TransportTrip), input.TripId);
            }

            return new PlanboardUnplannedNearDto();
        }

        var radius = input.RadiusKm;
        var latDelta = radius / KmPerDegree;
        var minLat = points.Min(p => p.Latitude) - latDelta;
        var maxLat = points.Max(p => p.Latitude) + latDelta;
        var widestLat = Math.Min(89, Math.Max(Math.Abs(minLat), Math.Abs(maxLat)));
        var lonDelta = radius / (KmPerDegree * Math.Cos(widestLat * Math.PI / 180));
        var minLon = points.Min(p => p.Longitude) - lonDelta;
        var maxLon = points.Max(p => p.Longitude) + lonDelta;

        var candidates = await AsyncExecuter.ToListAsync((await _legs.GetQueryableAsync())
            .Where(l => l.TripId == null && l.OriginPlannedDate >= input.From && l.OriginPlannedDate <= input.To)
            .Where(l => (l.OriginLatitude >= minLat && l.OriginLatitude <= maxLat && l.OriginLongitude >= minLon && l.OriginLongitude <= maxLon)
                        || (l.DestinationLatitude >= minLat && l.DestinationLatitude <= maxLat && l.DestinationLongitude >= minLon && l.DestinationLongitude <= maxLon))
            .OrderBy(l => l.OriginPlannedDate).ThenBy(l => l.Id)
            .Take(PlanningExecutionConsts.MaxUnplannedNearCandidates + 1)
            .Select(l => new PlanboardNearLegDto
            {
                Id = l.Id,
                OrderId = l.OrderId,
                CustomerRelationId = l.CustomerRelationId,
                OriginName = l.OriginName,
                OriginCity = l.OriginCity,
                OriginLatitude = l.OriginLatitude,
                OriginLongitude = l.OriginLongitude,
                OriginPlannedDate = l.OriginPlannedDate,
                OriginWindowFrom = l.OriginWindowFrom,
                OriginWindowTill = l.OriginWindowTill,
                DestinationName = l.DestinationName,
                DestinationCity = l.DestinationCity,
                DestinationLatitude = l.DestinationLatitude,
                DestinationLongitude = l.DestinationLongitude,
                DestinationPlannedDate = l.DestinationPlannedDate,
                DestinationWindowFrom = l.DestinationWindowFrom,
                DestinationWindowTill = l.DestinationWindowTill,
                Quantity = l.Quantity,
                WeightKg = l.WeightKg,
                LoadingMeters = l.LoadingMeters
            }));

        double Nearest(double? latitude, double? longitude)
            => latitude == null || longitude == null
                ? double.MaxValue
                : points.Min(p => GreatCircleKm(p.Latitude, p.Longitude, latitude.Value, longitude.Value));

        var near = candidates
            .Select(l => (Leg: l, Km: Math.Min(Nearest(l.OriginLatitude, l.OriginLongitude), Nearest(l.DestinationLatitude, l.DestinationLongitude))))
            .Where(x => x.Km <= radius)
            .OrderBy(x => x.Km).ThenBy(x => x.Leg.OriginPlannedDate).ThenBy(x => x.Leg.Id)
            .ToList();
        var legs = near.Take(PlanningExecutionConsts.MaxUnplannedNearLegs).Select(x =>
        {
            x.Leg.DistanceKm = Math.Round(x.Km, 1, MidpointRounding.AwayFromZero);
            return x.Leg;
        }).ToList();

        return new PlanboardUnplannedNearDto
        {
            Legs = legs,
            Names = await _masterData.RelationNamesAsync(legs.Select(l => (Guid?)l.CustomerRelationId)),
            Truncated = near.Count > PlanningExecutionConsts.MaxUnplannedNearLegs || candidates.Count > PlanningExecutionConsts.MaxUnplannedNearCandidates
        };
    }

    /* Resources pane reads (design 68 "API contract — reads", design 69). */

    /// <summary>
    /// A page of drivers (Master Data, ≤ 200) with their blocks and the ids of their live trips in the window (≤ 14 days).
    /// Statements: the drivers (Master Data), their trips, their availability (two).
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    [RequiresFeature(AppFeatures.PlanboardApp.Resources)]
    public virtual async Task<PlanboardResourceDriversDto> GetResourceDriversAsync(PlanboardResourceDriversRequestDto input)
    {
        EnsureWindow(input.From, input.To);
        var page = await _resources.GetDriversAsync(new MasterDataDriverResourceQuery
        {
            Search = input.Search,
            Skip = input.Skip,
            Take = Math.Clamp(input.Take, 1, PlanningExecutionConsts.MaxPlanboardPageSize)
        });
        var ids = page.Items.Select(d => d.Id).ToList();
        var trips = await TripIdsByResourceAsync(PlanboardResourceMode.Driver, ids, input.From, input.To);
        var blocks = (await BlocksAsync(ResourceKind.Driver, ids, input.From, input.To)).ToLookup(b => b.ResourceId);
        return new PlanboardResourceDriversDto
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(d => new PlanboardResourceDriverDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                TripIds = trips[d.Id].ToList(),
                Blocks = blocks[d.Id].ToList()
            }).ToList()
        };
    }

    /// <summary>The Vehicles tab of the Resources pane: vehicles of every kind, otherwise as <see cref="GetResourceDriversAsync"/>.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    [RequiresFeature(AppFeatures.PlanboardApp.Resources)]
    public virtual async Task<PlanboardResourceVehiclesDto> GetResourceVehiclesAsync(PlanboardResourceVehiclesRequestDto input)
    {
        EnsureWindow(input.From, input.To);
        var page = await _resources.GetVehiclesAsync(new MasterDataVehicleResourceQuery
        {
            Kinds = Enum.GetValues<VehicleTypeKind>().ToList(),
            OrganizationUnitIds = input.OrganizationUnitIds.Distinct().ToList(),
            Search = input.Search,
            Skip = input.Skip,
            Take = Math.Clamp(input.Take, 1, PlanningExecutionConsts.MaxPlanboardPageSize)
        });
        var ids = page.Items.Select(v => v.Id).ToList();
        var trips = await TripIdsByResourceAsync(PlanboardResourceMode.Tractor, ids, input.From, input.To);
        var blocks = (await BlocksAsync(ResourceKind.Vehicle, ids, input.From, input.To)).ToLookup(b => b.ResourceId);
        return new PlanboardResourceVehiclesDto
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(v => new PlanboardResourceVehicleDto
            {
                Id = v.Id,
                Name = v.Name,
                LicencePlate = v.LicencePlate,
                Kind = v.Kind,
                TypeName = v.VehicleTypeName,
                TripIds = trips[v.Id].ToList(),
                Blocks = blocks[v.Id].ToList()
            }).ToList()
        };
    }

    private const double KmPerDegree = 111.32;

    private static double GreatCircleKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static PlanboardMapPointDto? Point(Dictionary<Guid, MasterDataAddressReferenceDto> addresses, Guid? addressId)
        => addressId != null && addresses.TryGetValue(addressId.Value, out var a) && a.Latitude != null && a.Longitude != null
            ? new PlanboardMapPointDto { Latitude = a.Latitude.Value, Longitude = a.Longitude.Value }
            : null;

    private static void EnsureWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from || to - from > TimeSpan.FromDays(PlanningExecutionConsts.MaxSchedulerWindowDays))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidSchedulerWindow).WithData("max", PlanningExecutionConsts.MaxSchedulerWindowDays);
        }
    }

    /// <summary>The live trips of each resource overlapping the window, by planned departure: one statement, at most 2,000 pairs.</summary>
    private async Task<ILookup<Guid, Guid>> TripIdsByResourceAsync(PlanboardResourceMode mode, List<Guid> resourceIds, DateTimeOffset from, DateTimeOffset to)
    {
        if (resourceIds.Count == 0)
        {
            return Array.Empty<(Guid, Guid)>().ToLookup(p => p.Item1, p => p.Item2);
        }

        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var trips = Live(await _trips.GetQueryableAsync())
            .Where(t => t.PlannedDepartureUtc < toUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) >= fromUtc);
        var pairs = mode == PlanboardResourceMode.Driver
            ? from d in await _tripDrivers.GetQueryableAsync()
              where resourceIds.Contains(d.DriverId)
              join t in trips on d.TripId equals t.Id
              select new { ResourceId = d.DriverId, TripId = t.Id, t.PlannedDepartureUtc }
            : from v in await _tripVehicles.GetQueryableAsync()
              where resourceIds.Contains(v.VehicleId)
              join t in trips on v.TripId equals t.Id
              select new { ResourceId = v.VehicleId, TripId = t.Id, t.PlannedDepartureUtc };
        var rows = await AsyncExecuter.ToListAsync(pairs.Distinct()
            .OrderBy(p => p.PlannedDepartureUtc).ThenBy(p => p.TripId)
            .Take(PlanningExecutionConsts.MaxSchedulerBars));
        return rows.ToLookup(r => r.ResourceId, r => r.TripId);
    }

    /// <summary>
    /// The blocks of the resources in the window. The reason of an unavailability (it may say "sick") is shown only to users
    /// who may read resource availability; the others see the block without its reason.
    /// </summary>
    private async Task<List<PlanboardBlockDto>> BlocksAsync(ResourceKind kind, List<Guid> resourceIds, DateTimeOffset from, DateTimeOffset to)
    {
        if (resourceIds.Count == 0)
        {
            return [];
        }

        var blocks = await _availability.BlocksAsync(kind, resourceIds, from, to);
        if (blocks.Any(b => b.Reason != null) && !await AuthorizationService.IsGrantedAsync(PlanningExecutionPermissions.ResourceAvailability.Default))
        {
            foreach (var block in blocks)
            {
                block.Reason = null;
            }
        }

        return blocks;
    }

    private async Task<(List<PlanboardResourceRowDto> Rows, long Total)> RowsAsync(PlanboardSchedulerRequestDto input, int take)
    {
        if (input.Mode == PlanboardResourceMode.Driver)
        {
            var drivers = await _resources.GetDriversAsync(new MasterDataDriverResourceQuery { Search = input.Search, Skip = input.Skip, Take = take });
            return (drivers.Items.Select(d => new PlanboardResourceRowDto { Id = d.Id, Name = d.Name, Code = d.Code }).ToList(), drivers.TotalCount);
        }

        var vehicles = await _resources.GetVehiclesAsync(new MasterDataVehicleResourceQuery
        {
            Kinds = input.Mode == PlanboardResourceMode.Trailer ? [VehicleTypeKind.Trailer] : MotorKinds.ToList(),
            OrganizationUnitIds = input.OrganizationUnitIds.Distinct().ToList(),
            Search = input.Search,
            Skip = input.Skip,
            Take = take
        });
        return (vehicles.Items.Select(v => new PlanboardResourceRowDto
        {
            Id = v.Id,
            Name = v.Name,
            Code = v.LicencePlate,
            Kind = v.Kind,
            TypeName = v.VehicleTypeName,
            DefaultDriverIds = v.DefaultDriverIds.ToList()
        }).ToList(), vehicles.TotalCount);
    }

    /// <summary>The trips a Scheduler shows: neither cancelled nor archived.</summary>
    private static IQueryable<TransportTrip> Live(IQueryable<TransportTrip> trips)
        => trips.Where(t => !t.IsArchived && t.Status != TransportTripStatus.Cancelled);

    /// <summary>Three statements: the trips' scalar columns with their stop figures, their vehicle rows, their driver rows.</summary>
    private async Task<List<PlanboardBarDto>> BarsAsync(List<Guid> tripIds)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var sources = await AsyncExecuter.ToListAsync(Live(await _trips.GetQueryableAsync())
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Name,
                t.Status,
                t.Execution,
                t.PlannedDeparture,
                t.PlannedArrival,
                t.PlannedDepartureUtc,
                t.ActualDeparture,
                t.ActualArrival,
                t.CarrierRelationId,
                t.OrganizationUnitId,
                t.LoadFactor,
                t.Warnings,
                t.ConcurrencyStamp,
                StopCount = t.Stops.Count,
                FirstStopCity = t.Stops.OrderBy(s => s.Position).Select(s => s.City).FirstOrDefault(),
                LastStopCity = t.Stops.OrderByDescending(s => s.Position).Select(s => s.City).FirstOrDefault()
            }));
        var vehicles = (await AsyncExecuter.ToListAsync((await _tripVehicles.GetQueryableAsync())
                .Where(v => tripIds.Contains(v.TripId))
                .Select(v => new { v.TripId, v.Position, v.VehicleId })))
            .ToLookup(v => v.TripId);
        var drivers = (await AsyncExecuter.ToListAsync((await _tripDrivers.GetQueryableAsync())
                .Where(d => tripIds.Contains(d.TripId))
                .Select(d => new { d.TripId, d.Position, d.DriverId })))
            .ToLookup(d => d.TripId);

        return sources.OrderBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Number, StringComparer.Ordinal).Select(t => new PlanboardBarDto
        {
            Id = t.Id,
            Number = t.Number,
            Name = t.Name,
            Status = t.Status,
            Execution = t.Execution,
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            VehicleIds = vehicles[t.Id].OrderBy(v => v.Position).Select(v => v.VehicleId).ToList(),
            DriverIds = drivers[t.Id].OrderBy(d => d.Position).Select(d => d.DriverId).ToList(),
            CarrierRelationId = t.CarrierRelationId,
            OrganizationUnitId = t.OrganizationUnitId,
            FirstStopCity = t.FirstStopCity,
            LastStopCity = t.LastStopCity,
            StopCount = t.StopCount,
            LoadFactor = t.LoadFactor,
            WarningCodes = t.Warnings.Select(w => w.Code).Distinct().ToList(),
            ConcurrencyStamp = t.ConcurrencyStamp
        }).ToList();
    }

    /// <summary>Every vehicle, driver and carrier on the bars named: one batched lookup per kind.</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(List<PlanboardBarDto> bars)
    {
        var names = new Dictionary<Guid, string>();
        if (bars.Count == 0)
        {
            return names;
        }

        foreach (var (id, name) in await _masterData.VehicleNamesAsync(bars.SelectMany(b => b.VehicleIds)))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await _masterData.DriverNamesAsync(bars.SelectMany(b => b.DriverIds)))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await _masterData.RelationNamesAsync(bars.Select(b => b.CarrierRelationId)))
        {
            names[id] = name;
        }

        return names;
    }

    /// <summary>The page's figures: utilisation = the rows' planned trip hours inside the window / (rows × window hours).</summary>
    private static PlanboardSchedulerKpisDto Kpis(List<PlanboardResourceRowDto> rows, List<PlanboardBarDto> bars, PlanboardResourceMode mode, DateTimeOffset from, DateTimeOffset to)
    {
        var plannedHours = 0d;
        var rowsWithoutTrip = 0;
        foreach (var row in rows)
        {
            var rowBars = bars.Where(b => mode == PlanboardResourceMode.Driver ? b.DriverIds.Contains(row.Id) : b.VehicleIds.Contains(row.Id)).ToList();
            if (rowBars.Count == 0)
            {
                rowsWithoutTrip++;
            }

            foreach (var bar in rowBars)
            {
                var start = bar.PlannedDeparture > from ? bar.PlannedDeparture : from;
                var arrival = bar.PlannedArrival ?? bar.PlannedDeparture;
                var end = arrival < to ? arrival : to;
                if (end > start)
                {
                    plannedHours += (end - start).TotalHours;
                }
            }
        }

        var capacity = rows.Count * (to - from).TotalHours;
        return new PlanboardSchedulerKpisDto
        {
            Rows = rows.Count,
            Trips = bars.Count,
            InProgress = bars.Count(b => b.Status == TransportTripStatus.InProgress),
            RowsWithoutTrip = rowsWithoutTrip,
            UtilisationPercent = capacity <= 0 ? 0 : Math.Round((decimal)(plannedHours / capacity * 100), 1, MidpointRounding.AwayFromZero)
        };
    }

    /* Commands */

    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> RescheduleAsync(Guid id, PlanboardRescheduleDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var tripDate = DateOnly.FromDateTime(input.PlannedDeparture.Date);
        if (tripDate != trip.TripDate)
        {
            /* As the editor's date change: the trip date may not leave the numbering period of its number. */
            var type = await _types.GetAsync(trip.TransportTripTypeId);
            if (type.Numbering.Period(tripDate) != type.Numbering.Period(trip.TripDate))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.TripDateLocked).WithData("number", trip.Number).WithData("value", tripDate.ToString("yyyy-MM-dd"));
            }
        }

        trip.Reschedule(input.PlannedDeparture, input.PlannedArrival);
        await _ops.ChangedAsync(trip, TripChangeKind.Header, recalculate: true);
        return await SavedAsync(trip);
    }

    /// <summary>
    /// A bar dropped on another row, or a resource dropped on a bar. D11: a tractor fills only the empty slots of the
    /// trip — drivers from its default drivers, the carrier from its owner (not the own company), trailers from its
    /// standing combination (Master Data has none today: skipped); it never overwrites what is set. A trip that carried a
    /// combination keeps its vehicles as loose vehicles once one of them is replaced.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> ChangeResourceAsync(Guid id, PlanboardChangeResourceDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        if (input.Mode == PlanboardResourceMode.Driver)
        {
            await _ops.ValidateDriversAsync([input.ToResourceId]);
            var drivers = trip.DriverIdsInOrder.ToList();
            Replace(drivers, input.FromResourceId, input.ToResourceId, append: true);
            trip.SetDrivers(drivers.Distinct().ToList());
            await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: true);
            return await SavedAsync(trip);
        }

        var current = trip.VehicleIdsInOrder.ToList();
        var vehicles = await _masterData.VehiclesAsync(current.Append(input.ToResourceId));
        EnsureKind(vehicles, input.ToResourceId, input.Mode, "toResourceId");
        if (input.FromResourceId != null)
        {
            /* The replaced vehicle is of the pane's mode too: a tractor row's drop never replaces a trailer. */
            EnsureKind(vehicles, input.FromResourceId.Value, input.Mode, "fromResourceId");
            Replace(current, input.FromResourceId, input.ToResourceId, append: false);
        }
        else
        {
            Place(current, input.Mode, input.ToResourceId, vehicles);
        }

        trip.SetVehicles(null, current.Distinct().ToList());
        if (input.Mode == PlanboardResourceMode.Tractor)
        {
            var defaults = await TractorDefaultsAsync(input.ToResourceId, trip.Drivers.Count == 0, trip.CarrierRelationId == null);
            if (defaults.DriverIds.Count > 0)
            {
                trip.SetDrivers(defaults.DriverIds);
                await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: false);
            }

            if (defaults.CarrierRelationId != null)
            {
                trip.SetCarrier(defaults.CarrierRelationId, null);
                await _ops.ChangedAsync(trip, TripChangeKind.Carrier, recalculate: false);
            }
        }

        await _ops.VehiclesChangedAsync(trip);
        return await SavedAsync(trip);
    }

    /// <summary>S:Q-15: a driver from the Resources pane goes in the next position (at most three); one already on the trip is a no-op.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> AddDriverAsync(Guid id, PlanboardAddDriverDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        if (trip.Drivers.Any(d => d.DriverId == input.DriverId))
        {
            return Change(trip);
        }

        await _ops.ValidateDriversAsync([input.DriverId]);
        trip.SetDrivers(trip.DriverIdsInOrder.Append(input.DriverId).ToList());
        await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: true);
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> SetFieldAsync(Guid id, PlanboardUpdateFieldDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        switch (input.Field)
        {
            case PlanboardTripField.Name:
                trip.SetName(input.Value);
                break;
            case PlanboardTripField.Remarks:
                trip.SetRemarks(input.Value);
                break;
            case PlanboardTripField.DriverInstructions:
                trip.SetDriverInstructions(input.Value);
                break;
            default:
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "field").WithData("value", (int)input.Field);
        }

        await _ops.ChangedAsync(trip, TripChangeKind.Header, recalculate: true);
        return await SavedAsync(trip);
    }

    /// <summary>
    /// A new trip from an empty cell or a leg drop on a row: the type's defaults fill the empty groups (design 61), then the
    /// row's resource takes its mode's slot (a tractor also fills the empty driver and carrier slots, D11); the dropped legs
    /// are assigned in the same unit of work whatever their window.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Create)]
    public virtual async Task<PlanboardChangeDto> CreateTripAsync(PlanboardCreateTripDto input)
    {
        if (input.LegIds.Count > 0)
        {
            await AuthorizationService.CheckAsync(PlanningExecutionPermissions.TransportTrips.AssignLegs);
        }

        if (input.ResourceId != null && input.Mode == null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceKindMismatch).WithData("field", "mode");
        }

        var typeId = input.TransportTripTypeId ?? await DefaultTripTypeIdAsync();
        var legs = input.LegIds.Count > 0 ? await _ops.LegsAsync(input.LegIds) : [];
        var trip = await _ops.CreateAsync(
            new TransportTripCreateDto { TransportTripTypeId = typeId, PlannedDeparture = input.PlannedDeparture },
            null, null, null,
            input.ResourceId == null ? null : filled => PlaceInNewTripAsync(filled, input.Mode!.Value, input.ResourceId.Value));
        if (legs.Count > 0)
        {
            await _ops.AssignAsync(trip, legs, allowOutsideWindow: true, afterStopId: null, moving: false);
        }

        await _ops.RequestRecalculationAsync(trip.Id, "Created");
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<PlanboardChangeDto> AssignLegsAsync(Guid id, PlanboardLegsDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.AssignAsync(trip, legs, input.AllowOutsideWindow, afterStopId: null, moving: false);
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<PlanboardChangeDto> UnassignLegsAsync(Guid id, PlanboardLegsDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.UnassignAsync(trip, legs);
        return await SavedAsync(trip);
    }

    /// <summary>P:Q17: a running trip completed with both actual times; stops keep their recorded actuals.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Complete)]
    public virtual async Task<PlanboardChangeDto> CompleteAsync(Guid id, PlanboardCompleteDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        trip.Complete(input.ActualDeparture, input.ActualArrival);
        await _ops.StatusChangedAsync(trip, (await _ops.LegsOnTripAsync(id)).Select(l => l.OrderId));
        await _ops.RequestRecalculationAsync(trip.Id, "Completed");
        return await SavedAsync(trip);
    }

    /// <summary>
    /// The trip with its children, refused with 409 (ABP's <see cref="AbpDbConcurrencyException"/>) when the caller's
    /// stamp is not the stored one; a concurrent save between this read and the commit is refused by ABP's stamp check on
    /// save. The trip's state before the command goes to the record channel (merged with the state after).
    /// </summary>
    private async Task<TransportTrip> LoadAsync(Guid id, string concurrencyStamp)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        if (!string.Equals(trip.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
        {
            throw new AbpDbConcurrencyException($"Transport trip {id} was changed after it was read.");
        }

        await _ops.Notices.TripAsync(trip);
        return trip;
    }

    private async Task<PlanboardChangeDto> SavedAsync(TransportTrip trip)
    {
        await CurrentUnitOfWork!.SaveChangesAsync();
        return Change(trip);
    }

    private static PlanboardChangeDto Change(TransportTrip trip)
        => new() { Id = trip.Id, ConcurrencyStamp = trip.ConcurrencyStamp, LastModificationTime = trip.LastModificationTime };

    /// <summary>The tenant's default trip type, else its first active one by code.</summary>
    private async Task<Guid> DefaultTripTypeIdAsync()
    {
        var type = await AsyncExecuter.FirstOrDefaultAsync((await _types.GetQueryableAsync())
            .Where(t => !t.IsArchived)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code)
            .Select(t => new { t.Id }));
        return type?.Id ?? throw new BusinessException(PlanningExecutionErrorCodes.NoTripType);
    }

    /// <summary>The row's resource in the filled request of a new trip (after the type's defaults).</summary>
    private async Task PlaceInNewTripAsync(TransportTripCreateDto filled, PlanboardResourceMode mode, Guid resourceId)
    {
        if (mode == PlanboardResourceMode.Driver)
        {
            /* The driver row's driver is the first driver; the type's other drivers follow. */
            filled.DriverIds = new[] { resourceId }.Concat(filled.DriverIds.Where(d => d != resourceId)).Take(PlanningExecutionConsts.MaxDriversPerTrip).ToList();
            return;
        }

        var current = filled.VehicleIds.Where(v => v != Guid.Empty).ToList();
        if (filled.VehicleCombinationId != null)
        {
            /* The type's combination becomes its members as loose vehicles, so the row's vehicle can take its slot. */
            current = await _masterData.TripVehicleIdsAsync(filled.VehicleCombinationId, []);
            filled.VehicleCombinationId = null;
        }

        var vehicles = await _masterData.VehiclesAsync(current.Append(resourceId));
        EnsureKind(vehicles, resourceId, mode, "resourceId");
        Place(current, mode, resourceId, vehicles);
        filled.VehicleIds = current.Distinct().ToList();

        if (mode == PlanboardResourceMode.Tractor)
        {
            var defaults = await TractorDefaultsAsync(resourceId, filled.DriverIds.Count == 0, filled.CarrierRelationId == null);
            if (defaults.DriverIds.Count > 0)
            {
                filled.DriverIds = defaults.DriverIds;
            }

            if (defaults.CarrierRelationId != null)
            {
                filled.CarrierRelationId = defaults.CarrierRelationId;
                filled.CarrierContactPersonId = null;
            }
        }
    }

    /// <summary>A vehicle of the mode's kinds: a trailer for the trailer mode, a motor vehicle for the tractor mode.</summary>
    private static void EnsureKind(Dictionary<Guid, MasterDataVehicleReferenceDto> vehicles, Guid vehicleId, PlanboardResourceMode mode, string field)
    {
        if (!vehicles.TryGetValue(vehicleId, out var vehicle))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", vehicleId);
        }

        if ((mode == PlanboardResourceMode.Trailer) != (vehicle.Kind == VehicleTypeKind.Trailer))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceKindMismatch).WithData("field", field);
        }
    }

    /// <summary>
    /// A vehicle put on a trip without a row it came from: a tractor replaces the trip's motor vehicle (position 1; placed
    /// before the trailers when there is none), a trailer takes the next free position. Already on the trip: unchanged.
    /// </summary>
    private static void Place(List<Guid> current, PlanboardResourceMode mode, Guid vehicleId, Dictionary<Guid, MasterDataVehicleReferenceDto> vehicles)
    {
        if (current.Contains(vehicleId))
        {
            return;
        }

        if (mode == PlanboardResourceMode.Trailer)
        {
            current.Add(vehicleId);
            return;
        }

        var motor = current.FindIndex(id => vehicles.TryGetValue(id, out var v) && v.Kind != VehicleTypeKind.Trailer);
        if (motor >= 0)
        {
            current[motor] = vehicleId;
        }
        else
        {
            current.Insert(0, vehicleId);
        }
    }

    /// <summary>Replaces <paramref name="from"/>'s position; without <paramref name="from"/> the id is appended when <paramref name="append"/>.</summary>
    private static void Replace(List<Guid> current, Guid? from, Guid to, bool append)
    {
        if (from == null)
        {
            if (append && !current.Contains(to))
            {
                current.Add(to);
            }

            return;
        }

        var index = current.IndexOf(from.Value);
        if (index < 0)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceNotOnTrip).WithData("resourceId", from.Value);
        }

        current[index] = to;
    }

    /// <summary>D11: what a tractor brings for the empty slots — its active default drivers and its owner as carrier (not the own company).</summary>
    private async Task<(List<Guid> DriverIds, Guid? CarrierRelationId)> TractorDefaultsAsync(Guid tractorId, bool driversEmpty, bool carrierEmpty)
    {
        if (!driversEmpty && !carrierEmpty)
        {
            return ([], null);
        }

        var tractor = (await _resources.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = MotorKinds.ToList(), Ids = [tractorId], IncludeArchived = true, Take = 1 }))
            .Items.FirstOrDefault();
        if (tractor == null)
        {
            return ([], null);
        }

        var driverIds = new List<Guid>();
        if (driversEmpty && tractor.DefaultDriverIds.Count > 0)
        {
            var drivers = await _masterData.DriversAsync(tractor.DefaultDriverIds);
            driverIds = tractor.DefaultDriverIds.Where(d => drivers.TryGetValue(d, out var driver) && !driver.IsArchived)
                .Distinct().Take(PlanningExecutionConsts.MaxDriversPerTrip).ToList();
        }

        Guid? carrier = null;
        if (carrierEmpty && tractor.OwnerRelationId != Guid.Empty && tractor.OwnerRelationId != await _masterData.OwnCompanyIdAsync())
        {
            carrier = tractor.OwnerRelationId;
        }

        return (driverIds, carrier);
    }
}
