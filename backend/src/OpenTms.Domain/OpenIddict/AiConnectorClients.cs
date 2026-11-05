using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.OpenIddict.Applications;

namespace OpenTms.OpenIddict;

/// <summary>
/// The OpenIddict applications of the approved AI tools (design 74 "Approved tools"), for the seeder and for
/// <c>/connect/register</c>: explicit consent, authorization code and refresh token only, scopes
/// <see cref="AiConnectorConsts.Scope"/> and <c>offline_access</c> only, exact redirect URIs, and PKCE required of a public
/// client. A connection is meant to stay established (the PBI), so a refresh token lasts
/// <see cref="RefreshTokenLifetime"/> and renewing rolls it on; revoking or deactivating stops it far sooner.
/// </summary>
public static class AiConnectorClients
{
    /// <summary>OpenIddict's default of 14 days would drop a connection after two quiet weeks.</summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>ChatGPT's fixed callback.</summary>
    public const string ChatGptPlatformRedirectUri = "https://chatgpt.com/connector_platform_oauth_redirect";

    /* \z, not $: a $ would also accept a trailing newline. */
    private static readonly Regex ChatGptConnectorRedirectUri = new(
        @"\Ahttps://chatgpt\.com/connector/oauth/[A-Za-z0-9_-]{1,100}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>True for a redirect URI of ChatGPT: each gets an application of its own, OpenIddict compares them exactly.</summary>
    public static bool IsChatGptRedirectUri(string redirectUri) =>
        redirectUri == ChatGptPlatformRedirectUri || ChatGptConnectorRedirectUri.IsMatch(redirectUri);

    /// <summary>The same URI always yields the same client, so registering again returns it.</summary>
    public static string ChatGptClientId(string redirectUri)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(redirectUri));
        return AiConnectorConsts.ChatGptClientIdPrefix + Convert.ToHexStringLower(hash)[..32];
    }

    /// <param name="clientId">The fixed client id.</param>
    /// <param name="displayName">The name on the connect screen; from our configuration, never from a caller.</param>
    /// <param name="secret">Null for a public client.</param>
    /// <param name="redirectUris">The exact callbacks.</param>
    public static AbpApplicationDescriptor CreateDescriptor(string clientId, string displayName, string? secret, IReadOnlyCollection<string> redirectUris)
    {
        var descriptor = new AbpApplicationDescriptor
        {
            ApplicationType = OpenIddictConstants.ApplicationTypes.Web,
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = secret == null ? OpenIddictConstants.ClientTypes.Public : OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
            DisplayName = displayName,
        };

        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        foreach (var permission in new[]
                 {
                     OpenIddictConstants.Permissions.Endpoints.Authorization,
                     OpenIddictConstants.Permissions.Endpoints.Token,
                     OpenIddictConstants.Permissions.Endpoints.Revocation,
                     OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                     OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                     OpenIddictConstants.Permissions.ResponseTypes.Code,
                     OpenIddictConstants.Permissions.Prefixes.Scope + AiConnectorConsts.Scope,
                     OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                 })
        {
            descriptor.Permissions.Add(permission);
        }

        if (secret == null)
        {
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        }

        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.RefreshToken] =
            RefreshTokenLifetime.ToString("c", CultureInfo.InvariantCulture);

        return descriptor;
    }
}
