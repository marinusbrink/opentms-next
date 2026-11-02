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
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IntegrationsResource>(name);
    }
}
