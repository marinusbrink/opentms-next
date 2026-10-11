using OpenTms.MasterData.Features;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Features;

/// <summary>
/// Unit tests for the Master Data feature flag constants (design 41, "Flag &amp; rollout plan"; the
/// definition itself is covered in OpenTms.EntityFrameworkCore.Tests MasterDataDefinitionProviderTests). Risk class:
/// Critical — the flag name is what gates every service and what the release manager flips per
/// tenant (constitution rule 4); a wrong default would switch the feature on for every tenant.
/// </summary>
public class MasterDataFeatureDefinitionTests
{
    [Fact]
    public void The_flag_has_the_expected_name_under_the_MasterData_group()
    {
        MasterDataFeatures.GroupName.ShouldBe("MasterData");
        MasterDataFeatures.FirstRun.ShouldBe("MasterData.FirstRun");
    }
}
