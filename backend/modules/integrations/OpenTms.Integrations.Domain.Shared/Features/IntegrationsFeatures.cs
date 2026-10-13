namespace OpenTms.Integrations.Features;

public static class IntegrationsFeatures
{
    public const string GroupName = "Integrations";

    /// <summary>Postal-code lookup for addresses (design 44): off until a tenant switches it on (constitution rule 4).</summary>
    public const string AddressLookup = GroupName + ".AddressLookup";
}
