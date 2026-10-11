using OpenTms.Platform.Features;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Features;

/// <summary>
/// Unit tests for the Platform feature constants. Risk class: High — the flag name is what the
/// frontend checks and what the release manager flips per tenant (constitution rule 4).
/// </summary>
public class PlatformFeatureDefinitionTests
{
    [Fact]
    public void Maintenance_feature_has_the_expected_name_under_the_platform_group()
    {
        PlatformFeatures.GroupName.ShouldBe("Platform");
        PlatformFeatures.Maintenance.ShouldBe("Platform.Maintenance");
    }
}
