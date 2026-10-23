namespace OpenTms.Orders;

/// <summary>
/// Business error codes of the Orders module (design 51); the shared reference-data codes are Platform's
/// (ReferenceDataErrorCodes). The namespace prefix maps to the Orders localization resource (OrdersDomainSharedModule);
/// OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// </summary>
public static class OrdersErrorCodes
{
    public const string Namespace = "OpenTms.Orders";

    /// <summary>A waypoint type cannot propose itself as the type of the next stop.</summary>
    public const string SuccessorIsSelf = Namespace + ":SuccessorIsSelf";

    /// <summary>A time slot whose start and end are equal has no length.</summary>
    public const string EmptyTimeSlot = Namespace + ":EmptyTimeSlot";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes = [];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes = [SuccessorIsSelf, EmptyTimeSlot];
}
