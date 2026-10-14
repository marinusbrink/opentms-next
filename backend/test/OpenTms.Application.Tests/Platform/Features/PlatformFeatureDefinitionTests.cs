using Shouldly;
using Xunit;

namespace OpenTms.Platform.Features;

/// <summary>
/// Unit tests for the Platform feature constants. Risk class: High — the flag name is what the
/// frontend checks and what the release manager flips per tenant (constitution rule 4). Platform.Maintenance became
/// the Administration app's Maintenance node (PO 2026-10-14, "App features").
/// </summary>
public class PlatformFeatureDefinitionTests
{
    [Fact]
    public void Maintenance_feature_is_a_node_of_the_administration_app()
    {
        PlatformFeatures.GroupName.ShouldBe("Platform");
        AppFeatures.Administration.Maintenance.ShouldBe("Apps.Administration.Maintenance");
        typeof(PlatformFeatures).GetField("Maintenance").ShouldBeNull("Platform.Maintenance is replaced by the app node");
    }

    [Fact]
    public void The_administration_features_follow_the_navigation_tree()
    {
        AppFeatures.Administration.Default.ShouldBe("Apps.Administration");
        AppFeatures.Administration.Users.ShouldBe("Apps.Administration.Users");
        AppFeatures.Administration.Roles.ShouldBe("Apps.Administration.Roles");
        AppFeatures.Administration.OrganizationUnits.ShouldBe("Apps.Administration.OrganizationUnits");
        AppFeatures.Administration.AuditLogs.Default.ShouldBe("Apps.Administration.AuditLogs");
        AppFeatures.Administration.AuditLogs.FunctionalLogbook.ShouldBe("Apps.Administration.AuditLogs.FunctionalLogbook");
        AppFeatures.Administration.AuditLogs.TechnicalLogbook.ShouldBe("Apps.Administration.AuditLogs.TechnicalLogbook");

        AppFeatures.Dashboard.ShouldBe("Apps.Dashboard");
        AppFeatures.Planboard.ShouldBe("Apps.Planboard");
        AppFeatures.Transport.ShouldBe("Apps.Transport");
        AppFeatures.Finance.ShouldBe("Apps.Finance");
        AppFeatures.Integrations.ShouldBe("Apps.Integrations");
        AppFeatures.Reports.ShouldBe("Apps.Reports");
    }
}
