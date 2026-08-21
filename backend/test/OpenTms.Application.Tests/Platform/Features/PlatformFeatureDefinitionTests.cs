using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.Platform.Administration;

/// <summary>
/// Unit tests for PlatformFeatures constants.
/// Risk class: High — wrong flag name silently breaks rollout; frontend reads the flag
/// by the exact constant string value (design §Test risk analysis).
///
/// Note on provider default-value coverage: verifying that PlatformFeatureDefinitionProvider
/// registers UI.CommonToolbar with defaultValue "false" requires an ABP integration context
/// (IFeatureDefinitionContext is an ABP type whose concrete implementation has an internal
/// constructor). The default is a literal "false" in PlatformFeatureDefinitionProvider.cs —
/// confirmed by code review. A dedicated ABP integration test can extend coverage here
/// once the integration test module wires feature management without a database dependency.
/// </summary>
public class PlatformFeatureDefinitionTests
{
    // ── Constant value assertions ──────────────────────────────────────────────
    //
    // These constants are the bridge between the backend ABP feature definition
    // and the frontend flag check (features.values['UI.CommonToolbar'] === 'true').
    // A typo or rename would silently kill the feature flag rollout.

    [Fact]
    public void CommonToolbar_constant_equals_UI_CommonToolbar()
    {
        PlatformFeatures.CommonToolbar.ShouldBe("UI.CommonToolbar");
    }

    [Fact]
    public void GroupName_constant_equals_Platform()
    {
        PlatformFeatures.GroupName.ShouldBe("Platform");
    }

    [Fact]
    public void CommonToolbar_key_starts_with_GroupName_prefix_UI()
    {
        // ABP feature names don't carry the group prefix, but confirming the
        // well-known "UI." namespace is used keeps the flag discoverable in the
        // ABP management UI and consistent with the naming convention in the design.
        PlatformFeatures.CommonToolbar.ShouldStartWith("UI.");
    }
}
