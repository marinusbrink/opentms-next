using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Published;
using Microsoft.Extensions.Logging;
using OpenTms.PlanningExecution.Export;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.Settings;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.ChangeTracking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Settings;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 59 "TransportTripAppService": the trip grids, the editor's detail and every command of the lifecycle. Each
/// command is one unit of work: the aggregate changes, the facts go to the outbox, the calculation is requested (flow 2).
/// Reads never calculate (AD-07). Feature: the Transport app's Trips node (design 56, default on).
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class TransportTripAppService : PlanningExecutionAppServiceBase, ITransportTripAppService
{
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTripType, Guid> _types;
    private readonly TripOperations _ops;
    private readonly LegAssignmentService _assignment;
    private readonly MasterDataReferences _masterData;
    private readonly LegDtoMapper _legMapper;
    private readonly PlanningExecutionClock _clock;

    public TransportTripAppService(
        IRepository<TransportTrip, Guid> trips,
        IRepository<Leg, Guid> legs,
        IRepository<TransportTripType, Guid> types,
        TripOperations ops,
        LegAssignmentService assignment,
        MasterDataReferences masterData,
        LegDtoMapper legMapper,
        PlanningExecutionClock clock)
    {
        _trips = trips;
        _legs = legs;
        _types = types;
        _ops = ops;
        _assignment = assignment;
        _masterData = masterData;
        _legMapper = legMapper;
        _clock = clock;
    }

    private const string Trips = AppFeatures.TransportApp.Trips;

    /* Reads */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<GridResponse<TransportTripRowDto>> GetListAsync(TransportTripListRequestDto input)
    {
        var query = await _trips.GetQueryableAsync();
        var archived = input.IsArchived ?? false;
        query = query.Where(t => t.IsArchived == archived);
        var total = await AsyncExecuter.LongCountAsync(query);

        query = ApplyFilters(query, input);
        var filtered = await AsyncExecuter.LongCountAsync(query);

        var sorted = Sort(query, input.SortBy, input.Descending);
        var page = await PageAsync(sorted, input.StartRow, input.EndRow - input.StartRow);
        return new GridResponse<TransportTripRowDto> { Rows = await ToRowsAsync(page), TotalCount = total, FilteredCount = filtered };
    }

    private async Task<List<RowSource>> PageAsync(IQueryable<TransportTrip> sorted, int skip, int take)
        => await AsyncExecuter.ToListAsync(sorted
            .Skip(skip).Take(take)
            .Select(t => new RowSource
            {
                Trip = t,
                StopCount = t.Stops.Count,
                FirstStopCity = t.Stops.OrderBy(s => s.Position).Select(s => s.City).FirstOrDefault(),
                LastStopCity = t.Stops.OrderByDescending(s => s.Position).Select(s => s.City).FirstOrDefault()
            }));

    /// <summary>
    /// UI-GRID-013: the trip list as an xlsx file — the same filters, view, search and sort as <see cref="GetListAsync"/>
    /// (or, with <see cref="TransportTripExportRequestDto.AllRows"/>, every trip the list's TotalCount counts), the
    /// requested columns in their order, times in the tenant's zone. Read page by page with one batch of lookups per
    /// page; refused above the tenant's row cap before anything is read.
    /// </summary>
    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<IRemoteStreamContent> ExportAsync(TransportTripExportRequestDto input)
    {
        var columns = TransportTripExportColumns.Resolve(input.Columns, L, _clock.Zone);
        /* Read only: up to the cap of rows pass through, none is written, none needs change tracking. */
        using var untracked = LazyServiceProvider.LazyGetRequiredService<IEntityChangeTrackingProvider>().Change(false);
        var query = await _trips.GetQueryableAsync();
        var archived = input.IsArchived ?? false;
        query = query.Where(t => t.IsArchived == archived);
        if (!input.AllRows)
        {
            query = ApplyFilters(query, input);
        }

        var count = await AsyncExecuter.LongCountAsync(query);
        var max = await SettingProvider.GetAsync(PlanningExecutionSettings.ExportMaxRows, PlanningExecutionConsts.DefaultExportMaxRows);
        if (count > max)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ExportTooManyRows).WithData("max", max).WithData("count", count);
        }

        /* Sort ends on the number, unique per tenant: the pages neither skip nor repeat a row. */
        var sorted = Sort(query, input.SortBy, input.Descending);
        var stream = await XlsxTableWriter.WriteAsync(L["PlanningExecution:ExportSheetName"], columns.Select(c => c.Column).ToList(), ExportRowsAsync(sorted, columns, (int)count));
        Logger.LogInformation("Exported {Count} transport trips in {Columns} columns.", count, columns.Count);
        return new RemoteStreamContent(stream, $"transport-trips-{_clock.Today():yyyy-MM-dd}.xlsx", XlsxTableWriter.ContentType);
    }

    private async IAsyncEnumerable<IReadOnlyList<object?>> ExportRowsAsync(IQueryable<TransportTrip> sorted, IReadOnlyList<TransportTripExportColumns.Chosen> columns, int count)
    {
        for (var start = 0; start < count; start += PlanningExecutionConsts.ExportPageSize)
        {
            var page = await PageAsync(sorted, start, PlanningExecutionConsts.ExportPageSize);
            if (page.Count == 0)
            {
                yield break;
            }

            foreach (var row in await ToRowsAsync(page))
            {
                yield return columns.Select(c => c.Value(row)).ToList();
            }
        }
    }

    /// <summary>
    /// The trip picker (design 61 "Outsource origin": the target trip): paged, not archived, the statuses asked for
    /// (default Planned and InProgress), the trip date range, the search key, one trip left out; nearest departure first.
    /// </summary>
    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<GridResponse<TransportTripPickerDto>> GetPickerAsync(TransportTripPickerRequestDto input)
    {
        var statuses = input.Status is { Count: > 0 } ? input.Status.Distinct().ToList() : [TransportTripStatus.Planned, TransportTripStatus.InProgress];
        var query = (await _trips.GetQueryableAsync()).Where(t => !t.IsArchived && statuses.Contains(t.Status));
        var total = await AsyncExecuter.LongCountAsync(query);

        if (input.DateFrom != null)
        {
            query = query.Where(t => t.TripDate >= input.DateFrom);
        }

        if (input.DateTo != null)
        {
            query = query.Where(t => t.TripDate <= input.DateTo);
        }

        if (input.ExcludeTripId != null)
        {
            query = query.Where(t => t.Id != input.ExcludeTripId);
        }

        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(t => t.SearchKey.Contains(term));
        }

        var filtered = await AsyncExecuter.LongCountAsync(query);
        var sorted = string.IsNullOrEmpty(input.SortBy)
            ? query.OrderBy(t => t.TripDate).ThenBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Number)
            : Sort(query, input.SortBy, input.Descending);
        var page = await AsyncExecuter.ToListAsync(sorted.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        var carriers = await _masterData.RelationNamesAsync(page.Select(t => t.CarrierRelationId));

        return new GridResponse<TransportTripPickerDto>
        {
            Rows = page.Select(t => new TransportTripPickerDto
            {
                Id = t.Id,
                Number = t.Number,
                Name = t.Name,
                TripDate = t.TripDate,
                Status = t.Status,
                CarrierRelationId = t.CarrierRelationId,
                CarrierName = t.CarrierRelationId != null ? carriers.GetValueOrDefault(t.CarrierRelationId.Value, NotFound) : null,
                PlannedDeparture = t.PlannedDeparture
            }).ToList(),
            TotalCount = total,
            FilteredCount = filtered
        };
    }

    private sealed class RowSource
    {
        public TransportTrip Trip { get; init; } = default!;
        public int StopCount { get; init; }
        public string? FirstStopCity { get; init; }
        public string? LastStopCity { get; init; }
    }

    private IQueryable<TransportTrip> ApplyFilters(IQueryable<TransportTrip> query, TransportTripListRequestDto input)
    {
        if (input.Status is { Count: > 0 })
        {
            var statuses = input.Status.ToList();
            query = query.Where(t => statuses.Contains(t.Status));
        }

        if (input.DateFrom != null)
        {
            query = query.Where(t => t.TripDate >= input.DateFrom);
        }

        if (input.DateTo != null)
        {
            query = query.Where(t => t.TripDate <= input.DateTo);
        }

        if (input.ExecutionFrom is { } executionFrom)
        {
            var from = executionFrom.UtcDateTime;
            query = query.Where(t => (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) >= from);
        }

        if (input.ExecutionTo is { } executionTo)
        {
            var to = executionTo.UtcDateTime;
            query = query.Where(t => t.PlannedDepartureUtc <= to);
        }

        if (input.CarrierRelationId != null)
        {
            query = query.Where(t => t.CarrierRelationId == input.CarrierRelationId);
        }

        if (input.OrganizationUnitId != null)
        {
            query = query.Where(t => t.OrganizationUnitId == input.OrganizationUnitId);
        }

        if (input.HideEmpty)
        {
            query = query.Where(t => t.Stops.Any());
        }

        /* The planner's day and week run in the tenant's time zone, not UTC. */
        var today = _clock.Today();
        switch (input.View?.Trim().ToLowerInvariant())
        {
            case "today":
                query = query.Where(t => t.TripDate == today);
                break;
            case "thisweek":
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                var sunday = monday.AddDays(6);
                query = query.Where(t => t.TripDate >= monday && t.TripDate <= sunday);
                break;
            case "open":
                query = query.Where(t => t.Status == TransportTripStatus.Planned || t.Status == TransportTripStatus.InProgress);
                break;
        }

        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(t => t.SearchKey.Contains(term));
        }

        return query;
    }

    private static IOrderedQueryable<TransportTrip> Sort(IQueryable<TransportTrip> query, string? sortBy, bool descending)
    {
        IOrderedQueryable<TransportTrip> Order<TKey>(System.Linq.Expressions.Expression<Func<TransportTrip, TKey>> key)
            => descending ? query.OrderByDescending(key).ThenByDescending(t => t.Number) : query.OrderBy(key).ThenBy(t => t.Number);

        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "planneddeparture" => Order(t => t.PlannedDepartureUtc),
            "number" => Order(t => t.Number),
            "name" => Order(t => t.Name),
            "tripdate" => Order(t => t.TripDate),
            "status" => Order(t => t.Status),
            "execution" => Order(t => t.Execution),
            "plannedarrival" => Order(t => t.PlannedArrivalUtc),
            "planneddistancem" => Order(t => t.PlannedDistanceM),
            "actualdistancem" => Order(t => t.ActualDistanceM),
            "loadfactor" => Order(t => t.LoadFactor),
            "loadingmeters" => Order(t => t.LoadingMeters),
            "co2kg" => Order(t => t.Co2Kg),
            "costamount" => Order(t => t.CostAmount),
            "revenueamount" => Order(t => t.RevenueAmount),
            "stopcount" => Order(t => t.Stops.Count),
            "carrierrelationid" => Order(t => t.CarrierRelationId),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy ?? "")
        };
    }

    private async Task<List<TransportTripRowDto>> ToRowsAsync(List<RowSource> page)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var trips = page.Select(p => p.Trip).ToList();
        var tripIds = trips.Select(t => t.Id).ToList();
        var typeIds = trips.Select(t => t.TransportTripTypeId).Distinct().ToList();
        var types = (await _types.GetListAsync(t => typeIds.Contains(t.Id))).ToDictionary(t => t.Id, t => t.Name.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name));
        var carriers = await _masterData.RelationNamesAsync(trips.Select(t => t.CarrierRelationId));

        var vehicleRows = await AsyncExecuter.ToListAsync((await _trips.WithDetailsAsync(t => t.Vehicles, t => t.Drivers)).Where(t => tripIds.Contains(t.Id)));
        var combinations = await _masterData.CombinationsAsync(trips.Select(t => t.VehicleCombinationId));
        var vehicleIds = vehicleRows.SelectMany(r => r.Vehicles.Select(v => v.VehicleId)).Concat(combinations.Values.SelectMany(c => c.VehicleIds));
        var vehicles = await _masterData.VehiclesAsync(vehicleIds);
        var drivers = await _masterData.DriverNamesAsync(vehicleRows.SelectMany(r => r.Drivers.Select(d => d.DriverId)));

        var legRows = await AsyncExecuter.ToListAsync((await _legs.GetQueryableAsync())
            .Where(l => l.TripId != null && tripIds.Contains(l.TripId.Value))
            .Select(l => new { TripId = l.TripId!.Value, l.OrderId }));
        var orders = await _ops.OrderReferencesAsync(legRows.Select(l => l.OrderId));

        return page.Select(p =>
        {
            var t = p.Trip;
            var children = vehicleRows.FirstOrDefault(r => r.Id == t.Id);
            var tripVehicleIds = t.VehicleCombinationId != null && combinations.TryGetValue(t.VehicleCombinationId.Value, out var combination)
                ? combination.VehicleIds
                : (children?.Vehicles ?? []).OrderBy(v => v.Position).Select(v => v.VehicleId).ToList();
            var tripLegs = legRows.Where(l => l.TripId == t.Id).ToList();
            var tripOrders = tripLegs.Select(l => l.OrderId).Distinct().Select(id => orders.GetValueOrDefault(id)).Where(o => o != null).Select(o => o!).ToList();
            return new TransportTripRowDto
            {
                Id = t.Id,
                Number = t.Number,
                Name = t.Name,
                TripDate = t.TripDate,
                Status = t.Status,
                IsArchived = t.IsArchived,
                Execution = t.Execution,
                TransportTripTypeId = t.TransportTripTypeId,
                TransportTripTypeName = types.GetValueOrDefault(t.TransportTripTypeId),
                CarrierRelationId = t.CarrierRelationId,
                CarrierName = t.CarrierRelationId != null ? carriers.GetValueOrDefault(t.CarrierRelationId.Value, NotFound) : null,
                OrganizationUnitId = t.OrganizationUnitId,
                PlannedDeparture = t.PlannedDeparture,
                PlannedArrival = t.PlannedArrival,
                ActualDeparture = t.ActualDeparture,
                ActualArrival = t.ActualArrival,
                PlannedDistanceM = t.PlannedDistanceM,
                ActualDistanceM = t.ActualDistanceM,
                PlannedDurationS = t.PlannedDurationS,
                StopCount = p.StopCount,
                LegCount = tripLegs.Count,
                Vehicles = tripVehicleIds.Select(id => vehicles.TryGetValue(id, out var v) ? v.Name : NotFound).ToList(),
                Drivers = (children?.Drivers ?? []).OrderBy(d => d.Position).Select(d => drivers.GetValueOrDefault(d.DriverId, NotFound)).ToList(),
                Customers = tripOrders.Select(o => o.CustomerName).Distinct().ToList(),
                OrderNumbers = tripOrders.Where(o => o.Number != null).Select(o => o.Number!).Distinct().ToList(),
                FirstStopCity = p.FirstStopCity,
                LastStopCity = p.LastStopCity,
                LoadFactor = t.LoadFactor,
                LoadingMeters = t.LoadingMeters,
                Co2Kg = t.Co2Kg,
                CostAmount = t.CostAmount,
                RevenueAmount = t.RevenueAmount,
                Warnings = t.Warnings.Select(MapWarning).ToList(),
                CalculatedAt = t.CalculatedAt
            };
        }).ToList();
    }

    private string NotFound => L["PlanningExecution:NotFound"].Value;

    private static TripWarningDto MapWarning(TripWarning w) => new() { Code = w.Code, LegId = w.LegId, StopId = w.StopId, Data = w.Data };

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual Task<TransportTripDto> GetAsync(Guid id) => ToDtoAsync(id);

    private async Task<TransportTripDto> ToDtoAsync(Guid id)
    {
        var t = await _ops.GetWithDetailsAsync(id);
        var legs = await _ops.LegsOnTripAsync(id);
        var type = await _types.FindAsync(t.TransportTripTypeId);
        var relationNames = await _masterData.RelationNamesAsync(
            new Guid?[] { t.CarrierRelationId, t.DepartureRelationId, t.ArrivalRelationId }.Concat(t.Stops.Select(s => (Guid?)s.RelationId)));
        var combinations = await _masterData.CombinationsAsync([t.VehicleCombinationId]);
        var vehicles = await _masterData.VehiclesAsync(t.Vehicles.Select(v => v.VehicleId).Concat(combinations.Values.SelectMany(c => c.VehicleIds)));
        var drivers = await _masterData.DriverNamesAsync(t.Drivers.Select(d => d.DriverId));
        var orders = await _ops.OrderReferencesAsync(legs.Select(l => l.OrderId));
        var legById = legs.ToDictionary(l => l.Id);

        var dto = new TransportTripDto
        {
            Id = t.Id,
            Number = t.Number,
            Name = t.Name,
            TransportTripTypeId = t.TransportTripTypeId,
            TransportTripTypeName = type?.Name.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
            TripDate = t.TripDate,
            Status = t.Status,
            IsArchived = t.IsArchived,
            Execution = t.Execution,
            CarrierRelationId = t.CarrierRelationId,
            CarrierName = Name(relationNames, t.CarrierRelationId),
            CarrierContactPersonId = t.CarrierContactPersonId,
            DepartureRelationId = t.DepartureRelationId,
            DepartureRelationName = Name(relationNames, t.DepartureRelationId),
            DepartureAddressId = t.DepartureAddressId,
            ArrivalRelationId = t.ArrivalRelationId,
            ArrivalRelationName = Name(relationNames, t.ArrivalRelationId),
            ArrivalAddressId = t.ArrivalAddressId,
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            StopCompositionMode = t.StopCompositionMode,
            FirstLastStopRule = t.FirstLastStopRule,
            VehicleCombinationId = t.VehicleCombinationId,
            VehicleCombinationName = t.VehicleCombinationId != null && combinations.TryGetValue(t.VehicleCombinationId.Value, out var combination) ? combination.Name : null,
            Vehicles = (t.VehicleCombinationId != null && combinations.TryGetValue(t.VehicleCombinationId.Value, out var c)
                    ? c.VehicleIds.Select((vid, i) => (Position: i + 1, VehicleId: vid))
                    : t.Vehicles.OrderBy(v => v.Position).Select(v => (v.Position, v.VehicleId)))
                .Select(v => new TripVehicleDto
                {
                    Position = v.Position,
                    VehicleId = v.VehicleId,
                    Name = vehicles.TryGetValue(v.VehicleId, out var vehicle) ? vehicle.Name : NotFound,
                    LicencePlate = vehicles.TryGetValue(v.VehicleId, out var plate) ? plate.LicencePlate : null
                }).ToList(),
            Drivers = t.Drivers.OrderBy(d => d.Position).Select(d => new TripDriverDto { Position = d.Position, DriverId = d.DriverId, Name = drivers.GetValueOrDefault(d.DriverId, NotFound) }).ToList(),
            Remarks = t.Remarks,
            DriverInstructions = t.DriverInstructions,
            OrganizationUnitId = t.OrganizationUnitId,
            Attributes = new Dictionary<string, string>(t.Attributes),
            CreatedFromOrderId = t.CreatedFromOrderId,
            PlannedDistanceM = t.PlannedDistanceM,
            ActualDistanceM = t.ActualDistanceM,
            PlannedDurationS = t.PlannedDurationS,
            LoadFactor = t.LoadFactor,
            LoadingMeters = t.LoadingMeters,
            Co2Kg = t.Co2Kg,
            Warnings = t.Warnings.Select(MapWarning).ToList(),
            CostAmount = t.CostAmount,
            RevenueAmount = t.RevenueAmount,
            CalculatedAt = t.CalculatedAt,
            Stops = t.OrderedStops.Select(s => new TripStopDto
            {
                Id = s.Id,
                Position = s.Position,
                WaypointTypeId = s.WaypointTypeId,
                RelationId = s.RelationId,
                RelationName = Name(relationNames, s.RelationId),
                RelationAddressId = s.RelationAddressId,
                Name = s.Name,
                City = s.City,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                TimeZoneId = s.TimeZoneId,
                IsManual = s.IsManual,
                PlannedDurationMin = s.PlannedDurationMin,
                PlannedArrival = s.PlannedArrival,
                PlannedDeparture = s.PlannedDeparture,
                EstimatedArrival = s.EstimatedArrival,
                EstimatedDeparture = s.EstimatedDeparture,
                ActualArrival = s.ActualArrival,
                ActualDeparture = s.ActualDeparture,
                PlannedCumulativeDistanceM = s.PlannedCumulativeDistanceM,
                ActualCumulativeDistanceM = s.ActualCumulativeDistanceM,
                Remarks = s.Remarks,
                Legs = s.Legs
                    .Where(l => legById.ContainsKey(l.LegId))
                    .OrderBy(l => legById[l.LegId].TripPosition).ThenBy(l => l.Role)
                    .Select(l => MapStopLeg(legById[l.LegId], l.Role, orders.GetValueOrDefault(legById[l.LegId].OrderId)))
                    .ToList()
            }).ToList(),
            CreationTime = t.CreationTime,
            CreatorId = t.CreatorId,
            LastModificationTime = t.LastModificationTime,
            LastModifierId = t.LastModifierId,
            ConcurrencyStamp = t.ConcurrencyStamp
        };
        return dto;
    }

    private string? Name(Dictionary<Guid, string> names, Guid? id) => id == null ? null : names.GetValueOrDefault(id.Value, NotFound);

    private static TripStopLegDto MapStopLeg(Leg l, StopLegRole role, TransportOrderReferenceDto? order)
    {
        var end = l.End(role);
        return new TripStopLegDto
        {
            LegId = l.Id,
            Role = role,
            OrderId = l.OrderId,
            OrderNumber = order?.Number,
            CustomerRelationId = l.CustomerRelationId,
            CustomerName = order?.CustomerName,
            OrderLineId = l.OrderLineId,
            Position = l.Position,
            Quantity = l.Quantity,
            WeightKg = l.WeightKg,
            LoadingMeters = l.LoadingMeters,
            Reference = end.Reference,
            PlannedDate = end.PlannedDate,
            WindowFrom = end.WindowFrom,
            WindowTill = end.WindowTill,
            DistanceM = l.DistanceM,
            AllocatedCostAmount = l.AllocatedCostAmount,
            AllocatedRevenueAmount = l.AllocatedRevenueAmount,
            IsCarriedOutSeparately = l.IsCarriedOutSeparately
        };
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<TransportTripSummaryDto> GetSummaryAsync(Guid id)
    {
        var t = await _ops.GetWithDetailsAsync(id);
        var legs = await _ops.LegsOnTripAsync(id);
        return new TransportTripSummaryDto
        {
            Id = t.Id,
            Number = t.Number,
            Status = t.Status,
            StopCount = t.Stops.Count,
            LegCount = legs.Count,
            OrderCount = legs.Select(l => l.OrderId).Distinct().Count(),
            PlannedDistanceM = t.PlannedDistanceM,
            ActualDistanceM = t.ActualDistanceM,
            PlannedDurationS = t.PlannedDurationS,
            TotalQuantity = legs.Sum(l => l.Quantity),
            TotalWeightKg = legs.Sum(l => l.WeightKg ?? 0),
            TotalLoadingMeters = legs.Sum(l => l.LoadingMeters ?? 0),
            LoadFactor = t.LoadFactor,
            Co2Kg = t.Co2Kg,
            CostAmount = t.CostAmount,
            RevenueAmount = t.RevenueAmount,
            MarginAmount = t.CostAmount != null || t.RevenueAmount != null ? (t.RevenueAmount ?? 0) - (t.CostAmount ?? 0) : null,
            Warnings = t.Warnings.Select(MapWarning).ToList(),
            CalculatedAt = t.CalculatedAt
        };
    }

    /* Create and header */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Create)]
    public virtual async Task<TransportTripDto> CreateAsync(TransportTripCreateDto input)
    {
        var trip = await _ops.CreateAsync(input, null, null, null);
        await _ops.RequestRecalculationAsync(trip.Id, "Created");
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(trip.Id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<TransportTripDto> UpdateAsync(Guid id, TransportTripUpdateDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        if (input.TripDate != trip.TripDate)
        {
            var type = await _types.GetAsync(trip.TransportTripTypeId);
            if (type.Numbering.Period(input.TripDate) != type.Numbering.Period(trip.TripDate))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.TripDateLocked).WithData("number", trip.Number).WithData("value", input.TripDate.ToString("yyyy-MM-dd"));
            }
        }

        await _masterData.EnsureRelationsExistAsync([input.DepartureRelationId, input.ArrivalRelationId], "relations");
        var timingChanged = input.PlannedDeparture != trip.PlannedDeparture || input.PlannedArrival != trip.PlannedArrival
                            || input.DepartureAddressId != trip.DepartureAddressId || input.ArrivalAddressId != trip.ArrivalAddressId
                            || input.FirstLastStopRule != trip.FirstLastStopRule;
        var compositionChanged = input.StopCompositionMode != trip.StopCompositionMode;
        trip.SetHeader(input.Name, input.TripDate, input.PlannedDeparture, input.PlannedArrival, input.DepartureRelationId, input.DepartureAddressId,
            input.ArrivalRelationId, input.ArrivalAddressId, input.Remarks, input.DriverInstructions, input.OrganizationUnitId, input.Execution,
            input.StopCompositionMode, input.FirstLastStopRule, input.Attributes);
        var distanceChanged = input.ActualDistanceM != trip.ActualDistanceM;
        trip.SetActualDistance(input.ActualDistanceM);
        trip.ConcurrencyStamp = input.ConcurrencyStamp;
        trip.ApplyFirstLastStopRule();
        if (compositionChanged)
        {
            var legs = await _ops.LegsOnTripAsync(id);
            foreach (var stop in trip.Stops)
            {
                foreach (var link in stop.Legs.ToList())
                {
                    stop.Legs.Remove(link);
                }
            }

            _assignment.Recompose(trip, legs, _ops.NewId);
            await _legs.UpdateManyAsync(legs, autoSave: true);
        }

        await _ops.ChangedAsync(trip, TripChangeKind.Header, timingChanged || compositionChanged);
        if (distanceChanged)
        {
            /* Financial's own-fleet distance line follows the actual distance (design 57). */
            await _ops.ChangedAsync(trip, TripChangeKind.Distance, recalculate: !(timingChanged || compositionChanged));
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<TransportTripDto> SetCarrierAsync(Guid id, TripCarrierDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        await _masterData.EnsureRelationsExistAsync([input.CarrierRelationId], "carrierRelationId");
        trip.SetCarrier(input.CarrierRelationId, input.CarrierContactPersonId);
        await _ops.ChangedAsync(trip, TripChangeKind.Carrier, recalculate: true);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<TransportTripDto> SetVehiclesAsync(Guid id, TripVehiclesDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        await _ops.ValidateVehiclesAsync(input.VehicleCombinationId, input.VehicleIds);
        trip.SetVehicles(input.VehicleCombinationId, input.VehicleIds);
        await _ops.ChangedAsync(trip, TripChangeKind.Vehicles, recalculate: true);
        /* The legs' routes depend on the vehicle profile (toll, truck routing). */
        var legIds = await AsyncExecuter.ToListAsync((await _legs.GetQueryableAsync()).Where(l => l.TripId == id).Select(l => l.Id));
        if (legIds.Count > 0)
        {
            await LazyServiceProvider.LazyGetRequiredService<Platform.DerivedValues.IDerivedValueScheduler>()
                .RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, legIds, "VehiclesChanged");
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<TransportTripDto> SetDriversAsync(Guid id, TripDriversDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        await _ops.ValidateDriversAsync(input.DriverIds);
        trip.SetDrivers(input.DriverIds);
        await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: false);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    /* Legs */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<TransportTripDto> AssignLegsAsync(Guid id, AssignLegsDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.AssignAsync(trip, legs, input.AllowOutsideWindow, input.AfterStopId, moving: false);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<TransportTripDto> UnassignLegsAsync(Guid id, UnassignLegsDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.UnassignAsync(trip, legs);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<TransportTripDto> MoveLegsAsync(Guid id, MoveLegsDto input)
    {
        if (input.ToTripId == id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.SameTrip).WithData("tripId", id);
        }

        var source = await _ops.GetWithDetailsAsync(id);
        var target = await _ops.GetWithDetailsAsync(input.ToTripId);
        var legs = await _ops.LegsAsync(input.LegIds);
        if (legs.Any(l => l.TripId != source.Id))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", legs.First(l => l.TripId != source.Id).Id).WithData("number", source.Number);
        }

        await _ops.AssignAsync(target, legs, input.AllowOutsideWindow, null, moving: true);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    /* Stops */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<TransportTripDto> AddStopAsync(Guid id, AddManualStopDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var address = await _masterData.AddressOfRelationAsync(input.RelationId, input.RelationAddressId);
        if (address == null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "relationId").WithData("value", input.RelationId);
        }

        trip.AddManualStop(_ops.NewId(), input.Position, input.WaypointTypeId, address.RelationId, address.Id, address.RelationName, address.City,
            address.Latitude, address.Longitude, address.TimeZoneId, input.PlannedDurationMin, input.Remarks);
        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: true);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<TransportTripDto> UpdateStopAsync(Guid id, Guid stopId, UpdateStopDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var stop = trip.GetStop(stopId);
        var moved = input.Position != null && input.Position != stop.Position;
        if (moved)
        {
            trip.MoveStop(stopId, input.Position!.Value);
        }

        if (input.Remarks != null)
        {
            stop.SetRemarks(input.Remarks);
        }

        var stayChanged = input.PlannedDurationMin != null && input.PlannedDurationMin != stop.PlannedDurationMin;
        if (stayChanged)
        {
            stop.SetPlannedDurationMin(input.PlannedDurationMin);
        }

        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: moved || stayChanged);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<TransportTripDto> DeleteStopAsync(Guid id, Guid stopId)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.RemoveManualStop(stopId);
        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: true);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<TransportTripDto> RecordStopActualsAsync(Guid id, Guid stopId, StopActualsDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.RecordStopActual(stopId, input.ActualArrival, input.ActualDeparture, input.CumulativeDistanceM);
        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: false);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    /* Lifecycle */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Start)]
    public virtual async Task<TransportTripDto> StartAsync(Guid id, TripTimestampDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.Start(input.At ?? _ops.Now);
        await _ops.StatusChangedAsync(trip, (await _ops.LegsOnTripAsync(id)).Select(l => l.OrderId));
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Complete)]
    public virtual async Task<TransportTripDto> CompleteAsync(Guid id, TripTimestampDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.Complete(input.At ?? _ops.Now);
        await _ops.StatusChangedAsync(trip, (await _ops.LegsOnTripAsync(id)).Select(l => l.OrderId));
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Cancel)]
    public virtual async Task<TransportTripDto> CancelAsync(Guid id)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.EnsureEditable();
        if (trip.IsFinal)
        {
            /* Refused by the status machine before any leg is touched. */
            trip.Cancel();
        }

        var legs = await _ops.LegsOnTripAsync(id);
        var orderIds = legs.Select(l => l.OrderId).ToList();
        if (legs.Count > 0)
        {
            await _ops.UnassignAsync(trip, legs);
        }

        trip.Cancel();
        await _ops.StatusChangedAsync(trip, orderIds);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Archive)]
    public virtual async Task<TransportTripDto> ArchiveAsync(Guid id)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.Archive();
        await _trips.UpdateAsync(trip);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Archive)]
    public virtual async Task<TransportTripDto> UnarchiveAsync(Guid id)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.Unarchive();
        await _trips.UpdateAsync(trip);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.EnsureDeletable();
        var legs = await _ops.LegsOnTripAsync(id);
        if (legs.Count > 0)
        {
            await _ops.UnassignAsync(trip, legs);
        }

        await _trips.DeleteAsync(trip);
    }

    /* The four leg operations (trips.md 20–24) */

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<LegDto> SplitLineAsync(Guid id, SplitLineDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var leg = (await _ops.LegsAsync([input.LegId])).Single();
        if (leg.TripId != trip.Id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
        }

        var target = input.ToTripId == null ? null : input.ToTripId == id ? trip : await _ops.GetWithDetailsAsync(input.ToTripId.Value);
        var legsOnTarget = target == null ? [] : await _ops.LegsOnTripAsync(target.Id);
        var clone = _assignment.SplitLine(leg, input.Quantity, target, legsOnTarget, input.AllowOutsideWindow, _ops.NewId);
        await _legs.InsertAsync(clone, autoSave: true);
        await _legs.UpdateAsync(leg, autoSave: true);
        await _ops.RequestRecalculationAsync(trip.Id, "LegSplit");
        if (target != null)
        {
            await _ops.AfterLegsChangedAsync(target, [leg.OrderId], "LegSplit");
            await LazyServiceProvider.LazyGetRequiredService<Platform.DerivedValues.IDerivedValueScheduler>()
                .RequestAsync(PlanningExecutionApplicationModule.LegTargetType, clone.Id, "LegSplit");
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        return (await _legMapper.MapAsync([clone])).Single();
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<LegDto> ReturnLegAsync(Guid id, LegReferenceDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var leg = (await _ops.LegsAsync([input.LegId])).Single();
        if (leg.TripId != trip.Id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
        }

        var legsOnTrip = await _ops.LegsOnTripAsync(id);
        var legsOfLine = await _legs.GetListAsync(l => l.OrderLineId == leg.OrderLineId);
        var returnLeg = _assignment.CreateReturnLeg(leg, trip, legsOnTrip, legsOfLine, _ops.NewId);
        await _legs.InsertAsync(returnLeg, autoSave: true);
        await _ops.AfterLegsChangedAsync(trip, [leg.OrderId], "ReturnLegCreated");
        await LazyServiceProvider.LazyGetRequiredService<Platform.DerivedValues.IDerivedValueScheduler>()
            .RequestAsync(PlanningExecutionApplicationModule.LegTargetType, returnLeg.Id, "ReturnLegCreated");
        await CurrentUnitOfWork!.SaveChangesAsync();
        return (await _legMapper.MapAsync([returnLeg])).Single();
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<TransportTripDto> CarryOutSeparatelyAsync(Guid id, LegReferenceDto input)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        var legsOnTrip = await _ops.LegsOnTripAsync(id);
        var leg = legsOnTrip.FirstOrDefault(l => l.Id == input.LegId)
                  ?? throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", input.LegId).WithData("number", trip.Number);
        _assignment.CarryOutSeparately(trip, leg, legsOnTrip, _ops.NewId);
        await _legs.UpdateAsync(leg, autoSave: true);
        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: true);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await ToDtoAsync(id);
    }

    [RequiresFeature(AppFeatures.Transport, Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<LegDto> OutsourceOriginAsync(Guid id, OutsourceOriginDto input)
    {
        if (input.ToTripId == id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.SameTrip).WithData("tripId", id);
        }

        var trip = await _ops.GetWithDetailsAsync(id);
        var target = await _ops.GetWithDetailsAsync(input.ToTripId);
        var leg = (await _ops.LegsAsync([input.LegId])).Single();
        if (leg.TripId != trip.Id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
        }

        var hubAddress = await _masterData.AddressOfRelationAsync(input.HubRelationId, input.HubAddressId)
                         ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "hubRelationId").WithData("value", input.HubRelationId);
        var legsOnTrip = await _ops.LegsOnTripAsync(id);
        var legsOnTarget = await _ops.LegsOnTripAsync(target.Id);
        var hubLeg = _assignment.OutsourceOriginActivity(leg, MasterDataReferences.ToDepot(hubAddress), target, legsOnTarget, trip, legsOnTrip, input.AllowOutsideWindow, _ops.NewId);
        await _legs.InsertAsync(hubLeg, autoSave: true);
        await _legs.UpdateAsync(leg, autoSave: true);
        await _ops.AfterLegsChangedAsync(trip, [leg.OrderId], "OriginOutsourced");
        await _ops.AfterLegsChangedAsync(target, [leg.OrderId], "OriginOutsourced");
        await LazyServiceProvider.LazyGetRequiredService<Platform.DerivedValues.IDerivedValueScheduler>()
            .RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, [hubLeg.Id, leg.Id], "OriginOutsourced");
        await CurrentUnitOfWork!.SaveChangesAsync();
        return (await _legMapper.MapAsync([hubLeg])).Single();
    }
}
