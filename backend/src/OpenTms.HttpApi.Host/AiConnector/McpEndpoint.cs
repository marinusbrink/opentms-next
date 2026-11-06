using System;
using Microsoft.AspNetCore.Http;

namespace OpenTms.AiConnector;

/// <summary>Where the connector listens, and the facts the token gate takes from a request's token for what follows.</summary>
public static class McpEndpoint
{
    public const string Path = "/mcp";

    public const string ToolsRateLimitPolicy = "OpenTms.AiConnector.Tools";

    public const string RegistrationRateLimitPolicy = "OpenTms.AiConnector.Registration";

    private const string CallerKey = "OpenTms.AiConnector.Caller";

    /// <summary>The same match routing makes: case-insensitive, with or without a trailing slash, on the decoded path.</summary>
    public static bool IsPath(PathString path) =>
        path.Value != null && path.Value.TrimEnd('/').Equals(Path, StringComparison.OrdinalIgnoreCase);

    public static void SetCaller(HttpContext context, McpCaller caller) => context.Items[CallerKey] = caller;

    public static McpCaller? GetCaller(HttpContext context) => context.Items.TryGetValue(CallerKey, out var caller) ? caller as McpCaller : null;
}

/// <summary>What a connector token names beyond the user: the OpenIddict authorization and the AI tool's client.</summary>
public sealed record McpCaller(string? AuthorizationId, string? ClientId);
