namespace OpenTms.Financial;

/// <summary>
/// The business error codes of the Financial module (designs 52 and 57). The namespace maps to the Financial resource
/// (FinancialDomainSharedModule); OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// The reference-data base's own codes (duplicate key, in use, default, reference not found) are Platform's.
/// </summary>
public static class FinancialErrorCodes
{
    public const string Namespace = "OpenTms.Financial";

    /// <summary>A validity row dated today or earlier came back changed, was removed or was added (assessment §3.4).</summary>
    public const string FixedValidityRowChanged = Namespace + ":FixedValidityRowChanged";

    public const string ValidityRowRequired = Namespace + ":ValidityRowRequired";
    public const string DuplicateValidFrom = Namespace + ":DuplicateValidFrom";
    public const string CurrencyRequired = Namespace + ":CurrencyRequired";
    public const string CurrencyNotAllowed = Namespace + ":CurrencyNotAllowed";
    public const string DaysOfWeekRequired = Namespace + ":DaysOfWeekRequired";
    public const string PercentageOutOfRange = Namespace + ":PercentageOutOfRange";

    /* Design 57: price lists, rate lines, relation financial settings. */

    /// <summary>A price list referenced by a relation's settings or by a rate line cannot be deleted; archive it.</summary>
    public const string PriceListInUse = Namespace + ":PriceListInUse";

    /// <summary>A manual line or an adjustment conflicts with the stored lines (a repeated fixed price, a stale set).</summary>
    public const string RateLineConflict = Namespace + ":RateLineConflict";

    public const string ColumnsNotAscending = Namespace + ":ColumnsNotAscending";
    public const string BracketsNotAscending = Namespace + ":BracketsNotAscending";
    public const string OpenRowRequired = Namespace + ":OpenRowRequired";
    public const string LaneRequired = Namespace + ":LaneRequired";
    public const string RelationAndRegionOnOneSide = Namespace + ":RelationAndRegionOnOneSide";
    public const string CurrencyMismatch = Namespace + ":CurrencyMismatch";
    public const string CalculatedLineReadOnly = Namespace + ":CalculatedLineReadOnly";
    public const string ManualLineRequiresUnitPrice = Namespace + ":ManualLineRequiresUnitPrice";
    public const string ImportInvalid = Namespace + ":ImportInvalid";
    public const string TooManyRules = Namespace + ":TooManyRules";
    public const string AnchorRequired = Namespace + ":AnchorRequired";
    public const string TooManyRateLines = Namespace + ":TooManyRateLines";
    public const string InvalidValidity = Namespace + ":InvalidValidity";
    public const string AdjustmentRequired = Namespace + ":AdjustmentRequired";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes = [FixedValidityRowChanged, PriceListInUse, RateLineConflict];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        ValidityRowRequired, DuplicateValidFrom, CurrencyRequired, CurrencyNotAllowed, DaysOfWeekRequired, PercentageOutOfRange,
        ColumnsNotAscending, BracketsNotAscending, OpenRowRequired, LaneRequired, RelationAndRegionOnOneSide, CurrencyMismatch,
        CalculatedLineReadOnly, ManualLineRequiresUnitPrice, ImportInvalid, TooManyRules, AnchorRequired, TooManyRateLines,
        InvalidValidity, AdjustmentRequired
    ];

    /* The warning codes the resolver reports on a preview or a calculation state; not exceptions. */

    /// <summary>A line had no value for the quantity a list or rule needs (no distance on a per-kilometre list).</summary>
    public const string QuantityMissingWarning = "Financial:QuantityMissing";

    /// <summary>A price list in another currency than the order was skipped (QP2).</summary>
    public const string CurrencyMismatchWarning = "Financial:CurrencyMismatch";

    /// <summary>An own-fleet trip without a cost price per kilometre source (design 57, Assumption 5).</summary>
    public const string NoCostPriceSourceWarning = "Financial:NoCostPriceSource";
}
