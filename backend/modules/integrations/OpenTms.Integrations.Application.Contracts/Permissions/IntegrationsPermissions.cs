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

    /// <summary>Design 72: the connected order mailboxes (tenant side).</summary>
    public static class Mailboxes
    {
        public const string Default = GroupName + ".Mailboxes";
        /// <summary>Connect, reconnect, change, pause, delete.</summary>
        public const string Manage = Default + ".Manage";
    }

    /// <summary>Design 72: the API clients of the intake API (tenant side).</summary>
    public static class ApiClients
    {
        public const string Default = GroupName + ".ApiClients";
        /// <summary>Create, change, regenerate the secret, deactivate, delete.</summary>
        public const string Manage = Default + ".Manage";
    }

    /// <summary>Design 72: the learned import profiles (tenant side).</summary>
    public static class ImportProfiles
    {
        public const string Default = GroupName + ".ImportProfiles";
        /// <summary>Activate, retire, rename, delete.</summary>
        public const string Manage = Default + ".Manage";
    }

    /// <summary>Design 72: received messages, their trace and originals (tenant side).</summary>
    public static class InboundMessages
    {
        public const string Default = GroupName + ".InboundMessages";
        public const string Reprocess = Default + ".Reprocess";
    }

    /// <summary>Design 72: the intake API. Both sides; granted only to API clients (client provider), never to a role.</summary>
    public static class IntakeApi
    {
        public const string Submit = GroupName + ".IntakeApi.Submit";
    }
}
