using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Security.Claims;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>The permissions the current step of a test holds; null is everything, so the scene can be built through the real services.</summary>
public class PlanboardReviewGrants
{
    public HashSet<string>? Only { get; set; }

    public bool IsGranted(string name) => Only == null || Only.Contains(name);
}

/// <summary>Feeds ABP's real permission checker from <see cref="PlanboardReviewGrants"/>.</summary>
public class PlanboardReviewGrantsProvider : PermissionValueProvider
{
    private readonly PlanboardReviewGrants _grants;

    public PlanboardReviewGrantsProvider(IPermissionStore store, PlanboardReviewGrants grants)
        : base(store)
    {
        _grants = grants;
    }

    public override string Name => "PlanboardReviewTest";

    public override Task<PermissionGrantResult> CheckAsync(PermissionValueCheckContext context)
        => Task.FromResult(_grants.IsGranted(context.Permission.Name) ? PermissionGrantResult.Granted : PermissionGrantResult.Undefined);

    public override Task<MultiplePermissionGrantResult> CheckAsync(PermissionValuesCheckContext context)
    {
        var result = new MultiplePermissionGrantResult();
        foreach (var permission in context.Permissions)
        {
            result.Result[permission.Name] = _grants.IsGranted(permission.Name) ? PermissionGrantResult.Granted : PermissionGrantResult.Undefined;
        }

        return Task.FromResult(result);
    }
}

