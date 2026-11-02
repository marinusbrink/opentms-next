using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// A one-time link to ABP's own reset-password page on the auth server, where the user chooses a password. Used for
/// every invitation and every reset link: the token is Identity's password-reset token, so it expires with Identity's
/// token lifespan and is spent once the password is chosen. Builds the link for the current tenant.
/// </summary>
public class PasswordLinkBuilder : ITransientDependency
{
    /// <summary>ABP Account's <c>AccountUrlNames.PasswordReset</c>; the Account module maps it to <c>Account/ResetPassword</c>.</summary>
    private const string PasswordResetUrlName = "Abp.Account.PasswordReset";

    /// <summary>The application name the host configures in <c>AppUrlOptions</c> for the auth server's pages.</summary>
    private const string AuthServerApplicationName = "MVC";

    private readonly IdentityUserManager _userManager;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly ICurrentTenant _currentTenant;

    public PasswordLinkBuilder(IdentityUserManager userManager, IAppUrlProvider appUrlProvider, ICurrentTenant currentTenant)
    {
        _userManager = userManager;
        _appUrlProvider = appUrlProvider;
        _currentTenant = currentTenant;
    }

    public async Task<string> BuildAsync(IdentityUser user)
    {
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = await _appUrlProvider.GetUrlAsync(AuthServerApplicationName, PasswordResetUrlName);
        var tenantPart = _currentTenant.Id is { } tenantId ? $"&{TenantResolverConsts.DefaultTenantKey}={tenantId}" : string.Empty;
        return $"{resetUrl}?userId={user.Id}{tenantPart}&resetToken={UrlEncoder.Default.Encode(token)}";
    }
}
