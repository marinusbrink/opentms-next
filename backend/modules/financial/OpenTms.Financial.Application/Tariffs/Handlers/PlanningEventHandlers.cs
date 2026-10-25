using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Tariffs.Handlers;

/// <summary>
/// Financial's reactions to Planning's facts (design 57, design 56 flow 2). Each handler requests the trip's cost lines
/// and/or the orders' revenue lines for a new version; the calculators report unchanged sets without an event, so a
/// repeated delivery is one effect and the chain through Planning's trip calculator ends (rule 3).
/// </summary>
public class LegsGeneratedHandler : IDistributedEventHandler<LegsGeneratedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public LegsGeneratedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    public Task HandleEventAsync(LegsGeneratedEto eventData)
        => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, eventData.OrderId, "LegsGenerated");
}

public class TransportTripLegsChangedHandler : IDistributedEventHandler<TransportTripLegsChangedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportTripLegsChangedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    public async Task HandleEventAsync(TransportTripLegsChangedEto eventData)
    {
        await _scheduler.RequestAsync(TripRateLineCalculator.TargetType, eventData.TripId, "TripLegsChanged");
        var orderIds = eventData.OrderIds.Distinct().ToList();
        if (orderIds.Count > 0)
        {
            await _scheduler.RequestManyAsync(OrderRateLineCalculator.TargetType, orderIds, "TripLegsChanged");
        }
    }
}

public class TransportTripChangedHandler : IDistributedEventHandler<TransportTripChangedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportTripChangedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    public Task HandleEventAsync(TransportTripChangedEto eventData)
        => eventData.Change is TripChangeKind.Carrier or TripChangeKind.Vehicles or TripChangeKind.Distance or TripChangeKind.Header or TripChangeKind.Stops
            ? _scheduler.RequestAsync(TripRateLineCalculator.TargetType, eventData.TripId, "Trip" + eventData.Change)
            : Task.CompletedTask;
}

/// <summary>The trip calculator stored new distances: the orders' per-kilometre lines may change (design 57).</summary>
public class TransportTripRecalculatedHandler : IDistributedEventHandler<TransportTripRecalculatedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportTripRecalculatedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    public async Task HandleEventAsync(TransportTripRecalculatedEto eventData)
    {
        var orderIds = eventData.OrderIds.Distinct().ToList();
        if (orderIds.Count > 0)
        {
            await _scheduler.RequestManyAsync(OrderRateLineCalculator.TargetType, orderIds, "TripRecalculated");
        }
    }
}

/// <summary>
/// A trip created from an order's trip request carries the agreed carrier price: Financial writes it as the trip's
/// overriding cost line (AD-08; design 59 Assumption 4: Planning stores no money), once per trip, then requests the
/// trip's lines. A repeated delivery finds the line and leaves it.
/// </summary>
public class TransportTripCreatedHandler : IDistributedEventHandler<TransportTripCreatedEto>, ITransientDependency
{
    private readonly IRepository<RateLine, Guid> _lines;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public ILogger<TransportTripCreatedHandler> Logger { get; set; } = NullLogger<TransportTripCreatedHandler>.Instance;

    public TransportTripCreatedHandler(IRepository<RateLine, Guid> lines, IDerivedValueScheduler scheduler, IGuidGenerator guids, ICurrentTenant currentTenant)
    {
        _lines = lines;
        _scheduler = scheduler;
        _guids = guids;
        _currentTenant = currentTenant;
    }

    public async Task HandleEventAsync(TransportTripCreatedEto eventData)
    {
        if (eventData.AgreedPurchaseAmount != null)
        {
            var existing = await _lines.FirstOrDefaultAsync(l => l.TripId == eventData.TripId && l.Kind == RateLineKind.AgreedPrice);
            if (existing == null)
            {
                var line = RateLine.Overriding(_guids.Create(), _currentTenant.Id, RateSide.Cost, RateLineAnchor.Trip(eventData.TripId), RateLineKind.AgreedPrice,
                    eventData.AgreedPurchaseCurrencyCode ?? "EUR", RateLine.Round2(eventData.AgreedPurchaseAmount.Value));
                await _lines.InsertAsync(line);
                Logger.LogInformation("Agreed price line written for trip {TripId}.", eventData.TripId);
            }
        }

        await _scheduler.RequestAsync(TripRateLineCalculator.TargetType, eventData.TripId, "TripCreated");
    }
}
