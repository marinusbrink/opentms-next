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
/// docs/designs/tenants-and-editions.md, "App features"; design 48; PO 2026-10-16, one Master data group per domain; designs 51–53; design 54, the Platform group): 66 toggles in the "Apps" group, each a child of the node
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
        ["Apps.Transport"] = (null, "true"),
        ["Apps.Transport.Orders"] = ("Apps.Transport", "true"),
        ["Apps.Transport.Orders.Templates"] = ("Apps.Transport.Orders", "true"),
        ["Apps.Transport.Trips"] = ("Apps.Transport", "true"),
        ["Apps.Transport.Documents"] = ("Apps.Transport", "true"),
        ["Apps.Finance"] = (null, "false"),
        ["Apps.Integrations"] = (null, "false"),
        ["Apps.Reports"] = (null, "false"),

        ["Apps.MasterData"] = (null, "true"),
        ["Apps.MasterData.General"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.General.Relations"] = ("Apps.MasterData.General", "true"),
        ["Apps.MasterData.General.Relations.Relations"] = ("Apps.MasterData.General.Relations", "false"),
        ["Apps.MasterData.General.Relations.AddressTypes"] = ("Apps.MasterData.General.Relations", "false"),
        ["Apps.MasterData.General.Relations.RelationCategories"] = ("Apps.MasterData.General.Relations", "false"),
        ["Apps.MasterData.General.Transport"] = ("Apps.MasterData.General", "true"),
        ["Apps.MasterData.General.Transport.Products"] = ("Apps.MasterData.General.Transport", "false"),
        ["Apps.MasterData.General.Transport.TransportUnits"] = ("Apps.MasterData.General.Transport", "false"),
        ["Apps.MasterData.General.Transport.ProductRelationCodes"] = ("Apps.MasterData.General.Transport", "false"),
        ["Apps.MasterData.General.Transport.Temperatures"] = ("Apps.MasterData.General.Transport", "false"),
        ["Apps.MasterData.General.Fleet"] = ("Apps.MasterData.General", "true"),
        ["Apps.MasterData.General.Fleet.Vehicles"] = ("Apps.MasterData.General.Fleet", "false"),
        ["Apps.MasterData.General.Fleet.VehicleCombinations"] = ("Apps.MasterData.General.Fleet", "false"),
        ["Apps.MasterData.General.Fleet.Drivers"] = ("Apps.MasterData.General.Fleet", "false"),
        ["Apps.MasterData.General.Fleet.VehicleTypes"] = ("Apps.MasterData.General.Fleet", "false"),
        ["Apps.MasterData.General.Fleet.TransportCharacteristics"] = ("Apps.MasterData.General.Fleet", "false"),
        ["Apps.MasterData.General.Reference"] = ("Apps.MasterData.General", "true"),
        ["Apps.MasterData.General.Reference.Countries"] = ("Apps.MasterData.General.Reference", "false"),
        ["Apps.MasterData.General.Reference.Currencies"] = ("Apps.MasterData.General.Reference", "false"),
        ["Apps.MasterData.General.Reference.GeographicalRegions"] = ("Apps.MasterData.General.Reference", "false"),
        ["Apps.MasterData.Platform"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Platform.EmailTemplates"] = ("Apps.MasterData.Platform", "false"),
        ["Apps.MasterData.Platform.ReportDefinitions"] = ("Apps.MasterData.Platform", "false"),
        ["Apps.MasterData.Platform.DocumentTypes"] = ("Apps.MasterData.Platform", "false"),

        /* Designs 51–53: the Orders, Planning & Execution and Financial groups. */
        ["Apps.MasterData.Orders"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Orders.TransportOrders"] = ("Apps.MasterData.Orders", "true"),
        ["Apps.MasterData.Orders.TransportOrders.TransportOrderTypes"] = ("Apps.MasterData.Orders.TransportOrders", "false"),
        ["Apps.MasterData.Orders.TransportOrders.WaypointTypes"] = ("Apps.MasterData.Orders.TransportOrders", "false"),
        ["Apps.MasterData.Orders.TransportOrders.TimeSlots"] = ("Apps.MasterData.Orders.TransportOrders", "false"),
        ["Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles"] = ("Apps.MasterData.Orders.TransportOrders", "true"),
        ["Apps.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions"] = ("Apps.MasterData.Orders.TransportOrders", "true"),
        ["Apps.MasterData.Orders.TransportOrders.OrderAttributeDefinitions"] = ("Apps.MasterData.Orders.TransportOrders", "true"),
        ["Apps.MasterData.PlanningExecution"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.PlanningExecution.Trips"] = ("Apps.MasterData.PlanningExecution", "true"),
        ["Apps.MasterData.PlanningExecution.Trips.TransportTripTypes"] = ("Apps.MasterData.PlanningExecution.Trips", "false"),
        ["Apps.MasterData.PlanningExecution.Trips.ViaDepotRules"] = ("Apps.MasterData.PlanningExecution.Trips", "true"),
        ["Apps.MasterData.PlanningExecution.Trips.TripAttributeDefinitions"] = ("Apps.MasterData.PlanningExecution.Trips", "true"),
        ["Apps.MasterData.Financial"] = ("Apps.MasterData", "true"),
        ["Apps.MasterData.Financial.Invoicing"] = ("Apps.MasterData.Financial", "true"),
        ["Apps.MasterData.Financial.Invoicing.VatPercentages"] = ("Apps.MasterData.Financial.Invoicing", "false"),
        ["Apps.MasterData.Financial.Invoicing.OutboundInvoiceTypes"] = ("Apps.MasterData.Financial.Invoicing", "false"),
        ["Apps.MasterData.Financial.Invoicing.InboundInvoiceTypes"] = ("Apps.MasterData.Financial.Invoicing", "false"),
        ["Apps.MasterData.Financial.Pricing"] = ("Apps.MasterData.Financial", "true"),
        ["Apps.MasterData.Financial.Pricing.AdditionalServices"] = ("Apps.MasterData.Financial.Pricing", "false"),
        ["Apps.MasterData.Financial.Pricing.DiscountsOrSurcharges"] = ("Apps.MasterData.Financial.Pricing", "false"),
        ["Apps.MasterData.Financial.Pricing.PriceLists"] = ("Apps.MasterData.Financial.Pricing", "true"),
        ["Apps.MasterData.Financial.Pricing.RelationFinancialSettings"] = ("Apps.MasterData.Financial.Pricing", "true"),

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
    public async Task The_Apps_group_holds_exactly_the_66_tree_features()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();
        var group = (await manager.GetGroupsAsync()).Single(g => g.Name == AppFeatures.GroupName);

        Expected.Count.ShouldBe(66);
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
