using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Grid;

/// <summary>
/// Design 66 (Planboard workspace), the grid settings' free-form <c>ViewState</c> on SQLite: the dockview layout and a
/// pane's settings round-trip as given, at most 65,536 characters of JSON (one more is refused), a plain grid keeps a null
/// view state, and another user or tenant never sees it. The text column without a length limit is covered on PostgreSQL by
/// GridUserSettingsPostgreSqlTests. Risk class: Critical (Platform; tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class GridSettingsViewStateTests : OpenTmsEntityFrameworkCoreTestBase
{
    private IGridSettingsAppService Service => GetRequiredService<IGridSettingsAppService>();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A JSON object of exactly <paramref name="length"/> characters.</summary>
    private static string JsonOfLength(int length)
    {
        const string prefix = "{\"layout\":\"";
        const string suffix = "\"}";
        return prefix + new string('x', length - prefix.Length - suffix.Length) + suffix;
    }

    [Fact]
    public async Task The_planboard_layout_round_trips_as_given_and_a_plain_grid_keeps_no_view_state()
    {
        var layout = Json("""{"grid":{"root":{"type":"branch","data":[{"type":"leaf","data":{"views":["scheduler-1","map-1"],"activeView":"map-1"},"size":640}]}},"panels":{"scheduler-1":{"id":"scheduler-1","contentComponent":"scheduler","params":{"mode":"Tractor","zoom":2.5,"organizationUnitIds":[]}}},"activeGroup":"1"}""");
        var gridId = "planboard.layout";

        await Service.SetAsync(gridId, new GridSettingsDto { ViewState = layout });
        await Service.SetAsync("planboard.pane.scheduler-1", new GridSettingsDto { ViewState = Json("""{"mode":"Driver","showBlocks":true,"rowHeight":28}""") });
        await Service.SetAsync("transport.trips", new GridSettingsDto { ColumnStates = [new ColumnStateDto { ColId = "number", Visible = true, Order = 0, Width = 120 }] });

        var loaded = (await Service.GetAsync(gridId)).ShouldNotBeNull();
        loaded.ViewState.ShouldNotBeNull();
        JsonElement.DeepEquals(loaded.ViewState.Value, layout).ShouldBeTrue("stored as given");
        loaded.ColumnStates.ShouldBeEmpty();

        var pane = (await Service.GetAsync("planboard.pane.scheduler-1")).ShouldNotBeNull();
        pane.ViewState!.Value.GetProperty("mode").GetString().ShouldBe("Driver");
        pane.ViewState!.Value.GetProperty("rowHeight").GetInt32().ShouldBe(28);

        (await Service.GetAsync("transport.trips")).ShouldNotBeNull().ViewState.ShouldBeNull();

        await Service.SetAsync(gridId, new GridSettingsDto { ViewState = Json("""{"replaced":true}""") });
        (await Service.GetAsync(gridId))!.ViewState!.Value.GetProperty("replaced").GetBoolean().ShouldBeTrue("a save replaces the earlier state");

        await Service.ResetAsync(gridId);
        (await Service.GetAsync(gridId)).ShouldBeNull();
    }

    [Fact]
    public async Task A_view_state_of_65536_characters_is_stored_and_one_more_is_refused()
    {
        var max = JsonOfLength(GridUserSettingConsts.MaxViewStateLength);
        max.Length.ShouldBe(65_536);

        await Service.SetAsync("planboard.layout", new GridSettingsDto { ViewState = Json(max) });
        (await Service.GetAsync("planboard.layout"))!.ViewState!.Value.GetRawText().Length.ShouldBe(65_536);

        var tooLong = JsonOfLength(GridUserSettingConsts.MaxViewStateLength + 1);
        await Should.ThrowAsync<UserFriendlyException>(() => Service.SetAsync("planboard.layout", new GridSettingsDto { ViewState = Json(tooLong) }));
        (await Service.GetAsync("planboard.layout"))!.ViewState!.Value.GetRawText().Length.ShouldBe(65_536, "the refused save changed nothing");
    }

    [Fact]
    public async Task Negative_isolation_another_user_or_tenant_never_reads_the_view_state()
    {
        var tenantA = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(tenantA))
        {
            await Service.SetAsync("planboard.layout", new GridSettingsDto { ViewState = Json("""{"secret":"tenant-a"}""") });
        }

        using (currentTenant.Change(Guid.NewGuid()))
        {
            (await Service.GetAsync("planboard.layout")).ShouldBeNull("another tenant, same user id");
        }

        using (currentTenant.Change(tenantA))
        using (GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
                   [new Claim(AbpClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(AbpClaimTypes.UserName, "other-user")]))))
        {
            (await Service.GetAsync("planboard.layout")).ShouldBeNull("another user of the same tenant");
        }

        using (currentTenant.Change(tenantA))
        {
            (await Service.GetAsync("planboard.layout"))!.ViewState!.Value.GetProperty("secret").GetString().ShouldBe("tenant-a");
        }
    }
}
