using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using P = OpenTms.Orders.Permissions.OrdersPermissions;

namespace OpenTms.Orders.Permissions;

/// <summary>
/// The Orders permissions of the predefined roles (docs/designs/71-predefined-roles.md). Approving orders belongs to
/// both Order entry and Finance (PO 2026-11-02).
/// </summary>
public static class OrdersPredefinedRoles
{
    private static readonly string[] SettingViews =
    [
        P.TransportOrderTypes.Default, P.WaypointTypes.Default, P.TimeSlots.Default, P.TransportOrderTemplates.Default,
        P.ConsignmentNoteProfiles.Default, P.ConsignmentNoteFixedDescriptions.Default, P.OrderAttributeDefinitions.Default,
    ];

    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.OrderEntry, SettingViews);
        options.Grant(PlatformRoleNames.OrderEntry,
            P.TransportOrders.Default, P.TransportOrders.Create, P.TransportOrders.Update, P.TransportOrders.Confirm,
            P.TransportOrders.Cancel, P.TransportOrders.Approve, P.TransportOrders.Revise, P.TransportOrders.Print,
            P.TransportOrderTemplates.Create, P.TransportOrderTemplates.Update);

        options.Grant(PlatformRoleNames.Planner, SettingViews);
        options.Grant(PlatformRoleNames.Planner,
            P.TransportOrders.Default, P.TransportOrders.Update, P.TransportOrders.Print);

        options.Grant(PlatformRoleNames.Finance, SettingViews);
        options.Grant(PlatformRoleNames.Finance,
            P.TransportOrders.Default, P.TransportOrders.Approve, P.TransportOrders.Archive, P.TransportOrders.Print);

        options.Grant(PlatformRoleNames.MasterDataManager,
            P.TransportOrderTypes.Default, P.TransportOrderTypes.Create, P.TransportOrderTypes.Update, P.TransportOrderTypes.Delete,
            P.WaypointTypes.Default, P.WaypointTypes.Create, P.WaypointTypes.Update, P.WaypointTypes.Delete,
            P.TimeSlots.Default, P.TimeSlots.Create, P.TimeSlots.Update, P.TimeSlots.Delete,
            P.TransportOrderTemplates.Default, P.TransportOrderTemplates.Create, P.TransportOrderTemplates.Update, P.TransportOrderTemplates.Delete,
            P.ConsignmentNoteProfiles.Default, P.ConsignmentNoteProfiles.Create, P.ConsignmentNoteProfiles.Update, P.ConsignmentNoteProfiles.Delete,
            P.ConsignmentNoteFixedDescriptions.Default, P.ConsignmentNoteFixedDescriptions.Create, P.ConsignmentNoteFixedDescriptions.Update, P.ConsignmentNoteFixedDescriptions.Delete,
            P.OrderAttributeDefinitions.Default, P.OrderAttributeDefinitions.Create, P.OrderAttributeDefinitions.Update, P.OrderAttributeDefinitions.Delete);

        options.Grant(PlatformRoleNames.ReadOnly, SettingViews);
        options.Grant(PlatformRoleNames.ReadOnly, P.TransportOrders.Default);
    }
}
