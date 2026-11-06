using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Auditing;

namespace OpenTms.AiConnector;

/// <summary>
/// ABP's auditing stores the arguments of every application-service call of a request, and a tool's arguments hold a search
/// text, which can hold a name (design 74 "Logging"; constitution rule 5). The audit record of a <c>/mcp</c> request keeps
/// who called which service when and with what result, without the arguments. Excluding the URL would not do: the
/// interceptor then opens an audit scope of its own, with arguments, for a service that writes.
/// </summary>
public class AiConnectorAuditLogContributor : AuditLogContributor
{
    private const string NoParameters = "{}";

    public override void PostContribute(AuditLogContributionContext context)
    {
        var request = context.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.Request;
        if (request == null || !McpEndpoint.IsPath(request.Path))
        {
            return;
        }

        foreach (var action in context.AuditInfo.Actions)
        {
            action.Parameters = NoParameters;
        }
    }
}
