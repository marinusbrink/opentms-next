using System.Collections.Generic;
using System.Net;

namespace OpenTms.Platform;

/// <summary>
/// Business error codes of the Platform master-data lists (docs/designs/54-platform-master-data.md, API contract). The
/// namespace prefix maps to the Platform localization resource (PlatformDomainSharedModule); OpenTms.HttpApi maps the
/// conflict codes to HTTP 409 and the request-shape codes to HTTP 400. The document-rendering codes (design 55) carry
/// their own status in <see cref="DocumentRenderingCodes"/>.
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
    public const string UnknownSortColumn = Namespace + ":UnknownSortColumn";
    public const string TooManyRows = Namespace + ":TooManyRows";
    public const string NameRequired = Namespace + ":NameRequired";
    public const string FileNamePatternUnknownField = Namespace + ":FileNamePatternUnknownField";

    /* Document rendering (docs/designs/55-report-rendering.md). */
    public const string RenderingNotConfigured = Namespace + ":RenderingNotConfigured";
    public const string RenderingProviderUnavailable = Namespace + ":RenderingProviderUnavailable";
    public const string TemplateNotOwned = Namespace + ":TemplateNotOwned";
    public const string TemplateNotFound = Namespace + ":TemplateNotFound";
    public const string DocumentRenderingFailed = Namespace + ":DocumentRenderingFailed";
    public const string ReportDefinitionArchived = Namespace + ":ReportDefinitionArchived";

    /* The user menu's account switch (docs/designs/63-user-menu.md). */
    public const string AccountPasswordRequired = Namespace + ":AccountPasswordRequired";
    public const string AccountPasswordIncorrect = Namespace + ":AccountPasswordIncorrect";
    public const string AccountLockedOut = Namespace + ":AccountLockedOut";
    public const string AccountNotAvailable = Namespace + ":AccountNotAvailable";
    public const string AccountSwitchWhileImpersonating = Namespace + ":AccountSwitchWhileImpersonating";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        DuplicateKey, InUse
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes =
    [
        TemplateSyntaxError, UnsupportedCulture, InvalidRegex, TransformationArgumentRequired,
        DuplicateTransformation, UnknownSortColumn, TooManyRows, NameRequired, FileNamePatternUnknownField,
        AccountPasswordRequired, AccountPasswordIncorrect, AccountLockedOut, AccountNotAvailable, AccountSwitchWhileImpersonating
    ];

    /// <summary>
    /// The document-rendering codes and their HTTP status: the deployment lacks the provider configuration or the
    /// definition is archived (409), FiLogic's standard template must be copied first (409), the provider does not know
    /// the template (404), no provider implementation is registered (503), the provider refused or failed (502).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, HttpStatusCode> DocumentRenderingCodes = new Dictionary<string, HttpStatusCode>
    {
        [RenderingNotConfigured] = HttpStatusCode.Conflict,
        [RenderingProviderUnavailable] = HttpStatusCode.ServiceUnavailable,
        [TemplateNotOwned] = HttpStatusCode.Conflict,
        [TemplateNotFound] = HttpStatusCode.NotFound,
        [DocumentRenderingFailed] = HttpStatusCode.BadGateway,
        [ReportDefinitionArchived] = HttpStatusCode.Conflict
    };
}
