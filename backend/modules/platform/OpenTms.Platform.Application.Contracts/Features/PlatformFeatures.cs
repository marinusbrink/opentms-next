namespace OpenTms.Platform.Features;

public static class PlatformFeatures
{
    public const string GroupName = "Platform";

    /// <summary>
    /// Per-tenant switch for the Maintenance view (constitution rule 4; default off). It governs the
    /// tenant-side part only (the Outbox tab): the host-only tabs are gated by host-side
    /// permissions, because a host has no tenant to carry a feature value.
    /// </summary>
    public const string Maintenance = GroupName + ".Maintenance";
}
