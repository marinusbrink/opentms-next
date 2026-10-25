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

        /* Design 56 (Q14), 59: trips and legs are tenant data. */
        var trips = group.AddPermission(PlanningExecutionPermissions.TransportTrips.Default, L("Permission:TransportTrips"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Create, L("Permission:TransportTrips.Create"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Update, L("Permission:TransportTrips.Update"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Delete, L("Permission:TransportTrips.Delete"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Cancel, L("Permission:TransportTrips.Cancel"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Archive, L("Permission:TransportTrips.Archive"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Start, L("Permission:TransportTrips.Start"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.Complete, L("Permission:TransportTrips.Complete"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.AssignLegs, L("Permission:TransportTrips.AssignLegs"), MultiTenancySides.Tenant);
        trips.AddChild(PlanningExecutionPermissions.TransportTrips.ManageStops, L("Permission:TransportTrips.ManageStops"), MultiTenancySides.Tenant);

        var legs = group.AddPermission(PlanningExecutionPermissions.Legs.Default, L("Permission:Legs"), MultiTenancySides.Tenant);
        legs.AddChild(PlanningExecutionPermissions.Legs.Update, L("Permission:Legs.Update"), MultiTenancySides.Tenant);

        var viaDepotRules = group.AddPermission(PlanningExecutionPermissions.ViaDepotRules.Default, L("Permission:ViaDepotRules"), MultiTenancySides.Both);
        viaDepotRules.AddChild(PlanningExecutionPermissions.ViaDepotRules.Create, L("Permission:ViaDepotRules.Create"), MultiTenancySides.Both);
        viaDepotRules.AddChild(PlanningExecutionPermissions.ViaDepotRules.Update, L("Permission:ViaDepotRules.Update"), MultiTenancySides.Both);
        viaDepotRules.AddChild(PlanningExecutionPermissions.ViaDepotRules.Delete, L("Permission:ViaDepotRules.Delete"), MultiTenancySides.Both);

        var attributes = group.AddPermission(PlanningExecutionPermissions.TripAttributeDefinitions.Default, L("Permission:TripAttributeDefinitions"), MultiTenancySides.Both);
        attributes.AddChild(PlanningExecutionPermissions.TripAttributeDefinitions.Create, L("Permission:TripAttributeDefinitions.Create"), MultiTenancySides.Both);
        attributes.AddChild(PlanningExecutionPermissions.TripAttributeDefinitions.Update, L("Permission:TripAttributeDefinitions.Update"), MultiTenancySides.Both);
        attributes.AddChild(PlanningExecutionPermissions.TripAttributeDefinitions.Delete, L("Permission:TripAttributeDefinitions.Delete"), MultiTenancySides.Both);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlanningExecutionResource>(name);
    }
}
