using System;

namespace OpenTms.Integrations.AiConnector;

/// <summary>The fixed names and limits of the AI connector (design 74).</summary>
public static class AiConnectorConsts
{
    /// <summary>The OAuth scope of a connector token: it reaches <c>/mcp</c> and nothing else.</summary>
    public const string Scope = "OpenTmsMcp";

    /// <summary>The resource of <see cref="Scope"/>, so the audience of a connector token.</summary>
    public const string Resource = "OpenTms.Mcp";

    /// <summary>Every approved tool's OpenIddict client id starts with this.</summary>
    public const string ClientIdPrefix = "OpenTms_Mcp_";

    /// <summary>A ChatGPT client: this plus a hash of its one redirect URI (design 74 "Registration").</summary>
    public const string ChatGptClientIdPrefix = ClientIdPrefix + "ChatGPT_";

    public const string ChatGptDisplayName = "ChatGPT";

    public const int MaxClientIdLength = 100;

    public const int MaxAuthorizationIdLength = 100;

    /// <summary><c>AiConnection.LastUsedAt</c> is written at most this often per connection.</summary>
    public static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a connector client's refresh token lasts; renewing rolls it on. OpenIddict's default of 14 days would drop a
    /// connection after two quiet weeks. A connection unused for longer reads as <see cref="AiConnectionStatus.Expired"/>.
    /// </summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>True for a client id of an approved AI tool.</summary>
    public static bool IsConnectorClient(string? clientId) =>
        clientId != null && clientId.StartsWith(ClientIdPrefix, StringComparison.Ordinal);
}
