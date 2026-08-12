using OpenTms.Reporting.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.Reporting.Permissions;

public class ReportingPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(ReportingPermissions.GroupName, L("Permission:Reporting"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<ReportingResource>(name);
    }
}
