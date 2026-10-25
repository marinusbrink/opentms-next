using OpenTms.PlanningExecution.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.Permissions;

public class PlanningExecutionPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(PlanningExecutionPermissions.GroupName, L("Permission:PlanningExecution"));

        /* Both sides (assessment §3.11, §4: host-level reference data is parked but must stay possible). */
        var tripTypes = group.AddPermission(PlanningExecutionPermissions.TransportTripTypes.Default, L("Permission:TransportTripTypes"), MultiTenancySides.Both);
        tripTypes.AddChild(PlanningExecutionPermissions.TransportTripTypes.Create, L("Permission:TransportTripTypes.Create"), MultiTenancySides.Both);
        tripTypes.AddChild(PlanningExecutionPermissions.TransportTripTypes.Update, L("Permission:TransportTripTypes.Update"), MultiTenancySides.Both);
        tripTypes.AddChild(PlanningExecutionPermissions.TransportTripTypes.Delete, L("Permission:TransportTripTypes.Delete"), MultiTenancySides.Both);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlanningExecutionResource>(name);
    }
}
