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
        /* Design 72 adds the order intake (top level) with AI mapping beneath it. */
        groups.Single(g => g.Name == IntegrationsFeatures.GroupName).Features.Select(f => f.Name)
            .ShouldBe(["Integrations.AddressLookup", "Integrations.OrderIntake"]);
    }

    /// <summary>Design 72 "Features": both behaviour features are toggles that default to off (rule 4); AI mapping is a child of the intake.</summary>
    [Fact]
    public async Task The_order_intake_features_are_toggles_that_default_to_off_with_AI_mapping_beneath_the_intake()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();

        var intake = await manager.GetAsync(IntegrationsFeatures.OrderIntake);
        intake.Name.ShouldBe("Integrations.OrderIntake");
        intake.DefaultValue.ShouldBe("false");
        intake.ValueType.ShouldBeOfType<ToggleStringValueType>();
        intake.Parent.ShouldBeNull();
        intake.DisplayName.ShouldNotBeNull();
        intake.Description.ShouldNotBeNull();

        var ai = await manager.GetAsync(IntegrationsFeatures.OrderIntakeAiMapping);
        ai.Name.ShouldBe("Integrations.OrderIntake.AiMapping");
        ai.DefaultValue.ShouldBe("false");
        ai.ValueType.ShouldBeOfType<ToggleStringValueType>();
        ai.Parent.ShouldNotBeNull();
        ai.Parent!.Name.ShouldBe(IntegrationsFeatures.OrderIntake);
    }

    /// <summary>
    /// Design 72 "Permissions": eight tenant-side permissions in four parent/child pairs, and the intake API's permission on
    /// both sides that only the client provider ("C") may grant — never a role.
    /// </summary>
    [Fact]
    public async Task The_order_intake_permissions_are_tenant_side_and_the_intake_API_is_granted_to_clients_only()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var (parent, child) in new[]
                 {
                     (IntegrationsPermissions.Mailboxes.Default, IntegrationsPermissions.Mailboxes.Manage),
                     (IntegrationsPermissions.ApiClients.Default, IntegrationsPermissions.ApiClients.Manage),
                     (IntegrationsPermissions.ImportProfiles.Default, IntegrationsPermissions.ImportProfiles.Manage),
                     (IntegrationsPermissions.InboundMessages.Default, IntegrationsPermissions.InboundMessages.Reprocess)
                 })
        {
            var p = await manager.GetOrNullAsync(parent);
            p.ShouldNotBeNull(parent);
            p.MultiTenancySide.ShouldBe(MultiTenancySides.Tenant, parent);
            p.Parent.ShouldBeNull();
            var c = await manager.GetOrNullAsync(child);
            c.ShouldNotBeNull(child);
            c.MultiTenancySide.ShouldBe(MultiTenancySides.Tenant, child);
            c.Parent!.Name.ShouldBe(parent);
        }

        var submit = await manager.GetOrNullAsync(IntegrationsPermissions.IntakeApi.Submit);
        submit.ShouldNotBeNull();
        submit.Name.ShouldBe("Integrations.IntakeApi.Submit");
        submit.MultiTenancySide.ShouldBe(MultiTenancySides.Both);
        submit.Providers.ShouldBe([ClientPermissionValueProvider.ProviderName]);
    }
}
