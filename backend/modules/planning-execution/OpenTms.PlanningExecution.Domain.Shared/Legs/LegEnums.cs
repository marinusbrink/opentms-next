namespace OpenTms.PlanningExecution.Legs;

/// <summary>Where a via-depot rule applies (design 59 "ViaDepotRule"; OpenTMS relation → customer → region order).</summary>
public enum ViaDepotScope
{
    Relation = 1,
    Customer = 2,
    Region = 3
}

/// <summary>A leg's end on a trip stop (design 59 "TripStopLeg").</summary>
public enum StopLegRole
{
    Origin = 1,
    Destination = 2
}
