using System;

namespace OpenTms.SignIn;

/// <summary>
/// Which client the sign-in page signs in for (docs/designs/62-sign-in-tenant-detection.md, "App or API"): the SPA, or
/// the API through Swagger UI. The page looks different for the API, so nobody mistakes the one for the other.
/// </summary>
public static class SignInClient
{
    /// <summary>The ViewData key the sign-in page sets and the account layout reads.</summary>
    public const string ViewDataKey = "OpenTms.SignInClient";

    public const string Api = "api";

    /// <summary>True when the OpenID Connect request comes from the Swagger client (AuthServer:SwaggerClientId).</summary>
    public static bool IsApi(string? requestClientId, string? swaggerClientId) =>
        !string.IsNullOrEmpty(requestClientId)
        && !string.IsNullOrEmpty(swaggerClientId)
        && string.Equals(requestClientId, swaggerClientId, StringComparison.Ordinal);
}
