namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>The trip's lifecycle (design 56 "Status machines", PO decision Q7): a status, not OpenTMS's two booleans.</summary>
public enum TransportTripStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>How legs become trip stops (OpenTMS <c>StopCreationMode</c>; design 59 "Stop composition").</summary>
public enum StopCompositionMode
{
    /// <summary>Legs loading or unloading at the same address with the same activity share one stop.</summary>
    MergeByLocationAndActivity = 1,

    /// <summary>Every leg gets its own origin and destination stop (shuttle trips).</summary>
    OneStopPairPerLeg = 2
}

/// <summary>Whether the trip's departure and arrival follow its first and last stop (OpenTMS trip-type flags).</summary>
[System.Flags]
public enum FirstLastStopRule
{
    None = 0,
    DepartureIsFirstStop = 1,
    ArrivalIsLastStop = 2,
    Both = 3
}

/// <summary>
/// The warnings the trip calculator stores on the trip (design 59, the <c>Warnings</c> jsonb list): codes, never texts,
/// so the screen localises them (OpenTMS computed them on every grid read).
/// </summary>
public enum TripWarningCode
{
    MissingTransportCharacteristic = 1,
    IncompatibleTemperatures = 2,
    IncompatibleProducts = 3,
    IncompatibleTransportUnits = 4,
    LegOutsideTripWindow = 5,
    EtaOutsideStopWindow = 6,
    LegChangedAfterPlanning = 7,
    AddressNotGeocoded = 8,
    NoCostPriceSource = 9,
    RoutingNotConfigured = 10,
    CapacityExceeded = 11
}

/// <summary>The kind of change behind a <c>TransportTripChangedEto</c>.</summary>
public enum TripChangeKind
{
    Header = 1,
    Carrier = 2,
    Vehicles = 3,
    Drivers = 4,
    Distance = 5,
    Stops = 6
}
