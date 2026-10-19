namespace OpenTms.Platform;

/// <summary>
/// Business error codes of the Platform master-data lists (docs/designs/49-platform-master-data.md, API contract). The
/// namespace prefix maps to the Platform localization resource (PlatformDomainSharedModule); OpenTms.HttpApi maps the
/// conflict codes to HTTP 409 and the request-shape codes to HTTP 400.
/// </summary>
public static class PlatformErrorCodes
{
    public const string Namespace = "OpenTms.Platform";

    public const string DuplicateKey = Namespace + ":DuplicateKey";
    public const string InUse = Namespace + ":InUse";
    public const string TemplateSyntaxError = Namespace + ":TemplateSyntaxError";
    public const string UnsupportedCulture = Namespace + ":UnsupportedCulture";
    public const string InvalidRegex = Namespace + ":InvalidRegex";
    public const string TransformationArgumentRequired = Namespace + ":TransformationArgumentRequired";
    public const string DuplicateTransformation = Namespace + ":DuplicateTransformation";
    public const string NumberSequenceInUse = Namespace + ":NumberSequenceInUse";
    public const string UnknownSortColumn = Namespace + ":UnknownSortColumn";
    public const string TooManyRows = Namespace + ":TooManyRows";
    public const string NameRequired = Namespace + ":NameRequired";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        DuplicateKey, InUse, NumberSequenceInUse
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        TemplateSyntaxError, UnsupportedCulture, InvalidRegex, TransformationArgumentRequired,
        DuplicateTransformation, UnknownSortColumn, TooManyRows, NameRequired
    ];
}
