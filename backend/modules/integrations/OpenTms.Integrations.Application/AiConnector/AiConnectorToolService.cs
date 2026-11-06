using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Tariffs;
using OpenTms.Integrations.Permissions;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.Orders.TransportOrders.Published;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Reporting.Published;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Features;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// <see cref="IAiConnectorToolService"/>: every result is built field by field from the source DTO here, never serialized
/// from it, so an amount, a remark or a person's name stays out until a design adds it (design 74 "Fields returned"). The
/// reads of other modules are the synchronous calls design 74 states under "Cross-module synchronous calls"; each one
/// keeps its own permission, feature and tenant filter. Read-only: no method here reaches a write of a service.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(IntegrationsPermissions.AiConnector.Use)]
[RequiresFeature(AppFeatures.Integrations, AppFeatures.IntegrationsApp.AiConnector, RequiresAll = true)]
[UnitOfWork(isTransactional: false)]
public class AiConnectorToolService : IntegrationsAppServiceBase, IAiConnectorToolService
{
    private readonly ITransportOrderAppService _orders;
    private readonly ITransportTripAppService _trips;
    private readonly ILegAppService _legs;
    private readonly IRelationAppService _relations;
    private readonly IVehicleAppService _vehicles;
    private readonly IRateLineAppService _rateLines;
    private readonly ICustomerRevenueReader _revenue;
    private readonly IAiConnectorToolAccess _access;
    private readonly IMetricQuery _metrics;

    public AiConnectorToolService(
        ITransportOrderAppService orders,
        ITransportTripAppService trips,
        ILegAppService legs,
        IRelationAppService relations,
        IVehicleAppService vehicles,
        IRateLineAppService rateLines,
        ICustomerRevenueReader revenue,
        IAiConnectorToolAccess access,
        IMetricQuery metrics)
    {
        _orders = orders;
        _trips = trips;
        _legs = legs;
        _relations = relations;
        _vehicles = vehicles;
        _rateLines = rateLines;
        _revenue = revenue;
        _access = access;
        _metrics = metrics;
    }

