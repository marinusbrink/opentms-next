namespace OpenTms.Orders.Intake;

/// <summary>
/// The state of an order intake (design 72 "Orders: intake, resolution, review"): it waits for a clerk, became an order
/// (Draft or Confirmed), or was rejected.
/// </summary>
public enum OrderIntakeStatus
{
    NeedsReview = 1,
    OrderCreated = 2,
    Rejected = 3
}

/// <summary>What a learned alias resolves to (design 72 "Review"): the text a document used, mapped to a master-data row.</summary>
public enum IntakeAliasKind
{
    Customer = 1,
    Address = 2,
    OrderType = 3,
    WaypointType = 4,
    TransportUnit = 5,
    Product = 6,
    Temperature = 7
}

/// <summary>The role of a stop in the canonical intake model (design 72 <c>OrderIntakeDto</c>).</summary>
public enum IntakeStopRole
{
    Pickup = 1,
    Delivery = 2
}

/// <summary>How far one field of an intake is resolved against master data (design 72 "Resolution").</summary>
public enum IntakeResolutionStatus
{
    Resolved = 1,
    Ambiguous = 2,
    Unresolved = 3
}

/// <summary>
/// Which rule of the resolver resolved a field (design 72 "Resolution"). Only Given, Exact, Alias, Pattern and Default
/// may lead to an automatically confirmed order; Match and User never do.
/// </summary>
public enum IntakeResolutionMethod
{
    Given = 1,
    Exact = 2,
    Alias = 3,
    Match = 4,
    Default = 5,
    Pattern = 6,
    User = 7
}
