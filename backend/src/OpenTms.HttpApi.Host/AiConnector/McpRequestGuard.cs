using System;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace OpenTms.AiConnector;

/// <summary>
/// The six checks on every <c>/mcp</c> request (design 74), after token validation and in the design's order, each with 403
/// and no data. Runs right before the endpoints, so the request's tenant, dynamic claims and log context are set and a
/// refused call still shows in the audit log. Check 6 reads the connection in the tenant's own database, so revoking
/// blocks the very next request.
/// </summary>
public static class McpRequestGuard
{
    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var caller = McpEndpoint.IsPath(context.Request.Path) ? McpEndpoint.GetCaller(context) : null;
        if (caller == null)
        {
            await next();
            return;
        }

        var services = context.RequestServices;
        var refusal = await CheckAsync(services, context, caller);
        if (refusal != null)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpRequestGuard)).LogInformation(
                "AI connector request refused: {Refusal} (tenant {TenantId}, user {UserId}, client {ClientId})",
                refusal, services.GetRequiredService<ICurrentTenant>().Id, context.User.FindUserId(), caller.ClientId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "access_denied", reason = refusal.ToString() });
            return;
        }

        await next();
    }

    private static async Task<AiConnectorRefusal?> CheckAsync(IServiceProvider services, HttpContext context, McpCaller caller)
    {
        using var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>()
            .Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);

        var refusal = await services.GetRequiredService<AiConnectorAccessChecker>().CheckAsync(context.User);
        if (refusal == null)
        {
            var userId = context.User.FindUserId();
            var isActive = userId != null
                           && caller.AuthorizationId != null
                           && await services.GetRequiredService<IAiConnectionChecker>().IsActiveAsync(caller.AuthorizationId, userId.Value, context.RequestAborted);
            refusal = isActive ? null : AiConnectorRefusal.ConnectionRevoked;
        }

        await unitOfWork.CompleteAsync(context.RequestAborted);
        return refusal;
    }
}
