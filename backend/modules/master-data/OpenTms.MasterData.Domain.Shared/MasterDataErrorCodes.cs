namespace OpenTms.MasterData;

/// <summary>
/// Business error codes of the module. The namespace prefix maps to the MasterData localization resource
/// (MasterDataDomainSharedModule); OpenTms.HttpApi maps the conflict codes to HTTP 409.
/// </summary>
public static class MasterDataErrorCodes
{
    public const string Namespace = "OpenTms.MasterData";

    public const string DuplicateKey = Namespace + ":DuplicateKey";
    public const string InUse = Namespace + ":InUse";
    public const string DefaultCannotBeDeleted = Namespace + ":DefaultCannotBeDeleted";
    public const string DefaultMustBeActive = Namespace + ":DefaultMustBeActive";
    public const string InvalidTimeZone = Namespace + ":InvalidTimeZone";
    public const string SelfIncompatibility = Namespace + ":SelfIncompatibility";
    public const string AdrFieldsRequired = Namespace + ":AdrFieldsRequired";
    public const string InvalidPostalCodeRange = Namespace + ":InvalidPostalCodeRange";
    public const string InvalidTemperatureRange = Namespace + ":InvalidTemperatureRange";
    public const string InvalidCode = Namespace + ":InvalidCode";
    public const string ReferenceNotFound = Namespace + ":ReferenceNotFound";
    public const string DuplicateLoadFactor = Namespace + ":DuplicateLoadFactor";
    public const string UnknownSortColumn = Namespace + ":UnknownSortColumn";
    public const string NameRequired = Namespace + ":NameRequired";
    public const string TooManyRows = Namespace + ":TooManyRows";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        DuplicateKey, InUse, DefaultCannotBeDeleted, DefaultMustBeActive, SelfIncompatibility, DuplicateLoadFactor
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        InvalidTimeZone, AdrFieldsRequired, InvalidPostalCodeRange, InvalidTemperatureRange, InvalidCode, ReferenceNotFound, UnknownSortColumn, NameRequired, TooManyRows
    ];
}
