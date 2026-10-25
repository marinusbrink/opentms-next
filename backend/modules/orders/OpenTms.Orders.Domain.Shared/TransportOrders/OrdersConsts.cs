namespace OpenTms.Orders.TransportOrders;

/// <summary>The bounds of the Orders master data (design 51).</summary>
public static class OrdersConsts
{
    /// <summary>The stops a new order of a type starts with.</summary>
    public const int MaxDefaultWaypoints = 10;

    /// <summary>The on-board computer codes of one waypoint type.</summary>
    public const int MaxFleetManagementCodes = 20;

    public const int MaxFleetManagementVendorLength = 64;

    public const int MaxFleetManagementCodeLength = 64;

    /// <summary>The planned time at a stop: at most a day.</summary>
    public const int MaxDefaultDurationMinutes = 1440;
}
