using OpenTms.Orders.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.Permissions;

public class OrdersPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(OrdersPermissions.GroupName, L("Permission:Orders"));

        /* Both sides (assessment §3.11, §4: host-level reference data is parked but must stay possible). */
        var orderTypes = group.AddPermission(OrdersPermissions.TransportOrderTypes.Default, L("Permission:TransportOrderTypes"), MultiTenancySides.Both);
        orderTypes.AddChild(OrdersPermissions.TransportOrderTypes.Create, L("Permission:TransportOrderTypes.Create"), MultiTenancySides.Both);
        orderTypes.AddChild(OrdersPermissions.TransportOrderTypes.Update, L("Permission:TransportOrderTypes.Update"), MultiTenancySides.Both);
        orderTypes.AddChild(OrdersPermissions.TransportOrderTypes.Delete, L("Permission:TransportOrderTypes.Delete"), MultiTenancySides.Both);

        var waypointTypes = group.AddPermission(OrdersPermissions.WaypointTypes.Default, L("Permission:WaypointTypes"), MultiTenancySides.Both);
        waypointTypes.AddChild(OrdersPermissions.WaypointTypes.Create, L("Permission:WaypointTypes.Create"), MultiTenancySides.Both);
        waypointTypes.AddChild(OrdersPermissions.WaypointTypes.Update, L("Permission:WaypointTypes.Update"), MultiTenancySides.Both);
        waypointTypes.AddChild(OrdersPermissions.WaypointTypes.Delete, L("Permission:WaypointTypes.Delete"), MultiTenancySides.Both);

        var timeSlots = group.AddPermission(OrdersPermissions.TimeSlots.Default, L("Permission:TimeSlots"), MultiTenancySides.Both);
        timeSlots.AddChild(OrdersPermissions.TimeSlots.Create, L("Permission:TimeSlots.Create"), MultiTenancySides.Both);
        timeSlots.AddChild(OrdersPermissions.TimeSlots.Update, L("Permission:TimeSlots.Update"), MultiTenancySides.Both);
        timeSlots.AddChild(OrdersPermissions.TimeSlots.Delete, L("Permission:TimeSlots.Delete"), MultiTenancySides.Both);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OrdersResource>(name);
    }
}
