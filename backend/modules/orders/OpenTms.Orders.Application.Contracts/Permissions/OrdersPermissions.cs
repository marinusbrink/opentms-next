namespace OpenTms.Orders.Permissions;

public static class OrdersPermissions
{
    public const string GroupName = "Orders";

    /// <summary>Design 51: the transport order types list of the Master data app.</summary>
    public static class TransportOrderTypes
    {
        public const string Default = GroupName + ".TransportOrderTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 51: the waypoint types list of the Master data app.</summary>
    public static class WaypointTypes
    {
        public const string Default = GroupName + ".WaypointTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 51: the time slots list of the Master data app.</summary>
    public static class TimeSlots
    {
        public const string Default = GroupName + ".TimeSlots";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 56/58 (Q14): the transport orders of the Transport app, one permission per action.</summary>
    public static class TransportOrders
    {
        public const string Default = GroupName + ".TransportOrders";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Confirm = Default + ".Confirm";
        public const string Cancel = Default + ".Cancel";
        public const string Approve = Default + ".Approve";
        public const string Revise = Default + ".Revise";
        public const string Archive = Default + ".Archive";
        public const string Print = Default + ".Print";
    }

    /// <summary>Design 58: the templates of the Transport app.</summary>
    public static class TransportOrderTemplates
    {
        public const string Default = GroupName + ".TransportOrderTemplates";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 58: the consignment note profiles list of the Master data app.</summary>
    public static class ConsignmentNoteProfiles
    {
        public const string Default = GroupName + ".ConsignmentNoteProfiles";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 58: the consignment note fixed descriptions list of the Master data app.</summary>
    public static class ConsignmentNoteFixedDescriptions
    {
        public const string Default = GroupName + ".ConsignmentNoteFixedDescriptions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 58 (Q12): the order attribute definitions list of the Master data app.</summary>
    public static class OrderAttributeDefinitions
    {
        public const string Default = GroupName + ".OrderAttributeDefinitions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Every permission name of the module, for the definition and localization tests.</summary>
    public static readonly string[] All =
    [
        TransportOrderTypes.Default, TransportOrderTypes.Create, TransportOrderTypes.Update, TransportOrderTypes.Delete,
        WaypointTypes.Default, WaypointTypes.Create, WaypointTypes.Update, WaypointTypes.Delete,
        TimeSlots.Default, TimeSlots.Create, TimeSlots.Update, TimeSlots.Delete,
        TransportOrders.Default, TransportOrders.Create, TransportOrders.Update, TransportOrders.Delete, TransportOrders.Confirm,
        TransportOrders.Cancel, TransportOrders.Approve, TransportOrders.Revise, TransportOrders.Archive, TransportOrders.Print,
        TransportOrderTemplates.Default, TransportOrderTemplates.Create, TransportOrderTemplates.Update, TransportOrderTemplates.Delete,
        ConsignmentNoteProfiles.Default, ConsignmentNoteProfiles.Create, ConsignmentNoteProfiles.Update, ConsignmentNoteProfiles.Delete,
        ConsignmentNoteFixedDescriptions.Default, ConsignmentNoteFixedDescriptions.Create, ConsignmentNoteFixedDescriptions.Update, ConsignmentNoteFixedDescriptions.Delete,
        OrderAttributeDefinitions.Default, OrderAttributeDefinitions.Create, OrderAttributeDefinitions.Update, OrderAttributeDefinitions.Delete
    ];
}
