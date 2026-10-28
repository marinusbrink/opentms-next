using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.MultiTenancy;

namespace OpenTms.SignIn;

/// <summary>
/// The sign-in page never runs in a tenant chosen up front (docs/designs/62-sign-in-tenant-detection.md): the tenant
/// follows from the credentials. Resolving the host for it before the cookie and query-string resolvers also keeps the
/// account layout from showing ABP's tenant switch, which it shows only when one of those two was asked.
/// </summary>
public class SignInPageTenantResolveContributor : HttpTenantResolveContributorBase
{
    public const string ContributorName = "OpenTmsSignInPage";

    public const string SignInPath = "/Account/Login";

    public override string Name => ContributorName;

    protected override Task<string?> GetTenantIdOrNameFromHttpContextOrNullAsync(ITenantResolveContext context, HttpContext httpContext)
    {
        if (httpContext.Request.Path.Value?.TrimEnd('/').Equals(SignInPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Handled = true;
        }

        return Task.FromResult<string?>(null);
    }
}
