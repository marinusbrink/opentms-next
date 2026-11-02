using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;

namespace OpenTms.Integrations.Permissions;

/// <summary>The Integrations permissions of the predefined roles (docs/designs/71-predefined-roles.md).</summary>
public static class IntegrationsPredefinedRoles
{
    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.OrderEntry,
            IntegrationsPermissions.AddressLookup);

        options.Grant(PlatformRoleNames.Planner,
            IntegrationsPermissions.AddressLookup,
            IntegrationsPermissions.Routing);

        options.Grant(PlatformRoleNames.MasterDataManager,
            IntegrationsPermissions.AddressLookup);

        options.Grant(PlatformRoleNames.PlatformOperator,
            IntegrationsPermissions.ServiceSettings.Default,
            IntegrationsPermissions.ServiceSettings.Update,
            IntegrationsPermissions.ServiceSettings.Test);
    }
}
