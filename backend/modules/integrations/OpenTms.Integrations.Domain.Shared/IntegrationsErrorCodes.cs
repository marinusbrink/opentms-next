namespace OpenTms.Integrations;

/// <summary>
/// Business error codes of the Integrations module. The namespace prefix maps to the Integrations localization resource
/// (IntegrationsDomainSharedModule); OpenTms.HttpApi maps the conflict codes to HTTP 409 and the request codes to 400.
/// Design 60 adds no code of its own: the routing service answers null instead of throwing (the caller records a warning
/// on the trip), so both arrays are empty until a later design needs one.
/// </summary>
public static class IntegrationsErrorCodes
{
    public const string Namespace = "OpenTms.Integrations";

    /// <summary>The codes that mean "the request conflicts with the current state" and travel as HTTP 409.</summary>
    public static readonly string[] ConflictCodes = [];

    /// <summary>The codes that mean "the request itself is wrong" and travel as HTTP 400.</summary>
    public static readonly string[] BadRequestCodes = [];
}
