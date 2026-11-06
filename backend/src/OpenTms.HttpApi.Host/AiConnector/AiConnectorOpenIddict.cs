using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace OpenTms.AiConnector;

/// <summary>
/// What the connector adds to the token server: the metadata advertises <c>registration_endpoint</c> and that a public client
/// authenticates with <c>none</c>, and the connector's own <c>resource</c> parameter is accepted. MCP clients send it as
/// RFC 8707 asks, while OpenIddict rejects every resource not registered and permitted (<c>invalid_target</c>); a connector
/// token's audience is always <c>OpenTms.Mcp</c>, set by the scope, so the parameter is dropped here. Any other resource is
/// still refused.
/// </summary>
public static class AiConnectorOpenIddict
{
    public static void Configure(OpenIddictServerBuilder builder, IConfiguration configuration)
    {
        var ownResource = (configuration["App:SelfUrl"] ?? string.Empty).TrimEnd('/') + McpEndpoint.Path;

        builder.AddEventHandler<HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            context.Metadata["registration_endpoint"] = new Uri(context.BaseUri!, "connect/register").AbsoluteUri;
            context.TokenEndpointAuthenticationMethods.Add(OpenIddictConstants.ClientAuthenticationMethods.None);
            return default;
        }));

        builder.AddEventHandler<ExtractAuthorizationRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            DropOwnResource(context.Request, ownResource);
            return default;
        }));

        builder.AddEventHandler<ExtractTokenRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            DropOwnResource(context.Request, ownResource);
            return default;
        }));
    }

    private static void DropOwnResource(OpenIddictRequest? request, string ownResource)
    {
        if (request?.Resources is not { IsDefaultOrEmpty: false } resources)
        {
            return;
        }

        var remaining = resources.Where(resource => !string.Equals(resource?.TrimEnd('/'), ownResource, StringComparison.Ordinal)).ToImmutableArray();
        request.Resources = remaining.IsEmpty ? null : remaining;
    }
}
