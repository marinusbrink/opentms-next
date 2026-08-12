using OpenTms.MasterData.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.MasterData.Permissions;

public class MasterDataPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(MasterDataPermissions.GroupName, L("Permission:MasterData"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<MasterDataResource>(name);
    }
}
