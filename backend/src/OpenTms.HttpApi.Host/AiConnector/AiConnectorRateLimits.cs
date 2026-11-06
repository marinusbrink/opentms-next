using System;
using System.Globalization;
using System.Linq;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Load control", in process (exact with one instance per revision; AP-13 is not relied on): 60 requests per minute
/// per connection on <c>/mcp</c> and 10 registrations per minute per IP address. Every <c>/mcp</c> request counts, not only
/// <c>tools/call</c>: the limiter sits at the HTTP level, where a 429 can be answered, and the few <c>initialize</c> and
/// <c>tools/list</c> requests of a session change little.
/// </summary>
public static class AiConnectorRateLimits
{
    public const int ToolCallsPerMinute = 60;

    public const int RegistrationsPerMinute = 10;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) =>
        {
            context.HttpContext.Response.Headers[HeaderNames.RetryAfter] = ((int)Window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return default;
        };
        options.AddPolicy(McpEndpoint.ToolsRateLimitPolicy, context =>
            FixedWindow(McpEndpoint.GetCaller(context)?.AuthorizationId ?? "unknown", ToolCallsPerMinute));
        options.AddPolicy(McpEndpoint.RegistrationRateLimitPolicy, context =>
            FixedWindow(ClientAddress(context), RegistrationsPerMinute));
    }

    /// <summary>
    /// Cloud Run's proxy appends the address it saw as the last value of <c>X-Forwarded-For</c>, and the connection itself comes
    /// from the proxy, so that value is the caller and anything before it is the caller's own claim. Read here only: the host's
    /// forwarded-headers setup takes the scheme alone. Without the header (local) the connection is the caller.
    /// </summary>
    public static string ClientAddress(HttpContext context)
    {
        string? forwarded = context.Request.Headers["X-Forwarded-For"];
        var last = forwarded?.Split(',').LastOrDefault()?.Trim();
        return string.IsNullOrEmpty(last) ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown" : last;
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = Window,
            QueueLimit = 0,
        });
}
