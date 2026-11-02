using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using P = OpenTms.PlanningExecution.Permissions.PlanningExecutionPermissions;

namespace OpenTms.PlanningExecution.Permissions;

/// <summary>The Planning &amp; Execution permissions of the predefined roles (docs/designs/71-predefined-roles.md).</summary>
public static class PlanningExecutionPredefinedRoles
{
    private static readonly string[] SettingViews =
    [
        P.TransportTripTypes.Default, P.ViaDepotRules.Default, P.TripAttributeDefinitions.Default,
    ];

    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.OrderEntry, SettingViews);
        options.Grant(PlatformRoleNames.OrderEntry, P.TransportTrips.Default);

        options.Grant(PlatformRoleNames.Planner, SettingViews);
        options.Grant(PlatformRoleNames.Planner,
            P.TransportTrips.Default, P.TransportTrips.Create, P.TransportTrips.Update, P.TransportTrips.Delete,
            P.TransportTrips.Cancel, P.TransportTrips.Archive, P.TransportTrips.Start, P.TransportTrips.Complete,
            P.TransportTrips.AssignLegs, P.TransportTrips.ManageStops,
            P.Legs.Default, P.Legs.Update,
            P.ResourceAvailability.Default, P.ResourceAvailability.Manage);

        options.Grant(PlatformRoleNames.Finance, SettingViews);
        options.Grant(PlatformRoleNames.Finance, P.TransportTrips.Default);

        options.Grant(PlatformRoleNames.MasterDataManager,
            P.TransportTripTypes.Default, P.TransportTripTypes.Create, P.TransportTripTypes.Update, P.TransportTripTypes.Delete,
            P.ViaDepotRules.Default, P.ViaDepotRules.Create, P.ViaDepotRules.Update, P.ViaDepotRules.Delete,
            P.TripAttributeDefinitions.Default, P.TripAttributeDefinitions.Create, P.TripAttributeDefinitions.Update, P.TripAttributeDefinitions.Delete);

        options.Grant(PlatformRoleNames.ReadOnly, SettingViews);
        options.Grant(PlatformRoleNames.ReadOnly, P.TransportTrips.Default, P.Legs.Default, P.ResourceAvailability.Default);
    }
}
