namespace OpenTms.Integrations;

/// <summary>
/// Business error codes of the Integrations module. The namespace prefix maps to the Integrations localization resource
/// (IntegrationsDomainSharedModule); OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// The routing service answers null for "not configured" and "no path" (the caller records a warning on the trip) and
/// throws <c>RoutingUnavailableException</c> with <see cref="RoutingUnavailable"/> for an engine that is down, so the
/// calculation job fails and Hangfire retries it (constitution rule 3). That code is never an HTTP answer (routing runs in
/// jobs only). The order intake (design 72) adds its codes below: <see cref="ApiClientInactive"/> stays out of both
/// arrays and travels as ABP's default 403 for a business exception, <see cref="AiMappingFailed"/> is a step outcome and a
/// job failure, never an HTTP answer.
/// </summary>
public static class IntegrationsErrorCodes
{
    public const string Namespace = "OpenTms.Integrations";

    /// <summary>The routing engine timed out, was unreachable or answered an error; the calculation is retried.</summary>
    public const string RoutingUnavailable = Namespace + ":RoutingUnavailable";

    /* Design 72: the order intake. */

    /// <summary>The action is not allowed in the mailbox connection's current status ({status}).</summary>
    public const string MailboxStateInvalid = Namespace + ":MailboxStateInvalid";

    /// <summary>The same mailbox and folder are connected already ({emailAddress}).</summary>
    public const string MailboxAlreadyConnected = Namespace + ":MailboxAlreadyConnected";

    /// <summary>A reconnect consented with another account than the connection's ({emailAddress}).</summary>
    public const string MailboxAccountMismatch = Namespace + ":MailboxAccountMismatch";

    /// <summary>The OAuth callback's state does not unprotect, has expired, or names a connection that awaits no consent.</summary>
    public const string OAuthStateInvalid = Namespace + ":OAuthStateInvalid";

    /// <summary>The host has not configured the provider's OAuth client ({provider}): Administration → Services → Integrations.</summary>
    public const string ProviderNotConfigured = Namespace + ":ProviderNotConfigured";

    /// <summary>The calling API client is inactive or unknown in this tenant.</summary>
    public const string ApiClientInactive = Namespace + ":ApiClientInactive";

    /// <summary>A document or message exceeds its size limit ({maxBytes}).</summary>
    public const string DocumentTooLarge = Namespace + ":DocumentTooLarge";

    /// <summary>The document's format cannot be read ({contentType}).</summary>
    public const string UnsupportedFormat = Namespace + ":UnsupportedFormat";

    /// <summary>The action is not allowed in the import profile's current status ({status}).</summary>
    public const string ImportProfileStateInvalid = Namespace + ":ImportProfileStateInvalid";

    /// <summary>AI mapping is off for this tenant (feature Integrations.OrderIntake.AiMapping).</summary>
    public const string AiMappingDisabled = Namespace + ":AiMappingDisabled";

    /// <summary>The language model refused, answered outside the schema, or failed; the document needs attention.</summary>
    public const string AiMappingFailed = Namespace + ":AiMappingFailed";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes =
    [
        MailboxStateInvalid, MailboxAlreadyConnected, MailboxAccountMismatch, ProviderNotConfigured, ImportProfileStateInvalid,
        AiMappingDisabled
    ];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes = [OAuthStateInvalid, DocumentTooLarge, UnsupportedFormat];
}
