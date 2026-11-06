using OpenTms.Integrations.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.Permissions;

public class IntegrationsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(IntegrationsPermissions.GroupName, L("Permission:Integrations"));
        group.AddPermission(IntegrationsPermissions.AddressLookup, L("Permission:AddressLookup"), MultiTenancySides.Both);
        group.AddPermission(IntegrationsPermissions.Routing, L("Permission:Routing"), MultiTenancySides.Both);
        /* Administration → Services: host only, one configuration of the external services for every tenant. */
        var serviceSettings = group.AddPermission(IntegrationsPermissions.ServiceSettings.Default, L("Permission:ServiceSettings"), MultiTenancySides.Host);
        serviceSettings.AddChild(IntegrationsPermissions.ServiceSettings.Update, L("Permission:ServiceSettings.Update"), MultiTenancySides.Host);
        serviceSettings.AddChild(IntegrationsPermissions.ServiceSettings.Test, L("Permission:ServiceSettings.Test"), MultiTenancySides.Host);

        /* Design 72: the order intake, tenant data only. */
        var mailboxes = group.AddPermission(IntegrationsPermissions.Mailboxes.Default, L("Permission:Mailboxes"), MultiTenancySides.Tenant);
        mailboxes.AddChild(IntegrationsPermissions.Mailboxes.Manage, L("Permission:Mailboxes.Manage"), MultiTenancySides.Tenant);
        var apiClients = group.AddPermission(IntegrationsPermissions.ApiClients.Default, L("Permission:ApiClients"), MultiTenancySides.Tenant);
        apiClients.AddChild(IntegrationsPermissions.ApiClients.Manage, L("Permission:ApiClients.Manage"), MultiTenancySides.Tenant);
        var profiles = group.AddPermission(IntegrationsPermissions.ImportProfiles.Default, L("Permission:ImportProfiles"), MultiTenancySides.Tenant);
        profiles.AddChild(IntegrationsPermissions.ImportProfiles.Manage, L("Permission:ImportProfiles.Manage"), MultiTenancySides.Tenant);
        var messages = group.AddPermission(IntegrationsPermissions.InboundMessages.Default, L("Permission:InboundMessages"), MultiTenancySides.Tenant);
        messages.AddChild(IntegrationsPermissions.InboundMessages.Reprocess, L("Permission:InboundMessages.Reprocess"), MultiTenancySides.Tenant);
        /* Design 72 build note 25: the order intake's tenant settings (the name similarity threshold). */
        group.AddPermission(IntegrationsPermissions.OrderIntakeSettings, L("Permission:OrderIntakeSettings"), MultiTenancySides.Tenant);
        /* Design 74: the AI connector, tenant data only. */
        group.AddPermission(IntegrationsPermissions.AiConnector.Use, L("Permission:AiConnector.Use"), MultiTenancySides.Tenant);
        group.AddPermission(IntegrationsPermissions.AiConnector.Manage, L("Permission:AiConnector.Manage"), MultiTenancySides.Tenant);
        /* The intake API's permission is granted to API clients only (ABP's client provider "C"), so no role editor offers it. */
        group.AddPermission(IntegrationsPermissions.IntakeApi.Submit, L("Permission:IntakeApi.Submit"), MultiTenancySides.Both)
            .WithProviders(ClientPermissionValueProvider.ProviderName);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IntegrationsResource>(name);
    }
}
