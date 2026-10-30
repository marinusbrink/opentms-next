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

/// <summary>
/// How a leg came to exist (design 59 build note): the generator writes <see cref="Generated"/> and <see cref="ViaDepot"/>
/// legs and owns them; the planner's operations (trips.md 20–24, design 61 "Order page review" point 5) create the others,
/// which regeneration keeps.
/// </summary>
public enum LegSource
{
    Generated = 1,
    ViaDepot = 2,
    Split = 3,
    Return = 4,
    Outsourced = 5,

    /// <summary>One half of a leg split at an address (A→X or X→B, design 61 point 5); <c>SplitFromLegId</c> is the leg it replaced.</summary>
    SplitAtAddress = 6
}
