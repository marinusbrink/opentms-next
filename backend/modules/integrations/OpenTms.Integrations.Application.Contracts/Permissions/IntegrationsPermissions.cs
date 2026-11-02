namespace OpenTms.Integrations.Permissions;

public static class IntegrationsPermissions
{
    public const string GroupName = "Integrations";

    /// <summary>Ask the address lookup (design 44); granted to whoever maintains addresses.</summary>
    public const string AddressLookup = GroupName + ".AddressLookup";

    /// <summary>Read the routing provider's status (design 60); the Maintenance → Health view later.</summary>
    public const string Routing = GroupName + ".Routing";

    /// <summary>Administration → Services (host only): the settings of pdfgeneratorapi and routing for every tenant.</summary>
    public static class ServiceSettings
    {
        public const string Default = GroupName + ".ServiceSettings";
        public const string Update = Default + ".Update";
        public const string Test = Default + ".Test";
    }
}
