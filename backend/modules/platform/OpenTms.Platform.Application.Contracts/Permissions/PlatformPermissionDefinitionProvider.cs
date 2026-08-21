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

        var bothSides = MultiTenancySides.Host | MultiTenancySides.Tenant;

        var users = group.AddPermission(
            PlatformPermissions.Administration.Users.Default,
            L("Permission:Administration.Users"),
            bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Create, L("Permission:Administration.Users.Create"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Update, L("Permission:Administration.Users.Update"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Delete, L("Permission:Administration.Users.Delete"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.BulkDelete, L("Permission:Administration.Users.BulkDelete"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.ResetPassword, L("Permission:Administration.Users.ResetPassword"), bothSides);

        var roles = group.AddPermission(
            PlatformPermissions.Administration.Roles.Default,
            L("Permission:Administration.Roles"),
            bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Create, L("Permission:Administration.Roles.Create"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Update, L("Permission:Administration.Roles.Update"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Delete, L("Permission:Administration.Roles.Delete"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.BulkDelete, L("Permission:Administration.Roles.BulkDelete"), bothSides);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlatformResource>(name);
    }
}
