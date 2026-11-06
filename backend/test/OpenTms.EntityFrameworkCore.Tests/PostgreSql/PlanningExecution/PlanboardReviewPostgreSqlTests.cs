using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Orders.Events;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.RecordUpdates;
using OpenTms.Platform.ReferenceData;
using OpenTms.DerivedValues;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>Fails the derived-value request a test armed, inside the command's transaction: a failure after the step that came before it.</summary>
public class ArmedSchedulerFailure
{
    public string? TargetType { get; set; }

    public string? Reason { get; set; }

    public bool Hit { get; private set; }

    public void ThrowIfArmed(string targetType, string? reason)
    {
        if (TargetType == targetType && Reason == reason)
        {
            Hit = true;
            throw new InvalidOperationException($"Forced failure at {targetType} {reason}.");
        }
    }
}

public class FailingDerivedValueScheduler : IDerivedValueScheduler
{
    private readonly DerivedValueScheduler _real;
    private readonly ArmedSchedulerFailure _failure;

    public FailingDerivedValueScheduler(DerivedValueScheduler real, ArmedSchedulerFailure failure)
    {
        _real = real;
        _failure = failure;
    }

    public Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null)
    {
        _failure.ThrowIfArmed(targetType, reason);
        return _real.RequestAsync(targetType, targetId, reason);
    }

    public Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null)
    {
        _failure.ThrowIfArmed(targetType, reason);
        return _real.RequestManyAsync(targetType, targetIds, reason);
    }
}

