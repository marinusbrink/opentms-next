using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.TestDoubles;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>Design 74's shared plumbing on SQLite: reading an order's legs, building Manage legs rows and redelivering the line's event.</summary>
public abstract class PlanboardReviewTestBase : PlanboardTestBase
{
    protected IPlanboardOrderAppService OrderFields => GetRequiredService<IPlanboardOrderAppService>();

    protected IPlanboardOrderLineFieldAppService LineFields => GetRequiredService<IPlanboardOrderLineFieldAppService>();

    protected PlanboardReviewEventRecorder Events => GetRequiredService<PlanboardReviewEventRecorder>();

    protected async Task<List<LegDto>> LegRowsAsync(Guid orderId)
        => (await Legs.GetListAsync(LegList(orderId))).Rows.OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).ThenBy(l => l.Id).ToList();

    protected Task<TransportTripDto> OnTripAsync(TransportTripDto trip, params Guid[] legIds)
        => Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legIds.ToList(), AllowOutsideWindow = true });

    protected Task RedeliverLinesChangedAsync(Guid orderId)
        => WithUnitOfWorkAsync(() => Handler.HandleEventAsync(new TransportOrderLinesChangedEto { OrderId = orderId }));

    protected int TripRequests(Guid tripId) => Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, tripId).Count;

    protected int LegRequests(Guid legId) => Requests.ReasonsFor(PlanningExecutionApplicationModule.LegTargetType, legId).Count;

    /// <summary>Every stored fact of a leg a later delivery could change, to compare before and after.</summary>
    protected static string Fingerprint(LegDto l)
        => string.Join("|", l.Id, l.OrderLineId, l.Position, l.Source, l.TripId, l.Quantity, l.WeightKg, l.Origin.StopId, l.Destination.StopId, l.TransportUnitId, l.ConcurrencyStamp);

    protected static LegChainEndInputDto End(LegEndSnapshotDto e) => new()
    {
        WaypointTypeId = e.WaypointTypeId, RelationId = e.RelationId, RelationAddressId = e.RelationAddressId, PlannedDate = e.PlannedDate,
        WindowFrom = e.WindowFrom, WindowTill = e.WindowTill, Reference = e.Reference
    };

    protected static LegChainRowInputDto Row(LegDto l) => new() { Id = l.Id, Remarks = l.Remarks, Origin = End(l.Origin), Destination = End(l.Destination) };

    protected static List<LegChainStampDto> StampsOf(LegChainDto chain)
        => chain.Rows.Select(r => new LegChainStampDto { Id = r.Leg.Id, ConcurrencyStamp = r.Leg.ConcurrencyStamp }).ToList();

    protected LegChainEndInputDto EndAt((Guid RelationId, Guid AddressId) place, Guid waypointType, DateOnly? date = null)
        => new() { WaypointTypeId = waypointType, RelationId = place.RelationId, RelationAddressId = place.AddressId, PlannedDate = date ?? Day };

    /// <summary>A → X → Y → B, saved through Manage legs from a generated A → B.</summary>
    protected async Task<(Guid OrderId, LegChainDto Chain, LegDto Original)> ThreeLegChainAsync(FleetSet f, (Guid RelationId, Guid AddressId) x, (Guid RelationId, Guid AddressId) y)
    {
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var original = (await LegRowsAsync(orderId)).Single();
        var chain = await Legs.GetLineAsync(original.OrderLineId);
        var first = Row(original);
        first.Destination = EndAt(x, DepotIn);
        var saved = await Legs.UpdateLineAsync(original.OrderLineId, new LegChainUpdateDto
        {
            Stamps = StampsOf(chain),
            Rows =
            [
                first,
                new LegChainRowInputDto { Origin = EndAt(x, DepotOut), Destination = EndAt(y, DepotIn) },
                new LegChainRowInputDto { Origin = EndAt(y, DepotOut), Destination = End(original.Destination) }
            ]
        });
        return (orderId, saved, original);
    }
}
