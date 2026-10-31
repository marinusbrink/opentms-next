using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.DerivedValues;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.RecordUpdates;

/// <summary>
/// Design 67 (the record-type channel), the notifier with ABP's real unit-of-work manager on SQLite: notices of one unit
/// of work are coalesced per record (one notice, the last one wins), flushed only after the commit, never on a rollback,
/// sent at once outside a unit of work, and each carries the tenant that raised it so its type group is that tenant's.
/// The transactional rollback against PostgreSQL is covered by PlanboardRecordChannelPostgreSqlTests.
/// Risk class: Critical (Platform; a wrong tenant leaks another tenant's ids).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RecordUpdateNotifierTests : OpenTmsEntityFrameworkCoreTestBase
{
    private const string TripType = "PlanningExecution.TransportTrip";
    private const string LegType = "PlanningExecution.Leg";

    private IRecordUpdateNotifier Notifier => GetRequiredService<IRecordUpdateNotifier>();

    private CapturingRecordUpdateTransport Sent => GetRequiredService<CapturingRecordUpdateTransport>();

    private IUnitOfWorkManager UnitOfWorkManager => GetRequiredService<IUnitOfWorkManager>();

    private ICurrentTenant CurrentTenant => GetRequiredService<ICurrentTenant>();

    [Fact]
    public async Task Notices_of_one_unit_of_work_are_coalesced_per_record_the_last_wins_and_leave_after_the_commit()
    {
        var trip = Guid.NewGuid();
        var leg = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        using (CurrentTenant.Change(tenant))
        using (var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 26)));
            await Notifier.NotifyAsync(LegType, leg, new RecordUpdateHints(Removed: true));
            await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 28)), version: 3);

            Sent.For(trip).ShouldBeEmpty("nothing leaves before the commit");
            Sent.For(leg).ShouldBeEmpty();
            await uow.CompleteAsync();
        }

        var tripNotice = Sent.For(trip).ShouldHaveSingleItem("one notice per record per unit of work");
        tripNotice.Hints.ShouldBe(new RecordUpdateHints(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 28)), "the last notice wins");
        tripNotice.Version.ShouldBe(3);
        tripNotice.TenantId.ShouldBe(tenant);
        Sent.For(leg).ShouldHaveSingleItem().Hints!.Removed.ShouldBeTrue();

        var order = Sent.Sent.Where(u => u.TargetId == trip || u.TargetId == leg).Select(u => u.TargetId).ToList();
        order.ShouldBe([trip, leg], "in the order each record was first touched");
    }

    [Fact]
    public async Task A_child_unit_of_work_shares_its_parents_pending_set_and_commit()
    {
        var trip = Guid.NewGuid();
        using (var outer = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            using (var inner = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions()))
            {
                await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(Removed: false));
                await inner.CompleteAsync();
            }

            await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(Removed: true));
            Sent.For(trip).ShouldBeEmpty("the inner completion is not the commit");
            await outer.CompleteAsync();
        }

        Sent.For(trip).ShouldHaveSingleItem().Hints!.Removed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rolled_back_unit_of_work_sends_nothing()
    {
        var trip = Guid.NewGuid();
        using (var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(Removed: false));
            await uow.RollbackAsync();
        }

        using (UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(Removed: true));
            /* Disposed without completing: an exception left the method. */
        }

        Sent.For(trip).ShouldBeEmpty();
    }

    [Fact]
    public async Task Outside_a_unit_of_work_a_notice_is_sent_at_once_with_and_without_hints()
    {
        var trip = Guid.NewGuid();
        await Notifier.NotifyAsync(TripType, trip);
        await Notifier.NotifyAsync(TripType, trip, new RecordUpdateHints(Removed: true));

        var sent = Sent.For(trip);
        sent.Count.ShouldBe(2, "no unit of work, nothing to coalesce");
        sent[0].Hints.ShouldBeNull();
        sent[1].Hints!.Removed.ShouldBeTrue();
    }

    [Fact]
    public async Task Negative_tenant_isolation_a_notice_raised_in_tenant_A_reaches_only_tenant_As_groups()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tripA = Guid.NewGuid();
        var tripB = Guid.NewGuid();

        using (CurrentTenant.Change(tenantA))
        using (var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            await Notifier.NotifyAsync(TripType, tripA, new RecordUpdateHints(Removed: false));
            using (CurrentTenant.Change(tenantB))
            {
                /* The same unit of work, another tenant: keyed per tenant, never merged with tenant A's notice. */
                await Notifier.NotifyAsync(TripType, tripB, new RecordUpdateHints(Removed: false));
            }

            await uow.CompleteAsync();
        }

        var a = Sent.For(tripA).ShouldHaveSingleItem();
        a.TenantId.ShouldBe(tenantA);
        a.Group.ShouldBe(RecordUpdate.GroupName(tenantA, TripType, tripA));
        a.TypeGroup.ShouldBe(RecordUpdate.TypeGroupName(tenantA, TripType));
        a.TypeGroup.ShouldNotBe(RecordUpdate.TypeGroupName(tenantB, TripType));

        var b = Sent.For(tripB).ShouldHaveSingleItem();
        b.TenantId.ShouldBe(tenantB);
        b.TypeGroup.ShouldBe(RecordUpdate.TypeGroupName(tenantB, TripType));
    }
}
