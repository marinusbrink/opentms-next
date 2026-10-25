namespace OpenTms.Financial;

/// <summary>
/// The business error codes of the Financial module (design 52). The namespace maps to the Financial resource
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

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes = [FixedValidityRowChanged];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        ValidityRowRequired, DuplicateValidFrom, CurrencyRequired, CurrencyNotAllowed, DaysOfWeekRequired, PercentageOutOfRange
    ];
}
