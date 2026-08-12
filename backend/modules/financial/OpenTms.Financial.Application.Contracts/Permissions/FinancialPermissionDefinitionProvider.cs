using OpenTms.Financial.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.Financial.Permissions;

public class FinancialPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(FinancialPermissions.GroupName, L("Permission:Financial"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<FinancialResource>(name);
    }
}
