using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.MultiTenancy;

namespace OpenTms.SignIn;

/// <summary>
/// The sign-in page never runs in a tenant chosen up front (docs/designs/62-sign-in-tenant-detection.md): the tenant
/// follows from the credentials. The same holds for the pages of password recovery ("Password recovery"): forgot password
/// finds every account of the address, and the reset-password pages take the tenant of the link's own <c>__tenant</c>
/// and change to it themselves. Resolving the host for these pages before the cookie and query-string resolvers also
/// keeps the account layout from showing ABP's tenant switch, which it shows only when one of those two was asked.
/// </summary>
public class SignInPageTenantResolveContributor : HttpTenantResolveContributorBase
{
    public const string ContributorName = "OpenTmsSignInPage";

    public const string SignInPath = "/Account/Login";

    /// <summary>The account pages that run in the host, whatever tenant the request names.</summary>
    public static readonly string[] HostPinnedPaths =
    [
        SignInPath,
        "/Account/ForgotPassword",
        "/Account/PasswordResetLinkSent",
        "/Account/ResetPassword",
        "/Account/ResetPasswordConfirmation",
    ];

    public override string Name => ContributorName;

    protected override Task<string?> GetTenantIdOrNameFromHttpContextOrNullAsync(ITenantResolveContext context, HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value?.TrimEnd('/');
        if (path != null && HostPinnedPaths.Any(pinned => path.Equals(pinned, StringComparison.OrdinalIgnoreCase)))
        {
            context.Handled = true;
        }

        return Task.FromResult<string?>(null);
    }
}
