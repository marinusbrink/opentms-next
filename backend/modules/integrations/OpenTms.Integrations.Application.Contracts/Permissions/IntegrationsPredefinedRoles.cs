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

        /* Design 72 "Predefined roles": the mailboxes and API clients of the order intake. */
        options.Grant(PlatformRoleNames.MasterDataManager,
            IntegrationsPermissions.Mailboxes.Default,
            IntegrationsPermissions.Mailboxes.Manage,
            IntegrationsPermissions.ApiClients.Default,
            IntegrationsPermissions.ApiClients.Manage);

        /* Design 72 build note 25: the order intake's settings go with the role that manages the mailboxes. One permission
         * views and changes them, so Read-only does not get it. */
        options.Grant(PlatformRoleNames.MasterDataManager,
            IntegrationsPermissions.OrderIntakeSettings);

        options.Grant(PlatformRoleNames.ReadOnly,
            IntegrationsPermissions.Mailboxes.Default,
            IntegrationsPermissions.ApiClients.Default);

        /* Design 72 "Permissions": the received messages and their trace, and the import profiles. (The mailbox and API
         * client permissions of the same design belong with the channels.) */
        options.Grant(PlatformRoleNames.OrderEntry,
            IntegrationsPermissions.InboundMessages.Default);

        options.Grant(PlatformRoleNames.Planner,
            IntegrationsPermissions.InboundMessages.Default);

        options.Grant(PlatformRoleNames.MasterDataManager,
            IntegrationsPermissions.ImportProfiles.Default,
            IntegrationsPermissions.ImportProfiles.Manage,
            IntegrationsPermissions.InboundMessages.Default,
            IntegrationsPermissions.InboundMessages.Reprocess);

        options.Grant(PlatformRoleNames.ReadOnly,
            IntegrationsPermissions.InboundMessages.Default,
            IntegrationsPermissions.ImportProfiles.Default);

        options.Grant(PlatformRoleNames.PlatformOperator,
            IntegrationsPermissions.ServiceSettings.Default,
            IntegrationsPermissions.ServiceSettings.Update,
            IntegrationsPermissions.ServiceSettings.Test);
    }
}
