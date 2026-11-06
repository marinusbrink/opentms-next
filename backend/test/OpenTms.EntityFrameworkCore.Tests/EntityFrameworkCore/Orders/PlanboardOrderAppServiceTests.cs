using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Features;
using OpenTms.Orders;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// The Planboard's Orders pane read (design 70; <c>PlanboardOrderAppService</c>, route <c>/api/orders/planboard-order</c>)
/// on SQLite: the planned-date filter is an overlap of the board range with the order's first…last stop dates (not the
/// order date), several organisation units filter as "any of", status, search, sort and paging are the Transport list's
/// (<see cref="TransportOrderListQuery"/>), the service is gated on the Planboard and its Orders pane (and works with the
/// Transport app off, design 66 D7), carries the orders read permission, and never returns another tenant's orders.
/// Risk class: High (an Orders read for planning; tenant data) — the tenant-isolation part is Critical.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardOrderAppServiceTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private IPlanboardOrderAppService Planboard => GetRequiredService<IPlanboardOrderAppService>();

    /// <summary>Creates an Open order whose single line is loaded on <paramref name="firstStop"/> and unloaded the day after, and stores its summary as the job does.</summary>
    private async Task<TransportOrderDto> OrderAsync(TransportRunWorld f, string reference, DateOnly firstStop, Guid? organizationUnitId = null, DateOnly? orderDate = null, bool confirm = true)
    {
        var dto = CreateDto(f, confirm: confirm, customerReference: reference, orderDate: orderDate);
        dto.Lines = [Line(f, date: firstStop)];
        dto.OrganizationUnitId = organizationUnitId;
        var order = await Orders.CreateAsync(dto);
        await WithUnitOfWorkAsync(() => GetRequiredService<OrderSummaryCalculator>().CalculateAsync(new DerivedValueCalculationContext(OrdersConsts.OrderSummaryTargetType, order.Id, 1, default)));
        return order;
    }

    private Task<OpenTms.Platform.Grid.GridResponse<TransportOrderRowDto>> ListAsync(Action<PlanboardOrderListRequestDto>? change = null)
    {
        var input = new PlanboardOrderListRequestDto { StartRow = 0, EndRow = 100 };
        change?.Invoke(input);
        return Planboard.GetListAsync(input);
    }

    private static List<string?> References(OpenTms.Platform.Grid.GridResponse<TransportOrderRowDto> page) => page.Rows.Select(r => r.CustomerReference).OrderBy(r => r).ToList();

    // ── planned-date overlap ─────────────────────────────────────────────────

    [Fact]
    public async Task The_range_selects_orders_whose_first_to_last_stop_dates_overlap_it_not_by_order_date()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var from = Today.AddDays(10);
        var to = from.AddDays(6);
        await OrderAsync(f, "BEFORE", from.AddDays(-3));            // stops from-3 … from-2
        await OrderAsync(f, "ENDS-ON-FROM", from.AddDays(-1));      // stops from-1 … from  (overlaps on the first day)
        await OrderAsync(f, "INSIDE", from.AddDays(2));             // stops from+2 … from+3
        await OrderAsync(f, "STARTS-ON-TO", to);                    // stops to … to+1      (overlaps on the last day)
        await OrderAsync(f, "AFTER", to.AddDays(1));                // stops to+1 … to+2
        await OrderAsync(f, "OLD-ORDER-DATE", from.AddDays(1), orderDate: Today.AddDays(-30));

        var page = await ListAsync(i => { i.PlannedFrom = from; i.PlannedTo = to; });
        References(page).ShouldBe(["ENDS-ON-FROM", "INSIDE", "OLD-ORDER-DATE", "STARTS-ON-TO"]);
        page.TotalCount.ShouldBe(4);
        page.FilteredCount.ShouldBe(4);

        References(await ListAsync(i => i.PlannedFrom = to.AddDays(1))).ShouldBe(["AFTER", "STARTS-ON-TO"]);
        References(await ListAsync(i => i.PlannedTo = from.AddDays(-2))).ShouldBe(["BEFORE"]);
        (await ListAsync()).TotalCount.ShouldBe(6, "no range = every order");
    }

    [Fact]
    public async Task A_single_day_range_matches_an_order_that_spans_it()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var day = Today.AddDays(5);
        await OrderAsync(f, "SPANS", day.AddDays(-1)); // stops day-1 … day
        await OrderAsync(f, "NEXT", day.AddDays(1));

        References(await ListAsync(i => { i.PlannedFrom = day; i.PlannedTo = day; })).ShouldBe(["SPANS"]);
    }

    // ── organisation units, status, search, sort ─────────────────────────────

    [Fact]
    public async Task Several_organisation_units_filter_as_any_of_and_an_order_without_a_unit_is_left_out()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        Guid north = Guid.NewGuid(), south = Guid.NewGuid(), west = Guid.NewGuid();
        await OrderAsync(f, "NORTH", Today.AddDays(1), north);
        await OrderAsync(f, "SOUTH", Today.AddDays(1), south);
        await OrderAsync(f, "WEST", Today.AddDays(1), west);
        await OrderAsync(f, "NONE", Today.AddDays(1));

        References(await ListAsync(i => i.OrganizationUnitIds = [north, south, north])).ShouldBe(["NORTH", "SOUTH"]);
        References(await ListAsync(i => i.OrganizationUnitIds = [west])).ShouldBe(["WEST"]);
        (await ListAsync(i => i.OrganizationUnitIds = [Guid.NewGuid()])).TotalCount.ShouldBe(0);
        (await ListAsync(i => i.OrganizationUnitIds = [])).TotalCount.ShouldBe(4, "empty = all units, including orders without one");
    }

    [Fact]
    public async Task Status_search_sort_and_paging_are_the_Transport_lists()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var draft = await OrderAsync(f, "ALPHA-DRAFT", Today.AddDays(1), confirm: false);
        var open1 = await OrderAsync(f, "BRAVO-OPEN", Today.AddDays(1));
        var open2 = await OrderAsync(f, "CHARLIE-OPEN", Today.AddDays(2));

        (await ListAsync(i => i.Status = [TransportOrderStatus.Draft])).Rows.Select(r => r.Id).ShouldBe([draft.Id]);
        (await ListAsync(i => i.Status = [TransportOrderStatus.Open])).Rows.Select(r => r.Id).ShouldBe([open1.Id, open2.Id], ignoreOrder: true);

        var searched = await ListAsync(i => i.WildcardSearch = "  bravo ");
        searched.Rows.ShouldHaveSingleItem().Id.ShouldBe(open1.Id);
        searched.TotalCount.ShouldBe(3);
        searched.FilteredCount.ShouldBe(1);
        (await ListAsync(i => i.WildcardSearch = open2.Number)).Rows.ShouldHaveSingleItem().Id.ShouldBe(open2.Id);
        (await ListAsync(i => i.WildcardSearch = "klant foods")).FilteredCount.ShouldBe(3, "the customer name is searched like the Transport list does");

        (await ListAsync(i => { i.SortBy = "customerReference"; i.SortDir = "desc"; })).Rows.Select(r => r.CustomerReference).ShouldBe(["CHARLIE-OPEN", "BRAVO-OPEN", "ALPHA-DRAFT"]);
        var page = await ListAsync(i => { i.SortBy = "customerReference"; i.StartRow = 1; i.EndRow = 2; });
        page.Rows.ShouldHaveSingleItem().CustomerReference.ShouldBe("BRAVO-OPEN");
        page.TotalCount.ShouldBe(3);
        (await Should.ThrowAsync<BusinessException>(() => ListAsync(i => i.SortBy = "nope"))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);

        /* The same rows as the Transport list for the same filter. */
        var transport = await Orders.GetListAsync(new TransportOrderListRequestDto { StartRow = 0, EndRow = 100, SortBy = "customerReference" });
        (await ListAsync(i => i.SortBy = "customerReference")).Rows.Select(r => r.Id).ShouldBe(transport.Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task The_rows_carry_the_stored_first_and_last_stop_dates()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var first = Today.AddDays(3);
        var order = await OrderAsync(f, "DATES", first);

        var row = (await ListAsync()).Rows.Single(r => r.Id == order.Id);
        row.FirstStopDate.ShouldBe(first);
        row.LastStopDate.ShouldBe(first.AddDays(1));
    }

    // ── tenant isolation (negative) ──────────────────────────────────────────

    [Fact]
    public async Task Another_tenants_orders_never_appear_with_any_filter_or_search()
    {
        var unit = Guid.NewGuid();
        string otherNumber;
        using (InTenantB())
        {
            var fb = await FixtureAsync();
            var b = await OrderAsync(fb, "TENANT-B", Today.AddDays(1), unit);
            otherNumber = b.Number!;
        }

        using var _ = InTenantA();
        var f = await FixtureAsync();
        var a = await OrderAsync(f, "TENANT-A", Today.AddDays(1));

        var all = await ListAsync();
        all.Rows.Select(r => r.Id).ShouldBe([a.Id]);
        all.TotalCount.ShouldBe(1);
        (await ListAsync(i => i.OrganizationUnitIds = [unit])).TotalCount.ShouldBe(0, "B's unit selects nothing in A");
        (await ListAsync(i => i.WildcardSearch = "tenant-b")).FilteredCount.ShouldBe(0);
        (await ListAsync(i => i.WildcardSearch = otherNumber)).Rows.Select(r => r.Id).ShouldNotContain(id => id != a.Id);
        (await ListAsync(i => { i.PlannedFrom = Today; i.PlannedTo = Today.AddDays(7); })).Rows.Select(r => r.Id).ShouldBe([a.Id]);

        using (InTenantB())
        {
            /* Control: B sees its own order, so A's empty answers above are the isolation, not a broken query. */
            (await ListAsync()).Rows.ShouldHaveSingleItem().CustomerReference.ShouldBe("TENANT-B");
        }
    }

    // ── feature gating and permission ────────────────────────────────────────

    [Fact]
    public async Task Works_with_the_Transport_app_off_and_is_refused_with_the_Planboard_or_its_Orders_pane_off()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        await OrderAsync(f, "GATED", Today.AddDays(1));

        using (TestFeatureValueProvider.Override(AppFeatures.Transport, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Orders, "false"))
        {
            (await ListAsync()).TotalCount.ShouldBe(1, "design 66 D7: the Planboard's Orders pane does not need the Transport app");
            await Should.ThrowAsync<AbpAuthorizationException>(() => Orders.GetListAsync(new TransportOrderListRequestDto()));
        }

        foreach (var feature in new[] { AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders })
        {
            using (TestFeatureValueProvider.Override(feature, "false"))
            {
                var refusal = await Should.ThrowAsync<AbpAuthorizationException>(() => ListAsync());
                refusal.Code.ShouldNotBeNull().ShouldStartWith("Volo.Feature:", customMessage: feature);
            }
        }
    }

    [Fact]
    public void Carries_the_orders_read_permission_and_requires_both_Planboard_features()
    {
        var gate = typeof(PlanboardOrderAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull();
        gate.Features.ShouldBe([AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders], ignoreOrder: true);
        gate.RequiresAll.ShouldBeTrue();

        var method = typeof(PlanboardOrderAppService).GetMethod(nameof(PlanboardOrderAppService.GetListAsync)).ShouldNotBeNull();
        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(OrdersPermissions.TransportOrders.Default);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");

        /* Design 74 §6 (PO decision): the pane reads with the read permission and writes its two inline fields with the update permission, nothing else. */
        typeof(IPlanboardOrderAppService).GetMethods().Select(m => m.Name)
            .ShouldBe([nameof(IPlanboardOrderAppService.GetListAsync), nameof(IPlanboardOrderAppService.GetAsync), nameof(IPlanboardOrderAppService.UpdateFieldAsync)], ignoreOrder: true);
        var read = typeof(PlanboardOrderAppService).GetMethod(nameof(PlanboardOrderAppService.GetAsync)).ShouldNotBeNull();
        read.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(OrdersPermissions.TransportOrders.Default);
        read.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        read.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
        var write = typeof(PlanboardOrderAppService).GetMethod(nameof(PlanboardOrderAppService.UpdateFieldAsync)).ShouldNotBeNull();
        write.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(OrdersPermissions.TransportOrders.Update);
        write.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        write.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");

        var lineGate = typeof(PlanboardOrderLineFieldAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull();
        lineGate.Features.ShouldBe([AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders], ignoreOrder: true);
        lineGate.RequiresAll.ShouldBeTrue();
        typeof(IPlanboardOrderLineFieldAppService).GetMethods().Select(m => m.Name).ShouldBe([nameof(IPlanboardOrderLineFieldAppService.UpdateLineFieldAsync)]);
        var lineWrite = typeof(PlanboardOrderLineFieldAppService).GetMethod(nameof(PlanboardOrderLineFieldAppService.UpdateLineFieldAsync)).ShouldNotBeNull();
        lineWrite.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(OrdersPermissions.TransportOrders.Update);
        lineWrite.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
    }
}
