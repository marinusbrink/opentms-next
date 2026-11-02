using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.DerivedValues;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Design 67 (record-type channel) and design 68 "Live updates" against real PostgreSQL transactions: a committed Planboard
/// command notifies its trip once, after the commit, with the raising tenant's id (so only that tenant's record and type
/// groups hear it); a command refused after it loaded the trip, and a command whose surrounding transaction rolls back,
/// notify nobody and change nothing; another tenant (the shared tier in the host database) can neither reach the trip nor
/// make it notify. The rows commit in the committing tests' own tenant database. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanboardRecordChannelPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private static new Guid TenantA => PostgreSqlTestDatabases.TransportTenantAId;

    private IPlanboardAppService Planboard => GetRequiredService<IPlanboardAppService>();

    private CapturingRecordUpdateTransport Sent => GetRequiredService<CapturingRecordUpdateTransport>();

    private async Task<T> CommittedAsync<T>(Func<Task<T>> action)
    {
        using (CurrentTenant.Change(TenantA))
        using (var unitOfWork = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    private async Task DrainOutboxAsync()
    {
        var dispatcher = GetRequiredService<IOutboxDispatcher>();
        using var anonymous = AsBackgroundProcess();
        while (await dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    private Task<TransportTrip> NewTripAsync() => CommittedAsync(async () => await CreateTripAsync(await SeededTripTypeAsync(), At(6), At(18)));

    [Fact]
    public async Task A_committed_command_notifies_its_trip_once_after_the_commit_in_the_raising_tenant()
    {
        var trip = await NewTripAsync();
        var before = Sent.For(trip.Id).Count;

        PlanboardChangeDto change;
        using (CurrentTenant.Change(TenantA))
        {
            change = await Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(7, 0, 27), ConcurrencyStamp = trip.ConcurrencyStamp });
        }

        var notice = Sent.For(trip.Id).Skip(before).ShouldHaveSingleItem();
        notice.TenantId.ShouldBe(TenantA);
        notice.TargetType.ShouldBe(PlanningExecutionApplicationModule.TripTargetType);
        notice.Group.ShouldBe(RecordUpdate.GroupName(TenantA, PlanningExecutionApplicationModule.TripTargetType, trip.Id));
        notice.TypeGroup.ShouldBe(RecordUpdate.TypeGroupName(TenantA, PlanningExecutionApplicationModule.TripTargetType));
        notice.Hints!.From.ShouldBe(new DateOnly(2026, 10, 26));
        notice.Hints.Till.ShouldBe(new DateOnly(2026, 10, 27));

        var stored = await CommittedAsync(() => Operations.GetWithDetailsAsync(trip.Id));
        stored.PlannedDeparture.ShouldBe(At(7, 0, 27));
        stored.ConcurrencyStamp.ShouldBe(change.ConcurrencyStamp);
        await DrainOutboxAsync();
    }

    [Fact]
    public async Task A_command_refused_after_loading_the_trip_and_a_rolled_back_transaction_notify_nobody_and_change_nothing()
    {
        var trip = await NewTripAsync();
        var before = Sent.For(trip.Id).Count;

        using (CurrentTenant.Change(TenantA))
        {
            (await Should.ThrowAsync<BusinessException>(() => Planboard.RescheduleAsync(trip.Id,
                new PlanboardRescheduleDto { PlannedDeparture = At(8), PlannedArrival = At(7), ConcurrencyStamp = trip.ConcurrencyStamp })))
                .Code.ShouldBe(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);

            using (var outer = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Name, Value = "Rolled back", ConcurrencyStamp = trip.ConcurrencyStamp });
                await outer.RollbackAsync();
            }
        }

        Sent.For(trip.Id).Count.ShouldBe(before, "nothing leaves a unit of work that did not commit");
        var stored = await CommittedAsync(() => Operations.GetWithDetailsAsync(trip.Id));
        stored.Name.ShouldBeNull();
        stored.PlannedDeparture.ShouldBe(At(6));
        stored.ConcurrencyStamp.ShouldBe(trip.ConcurrencyStamp);
        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Negative_tenant_isolation_another_tenant_cannot_reach_the_trip_nor_make_it_notify()
    {
        var trip = await NewTripAsync();
        var before = Sent.For(trip.Id).Count;

        using (CurrentTenant.Change(SharedTenant))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8), ConcurrencyStamp = trip.ConcurrencyStamp }));
            (await Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = [trip.Id] })).Bars.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetMapAsync(trip.Id));
        }

        Sent.For(trip.Id).Count.ShouldBe(before);
        Sent.Sent.ShouldNotContain(u => u.TargetId == trip.Id && u.TenantId != TenantA, "no notice of this trip ever carries another tenant");

        using (CurrentTenant.Change(TenantA))
        {
            (await Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = [trip.Id] })).Bars.ShouldHaveSingleItem().Id.ShouldBe(trip.Id, "the control: the owner sees it");
        }
    }
}
