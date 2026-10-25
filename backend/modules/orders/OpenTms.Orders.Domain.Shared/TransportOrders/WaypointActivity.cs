namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// What happens at a stop of a waypoint type (design 51, assessment §3.9: the stop's semantics are typed, never matched
/// on the type's name). Load and Unload move freight; the others do not.
/// </summary>
public enum WaypointActivity
{
    Load = 1,
    Unload = 2,
    Via = 3,
    CoupleTrailer = 4,
    UncoupleTrailer = 5,
    Customs = 6,
    Cleaning = 7,
    Rest = 8,
    Refuel = 9,
    Other = 10
}
