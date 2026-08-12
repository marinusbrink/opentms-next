using OpenTms.Platform.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Permissions;

public class PlatformPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(PlatformPermissions.GroupName, L("Permission:Platform"));

        group.AddPermission(
            PlatformPermissions.TenantProvisioning.Manage,
            L("Permission:TenantProvisioning.Manage"),
            MultiTenancySides.Host);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlatformResource>(name);
    }
}