    public virtual async Task<AiToolList<AiOrderRow>> SearchOrdersAsync(SearchOrdersInput input)
    {
        var page = await _orders.GetListAsync(new TransportOrderListRequestDto
        {
            StartRow = input.StartRow,
            EndRow = input.EndRow,
            WildcardSearch = input.Text,
            Status = input.Status is { } status ? [status] : [],
            DateFrom = input.DateFrom,
            DateTo = input.DateTo,
            CustomerRelationId = input.CustomerRelationId,
        });

        return ToList(page, o => new AiOrderRow
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            OrderDate = o.OrderDate,
            CustomerRelationId = o.CustomerRelationId,
            CustomerName = o.CustomerName,
            CustomerReference = o.CustomerReference,
            TransportOrderTypeName = o.TransportOrderTypeName,
            Source = o.Source,
            FirstStopName = o.FirstStopName,
            FirstStopCity = o.FirstStopCity,
            FirstStopDate = o.FirstStopDate,
            LastStopName = o.LastStopName,
            LastStopCity = o.LastStopCity,
            LastStopDate = o.LastStopDate,
            LineCount = o.LineCount,
            StopCount = o.StopCount,
            TripNumbers = o.TripNumbers,
            ExecutionProgress = o.ExecutionProgress,
            Co2Kg = o.Co2Kg,
            HasTripRequest = o.HasTripRequest,
        });
    }

    public virtual async Task<AiOrderDetail> GetOrderAsync(GetOrderInput input)
    {
        var o = await _orders.GetAsync(input.Id);
        var summary = o.Summary;
        return new AiOrderDetail
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            OrderDate = o.OrderDate,
            CustomerRelationId = o.CustomerRelationId,
            CustomerName = o.CustomerName,
            CustomerReference = o.CustomerReference,
            TransportOrderTypeName = o.TransportOrderTypeName,
            Source = o.Source,
            FirstStopName = summary.FirstStopName,
            FirstStopCity = summary.FirstStopCity,
            FirstStopDate = summary.FirstStopDate,
            LastStopName = summary.LastStopName,
            LastStopCity = summary.LastStopCity,
            LastStopDate = summary.LastStopDate,
            LineCount = summary.LineCount,
            StopCount = summary.StopCount,
            TripNumbers = summary.TripNumbers,
            ExecutionProgress = summary.ExecutionProgress,
            Co2Kg = summary.Co2Kg,
            References = o.References.Select(r => new AiOrderReference { Type = r.Type, Value = r.Value }).ToList(),
            DefaultStops = o.DefaultStops.Select(s => new AiOrderDefaultStop
            {
                Position = s.Position,
                RelationName = s.RelationName,
                WaypointTypeName = s.WaypointTypeName,
                WindowFrom = s.WindowFrom,
                WindowTill = s.WindowTill,
            }).ToList(),
            Lines = o.Lines.Select(l => new AiOrderLine
            {
                Position = l.Position,
                ProductName = l.ProductName,
                TransportUnitName = l.TransportUnitName,
                Quantity = l.Quantity,
                Colli = l.Colli,
                WeightKg = l.WeightKg,
                VolumeM3 = l.VolumeM3,
                LoadingMeters = l.LoadingMeters,
                TemperatureName = l.TemperatureName,
                VehicleTypeName = l.VehicleTypeName,
                RequiredTransportCharacteristicNames = l.RequiredTransportCharacteristicNames.ToList(),
                ShipperRelationName = l.ShipperRelationName,
            }).ToList(),
        };
    }

    public virtual async Task<AiToolList<AiTripRow>> SearchTripsAsync(SearchTripsInput input)
    {
        var page = await _trips.GetListAsync(new TransportTripListRequestDto
        {
            StartRow = input.StartRow,
            EndRow = input.EndRow,
            WildcardSearch = input.Text,
            Status = input.Status is { } status ? [status] : null,
            DateFrom = input.DateFrom,
            DateTo = input.DateTo,
            CarrierRelationId = input.CarrierRelationId,
        });

        return ToList(page, t => new AiTripRow
        {
            Id = t.Id,
            Number = t.Number,
            Status = t.Status,
            Execution = t.Execution,
            TripDate = t.TripDate,
            TransportTripTypeName = t.TransportTripTypeName,
            CarrierRelationId = t.CarrierRelationId,
            CarrierName = t.CarrierName,
            Customers = t.Customers.ToList(),
            OrderNumbers = t.OrderNumbers.ToList(),
            Vehicles = t.Vehicles.ToList(),
            FirstStopCity = t.FirstStopCity,
            LastStopCity = t.LastStopCity,
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            LegCount = t.LegCount,
            StopCount = t.StopCount,
            LoadFactor = t.LoadFactor,
            LoadingMeters = t.LoadingMeters,
            PlannedDistanceM = t.PlannedDistanceM,
            ActualDistanceM = t.ActualDistanceM,
            PlannedDurationS = t.PlannedDurationS,
            Co2Kg = t.Co2Kg,
            Warnings = ToWarnings(t.Warnings),
        });
    }

    public virtual async Task<AiTripDetail> GetTripAsync(GetTripInput input)
    {
        var t = await _trips.GetAsync(input.Id);
        var summary = await _trips.GetSummaryAsync(input.Id);
        return new AiTripDetail
        {
            Id = t.Id,
            Number = t.Number,
            Status = t.Status,
            Execution = t.Execution,
            TripDate = t.TripDate,
            TransportTripTypeName = t.TransportTripTypeName,
            CarrierRelationId = t.CarrierRelationId,
            CarrierName = t.CarrierName,
            VehicleCombinationName = t.VehicleCombinationName,
            Vehicles = t.Vehicles.Select(v => new AiTripVehicle { Name = v.Name, LicencePlate = v.LicencePlate }).ToList(),
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            LoadFactor = t.LoadFactor,
            LoadingMeters = t.LoadingMeters,
            PlannedDistanceM = t.PlannedDistanceM,
            ActualDistanceM = t.ActualDistanceM,
            PlannedDurationS = t.PlannedDurationS,
            Co2Kg = t.Co2Kg,
            Warnings = ToWarnings(t.Warnings),
            Stops = t.Stops.Select(s => new AiTripStop
            {
                Position = s.Position,
                Name = s.Name,
                RelationName = s.RelationName,
                City = s.City,
                PlannedArrival = s.PlannedArrival,
                PlannedDeparture = s.PlannedDeparture,
                EstimatedArrival = s.EstimatedArrival,
                EstimatedDeparture = s.EstimatedDeparture,
                ActualArrival = s.ActualArrival,
                ActualDeparture = s.ActualDeparture,
            }).ToList(),
            LegCount = summary.LegCount,
            StopCount = summary.StopCount,
            OrderCount = summary.OrderCount,
            TotalQuantity = summary.TotalQuantity,
            TotalWeightKg = summary.TotalWeightKg,
        };
    }

    public virtual async Task<AiToolList<AiLegRow>> GetLegsAsync(GetLegsInput input)
    {
        var page = await _legs.GetListAsync(new LegListRequestDto
        {
            StartRow = input.StartRow,
            EndRow = input.EndRow,
            OrderId = input.OrderId,
            TripId = input.TripId,
        });

        return ToList(page, l => new AiLegRow
        {
            Id = l.Id,
            OrderId = l.OrderId,
            OrderNumber = l.OrderNumber,
            TripId = l.TripId,
            TripNumber = l.TripNumber,
            TripStatus = l.TripStatus,
            Position = l.Position,
            CustomerName = l.CustomerName,
            Origin = ToLegEnd(l.Origin),
            Destination = ToLegEnd(l.Destination),
            Quantity = l.Quantity,
            WeightKg = l.WeightKg,
            VolumeM3 = l.VolumeM3,
            LoadingMeters = l.LoadingMeters,
            DistanceM = l.DistanceM,
            DurationS = l.DurationS,
            EstimatedCo2Kg = l.EstimatedCo2Kg,
            IsViaLeg = l.IsViaLeg,
        });
    }

    public virtual async Task<AiToolList<AiRelationRow>> SearchRelationsAsync(SearchRelationsInput input)
    {
        var page = await _relations.GetListAsync(new RelationListRequestDto
        {
            StartRow = input.StartRow,
            EndRow = input.EndRow,
            WildcardSearch = input.Text,
            Roles = input.Roles,
            /* The answer states isArchived: the model tells an archived relation apart instead of missing it. */
            IncludeArchived = true,
        });

        return ToList(page, r => new AiRelationRow
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            City = r.City,
            CountryCode = r.CountryCode,
            Roles = r.Roles,
            CategoryNames = r.CategoryNames.ToList(),
            IsBlocked = r.IsBlocked,
            IsArchived = r.IsArchived,
        });
    }

    public virtual async Task<AiToolList<AiVehicleRow>> SearchVehiclesAsync(SearchVehiclesInput input)
    {
        var page = await _vehicles.GetListAsync(new VehicleListRequestDto
        {
            StartRow = input.StartRow,
            EndRow = input.EndRow,
            WildcardSearch = input.Text,
            VehicleTypeId = input.VehicleTypeId,
            IncludeArchived = true,
        });

        return ToList(page, v => new AiVehicleRow
        {
            Id = v.Id,
            Name = v.Name,
            Kind = v.Kind,
            LicencePlate = v.LicencePlate,
            RegistrationCountryCode = v.RegistrationCountryCode,
            VehicleTypeName = v.VehicleTypeName,
            FuelType = v.FuelType,
            EuroClass = v.EuroClass,
            OwnerName = v.OwnerName,
            HomeDepotName = v.HomeDepotName,
            CombinationName = v.CombinationName,
            IsArchived = v.IsArchived,
        });
    }

    public virtual async Task<AiOrderRates> GetOrderRatesAsync(GetOrderRatesInput input)
    {
        var rates = await _rateLines.GetAsync(new RateLineGetRequestDto { OrderId = input.OrderId, Side = input.Side });
        return new AiOrderRates
        {
            CurrencyCode = rates.CurrencyCode,
            State = rates.State?.Status,
            Totals = new AiRateTotals
            {
                LinesAmount = rates.Totals.LinesAmount,
                DiscountsAndSurchargesAmount = rates.Totals.DiscountsAndSurchargesAmount,
                AdditionalServicesAmount = rates.Totals.AdditionalServicesAmount,
                Total = rates.Totals.Total,
                HasFixedPrice = rates.Totals.HasFixedPrice,
            },
            Lines = rates.Lines.Select(l => new AiRateLine
            {
                Side = l.Side,
                Kind = l.Kind,
                AdditionalServiceName = l.AdditionalServiceName,
                DiscountOrSurchargeName = l.DiscountOrSurchargeName,
                PriceListCode = l.PriceListCode,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Amount = l.Amount,
                CurrencyCode = l.CurrencyCode,
                IsManual = l.IsManual,
                TripNumber = l.TripNumber,
            }).ToList(),
        };
    }

    public virtual async Task<AiCustomerRevenue> GetCustomerRevenueAsync(GetCustomerRevenueInput input)
    {
        /* ICustomerRevenueReader has no permission of its own (design 74): this tool is its authorization. */
        if (!await _access.IsAllowedAsync(AiConnectorToolCatalog.Find(AiConnectorToolCatalog.GetCustomerRevenue)!))
        {
            throw new AbpAuthorizationException("The customer's revenue needs its permissions and features.");
        }

        var revenue = await _revenue.GetAsync(input.CustomerRelationId, input.DateFrom, input.DateTo);
        return new AiCustomerRevenue
        {
            CustomerRelationId = input.CustomerRelationId,
            DateFrom = input.DateFrom,
            DateTo = input.DateTo,
            Definition = AiConnectorToolCatalog.RevenueDefinition,
            Totals = revenue.Totals.Select(t => new AiCustomerRevenueTotal
            {
                CurrencyCode = t.CurrencyCode,
                RevenueAmount = t.RevenueAmount,
                OrderCount = t.OrderCount,
            }).ToList(),
            OrdersWithoutAmount = revenue.OrdersWithoutAmount,
            ExcludedDraftCount = revenue.ExcludedDraftCount,
            ExcludedCancelledCount = revenue.ExcludedCancelledCount,
        };
    }

    /// <summary>Design 75: <see cref="IMetricQuery"/> checks each metric's permissions and features for this user itself.</summary>
    public virtual async Task<AiMetricList> ListMetricsAsync(ListMetricsInput input)
    {
        var list = await _metrics.ListAsync();
        return new AiMetricList
        {
            FactsReady = list.FactsReady,
            Metrics = list.Metrics.Select(m => new AiMetricListItem
            {
                Key = m.Key,
                Unit = m.Unit,
                Description = m.Description,
                Definition = m.Definition,
                Breakdowns = m.Breakdowns,
                Filters = m.Filters,
            }).ToList(),
        };
    }

    public virtual async Task<AiMetricAnswer> QueryMetricsAsync(QueryMetricsInput input)
    {
        var answer = await _metrics.QueryAsync(new MetricQueryRequestDto
        {
            Metrics = input.Metrics,
            DateFrom = input.DateFrom,
            DateTo = input.DateTo,
            GroupBy = input.GroupBy,
            Sort = input.Sort,
            Top = input.Top,
            Filters = input.Filters == null ? null : new MetricFiltersDto
            {
                Status = input.Filters.Status,
                CustomerRelationId = input.Filters.CustomerRelationId,
                TransportOrderTypeId = input.Filters.TransportOrderTypeId,
                OrganizationUnitId = input.Filters.OrganizationUnitId,
                CarrierRelationId = input.Filters.CarrierRelationId,
                VehicleId = input.Filters.VehicleId,
                TransportTripTypeId = input.Filters.TransportTripTypeId,
                Execution = input.Filters.Execution,
            },
        });

        return new AiMetricAnswer
        {
            Status = answer.Status,
            Message = answer.Message,
            Hint = answer.Hint,
            Period = answer.Period == null ? null : new AiMetricPeriod { DateFrom = answer.Period.DateFrom, DateTo = answer.Period.DateTo },
            Definitions = answer.Definitions?.Select(d => new AiMetricDefinitionText { Metric = d.Metric, Text = d.Text }).ToList(),
            Rows = answer.Rows?.Select(r => new AiMetricRow
            {
                Groups = r.Groups.Select(g => new AiMetricGroup { Breakdown = g.Breakdown, Id = g.Id, Name = g.Name }).ToList(),
                CurrencyCode = r.CurrencyCode,
                Values = r.Values,
            }).ToList(),
            TotalGroups = answer.TotalGroups,
            Truncated = answer.Truncated,
            Coverage = answer.Coverage == null ? null : new AiMetricCoverage
            {
                RecordsIncluded = answer.Coverage.RecordsIncluded,
                RecordsWithoutValue = answer.Coverage.RecordsWithoutValue,
                ExcludedByStatus = answer.Coverage.ExcludedByStatus,
            },
            LastFactUpdateAt = answer.LastFactUpdateAt,
            Warnings = answer.Warnings,
            FactsMissing = answer.FactsMissing,
        };
    }

    /// <summary>Total is the grid's filtered count: the trip list's total count ignores filters (design 74).</summary>
    private static AiToolList<TRow> ToList<TSource, TRow>(GridResponse<TSource> page, Func<TSource, TRow> map) => new()
    {
        Total = page.FilteredCount,
        Rows = page.Rows.Take(AiToolListInput.MaxTake).Select(map).ToList(),
    };

    private static List<AiTripWarning> ToWarnings(List<TripWarningDto> warnings) =>
        warnings.Select(w => new AiTripWarning { Code = w.Code }).ToList();

    private static AiLegEnd ToLegEnd(LegEndSnapshotDto end) => new()
    {
        Name = end.Name,
        City = end.City,
        CountryCode = end.CountryCode,
        PlannedDate = end.PlannedDate,
        WindowFrom = end.WindowFrom,
        WindowTill = end.WindowTill,
    };
}
