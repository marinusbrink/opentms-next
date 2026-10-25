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
}
