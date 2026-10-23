using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.Platform.DerivedValues;

/* Design 49, the parts without a database: the registry's rules, the hub group names and the watch policy (who may
 * join which group), and the request handler's idempotency. The transactional behaviour is covered against PostgreSQL
 * (DerivedValuesPostgreSqlTests). Risk class: Critical for the watch policy (authorization, tenant isolation). */
public class DerivedValueUnitTests
{
    private sealed class Calculator : IDerivedValueCalculator
    {
        public Task CalculateAsync(DerivedValueCalculationContext context) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("PlanningExecution.TransportTrip", true)]
    [InlineData("Orders.TransportOrder", true)]
    [InlineData("orders.TransportOrder", false)]
    [InlineData("Orders", false)]
    [InlineData("Orders.Transport.Order", false)]
    [InlineData("Orders.Transport-Order", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_target_type_is_Module_dot_Entity(string? targetType, bool valid)
    {
        DerivedValueOptions.IsValidTargetType(targetType).ShouldBe(valid);
    }

    [Fact]
    public void The_registry_refuses_a_malformed_or_duplicate_type_and_a_missing_permission()
    {
        var options = new DerivedValueOptions();
        options.AddCalculator<Calculator>("Orders.TransportOrder", "Orders.TransportOrders");

        Should.Throw<InvalidOperationException>(() => options.AddWatchable("Orders.TransportOrder", "X"));
        Should.Throw<ArgumentException>(() => options.AddCalculator<Calculator>("orders.order", "X"));
        Should.Throw<ArgumentException>(() => options.AddWatchable("Orders.Other", " "));

        options.Find("Orders.TransportOrder")!.CalculatorType.ShouldBe(typeof(Calculator));
        options.AddWatchable("Orders.TransportOrderLine", "Orders.TransportOrders").Find("Orders.TransportOrderLine")!.CalculatorType.ShouldBeNull();
        options.Find("Orders.Unknown").ShouldBeNull();
        options.Find(null).ShouldBeNull();
    }

    [Fact]
    public void A_group_name_carries_the_tenant_the_type_and_the_id()
    {
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        RecordUpdate.GroupName(tenant, "Orders.TransportOrder", id).ShouldBe("11111111222233334444555555555555:Orders.TransportOrder:aaaaaaaabbbbccccddddeeeeeeeeeeee");
        RecordUpdate.GroupName(null, "Orders.TransportOrder", id).ShouldBe("host:Orders.TransportOrder:aaaaaaaabbbbccccddddeeeeeeeeeeee");
    }

    private static RecordUpdateWatchPolicy Policy(bool granted, Guid? tenantId)
    {
        var options = new DerivedValueOptions().AddCalculator<Calculator>("Orders.TransportOrder", "Orders.TransportOrders");
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.IsGrantedAsync("Orders.TransportOrders").Returns(granted);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(tenantId);
        return new RecordUpdateWatchPolicy(permissions, tenant, Options.Create(options));
    }

    [Fact]
    public async Task The_watch_policy_joins_the_callers_own_tenant_groups_only()
    {
        var tenant = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var groups = (await Policy(granted: true, tenant).ResolveGroupsAsync("Orders.TransportOrder", [ids[0], ids[1], ids[0], Guid.Empty])).ShouldNotBeNull();
        groups.ShouldBe(ids.Select(id => RecordUpdate.GroupName(tenant, "Orders.TransportOrder", id)));
    }

    [Fact]
    public async Task The_watch_policy_refuses_an_unknown_type_a_missing_permission_and_too_many_records()
    {
        (await Policy(granted: true, Guid.NewGuid()).ResolveGroupsAsync("Orders.Unknown", [Guid.NewGuid()])).ShouldBeNull();
        (await Policy(granted: false, Guid.NewGuid()).ResolveGroupsAsync("Orders.TransportOrder", [Guid.NewGuid()])).ShouldBeNull();
        var tooMany = Enumerable.Range(0, DerivedValueConsts.MaxWatchPerCall + 1).Select(_ => Guid.NewGuid()).ToList();
        (await Policy(granted: true, Guid.NewGuid()).ResolveGroupsAsync("Orders.TransportOrder", tooMany)).ShouldBeNull();
    }

    private sealed class FakeState : DerivedValueState
    {
        public FakeState(long requested, long calculated)
            : base(Guid.NewGuid(), null, "Orders.TransportOrder", Guid.NewGuid(), requested, DateTime.UtcNow)
        {
            typeof(DerivedValueState).GetProperty(nameof(CalculatedVersion))!.SetValue(this, calculated);
        }
    }

    private static DerivedValuesRequestedEto Event(Guid targetId, long version) => new()
    {
        TargetType = "Orders.TransportOrder",
        Targets = [new DerivedValueTargetVersion { TargetId = targetId, Version = version }]
    };

    private static (DerivedValuesRequestedHandler Handler, IBackgroundJobManager Jobs) Handler(DerivedValueState? state, Guid? tenantId)
    {
        var store = Substitute.For<IDerivedValueStateStore>();
        store.GetListAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Guid>>()).Returns(state == null ? [] : [state]);
        var jobs = Substitute.For<IBackgroundJobManager>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(tenantId);
        return (new DerivedValuesRequestedHandler(store, jobs, tenant), jobs);
    }

    [Fact]
    public async Task The_handler_enqueues_a_job_with_ids_only_for_the_requested_version()
    {
        var tenant = Guid.NewGuid();
        var state = new FakeState(requested: 3, calculated: 2);
        var targetId = state.TargetId;
        var (handler, jobs) = Handler(state, tenant);

        await handler.HandleEventAsync(Event(targetId, 3));

        await jobs.Received(1).EnqueueAsync(
            Arg.Is<DerivedValueCalculationArgs>(a => a.TenantId == tenant && a.TargetType == "Orders.TransportOrder" && a.TargetId == targetId && a.Version == 3),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Theory]
    [InlineData(4L, 2L, 3L)]
    [InlineData(3L, 3L, 3L)]
    public async Task The_handler_enqueues_nothing_for_a_stale_or_calculated_version(long requested, long calculated, long eventVersion)
    {
        var state = new FakeState(requested, calculated);
        var (handler, jobs) = Handler(state, Guid.NewGuid());
        await handler.HandleEventAsync(Event(state.TargetId, eventVersion));
        await jobs.DidNotReceiveWithAnyArgs().EnqueueAsync<DerivedValueCalculationArgs>(default!);
    }

    [Fact]
    public async Task The_handler_enqueues_nothing_without_a_state()
    {
        var (handler, jobs) = Handler(null, Guid.NewGuid());
        await handler.HandleEventAsync(Event(Guid.NewGuid(), 1));
        await jobs.DidNotReceiveWithAnyArgs().EnqueueAsync<DerivedValueCalculationArgs>(default!);
    }
}
