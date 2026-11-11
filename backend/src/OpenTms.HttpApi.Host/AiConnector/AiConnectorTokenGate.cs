using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using OpenTms.Integrations.AiConnector;

namespace OpenTms.AiConnector;

/// <summary>
/// Token separation (design 74), both ways, after token validation and before the tenant is resolved. A token whose
/// audience is <see cref="AiConnectorConsts.Resource"/> gets 403 on every other path, so a connector token can never call the
/// REST API. <c>/mcp</c> authenticates with the bearer scheme only: the host's default scheme is the Identity cookie, which
/// answers when no bearer is sent (<c>ForwardIdentityAuthenticationForBearer</c>), so a signed-in browser would otherwise
/// reach it. It refuses every token of another audience, and <c>GET</c> and <c>DELETE</c> answer 405 (stateless mode).
/// </summary>
public static class AiConnectorTokenGate
{
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";

    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!McpEndpoint.IsPath(context.Request.Path))
        {
            if (IsConnectorToken(context.User))
            {
                await RefuseAsync(context, "connector_token_not_allowed_here");
                return;
            }

            await next();
            return;
        }

        /* Outside Development the host's UseErrorPage turns a 4xx without a body into a redirect to /Error: an AI tool needs the
         * 401 and its challenge, and the 405, as they are. Development skips that page, which is why only production showed it. */
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = HttpMethods.Post;
            return;
        }

        /* Without a bearer header there is nothing to authenticate: the principal the default scheme found is a browser session. */
        string? authorization = context.Request.Headers.Authorization;
        var hasBearer = authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var result = hasBearer ? await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) : null;
        if (result?.Succeeded != true || result.Principal == null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            WriteChallenge(context, hasBearer);
            return;
        }

        if (!IsConnectorToken(result.Principal))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await RefuseAsync(context, "not_a_connector_token");
            return;
        }

        context.User = result.Principal;
        McpEndpoint.SetCaller(context, new McpCaller(result.Principal.GetAuthorizationId(), result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId)));
        await next();
    }

    private static bool IsConnectorToken(ClaimsPrincipal principal) =>
        principal.GetAudiences().Contains(AiConnectorConsts.Resource, StringComparer.Ordinal);

    /// <summary>RFC 9728: the 401 points to the protected-resource metadata, where the AI tool finds the authorization server.</summary>
    private static void WriteChallenge(HttpContext context, bool hadToken)
    {
        var selfUrl = context.RequestServices.GetRequiredService<IConfiguration>()["App:SelfUrl"]?.TrimEnd('/');
        var challenge = $"Bearer resource_metadata=\"{selfUrl}{ProtectedResourceMetadataPath}\", scope=\"{AiConnectorConsts.Scope} {OpenIddictConstants.Scopes.OfflineAccess}\"";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = hadToken ? challenge + ", error=\"invalid_token\"" : challenge;
    }

    private static Task RefuseAsync(HttpContext context, string reason)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { error = "access_denied", reason });
    }
}