/// <summary>
/// Design 74 against real PostgreSQL, both tenant tiers. §7's atomicity across the Orders and Planning contexts: a failure after the
/// Orders step leaves nothing, in the database or the outbox; the commands' SQL against the real schema (the unique keys, soft
/// deletes, owned freight); and constitution rule 1: every new read and write refuses another tenant's data. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanboardReviewPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private ILegAppService Legs => GetRequiredService<ILegAppService>();

    private IPlanboardAppService Planboard => GetRequiredService<IPlanboardAppService>();

    private ITransportTripAppService Trips => GetRequiredService<ITransportTripAppService>();

    private IPlanboardOrderAppService OrderFields => GetRequiredService<IPlanboardOrderAppService>();

    private IPlanboardOrderLineFieldAppService LineFields => GetRequiredService<IPlanboardOrderLineFieldAppService>();

    private ArmedSchedulerFailure Failure => GetRequiredService<ArmedSchedulerFailure>();

    private PlanboardReviewEventRecorder Events => GetRequiredService<PlanboardReviewEventRecorder>();

    private CapturingRecordUpdateTransport Sent => GetRequiredService<CapturingRecordUpdateTransport>();

    protected override void AfterAddApplication(IServiceCollection services)
    {
        services.AddSingleton<ArmedSchedulerFailure>();
        services.Replace(ServiceDescriptor.Transient<IDerivedValueScheduler, FailingDerivedValueScheduler>());
    }

    private static Guid Committing(bool ownDatabase) => ownDatabase ? PostgreSqlTestDatabases.TransportTenantAId : PostgreSqlTestDatabases.TransportSharedTenantId;

    private async Task<T> CommittedAsync<T>(Guid tenantId, Func<Task<T>> action)
    {
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    private async Task DrainOutboxAsync(bool ownDatabase)
    {
        var dispatcher = GetRequiredService<IOutboxDispatcher>();
        using var anonymous = AsBackgroundProcess();
        Guid? database = ownDatabase ? PostgreSqlTestDatabases.TransportTenantAId : null;
        while (await dispatcher.DispatchAsync(database) > 0)
        {
        }
    }

    private sealed record Stored(int LineCount, decimal LineQuantity, double? LineWeight, string OrderStamp, string Legs, string TripLegs);

    private Task<Stored> StoredAsync(Guid tenantId, Guid orderId, Guid tripId) => CommittedAsync(tenantId, async () =>
    {
        var order = await Orders.GetAsync(orderId);
        var legs = await LegRepository.GetListAsync(l => l.OrderId == orderId);
        var trip = await Operations.GetWithDetailsAsync(tripId);
        return new Stored(order.Lines.Count, order.Lines.Sum(l => l.Quantity), order.Lines.Sum(l => l.WeightKg), order.ConcurrencyStamp,
            string.Join(";", legs.OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Quantity}:{l.WeightKg}:{l.TripId}:{l.ConcurrencyStamp}")),
            string.Join(";", trip.Stops.SelectMany(s => s.Legs).Select(l => l.LegId).OrderBy(id => id)));
    });

    /// <summary>A committed confirmed order of two legs, both on one committed trip; the outbox drained. Reshaped: the first leg was split at an address before the assignment.</summary>
    private async Task<(Guid OrderId, Guid ChosenLegId, Guid TripId)> CommittedSceneAsync(Guid tenantId, bool ownDatabase, bool reshaped)
    {
        var scene = await CommittedAsync(tenantId, async () =>
        {
            /* These rows commit: their codes must not clash with an earlier test's or an earlier run's. */
            var suffix = OpenTms.TransportRun.TransportRunFixture.NewSuffix();
            var a = await PlaceAsync("SPA" + suffix, "Split A", 52.0, 5.0);
            var b = await PlaceAsync("SPB" + suffix, "Split B", 52.5, 5.5);
            var c = await PlaceAsync("SPC" + suffix, "Split C", 52.6, 5.6);
            var x = await PlaceAsync("SPX" + suffix, "Split X", 52.2, 5.2);
            var orderId = (await OrderAsync(a.RelationId, LineOf(3, 100, StopAt(a.RelationId, a.AddressId, Load), StopAt(b.RelationId, b.AddressId, Unload), StopAt(c.RelationId, c.AddressId, Unload)))).Id;
            var legs = await ConfirmAsync(orderId);
            if (reshaped)
            {
                await Legs.SplitAtAddressAsync(legs[0].Id, new LegSplitAtAddressDto { RelationId = x.RelationId, RelationAddressId = x.AddressId, ArrivalWaypointTypeId = DepotIn, DepartureWaypointTypeId = DepotOut });
                await FlushAsync();
                legs = (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Position).ToList();
            }

            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            await Operations.AssignAsync(trip, legs, allowOutsideWindow: true, afterStopId: null, moving: false);
            await FlushAsync();
            return (orderId, legs[0].Id, trip.Id);
        });
        await DrainOutboxAsync(ownDatabase);
        return scene;
    }

    [Theory]
    [InlineData(true, false, PlanningExecutionApplicationModule.LegTargetType, "OrderLineSplit")]
    [InlineData(false, false, PlanningExecutionApplicationModule.LegTargetType, "OrderLineSplit")]
    [InlineData(true, false, PlanningExecutionApplicationModule.TripTargetType, "LegsAssigned")]
    [InlineData(false, false, PlanningExecutionApplicationModule.TripTargetType, "LegsAssigned")]
    [InlineData(true, true, PlanningExecutionApplicationModule.LegTargetType, "OrderLineSplit")]
    [InlineData(false, true, PlanningExecutionApplicationModule.LegTargetType, "OrderLineSplit")]
    [InlineData(true, true, PlanningExecutionApplicationModule.TripTargetType, "LegsAssigned")]
    public async Task A_failure_after_the_orders_step_stores_nothing_in_either_context_and_publishes_nothing(bool ownDatabase, bool reshaped, string failingType, string failingReason)
    {
        var tenantId = Committing(ownDatabase);
        var (orderId, chosen, tripId) = await CommittedSceneAsync(tenantId, ownDatabase, reshaped);
        var before = await StoredAsync(tenantId, orderId, tripId);
        var notices = Sent.For(tripId).Count;
        Events.Clear();
        Failure.TargetType = failingType;
        Failure.Reason = failingReason;

        using (CurrentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => Legs.SplitOrderLineAsync(chosen, new LegSplitOrderLineDto { Quantity = 1 }));
        }

        Failure.Hit.ShouldBeTrue("the failure must have happened, after the Orders step");
        Failure.TargetType = null;
        (await StoredAsync(tenantId, orderId, tripId)).ShouldBe(before, "the order's line and every leg are as they were: one transaction");
        Sent.For(tripId).Count.ShouldBe(notices, "nothing leaves a unit of work that did not commit");
        await DrainOutboxAsync(ownDatabase);
        Events.Of<TransportOrderLinesChangedEto>().ShouldNotContain(e => e.OrderId == orderId, "the fact was written in the same transaction, so it went too");

        /* The control: the same command without the failure stores both halves, and the outbox's delivery changes nothing more. */
        SplitDone done;
        using (CurrentTenant.Change(tenantId))
        {
            var result = await Legs.SplitOrderLineAsync(chosen, new LegSplitOrderLineDto { Quantity = 1 });
            done = new SplitDone(result.NewLineId, result.NewLegs.Count);
        }

        var after = await StoredAsync(tenantId, orderId, tripId);
        after.LineCount.ShouldBe(2);
        after.LineQuantity.ShouldBe(3);
        after.LineWeight!.Value.ShouldBe(100, 0.000001);
        done.NewLegs.ShouldBeGreaterThan(0);
        await DrainOutboxAsync(ownDatabase);
        Events.Of<TransportOrderLinesChangedEto>().ShouldContain(e => e.OrderId == orderId && e.LineIds.Contains(done.NewLineId));
        (await StoredAsync(tenantId, orderId, tripId)).Legs.ShouldBe(after.Legs, "the later delivery changes nothing");
    }

    private sealed record SplitDone(Guid NewLineId, int NewLegs);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stale_stamps_conflict_on_real_transactions_and_leave_the_row_alone(bool ownDatabase)
    {
        var tenantId = Committing(ownDatabase);
        var (orderId, chosen, tripId) = await CommittedSceneAsync(tenantId, ownDatabase, reshaped: false);
        var (stale, lineId) = await CommittedAsync(tenantId, async () => { var o = await Orders.GetAsync(orderId); return (o.ConcurrencyStamp, o.Lines[0].Id!.Value); });
        var tripStale = await CommittedAsync(tenantId, async () => (await Operations.GetWithDetailsAsync(tripId)).ConcurrencyStamp);

        using (CurrentTenant.Change(tenantId))
        {
            var saved = await OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.CustomerReference, Value = "R1", ConcurrencyStamp = stale });
            saved.ConcurrencyStamp.ShouldNotBe(stale);
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.CustomerReference, Value = "R2", ConcurrencyStamp = stale }));
            var unit = await CommittedAsync(tenantId, () => Run.TransportUnitAsync("Crate"));
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = unit, ConcurrencyStamp = stale }));
            (await CommittedAsync(tenantId, async () => (await Orders.GetAsync(orderId)).Lines[0].TransportUnitId)).ShouldNotBe(unit, "a refused save leaves the freight: the line's row and the order's row are one transaction");
            var line = await LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = unit, ConcurrencyStamp = saved.ConcurrencyStamp });
            line.TransportUnitName.ShouldBe("Crate");
            line.ConcurrencyStamp.ShouldNotBe(saved.ConcurrencyStamp);

            var moved = await Planboard.SetFieldAsync(tripId, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Remarks, Value = "moved", ConcurrencyStamp = tripStale });
            moved.ConcurrencyStamp.ShouldNotBe(tripStale);
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.AssignOrderAsync(tripId, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = tripStale, AllowOutsideWindow = true }));
        }

        var reference = await CommittedAsync(tenantId, async () => (await Orders.GetAsync(orderId)).CustomerReference);
        reference.ShouldBe("R1");
        await DrainOutboxAsync(ownDatabase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_commands_run_against_the_real_schema(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var a = await PlaceAsync("SCA", "A", 52.0, 5.0);
            var b = await PlaceAsync("SCB", "B", 52.5, 5.5);
            var x = await PlaceAsync("SCX", "X", 52.2, 5.2);
            var y = await PlaceAsync("SCY", "Y", 52.3, 5.3);
            var orderId = (await OrderAsync(a.RelationId, LineOf(10, 1000, StopAt(a.RelationId, a.AddressId, Load), StopAt(b.RelationId, b.AddressId, Unload)))).Id;
            var leg = (await ConfirmAsync(orderId)).Single();
            var lineId = leg.OrderLineId;

            LegChainEndInputDto EndAt((Guid RelationId, Guid AddressId) p, Guid type) => new() { WaypointTypeId = type, RelationId = p.RelationId, RelationAddressId = p.AddressId, PlannedDate = Day };
            static LegChainEndInputDto End(LegEndSnapshotDto e) => new() { WaypointTypeId = e.WaypointTypeId, RelationId = e.RelationId, RelationAddressId = e.RelationAddressId, PlannedDate = e.PlannedDate, WindowFrom = e.WindowFrom, WindowTill = e.WindowTill, Reference = e.Reference };
            static LegChainRowInputDto Row(LegDto l) => new() { Id = l.Id, Remarks = l.Remarks, Origin = End(l.Origin), Destination = End(l.Destination) };
            static List<LegChainStampDto> Stamps(LegChainDto c) => c.Rows.Select(r => new LegChainStampDto { Id = r.Leg.Id, ConcurrencyStamp = r.Leg.ConcurrencyStamp }).ToList();

            /* Manage legs: add two places, remove the last (soft delete), add the place again under the same synthetic key. */
            var read = await Legs.GetLineAsync(lineId);
            var first = Row(read.Rows[0].Leg);
            var outer = first.Destination;
            first.Destination = EndAt(x, DepotIn);
            var three = await Legs.UpdateLineAsync(lineId, new LegChainUpdateDto
            {
                Stamps = Stamps(read),
                Rows = [first, new LegChainRowInputDto { Origin = EndAt(x, DepotOut), Destination = EndAt(y, DepotIn) }, new LegChainRowInputDto { Origin = EndAt(y, DepotOut), Destination = outer }]
            });
            await FlushAsync();
            three.Rows.Count.ShouldBe(3);

            var second = Row(three.Rows[1].Leg);
            second.Destination = outer;
            var two = await Legs.UpdateLineAsync(lineId, new LegChainUpdateDto { Stamps = Stamps(three), Rows = [Row(three.Rows[0].Leg), second] });
            await FlushAsync();
            two.Rows.Count.ShouldBe(2);

            var again = Row(two.Rows[1].Leg);
            again.Destination = EndAt(y, DepotIn);
            var back = await Legs.UpdateLineAsync(lineId, new LegChainUpdateDto { Stamps = Stamps(two), Rows = [Row(two.Rows[0].Leg), again, new LegChainRowInputDto { Origin = EndAt(y, DepotOut), Destination = outer }] });
            await FlushAsync();
            back.Rows.Count.ShouldBe(3, "the unique key counts live legs only");

            /* The split of the reshaped line, on trips. */
            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            await Operations.AssignAsync(trip, await LegRepository.GetListAsync(l => l.Id == back.Rows[1].Leg.Id), allowOutsideWindow: true, afterStopId: null, moving: false);
            await FlushAsync();
            var split = await Legs.SplitOrderLineAsync(back.Rows[0].Leg.Id, new LegSplitOrderLineDto { Quantity = 3.3333m });
            await FlushAsync();
            split.NewLegs.Count.ShouldBe(3);
            split.NewLegs.ShouldAllBe(l => l.Quantity == 3.3333m);
            split.RemainingLegs.ShouldAllBe(l => l.Quantity == 6.6667m);
            split.NewLegs.Single(l => l.Position == 2).TripId.ShouldBe(trip.Id);
            var order = await Orders.GetAsync(orderId);
            order.Lines.Sum(l => l.Quantity).ShouldBe(10);
            order.Lines.Sum(l => l.WeightKg!.Value).ShouldBe(1000, 0.000001);

            /* A later delivery of the fact changes nothing. */
            var stored = string.Join(";", (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Quantity}:{l.TripId}"));
            await Handler.HandleEventAsync(new TransportOrderLinesChangedEto { OrderId = orderId });
            await FlushAsync();
            string.Join(";", (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Quantity}:{l.TripId}")).ShouldBe(stored);

            /* Dragging the order: the movable legs go on a new trip, the old trip is recomposed. */
            var target = await CreateTripAsync(await SeededTripTypeAsync());
            var preview = await Planboard.GetAssignOrderPreviewAsync(target.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId });
            preview.Legs.Count.ShouldBe(6);
            preview.PulledFrom.ShouldHaveSingleItem().TripId.ShouldBe(trip.Id);
            var moved = await Planboard.AssignOrderAsync(target.Id, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = target.ConcurrencyStamp, AllowOutsideWindow = true });
            await FlushAsync();
            moved.Moved.Count.ShouldBe(6);
            (await Operations.LegsOnTripAsync(trip.Id)).ShouldBeEmpty();

            /* A generator-owned line through the regeneration, with both Orders field endpoints on the owned freight. */
            var plain = (await OrderAsync(a.RelationId, LineOf(3, 100, StopAt(a.RelationId, a.AddressId, Load), StopAt(b.RelationId, b.AddressId, Unload)))).Id;
            var plainLeg = (await ConfirmAsync(plain)).Single();
            var plainSplit = await Legs.SplitOrderLineAsync(plainLeg.Id, new LegSplitOrderLineDto { Quantity = 1 });
            await FlushAsync();
            plainSplit.RemainingLegs.ShouldHaveSingleItem().Quantity.ShouldBe(2);
            plainSplit.NewLegs.ShouldHaveSingleItem().Quantity.ShouldBe(1);

            var dto = await Orders.GetAsync(plain);
            var saved = await OrderFields.UpdateFieldAsync(plain, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.InternalRemarks, Value = "note", ConcurrencyStamp = dto.ConcurrencyStamp });
            await FlushAsync();
            saved.InternalRemarks.ShouldBe("note");
            var crate = await Run.TransportUnitAsync("Crate");
            await FlushAsync();
            var line = await LineFields.UpdateLineFieldAsync(plain, plainSplit.NewLineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = crate, ConcurrencyStamp = saved.ConcurrencyStamp });
            await FlushAsync();
            line.TransportUnitId.ShouldBe(crate);
            (await Orders.GetAsync(plain)).Lines.Single(l => l.Id == plainSplit.NewLineId).Quantity.ShouldBe(1, "the owned freight kept its other fields");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Negative_tenant_isolation_another_tenant_reaches_no_new_read_or_write(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var a = await PlaceAsync("ISA", "A", 52.0, 5.0);
            var b = await PlaceAsync("ISB", "B", 52.5, 5.5);
            var orderId = (await OrderAsync(a.RelationId, LineOf(10, 1000, StopAt(a.RelationId, a.AddressId, Load), StopAt(b.RelationId, b.AddressId, Unload)))).Id;
            var leg = (await ConfirmAsync(orderId)).Single();
            var lineId = leg.OrderLineId;
            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            await Operations.AssignAsync(trip, [leg], allowOutsideWindow: true, afterStopId: null, moving: false);
            await FlushAsync();
            var free = await CreateTripAsync(await SeededTripTypeAsync());
            var manual = await Trips.AddStopAsync(free.Id, new AddManualStopDto { WaypointTypeId = Load, RelationId = a.RelationId, RelationAddressId = a.AddressId });
            await FlushAsync();
            var order = await Orders.GetAsync(orderId);
            var chain = await Legs.GetLineAsync(lineId);
            var stamps = chain.Rows.Select(r => new LegChainStampDto { Id = r.Leg.Id, ConcurrencyStamp = r.Leg.ConcurrencyStamp }).ToList();
            var ownerStamp = order.ConcurrencyStamp;
            var ownerUnit = Pallet;

            (Guid RelationId, Guid AddressId) foreign;
            Guid foreignUnit;
            using (CurrentTenant.Change(otherId))
            {
                foreign = await PlaceAsync("ISF", "Foreign", 52.1, 5.1);
                foreignUnit = await Run.TransportUnitAsync("ForeignUnit");
                await FlushAsync();
                var own = await CreateTripAsync(await SeededTripTypeAsync());
                await FlushAsync();
                var tripsBefore = await TripRepository.GetCountAsync();

                /* Reads. */
                await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetLineAsync(lineId));
                await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetAssignOrderPreviewAsync(trip.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId }));
                var preview = await Planboard.GetAssignOrderPreviewAsync(own.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId });
                preview.Legs.ShouldBeEmpty();
                preview.PulledFrom.ShouldBeEmpty();
                preview.Stuck.ShouldBeEmpty();
                preview.MovableCount.ShouldBe(0);

                /* Writes. */
                await Should.ThrowAsync<EntityNotFoundException>(() => Legs.UpdateLineAsync(lineId, new LegChainUpdateDto { Stamps = stamps, Rows = [new LegChainRowInputDto { Id = leg.Id }] }));
                await Should.ThrowAsync<EntityNotFoundException>(() => Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 1 }));
                await Should.ThrowAsync<EntityNotFoundException>(() => Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "x", ConcurrencyStamp = leg.ConcurrencyStamp }));
                await Should.ThrowAsync<EntityNotFoundException>(() => GetRequiredService<IOrderLineSplitter>().SplitAsync(orderId, lineId, 1));
                await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.AssignOrderAsync(trip.Id, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = trip.ConcurrencyStamp }));
                (await Should.ThrowAsync<BusinessException>(() => Planboard.AssignOrderAsync(own.Id, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = own.ConcurrencyStamp, AllowOutsideWindow = true })))
                    .Code.ShouldBe(PlanningExecutionErrorCodes.OrderHasNoLegs);
                (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { PlannedDeparture = At(9), OrderId = orderId })))
                    .Code.ShouldBe(PlanningExecutionErrorCodes.OrderHasNoLegs);
                (await TripRepository.GetCountAsync()).ShouldBe(tripsBefore, "a refused drop creates no trip");
                await Should.ThrowAsync<EntityNotFoundException>(() => OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.InternalRemarks, Value = "x", ConcurrencyStamp = ownerStamp }));
                await Should.ThrowAsync<EntityNotFoundException>(() => LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = foreignUnit, ConcurrencyStamp = ownerStamp }));
                await Should.ThrowAsync<EntityNotFoundException>(() => Trips.UpdateStopAsync(free.Id, manual.Stops[0].Id, new UpdateStopDto { Remarks = "x" }));
                (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, OrderId = orderId })).Rows.ShouldBeEmpty();
            }

            /* The owner cannot name the other tenant's rows either. */
            var row = new LegChainRowInputDto
            {
                Id = leg.Id,
                Origin = new LegChainEndInputDto { WaypointTypeId = leg.OriginWaypointTypeId, RelationId = a.RelationId, RelationAddressId = a.AddressId, PlannedDate = Day },
                Destination = new LegChainEndInputDto { WaypointTypeId = DepotIn, RelationId = foreign.RelationId, RelationAddressId = foreign.AddressId, PlannedDate = Day }
            };
            var unplanned = await Trips.UnassignLegsAsync(trip.Id, new UnassignLegsDto { LegIds = [leg.Id] });
            await FlushAsync();
            unplanned.Stops.ShouldBeEmpty();
            var current = await Legs.GetLineAsync(lineId);
            var next = new LegChainRowInputDto
            {
                Origin = new LegChainEndInputDto { WaypointTypeId = DepotOut, RelationId = foreign.RelationId, RelationAddressId = foreign.AddressId, PlannedDate = Day },
                Destination = new LegChainEndInputDto { WaypointTypeId = leg.DestinationWaypointTypeId, RelationId = b.RelationId, RelationAddressId = b.AddressId, PlannedDate = Day }
            };
            (await Should.ThrowAsync<BusinessException>(() => Legs.UpdateLineAsync(lineId, new LegChainUpdateDto
            {
                Stamps = current.Rows.Select(r => new LegChainStampDto { Id = r.Leg.Id, ConcurrencyStamp = r.Leg.ConcurrencyStamp }).ToList(),
                Rows = [row, next]
            }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            (await Should.ThrowAsync<BusinessException>(() => LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = foreignUnit, ConcurrencyStamp = ownerStamp })))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(free.Id, manual.Stops[0].Id, new UpdateStopDto { RelationId = foreign.RelationId })))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

            /* The control: nothing of the owner's moved, and the owner reaches it all. */
            var stored = await Orders.GetAsync(orderId);
            stored.ConcurrencyStamp.ShouldBe(ownerStamp);
            stored.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(10);
            stored.Lines[0].TransportUnitId.ShouldBe(ownerUnit);
            (await Legs.GetLineAsync(lineId)).Rows.ShouldHaveSingleItem().Leg.Quantity.ShouldBe(10);
            (await Planboard.GetAssignOrderPreviewAsync(free.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId })).MovableCount.ShouldBe(1);
        });
    }
}
