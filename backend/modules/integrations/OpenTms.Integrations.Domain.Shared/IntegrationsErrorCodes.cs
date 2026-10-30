namespace OpenTms.Integrations;

/// <summary>
/// Business error codes of the Integrations module. The namespace prefix maps to the Integrations localization resource
/// (IntegrationsDomainSharedModule); OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// The routing service answers null for "not configured" and "no path" (the caller records a warning on the trip) and
/// throws <c>RoutingUnavailableException</c> with <see cref="RoutingUnavailable"/> for an engine that is down, so the
/// calculation job fails and Hangfire retries it (constitution rule 3). That code is never an HTTP answer (routing runs in
/// jobs only), so both arrays stay empty.
/// </summary>
public static class IntegrationsErrorCodes
{
    public const string Namespace = "OpenTms.Integrations";

    /// <summary>The routing engine timed out, was unreachable or answered an error; the calculation is retried.</summary>
    public const string RoutingUnavailable = Namespace + ":RoutingUnavailable";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes = [];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes = [];
}
