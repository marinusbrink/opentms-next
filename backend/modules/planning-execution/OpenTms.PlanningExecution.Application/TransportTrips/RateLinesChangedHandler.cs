using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 59 "Requests": Financial's rate lines changed, so the allocation over the legs (AD-20) is recalculated — for the
/// trip itself, or for every trip holding a leg of the order. Requesting is idempotent (a newer version wins).
/// </summary>
public class RateLinesChangedHandler : IDistributedEventHandler<RateLinesChangedEto>, ITransientDependency
{
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IDerivedValueScheduler _scheduler;

    public RateLinesChangedHandler(IRepository<Leg, Guid> legs, IRepository<TransportTrip, Guid> trips, IDerivedValueScheduler scheduler)
    {
        _legs = legs;
        _trips = trips;
        _scheduler = scheduler;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(RateLinesChangedEto eventData)
    {
        if (eventData.TripId is { } tripId)
        {
            if (await _trips.AnyAsync(t => t.Id == tripId))
            {
                await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, tripId, "RateLinesChanged");
            }

            return;
        }

        if (eventData.OrderId is { } orderId)
        {
            var tripIds = await AsyncExecuter.ToListAsync((await _legs.GetQueryableAsync())
                .Where(l => l.OrderId == orderId && l.TripId != null)
                .Select(l => l.TripId!.Value)
                .Distinct());
            if (tripIds.Count > 0)
            {
                await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.TripTargetType, tripIds, "RateLinesChanged");
            }
        }
    }

    private Volo.Abp.Linq.IAsyncQueryableExecuter AsyncExecuter => _legs.AsyncExecuter;
}
