using System.Linq;
using System.Threading.Tasks;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation.StringValues;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations;

/// <summary>
/// The Integrations permission and feature definitions through the real definition managers (design
/// 44, "API contract": permission on both sides, feature a toggle that defaults to off).
/// Risk class: Critical — authorization and the per-tenant switch (constitution rule 4); a wrong
/// default would switch the lookup on for every tenant, a wrong side would hide the permission from
/// tenant administrators.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class IntegrationsDefinitionProviderTests : OpenTmsEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task The_permission_is_defined_on_both_sides_in_the_Integrations_group()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        var groups = await manager.GetGroupsAsync();
        var group = groups.Single(g => g.Name == IntegrationsPermissions.GroupName);
        group.Permissions.Select(p => p.Name).ShouldContain(IntegrationsPermissions.AddressLookup);

        var permission = await manager.GetOrNullAsync(IntegrationsPermissions.AddressLookup);
        permission.ShouldNotBeNull();
        permission.Name.ShouldBe("Integrations.AddressLookup");
        permission.MultiTenancySide.ShouldBe(MultiTenancySides.Both);
        permission.IsEnabled.ShouldBeTrue();
        permission.Parent.ShouldBeNull();
        permission.DisplayName.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_feature_is_a_toggle_that_defaults_to_off()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();

        var feature = await manager.GetAsync(IntegrationsFeatures.AddressLookup);

        feature.Name.ShouldBe("Integrations.AddressLookup");
        feature.DefaultValue.ShouldBe("false");
        feature.ValueType.ShouldBeOfType<ToggleStringValueType>();
        feature.IsVisibleToClients.ShouldBeTrue();
        feature.IsAvailableToHost.ShouldBeTrue();
        feature.DisplayName.ShouldNotBeNull();
        feature.Description.ShouldNotBeNull();

        var groups = await manager.GetGroupsAsync();
        groups.Single(g => g.Name == IntegrationsFeatures.GroupName).Features.Select(f => f.Name).ShouldBe(["Integrations.AddressLookup"]);
    }
}
