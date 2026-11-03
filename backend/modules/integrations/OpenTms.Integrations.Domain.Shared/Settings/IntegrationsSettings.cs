namespace OpenTms.Integrations.Settings;

/// <summary>The tenant settings of the Integrations module (design 72); the host's service settings are <c>ServiceSettingNames</c>.</summary>
public static class IntegrationsSettings
{
    /// <summary>
    /// The confirmations without a correction after which a proposed import profile becomes Active (1–20, default
    /// <see cref="OrderIntake.OrderIntakeConsts.DefaultProfileActivationThreshold"/>). Per tenant, no screen in this run.
    /// </summary>
    public const string ProfileActivationThreshold = "Integrations.OrderIntake.ProfileActivationThreshold";
}
