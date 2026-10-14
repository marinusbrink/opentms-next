using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Integrations.Features;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Volo.Abp.Validation.StringValues;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Features;

/// <summary>
/// The app and navigation feature definitions through the real definition manager (PO decision 2026-10-14,
/// docs/designs/tenants-and-editions.md, "App features"): 31 toggles in the "Apps" group, each a child of the node
/// above it so an edition's Features tab shows the tree, with the PO's defaults; the old ad-hoc features are gone.
/// Risk class: High — a wrong default switches a screen on or off for every tenant without a stored value.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class AppFeatureDefinitionTests : OpenTmsEntityFrameworkCoreTestBase
{
    /// <summary>Every app feature: name → (parent, default).</summary>
    private static readonly Dictionary<string, (string? Parent, string Default)> Expected = new()
    {
        ["Apps.Dashboard"] = (null, "false"),
        ["Apps.Planboard"] = (null, "false"),
        ["Apps.Transport"] = (null, "false"),
        ["Apps.Finance"] = (null, "false"),
        ["Apps.Integrations"] = (null, "false"),
        ["Apps.Reports"] = (null, "false"),

        ["Apps.MasterData"] = (null, "true"),
        ["Apps.MasterData.Relations"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Relations.Relations"] = ("Apps.MasterData.Relations", "false"),
        ["Apps.MasterData.Relations.AddressTypes"] = ("Apps.MasterData.Relations", "false"),
        ["Apps.MasterData.Relations.RelationCategories"] = ("Apps.MasterData.Relations", "false"),
        ["Apps.MasterData.Transport"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Transport.Products"] = ("Apps.MasterData.Transport", "false"),
        ["Apps.MasterData.Transport.TransportUnits"] = ("Apps.MasterData.Transport", "false"),
        ["Apps.MasterData.Transport.Temperatures"] = ("Apps.MasterData.Transport", "false"),
        ["Apps.MasterData.Fleet"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Fleet.Drivers"] = ("Apps.MasterData.Fleet", "false"),
        ["Apps.MasterData.Fleet.VehicleTypes"] = ("Apps.MasterData.Fleet", "false"),
        ["Apps.MasterData.Fleet.TransportCharacteristics"] = ("Apps.MasterData.Fleet", "false"),
        ["Apps.MasterData.Reference"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Reference.Countries"] = ("Apps.MasterData.Reference", "false"),
        ["Apps.MasterData.Reference.Currencies"] = ("Apps.MasterData.Reference", "false"),
        ["Apps.MasterData.Reference.GeographicalRegions"] = ("Apps.MasterData.Reference", "false"),

        ["Apps.Administration"] = (null, "true"),
        ["Apps.Administration.Users"] = ("Apps.Administration", "true"),
        ["Apps.Administration.Roles"] = ("Apps.Administration", "true"),
        ["Apps.Administration.OrganizationUnits"] = ("Apps.Administration", "true"),
        ["Apps.Administration.AuditLogs"] = ("Apps.Administration", "true"),
        ["Apps.Administration.AuditLogs.FunctionalLogbook"] = ("Apps.Administration.AuditLogs", "true"),
        ["Apps.Administration.AuditLogs.TechnicalLogbook"] = ("Apps.Administration.AuditLogs", "true"),
        ["Apps.Administration.Maintenance"] = ("Apps.Administration", "false"),
    };

    [Fact]
    public async Task The_Apps_group_holds_exactly_the_31_tree_features()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();
        var group = (await manager.GetGroupsAsync()).Single(g => g.Name == AppFeatures.GroupName);

        Expected.Count.ShouldBe(31);
        group.GetFeaturesWithChildren().Select(f => f.Name).ShouldBe(Expected.Keys, ignoreOrder: true);
        group.Features.Select(f => f.Name).ShouldBe(Expected.Where(e => e.Value.Parent == null).Select(e => e.Key), ignoreOrder: true);
    }

    [Fact]
    public async Task Every_app_feature_has_the_designed_parent_default_and_toggle_type()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();

        foreach (var (name, (parent, defaultValue)) in Expected)
        {
            var feature = await manager.GetAsync(name);
            feature.Parent?.Name.ShouldBe(parent, name);
            if (parent == null)
            {
                feature.Parent.ShouldBeNull(name);
            }

            feature.DefaultValue.ShouldBe(defaultValue, name);
            feature.ValueType.ShouldBeOfType<ToggleStringValueType>(name);
            feature.IsVisibleToClients.ShouldBeTrue(name);
            feature.IsAvailableToHost.ShouldBeTrue(name);
            feature.DisplayName.ShouldNotBeNull(name);

            /* A node's ancestors are its dot-prefixes; the definition tree must say the same. */
            if (parent != null)
            {
                name.ShouldStartWith(parent + ".", Case.Sensitive, name);
            }
        }
    }

    [Theory]
    [InlineData("MasterData.FirstRun")]
    [InlineData("MasterData.Relations")]
    [InlineData("Platform.Maintenance")]
    [InlineData("UI.CommonToolbar")]
    public async Task The_replaced_features_are_no_longer_defined(string name)
    {
        (await GetRequiredService<IFeatureDefinitionManager>().GetOrNullAsync(name)).ShouldBeNull();
    }

    [Fact]
    public async Task The_address_lookup_feature_is_unchanged()
    {
        var feature = await GetRequiredService<IFeatureDefinitionManager>().GetAsync(IntegrationsFeatures.AddressLookup);
        feature.Name.ShouldBe("Integrations.AddressLookup");
    }

    [Fact]
    public async Task Without_stored_values_the_checker_answers_the_PO_defaults_for_the_administration_tree()
    {
        /* The test-side provider answers only the Master data tree; Administration is the production path. */
        var checker = GetRequiredService<IFeatureChecker>();
        (await checker.IsEnabledAsync(AppFeatures.Administration.Users)).ShouldBeTrue();
        (await checker.IsEnabledAsync(AppFeatures.Administration.AuditLogs.TechnicalLogbook)).ShouldBeTrue();
        (await checker.IsEnabledAsync(AppFeatures.Administration.Maintenance)).ShouldBeFalse();
        (await checker.IsEnabledAsync(AppFeatures.Dashboard)).ShouldBeFalse();
    }
}
