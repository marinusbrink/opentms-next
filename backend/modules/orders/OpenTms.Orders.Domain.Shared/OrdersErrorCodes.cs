namespace OpenTms.Orders;

/// <summary>
/// Business error codes of the Orders module (designs 51 and 58); the shared reference-data codes are Platform's
/// (ReferenceDataErrorCodes). The namespace prefix maps to the Orders localization resource (OrdersDomainSharedModule);
/// OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// </summary>
public static class OrdersErrorCodes
{
    public const string Namespace = "OpenTms.Orders";

    /* Design 51. */

    /// <summary>A waypoint type cannot propose itself as the type of the next stop.</summary>
    public const string SuccessorIsSelf = Namespace + ":SuccessorIsSelf";

    /// <summary>A time slot whose start and end are equal has no length.</summary>
    public const string EmptyTimeSlot = Namespace + ":EmptyTimeSlot";

    /* Design 58: the order lifecycle. */

    /// <summary>The command is not allowed in the order's current status ({from}, {to}).</summary>
    public const string InvalidTransition = Namespace + ":InvalidTransition";

    /// <summary>Only a Draft or Open order can be changed ({status}).</summary>
    public const string OrderNotEditable = Namespace + ":OrderNotEditable";

    /// <summary>The stops of a line whose leg is on a trip in progress cannot change.</summary>
    public const string StopLockedByTrip = Namespace + ":StopLockedByTrip";

    /// <summary>An order with a leg on a trip in progress or completed cannot be cancelled.</summary>
    public const string CancelRefusedTripInProgress = Namespace + ":CancelRefusedTripInProgress";

    /// <summary>An order with assigned legs cannot be deleted (orders.md scenario 45).</summary>
    public const string DeleteRefusedLegsAssigned = Namespace + ":DeleteRefusedLegsAssigned";

    /// <summary>The client's approval results differ from the current evaluation.</summary>
    public const string ApprovalResultsStale = Namespace + ":ApprovalResultsStale";

    /// <summary>An approval with an error-level finding cannot pass ({rule}).</summary>
    public const string ApprovalHasErrors = Namespace + ":ApprovalHasErrors";

    /// <summary>A field the order type requires is empty ({field}).</summary>
    public const string RequiredFieldMissing = Namespace + ":RequiredFieldMissing";

    /// <summary>A confirmed order needs at least one line.</summary>
    public const string LineRequired = Namespace + ":LineRequired";

    /// <summary>Every line needs at least two stops ({position}).</summary>
    public const string LineNeedsTwoStops = Namespace + ":LineNeedsTwoStops";

    /// <summary>A stop has both a time slot and an explicit window ({position}).</summary>
    public const string StopWindowAmbiguous = Namespace + ":StopWindowAmbiguous";

    /// <summary>A stop's address does not belong to its relation ({position}).</summary>
    public const string StopAddressMismatch = Namespace + ":StopAddressMismatch";

    /// <summary>The order date cannot leave the numbering period the number was drawn in.</summary>
    public const string OrderDatePeriodLocked = Namespace + ":OrderDatePeriodLocked";

    /// <summary>A revision needs a reason.</summary>
    public const string RevisionReasonRequired = Namespace + ":RevisionReasonRequired";

    /// <summary>The attributes exceed their bounds or use an undefined key ({key}).</summary>
    public const string InvalidAttributes = Namespace + ":InvalidAttributes";

    /// <summary>A trip request may name at most three vehicles and three drivers ({field}).</summary>
    public const string TooManyTripRequestMembers = Namespace + ":TooManyTripRequestMembers";

    /* Design 58: templates. */

    /// <summary>The template is inactive or outside its validity; nothing is created from it.</summary>
    public const string TemplateInactive = Namespace + ":TemplateInactive";

    /// <summary>Materialisation needs at least one date and 1..{max} copies.</summary>
    public const string MaterialisationInputInvalid = Namespace + ":MaterialisationInputInvalid";

    /// <summary>A materialisation override names a line the template (or the copied order) does not have ({lineId}).</summary>
    public const string MaterialisationLineUnknown = Namespace + ":MaterialisationLineUnknown";

    /// <summary>A materialisation override names a date or copy that is not generated, or names one twice ({date}, {copyIndex}).</summary>
    public const string MaterialisationOverrideInvalid = Namespace + ":MaterialisationOverrideInvalid";

    /* UI-GRID-013: the list export. */

    /// <summary>The export would hold {count} rows; at most {max} are allowed: filter the list first.</summary>
    public const string ExportTooManyRows = Namespace + ":ExportTooManyRows";

    /// <summary>The export names a column the list does not have ({field}).</summary>
    public const string ExportUnknownColumn = Namespace + ":ExportUnknownColumn";

    /// <summary>A schedule's interval, days or day of month are out of range ({field}).</summary>
    public const string InvalidSchedule = Namespace + ":InvalidSchedule";

    /* Design 58: the consignment note. */

    /// <summary>No report definition of purpose ConsignmentNote applies: pick a template in the Master data app first.</summary>
    public const string ConsignmentNoteTemplateMissing = Namespace + ":ConsignmentNoteTemplateMissing";

    /// <summary>The renderer did not answer within the print budget.</summary>
    public const string RenderTimedOut = Namespace + ":RenderTimedOut";

    /// <summary>A print needs lines of one order or a trip, and found none.</summary>
    public const string NothingToPrint = Namespace + ":NothingToPrint";

    /// <summary>The copies of a consignment note are 1..5.</summary>
    public const string InvalidCopies = Namespace + ":InvalidCopies";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        InvalidTransition, OrderNotEditable, StopLockedByTrip, CancelRefusedTripInProgress, DeleteRefusedLegsAssigned,
        ApprovalResultsStale, ApprovalHasErrors, OrderDatePeriodLocked, TemplateInactive, RenderTimedOut
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        SuccessorIsSelf, EmptyTimeSlot, RequiredFieldMissing, LineRequired, LineNeedsTwoStops, StopWindowAmbiguous,
        StopAddressMismatch, RevisionReasonRequired, InvalidAttributes, TooManyTripRequestMembers, MaterialisationInputInvalid,
        InvalidSchedule, ConsignmentNoteTemplateMissing, NothingToPrint, InvalidCopies, MaterialisationLineUnknown,
        MaterialisationOverrideInvalid, ExportTooManyRows, ExportUnknownColumn
    ];
}

/// <summary>
/// Warning codes the Orders module stores on its records (never thrown, so no HTTP status); the namespace prefix maps to
/// the Orders localization resource like the error codes, so a client localizes them the same way.
/// </summary>
public static class OrdersWarningCodes
{
    /// <summary>The order's trip request was not honoured: {field} does not resolve in Planning; the legs stay unplanned (design 59 build note 13).</summary>
    public const string TripRequestNotHonoured = OrdersErrorCodes.Namespace + ":TripRequestNotHonoured";
}
