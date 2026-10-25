using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace OpenTms.RecordUpdates;

/// <summary>
/// Browsers cannot send a bearer header on a WebSocket, so the SignalR client puts the token in the <c>access_token</c>
/// query parameter (design 49). This copies it into the Authorization header, only for <c>/signalr-hubs/*</c> and only
/// when no header is present, before authentication runs. The token never reaches the logs: see
/// <see cref="AccessTokenRedactionEnricher"/>.
/// </summary>
public static class HubAccessTokenMiddleware
{
    public const string HubPathPrefix = "/signalr-hubs";
    public const string QueryParameter = "access_token";

    public static Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(request.Headers.Authorization)
            && request.Query.TryGetValue(QueryParameter, out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = "Bearer " + token.ToString();
        }

        return next();
    }
}
