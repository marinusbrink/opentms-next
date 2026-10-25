namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>How a transport trip is normally executed (design 53): by the own fleet or by a subcontracted carrier.</summary>
public enum TripExecution
{
    OwnFleet = 1,
    Subcontracted = 2
}
