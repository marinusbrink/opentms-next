namespace OpenTms.PlanningExecution;

/// <summary>
/// Business error codes of the Planning &amp; Execution module (design 59 "Lifecycle and commands": the refusals). The
/// namespace prefix maps to the module's localization resource (PlanningExecutionDomainSharedModule); OpenTms.HttpApi maps
/// the conflict codes to HTTP 409 and the request codes to 400 (added at integration, design 56 "Platform changes" 3).
/// The design names them "Planning:…"; the constants drop that prefix because the code namespace is the module name.
/// </summary>
public static class PlanningExecutionErrorCodes
{
    public const string Namespace = "OpenTms.PlanningExecution";

    /* Conflicts with the current state (409). */

    /// <summary>The trip is cancelled, completed or archived and takes no legs.</summary>
    public const string TripNotAssignable = Namespace + ":TripNotAssignable";

    /// <summary>The leg is on another trip; move it instead of assigning it.</summary>
    public const string LegOnOtherTrip = Namespace + ":LegOnOtherTrip";

    /// <summary>The leg is not on this trip.</summary>
    public const string LegNotOnTrip = Namespace + ":LegNotOnTrip";

    /// <summary>A leg's planned dates fall outside the trip's execution window; <c>allowOutsideWindow</c> overrides.</summary>
    public const string LegOutsideTripWindow = Namespace + ":LegOutsideTripWindow";

    /// <summary>A stop with actual times cannot be moved or removed.</summary>
    public const string StopHasActuals = Namespace + ":StopHasActuals";

    /// <summary>The status machine refuses the transition (design 56 "Status machines").</summary>
    public const string InvalidTransition = Namespace + ":InvalidTransition";

    /// <summary>An archived trip is read-only until it is unarchived.</summary>
    public const string TripArchived = Namespace + ":TripArchived";

    /// <summary>The trip date may not leave the numbering period of its number (as orders).</summary>
    public const string TripDateLocked = Namespace + ":TripDateLocked";

    /// <summary>Only a manual stop can be removed; the others follow their legs.</summary>
    public const string NotManualStop = Namespace + ":NotManualStop";

    /// <summary>The leg's order is cancelled; its legs cannot be planned.</summary>
    public const string OrderCancelled = Namespace + ":OrderCancelled";

    /// <summary>The mirrored leg already exists.</summary>
    public const string ReturnLegExists = Namespace + ":ReturnLegExists";

    /* The request itself is wrong (400). */

    public const string TooManyVehicles = Namespace + ":TooManyVehicles";

    public const string TooManyDrivers = Namespace + ":TooManyDrivers";

    public const string TooManyStops = Namespace + ":TooManyStops";

    public const string TooManyLegs = Namespace + ":TooManyLegs";

    /// <summary>A trip carries either a vehicle combination or up to three vehicle ids, not both.</summary>
    public const string VehicleSourceConflict = Namespace + ":VehicleSourceConflict";

    /// <summary>A split quantity must be positive and below the leg's quantity.</summary>
    public const string InvalidSplit = Namespace + ":InvalidSplit";

    /// <summary>Attributes: too many keys, a key or value too long, an empty key.</summary>
    public const string InvalidAttributes = Namespace + ":InvalidAttributes";

    /// <summary>A stop position outside 1..n.</summary>
    public const string InvalidPosition = Namespace + ":InvalidPosition";

    /// <summary>A leg's planned date must stay within the stop's window.</summary>
    public const string LegDateOutsideStopWindow = Namespace + ":LegDateOutsideStopWindow";

    /// <summary>A leg's planned time window ends before it starts ({field}).</summary>
    public const string InvalidLegWindow = Namespace + ":InvalidLegWindow";

    /// <summary>The export would hold {count} rows; at most {max} are allowed: filter the list first.</summary>
    public const string ExportTooManyRows = Namespace + ":ExportTooManyRows";

    /// <summary>The export names a column the list does not have ({field}).</summary>
    public const string ExportUnknownColumn = Namespace + ":ExportUnknownColumn";

    /// <summary>A via-depot rule's scope needs its relation or region, and only that one.</summary>
    public const string InvalidViaDepotScope = Namespace + ":InvalidViaDepotScope";

    /// <summary>The planned arrival lies before the planned departure.</summary>
    public const string ArrivalBeforeDeparture = Namespace + ":ArrivalBeforeDeparture";

    /// <summary>The target trip of a move or outsource must be another trip.</summary>
    public const string SameTrip = Namespace + ":SameTrip";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        TripNotAssignable, LegOnOtherTrip, LegNotOnTrip, LegOutsideTripWindow, StopHasActuals, InvalidTransition, TripArchived,
        TripDateLocked, NotManualStop, OrderCancelled, ReturnLegExists
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        TooManyVehicles, TooManyDrivers, TooManyStops, TooManyLegs, VehicleSourceConflict, InvalidSplit, InvalidAttributes,
        InvalidPosition, LegDateOutsideStopWindow, InvalidViaDepotScope, ArrivalBeforeDeparture, SameTrip, InvalidLegWindow,
        ExportTooManyRows, ExportUnknownColumn
    ];
}
