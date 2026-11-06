using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.AspNetCore.Mvc;

namespace OpenTms.AiConnector;

/// <summary>
/// <c>POST /connect/register</c> (design 74 "Registration"), shaped as RFC 7591: an AI tool hands in its redirect URIs and gets
/// the fixed client of the one approved tool they all belong to, or <c>400 invalid_redirect_uri</c>. Nothing a caller sends
/// other than the URIs is read, so a name on the connect screen never comes from the caller.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
public class AiConnectorRegistrationController : AbpController
{
    private const int MaxBodyBytes = 16 * 1024;

    private readonly AiConnectorClientRegistrar _registrar;

    public AiConnectorRegistrationController(AiConnectorClientRegistrar registrar)
    {
        _registrar = registrar;
    }

    [HttpPost("connect/register")]
    [EnableRateLimiting(McpEndpoint.RegistrationRateLimitPolicy)]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> RegisterAsync(CancellationToken cancellationToken)
    {
        List<string>? redirectUris;
        try
        {
            using var body = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            redirectUris = ReadRedirectUris(body.RootElement);
        }
        catch (JsonException)
        {
            return Error("invalid_client_metadata", "The body is not a JSON object.");
        }

        var client = redirectUris == null ? null : await _registrar.RegisterAsync(redirectUris, cancellationToken);
        if (client == null)
        {
            return Error("invalid_redirect_uri", "The redirect URIs do not belong to one approved AI tool.");
        }

        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new Dictionary<string, object>
        {
            ["client_id"] = client.ClientId,
            ["client_name"] = client.DisplayName,
            ["redirect_uris"] = redirectUris!,
            ["grant_types"] = new[] { OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken },
            ["response_types"] = new[] { OpenIddictConstants.ResponseTypes.Code },
            ["token_endpoint_auth_method"] = OpenIddictConstants.ClientAuthenticationMethods.None,
            ["scope"] = AiConnectorConsts.Scope + " " + OpenIddictConstants.Scopes.OfflineAccess,
        })
        {
            StatusCode = StatusCodes.Status201Created,
        };
    }

    /// <summary>Null when the field is missing, not an array, or holds anything but strings.</summary>
    private static List<string>? ReadRedirectUris(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("redirect_uris", out var uris)
            || uris.ValueKind != JsonValueKind.Array
            || uris.EnumerateArray().Any(uri => uri.ValueKind != JsonValueKind.String))
        {
            return null;
        }

        return uris.EnumerateArray().Select(uri => uri.GetString()!).ToList();
    }

    private static JsonResult Error(string error, string description) =>
        new(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
}
