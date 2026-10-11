using OpenTms.MasterData.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Permissions;

public class MasterDataPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(MasterDataPermissions.GroupName, L("Permission:MasterData"));
        var bothSides = MultiTenancySides.Host | MultiTenancySides.Tenant;

        foreach (var (entity, view, create, update, delete) in MasterDataPermissions.All)
        {
            var permission = group.AddPermission(view, L($"Permission:{entity}"), bothSides);
            permission.AddChild(create, L($"Permission:{entity}.Create"), bothSides);
            permission.AddChild(update, L($"Permission:{entity}.Update"), bothSides);
            permission.AddChild(delete, L($"Permission:{entity}.Delete"), bothSides);
        }
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<MasterDataResource>(name);
    }
}
