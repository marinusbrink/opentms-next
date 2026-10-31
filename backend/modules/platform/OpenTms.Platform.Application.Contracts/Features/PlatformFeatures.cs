namespace OpenTms.Platform.Features;

/// <summary>
/// The Platform feature group. Its app and navigation features are <see cref="AppFeatures"/>; the former
/// <c>Platform.Maintenance</c> is now <see cref="AppFeatures.Administration.Maintenance"/>.
/// </summary>
public static class PlatformFeatures
{
    public const string GroupName = "Platform";

    /// <summary>
    /// Document rendering through a rendering provider (design 55): every provider call per tenant, off until the
    /// deployment holds the provider's key and secret and the tenant is switched on (constitution rule 4). Defined in
    /// Platform, not Integrations, because Platform's renderer checks it and Platform references no domain module.
    /// </summary>
    public const string DocumentRendering = GroupName + ".DocumentRendering";

    /// <summary>
    /// E-mail delivery through the send log and the mail service (design 64): a tenant's mail is sent and tracked, off
    /// until the deployment holds the mail service's token and webhook credentials (constitution rule 4). Off: the mail is
    /// logged, not sent, as before.
    /// </summary>
    public const string EmailDelivery = GroupName + ".EmailDelivery";
}
