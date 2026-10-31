namespace OpenTms.PlanningExecution.Planboard;

/// <summary>
/// The resource a Scheduler shows per row and a Planboard resource command changes (design 68): tractor = the motor
/// vehicles (Master Data kinds Tractor, Rigid, Van), trailer = Trailer, driver = the drivers.
/// </summary>
public enum PlanboardResourceMode
{
    Tractor = 1,
    Trailer = 2,
    Driver = 3
}

/// <summary>The trip header fields the trips pane edits inline (design 68 "Update header field").</summary>
public enum PlanboardTripField
{
    Name = 1,
    Remarks = 2,
    DriverInstructions = 3
}

/// <summary>The groupings of the unplanned leg list (design 68 "Legs without a trip", PO decision D18).</summary>
public static class LegGroupings
{
    public const string OriginDate = "origindate";
    public const string Customer = "customer";
    public const string OriginCity = "origincity";
    public const string DestinationCity = "destinationcity";

    public static readonly string[] All = [OriginDate, Customer, OriginCity, DestinationCity];
}
