using System;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The order's lifecycle (design 56 "Status machines", PO decision Q5). Draft replaces OpenTMS's PendingAcceptance and is
/// the unconfirmed order a boundary agent or a person confirms; Invoiced is written by the invoicing run only.
/// </summary>
public enum TransportOrderStatus
{
    Draft = 1,
    Open = 2,
    ReadyForInvoicing = 3,
    Invoiced = 4,
    Archived = 5,
    Cancelled = 6
}

/// <summary>Who created the order (design 58).</summary>
public enum OrderSource
{
    Screen = 1,
    Template = 2,
    Agent = 3,
    Api = 4
}

/// <summary>
/// How far the order's legs are: a stored derived value written by the order summary calculator from Planning's events
/// (design 56), never a status the client sets.
/// </summary>
public enum ExecutionProgress
{
    Unplanned = 1,
    PartlyPlanned = 2,
    Planned = 3,
    InProgress = 4,
    Delivered = 5
}

/// <summary>OpenTMS <c>FreightPaymentInstruction</c> (franking): who pays the carriage (design 58).</summary>
public enum PaymentInstruction
{
    NotSet = 0,
    Free = 1,
    NotFree = 2
}

/// <summary>Weight or volume as measured (OpenTMS MeasuredWeight1-3, MeasuredVolume1-3), a child row per measurement.</summary>
public enum MeasurementKind
{
    WeightKg = 1,
    VolumeM3 = 2
}

/// <summary>
/// The approval-for-invoicing rules (design 58 "Approval", PO decision Q6): a typed set instead of OpenTMS's class names in
/// a tenant setting. The first four are the system rules every tenant runs.
/// </summary>
public enum ApprovalRule
{
    OrderMustBeOpen = 1,
    ReferencesMustExist = 2,
    NextInvoicePrice = 3,
    FreightDocumentsAvailable = 4,
    PriceListApplied = 5,
    PriceCalculationCorrect = 6,
    DuplicateCustomerReference = 7,
    EmptyLinePrice = 8,
    FinancialDefaultsMismatch = 9,
    FixedPriceFilled = 10,
    LegDistanceMatchesLine = 11,
    AllStopsArrived = 12
}

/// <summary>The outcome severity of one approval rule.</summary>
public enum ApprovalSeverity
{
    Warning = 1,
    Error = 2
}

/// <summary>Whether an attribute definition describes the order or its lines (design 58, Q12).</summary>
public enum OrderAttributeTarget
{
    Order = 1,
    Line = 2
}

/// <summary>Weekly or monthly recurrence of a template (design 58 "Templates and recurrence").</summary>
public enum TemplateRecurrence
{
    None = 0,
    Weekly = 1,
    Monthly = 2
}

/// <summary>Days of the week as flags (design 52 precedent).</summary>
[Flags]
public enum RecurrenceDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64
}
