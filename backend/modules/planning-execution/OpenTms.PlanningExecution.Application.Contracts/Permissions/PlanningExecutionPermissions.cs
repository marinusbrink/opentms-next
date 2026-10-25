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
}
