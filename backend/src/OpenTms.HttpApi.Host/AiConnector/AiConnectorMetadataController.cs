using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.AspNetCore.Mvc;

namespace OpenTms.AiConnector;

/// <summary>
/// RFC 9728 protected-resource metadata (design 74): how an AI tool finds the authorization server and the scopes, with
/// <c>offline_access</c>, without which it gets no refresh token and the connection drops. Also on the path-suffixed
/// address RFC 9728 derives from <c>/mcp</c>. The issuer is written as OpenIddict writes it, so the two documents agree.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class AiConnectorMetadataController : AbpController
{
    private readonly IConfiguration _configuration;

    public AiConnectorMetadataController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet(AiConnectorTokenGate.ProtectedResourceMetadataPath)]
    [HttpGet(AiConnectorTokenGate.ProtectedResourceMetadataPath + McpEndpoint.Path)]
    public IActionResult GetProtectedResource()
    {
        var selfUrl = (_configuration["App:SelfUrl"] ?? string.Empty).TrimEnd('/');
        return new JsonResult(new Dictionary<string, object>
        {
            ["resource"] = selfUrl + McpEndpoint.Path,
            ["authorization_servers"] = new[] { new Uri(_configuration["AuthServer:Authority"]!, UriKind.Absolute).AbsoluteUri },
            ["scopes_supported"] = new[] { AiConnectorConsts.Scope, OpenIddictConstants.Scopes.OfflineAccess },
            ["bearer_methods_supported"] = new[] { "header" },
        });
    }
}
