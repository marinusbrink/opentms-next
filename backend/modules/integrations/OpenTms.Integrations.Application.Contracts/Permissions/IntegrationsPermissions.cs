namespace OpenTms.Integrations.Permissions;

public static class IntegrationsPermissions
{
    public const string GroupName = "Integrations";

    /// <summary>Ask the address lookup (design 44); granted to whoever maintains addresses.</summary>
    public const string AddressLookup = GroupName + ".AddressLookup";

    /// <summary>Read the routing provider's status (design 60); the Maintenance → Health view later.</summary>
    public const string Routing = GroupName + ".Routing";
}
