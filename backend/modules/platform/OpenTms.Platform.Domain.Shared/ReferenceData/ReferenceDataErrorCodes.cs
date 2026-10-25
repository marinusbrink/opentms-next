namespace OpenTms.Platform.ReferenceData;

/// <summary>
/// The business error codes of the reference-data base (design 50). The namespace maps to the Platform resource;
/// OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// </summary>
public static class ReferenceDataErrorCodes
{
    public const string Namespace = "OpenTms.Platform";

    public const string DuplicateKey = Namespace + ":ReferenceData.DuplicateKey";
    public const string InUse = Namespace + ":ReferenceData.InUse";
    public const string ReferenceNotFound = Namespace + ":ReferenceData.ReferenceNotFound";
    public const string TooManyRows = Namespace + ":ReferenceData.TooManyRows";
    public const string UnknownSortColumn = Namespace + ":ReferenceData.UnknownSortColumn";
    public const string NameRequired = Namespace + ":ReferenceData.NameRequired";
    public const string DefaultRequired = Namespace + ":ReferenceData.DefaultRequired";
    public const string TooManyChildren = Namespace + ":ReferenceData.TooManyChildren";
    public const string InvalidCode = Namespace + ":ReferenceData.InvalidCode";
    public const string InvalidNumberSequence = Namespace + ":ReferenceData.InvalidNumberSequence";
    public const string NextNumberTooLow = Namespace + ":ReferenceData.NextNumberTooLow";
    public const string RequiresTransaction = Namespace + ":ReferenceData.RequiresTransaction";

    public static readonly string[] ConflictCodes = [DuplicateKey, InUse, DefaultRequired, NextNumberTooLow];

    public static readonly string[] BadRequestCodes =
    [
        ReferenceNotFound, TooManyRows, UnknownSortColumn, NameRequired, TooManyChildren, InvalidCode, InvalidNumberSequence
    ];
}
