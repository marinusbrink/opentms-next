using OpenTms.PlanningExecution.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.PlanningExecution.Permissions;

public class PlanningExecutionPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(PlanningExecutionPermissions.GroupName, L("Permission:PlanningExecution"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlanningExecutionResource>(name);
    }
}
