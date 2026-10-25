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
        Crud(group, OrdersPermissions.TransportOrderTypes.Default, "TransportOrderTypes");
        Crud(group, OrdersPermissions.WaypointTypes.Default, "WaypointTypes");
        Crud(group, OrdersPermissions.TimeSlots.Default, "TimeSlots");

        /* Design 56/58 (Q14): one permission per action on the transport order. */
        var orders = Crud(group, OrdersPermissions.TransportOrders.Default, "TransportOrders");
        orders.AddChild(OrdersPermissions.TransportOrders.Confirm, L("Permission:TransportOrders.Confirm"), MultiTenancySides.Both);
        orders.AddChild(OrdersPermissions.TransportOrders.Cancel, L("Permission:TransportOrders.Cancel"), MultiTenancySides.Both);
        orders.AddChild(OrdersPermissions.TransportOrders.Approve, L("Permission:TransportOrders.Approve"), MultiTenancySides.Both);
        orders.AddChild(OrdersPermissions.TransportOrders.Revise, L("Permission:TransportOrders.Revise"), MultiTenancySides.Both);
        orders.AddChild(OrdersPermissions.TransportOrders.Archive, L("Permission:TransportOrders.Archive"), MultiTenancySides.Both);
        orders.AddChild(OrdersPermissions.TransportOrders.Print, L("Permission:TransportOrders.Print"), MultiTenancySides.Both);

        Crud(group, OrdersPermissions.TransportOrderTemplates.Default, "TransportOrderTemplates");
        Crud(group, OrdersPermissions.ConsignmentNoteProfiles.Default, "ConsignmentNoteProfiles");
        Crud(group, OrdersPermissions.ConsignmentNoteFixedDescriptions.Default, "ConsignmentNoteFixedDescriptions");
        Crud(group, OrdersPermissions.OrderAttributeDefinitions.Default, "OrderAttributeDefinitions");
    }

    private static PermissionDefinition Crud(PermissionGroupDefinition group, string name, string entity)
    {
        var permission = group.AddPermission(name, L("Permission:" + entity), MultiTenancySides.Both);
        permission.AddChild(name + ".Create", L($"Permission:{entity}.Create"), MultiTenancySides.Both);
        permission.AddChild(name + ".Update", L($"Permission:{entity}.Update"), MultiTenancySides.Both);
        permission.AddChild(name + ".Delete", L($"Permission:{entity}.Delete"), MultiTenancySides.Both);
        return permission;
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OrdersResource>(name);
    }
}
