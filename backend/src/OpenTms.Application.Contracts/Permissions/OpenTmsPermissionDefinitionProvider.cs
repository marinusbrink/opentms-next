using OpenTms.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Permissions;

public class OpenTmsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(OpenTmsPermissions.GroupName);

        //Define your own permissions here. Example:
        //myGroup.AddPermission(OpenTmsPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OpenTmsResource>(name);
    }
}