/// <summary>
/// Design 74's two new reads on SQLite, with ABP's real authorization and permission checker: <c>GET leg/{id}/line-totals</c> (§7, the base of the
/// split dialog) and <c>GET planboard-order/{id}</c> (§6, the Orders pane's stale retry). Risk class: Critical for permission, feature
/// and tenant isolation (proven on PostgreSQL for both tiers in
/// <see cref="OpenTms.PostgreSql.PlanningExecution.PlanboardReviewPostgreSqlTests"/>), High for what they return.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewReadsTests : PlanboardReviewTestBase
{
    private PlanboardReviewGrants Grants => GetRequiredService<PlanboardReviewGrants>();

    private IOrderLineSplitter Splitter => GetRequiredService<IOrderLineSplitter>();

    protected override void AfterAddApplication(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Transient<IAuthorizationService, AbpAuthorizationService>());
        services.Replace(ServiceDescriptor.Transient<IAbpAuthorizationService, AbpAuthorizationService>());
        services.Replace(ServiceDescriptor.Transient<IMethodInvocationAuthorizationService, MethodInvocationAuthorizationService>());
        services.Replace(ServiceDescriptor.Transient<IPermissionChecker, PermissionChecker>());
        services.AddSingleton<PlanboardReviewGrants>();
        services.AddTransient<PlanboardReviewGrantsProvider>();
        services.Configure<AbpPermissionOptions>(o => o.ValueProviders.Add<PlanboardReviewGrantsProvider>());
    }

    /// <summary>The tenant and a user of it: ABP's real checker takes the side of a tenant-only permission from the principal's claims.</summary>
    private IDisposable AsMemberOf(Guid tenantId)
    {
        var principal = GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AbpClaimTypes.TenantId, tenantId.ToString())], "test")));
        var tenant = CurrentTenant.Change(tenantId);
        return new DisposeAction(() =>
        {
            tenant.Dispose();
            principal.Dispose();
        });
    }

    private static bool IsFeatureRefusal(AbpAuthorizationException refusal) => refusal.Code?.StartsWith("Volo.Feature:", StringComparison.Ordinal) == true;

    private async Task<Guid> OrderAsync(FleetSet f, decimal quantity = 10, double weight = 1000)
    {
        var input = LineOf(quantity, weight, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day));
        input.Colli = 20m;
        input.VolumeM3 = 5;
        input.LoadingMeters = 6.8;
        input.LoadFactor = 0.5;
        input.Epq = 16;
        return await ConfirmedOrderAsync(f.CustomerA.RelationId, input);
    }

    /* Line totals: what they return. */

    [Fact]
    public async Task On_a_parallel_reshaped_line_the_totals_are_the_lines_not_the_legs()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.SplitLineAsync(leg.Id, new LegSplitLineDto { Quantity = 3 });
        var parts = await LegRowsAsync(orderId);
        parts.Count.ShouldBe(2);
        parts.Min(l => l.Quantity).ShouldBe(3, "the control: no leg carries the line's total");
        parts.Max(l => l.Quantity).ShouldBe(7, "the control: no leg carries the line's total");
        var stamp = (await Orders.GetAsync(orderId)).ConcurrencyStamp;
        var legs = parts.Select(Fingerprint).ToList();

        foreach (var part in parts)
        {
            var totals = await Legs.GetLineTotalsAsync(part.Id);

            totals.Quantity.ShouldBe(10);
            totals.Colli.ShouldBe(20m);
            totals.WeightKg.ShouldBe(1000);
            totals.VolumeM3.ShouldBe(5);
            totals.LoadingMeters.ShouldBe(6.8);
            totals.LoadFactor.ShouldBe(0.5);
            totals.Epq.ShouldBe(16);
        }

        (await Orders.GetAsync(orderId)).ConcurrencyStamp.ShouldBe(stamp, "a read");
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(legs, "a read");
    }

    [Fact]
    public async Task The_totals_follow_the_line_after_a_split_of_the_order_line_and_a_measure_the_line_lacks_stays_null()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var leg = (await LegRowsAsync(orderId)).Single();

        var before = await Legs.GetLineTotalsAsync(leg.Id);
        before.Quantity.ShouldBe(10);
        before.VolumeM3.ShouldBeNull();
        before.Colli.ShouldBeNull();

        var split = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 4 });

        (await Legs.GetLineTotalsAsync(leg.Id)).Quantity.ShouldBe(6);
        (await Legs.GetLineTotalsAsync(split.NewLegs.Single().Id)).Quantity.ShouldBe(4);
    }

    [Fact]
    public async Task A_missing_leg_order_or_line_is_not_found()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        var lineId = (await LegRowsAsync(orderId)).Single().OrderLineId;

        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetLineTotalsAsync(Guid.NewGuid()));
        await Should.ThrowAsync<EntityNotFoundException>(() => Splitter.GetLineTotalsAsync(orderId, Guid.NewGuid()));
        await Should.ThrowAsync<EntityNotFoundException>(() => Splitter.GetLineTotalsAsync(Guid.NewGuid(), lineId));
        (await Splitter.GetLineTotalsAsync(orderId, lineId)).Quantity.ShouldBe(10, "the control");
    }

    /* Line totals: the gates. */

    [Fact]
    public async Task The_totals_need_only_the_right_to_read_legs_not_the_right_to_read_orders()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        var leg = (await LegRowsAsync(orderId)).Single();

        Grants.Only = [PlanningExecutionPermissions.Legs.Default];
        (await Legs.GetLineTotalsAsync(leg.Id)).Quantity.ShouldBe(10);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Orders.GetAsync(orderId));

        Grants.Only = [OrdersPermissions.TransportOrders.Default, PlanningExecutionPermissions.Legs.Update];
        var refusal = await Should.ThrowAsync<AbpAuthorizationException>(() => Legs.GetLineTotalsAsync(leg.Id));
        IsFeatureRefusal(refusal).ShouldBeFalse("refused by the permission: " + refusal.Code);
    }

    [Fact]
    public async Task The_totals_work_without_the_Transport_feature_and_are_refused_with_neither_app()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        var leg = (await LegRowsAsync(orderId)).Single();
        Grants.Only = [PlanningExecutionPermissions.Legs.Default];

        using (TestFeatureValueProvider.Override(AppFeatures.Planboard, "true"))
        using (TestFeatureValueProvider.Override(AppFeatures.Transport, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Trips, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Orders, "false"))
        {
            (await Legs.GetLineTotalsAsync(leg.Id)).Quantity.ShouldBe(10);
        }

        using (TestFeatureValueProvider.Override(AppFeatures.Planboard, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.Transport, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Trips, "false"))
        {
            IsFeatureRefusal(await Should.ThrowAsync<AbpAuthorizationException>(() => Legs.GetLineTotalsAsync(leg.Id))).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Another_tenant_gets_no_totals()
    {
        Guid legId, orderId, lineId;
        using (AsMemberOf(TenantA))
        {
            var f = await FleetAsync();
            orderId = await OrderAsync(f);
            var leg = (await LegRowsAsync(orderId)).Single();
            (legId, lineId) = (leg.Id, leg.OrderLineId);
            (await Legs.GetLineTotalsAsync(legId)).Quantity.ShouldBe(10, "the control: the owner reads them");
        }

        using (AsMemberOf(TenantB))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetLineTotalsAsync(legId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Splitter.GetLineTotalsAsync(orderId, lineId));
        }
    }

    /* planboard-order/{id}. */

    [Fact]
    public async Task The_order_row_is_the_order_as_it_is_now_with_its_fresh_stamp()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        var order = await Orders.GetAsync(orderId);

        var row = await OrderFields.GetAsync(orderId);
        row.Id.ShouldBe(orderId);
        row.Number.ShouldBe(order.Number);
        row.ConcurrencyStamp.ShouldBe(order.ConcurrencyStamp);
        (await Orders.GetAsync(orderId)).ConcurrencyStamp.ShouldBe(order.ConcurrencyStamp, "a read");

        var saved = await OrderFields.UpdateFieldAsync(orderId, new OpenTms.Orders.TransportOrders.PlanboardOrderFieldUpdateDto
        {
            Field = OpenTms.Orders.TransportOrders.PlanboardOrderField.CustomerReference, Value = "SOMEONE ELSE", ConcurrencyStamp = order.ConcurrencyStamp
        });

        var fresh = await OrderFields.GetAsync(orderId);
        fresh.CustomerReference.ShouldBe("SOMEONE ELSE");
        fresh.ConcurrencyStamp.ShouldBe(saved.ConcurrencyStamp);
        await Should.ThrowAsync<EntityNotFoundException>(() => OrderFields.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_order_row_needs_the_orders_read_permission()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);

        Grants.Only = [OrdersPermissions.TransportOrders.Default];
        (await OrderFields.GetAsync(orderId)).Id.ShouldBe(orderId);

        Grants.Only = [OrdersPermissions.TransportOrders.Update, PlanningExecutionPermissions.Legs.Default];
        var refusal = await Should.ThrowAsync<AbpAuthorizationException>(() => OrderFields.GetAsync(orderId));
        IsFeatureRefusal(refusal).ShouldBeFalse("refused by the permission: " + refusal.Code);
    }

    [Fact]
    public async Task The_order_row_works_with_the_Transport_app_off_and_is_refused_with_the_Planboard_or_its_Orders_pane_off()
    {
        using var _ = AsMemberOf(TenantA);
        var f = await FleetAsync();
        var orderId = await OrderAsync(f);
        Grants.Only = [OrdersPermissions.TransportOrders.Default];

        using (TestFeatureValueProvider.Override(AppFeatures.Transport, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Orders, "false"))
        {
            (await OrderFields.GetAsync(orderId)).Id.ShouldBe(orderId);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Orders.GetAsync(orderId));
        }

        foreach (var feature in new[] { AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders })
        {
            using (TestFeatureValueProvider.Override(feature, "false"))
            {
                IsFeatureRefusal(await Should.ThrowAsync<AbpAuthorizationException>(() => OrderFields.GetAsync(orderId))).ShouldBeTrue(feature);
            }
        }
    }

    [Fact]
    public async Task Another_tenant_does_not_get_the_order_row()
    {
        Guid orderId;
        using (AsMemberOf(TenantA))
        {
            var f = await FleetAsync();
            orderId = await OrderAsync(f);
            (await OrderFields.GetAsync(orderId)).Id.ShouldBe(orderId, "the control: the owner reads it");
        }

        using (AsMemberOf(TenantB))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => OrderFields.GetAsync(orderId));
        }
    }
}
