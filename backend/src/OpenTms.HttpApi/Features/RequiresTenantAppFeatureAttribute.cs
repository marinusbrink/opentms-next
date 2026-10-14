using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Features;

/// <summary>
/// Requires every given app feature (a node and its ancestors) when a tenant is current; on the host side it does
/// nothing, because the host has no tenant or edition to carry a feature and always has these screens
/// (docs/designs/tenants-and-editions.md, "App features"). ABP's <c>[RequiresFeature]</c> cannot make that
/// distinction. Fails the way ABP's feature check fails (AbpAuthorizationException, 403).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresTenantAppFeatureAttribute : Attribute, IAsyncActionFilter
{
    public string[] Features { get; }

    public RequiresTenantAppFeatureAttribute(params string[] features)
    {
        Features = features;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (services.GetRequiredService<ICurrentTenant>().IsAvailable)
        {
            await services.GetRequiredService<IFeatureChecker>().CheckEnabledAsync(requiresAll: true, Features);
        }

        await next();
    }
}
