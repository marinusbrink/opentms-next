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
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IntegrationsResource>(name);
    }
}
