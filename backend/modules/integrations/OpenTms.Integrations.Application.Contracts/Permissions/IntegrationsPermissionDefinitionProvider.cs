using OpenTms.Integrations.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.Integrations.Permissions;

public class IntegrationsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(IntegrationsPermissions.GroupName, L("Permission:Integrations"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IntegrationsResource>(name);
    }
}
