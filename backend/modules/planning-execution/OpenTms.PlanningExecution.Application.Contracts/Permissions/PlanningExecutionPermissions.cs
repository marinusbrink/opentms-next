namespace OpenTms.PlanningExecution.Permissions;

public static class PlanningExecutionPermissions
{
    public const string GroupName = "PlanningExecution";

    /// <summary>Design 53: the transport trip types list of the Master data app.</summary>
    public static class TransportTripTypes
    {
        public const string Default = GroupName + ".TransportTripTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 56 (security quickscan, Q14) and 59: the trips of the Transport app, per action.</summary>
    public static class TransportTrips
    {
        public const string Default = GroupName + ".TransportTrips";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Cancel = Default + ".Cancel";
        public const string Archive = Default + ".Archive";
        public const string Start = Default + ".Start";
        public const string Complete = Default + ".Complete";
        public const string AssignLegs = Default + ".AssignLegs";
        public const string ManageStops = Default + ".ManageStops";
    }

    /// <summary>The legs (the unplanned list, the legs of an order or trip) and their editable fields.</summary>
    public static class Legs
    {
        public const string Default = GroupName + ".Legs";
        public const string Update = Default + ".Update";
    }

    public static class ViaDepotRules
    {
        public const string Default = GroupName + ".ViaDepotRules";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class TripAttributeDefinitions
    {
        public const string Default = GroupName + ".TripAttributeDefinitions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }
}
