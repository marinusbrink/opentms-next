using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.Platform.DerivedValues;

/* Design 67 (the record-type channel), the parts without a database: the type group's name, and who may join it — a
 * registered type only, only with its watch permission, always in the caller's server-side tenant whatever the client
 * sends as the type. Risk class: Critical (authorization, tenant isolation). */
public class RecordTypeChannelPolicyTests
{
    private const string TripType = "PlanningExecution.TransportTrip";
    private const string AvailabilityType = "PlanningExecution.ResourceAvailability";
    private const string TripPermission = "PlanningExecution.TransportTrips";
    private const string AvailabilityPermission = "PlanningExecution.ResourceAvailability";

    private sealed class Calculator : IDerivedValueCalculator
    {
        public Task CalculateAsync(DerivedValueCalculationContext context) => Task.CompletedTask;
    }

    private static RecordUpdateWatchPolicy Policy(Guid? tenantId, bool tripGranted = true, bool availabilityGranted = true)
    {
        var options = new DerivedValueOptions()
            .AddCalculator<Calculator>(TripType, TripPermission)
            .AddWatchable(AvailabilityType, AvailabilityPermission);
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.IsGrantedAsync(TripPermission).Returns(tripGranted);
        permissions.IsGrantedAsync(AvailabilityPermission).Returns(availabilityGranted);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(tenantId);
        return new RecordUpdateWatchPolicy(permissions, tenant, Options.Create(options));
    }

    [Fact]
    public void The_type_group_name_is_tenant_type_star_and_differs_from_every_record_group()
    {
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
        RecordUpdate.TypeGroupName(tenant, TripType).ShouldBe("11111111222233334444555555555555:PlanningExecution.TransportTrip:*");
        RecordUpdate.TypeGroupName(null, TripType).ShouldBe("host:PlanningExecution.TransportTrip:*");

        var update = new RecordUpdate(tenant, TripType, Guid.NewGuid(), null, null);
        update.TypeGroup.ShouldBe(RecordUpdate.TypeGroupName(tenant, TripType));
        update.Group.ShouldNotBe(update.TypeGroup);
    }

    [Fact]
    public async Task The_type_group_carries_the_server_tenant_of_the_caller()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        (await Policy(tenantA).ResolveTypeGroupAsync(TripType)).ShouldBe(RecordUpdate.TypeGroupName(tenantA, TripType));
        (await Policy(tenantB).ResolveTypeGroupAsync(TripType)).ShouldBe(RecordUpdate.TypeGroupName(tenantB, TripType));
        (await Policy(null).ResolveTypeGroupAsync(TripType)).ShouldBe(RecordUpdate.TypeGroupName(null, TripType));
        (await Policy(tenantA).ResolveTypeGroupAsync(AvailabilityType)).ShouldBe(RecordUpdate.TypeGroupName(tenantA, AvailabilityType), "a watchable type without a calculator");
    }

    [Theory]
    [InlineData("PlanningExecution.Unknown")]
    [InlineData("planningexecution.transporttrip")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unregistered_type_is_refused(string? targetType)
    {
        (await Policy(Guid.NewGuid()).ResolveTypeGroupAsync(targetType)).ShouldBeNull();
    }

    [Fact]
    public async Task A_type_without_its_watch_permission_is_refused_and_another_type_still_works()
    {
        var tenant = Guid.NewGuid();
        (await Policy(tenant, tripGranted: false).ResolveTypeGroupAsync(TripType)).ShouldBeNull();
        (await Policy(tenant, tripGranted: false).ResolveTypeGroupAsync(AvailabilityType)).ShouldBe(RecordUpdate.TypeGroupName(tenant, AvailabilityType));
        (await Policy(tenant, availabilityGranted: false).ResolveTypeGroupAsync(AvailabilityType)).ShouldBeNull();
    }

    [Theory]
    [InlineData("99999999888877776666555555555555:PlanningExecution.TransportTrip")]
    [InlineData("PlanningExecution.TransportTrip:*")]
    [InlineData("host:PlanningExecution.TransportTrip")]
    [InlineData("PlanningExecution.TransportTrip:99999999888877776666555555555555")]
    public async Task Negative_tenant_isolation_a_client_cannot_smuggle_another_tenant_into_the_group_name(string crafted)
    {
        /* Only the registered type name resolves; anything carrying a tenant part or a wildcard is not a registered type. */
        (await Policy(Guid.NewGuid()).ResolveTypeGroupAsync(crafted)).ShouldBeNull();
        (await Policy(Guid.NewGuid()).ResolveGroupsAsync(crafted, [Guid.NewGuid()])).ShouldBeNull();
    }
}
