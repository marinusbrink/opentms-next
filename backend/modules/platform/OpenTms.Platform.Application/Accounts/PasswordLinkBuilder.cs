using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// A one-time link to the auth server's reset-password page, where the user chooses a password. Used for every
/// invitation and every reset link, with the token of its <see cref="PasswordLinkKind"/>: it expires after that kind's
/// lifetime and is spent once the password is chosen. Builds the link for the current tenant. The link carries no return
/// URL: the page sends the user to the SPA afterwards, which starts a fresh sign-in.
/// </summary>
public class PasswordLinkBuilder : ITransientDependency
{
    /// <summary>ABP Account's <c>AccountUrlNames.PasswordReset</c>; the Account module maps it to <c>Account/ResetPassword</c>.</summary>
    private const string PasswordResetUrlName = "Abp.Account.PasswordReset";

    /// <summary>The application name the host configures in <c>AppUrlOptions</c> for the auth server's pages.</summary>
    private const string AuthServerApplicationName = "MVC";

    private readonly PasswordLinkTokens _tokens;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly ICurrentTenant _currentTenant;

    public PasswordLinkBuilder(PasswordLinkTokens tokens, IAppUrlProvider appUrlProvider, ICurrentTenant currentTenant)
    {
        _tokens = tokens;
        _appUrlProvider = appUrlProvider;
        _currentTenant = currentTenant;
    }

    public async Task<string> BuildAsync(IdentityUser user, PasswordLinkKind kind)
    {
        var token = await _tokens.GenerateAsync(user, kind);
        var resetUrl = await _appUrlProvider.GetUrlAsync(AuthServerApplicationName, PasswordResetUrlName);
        var tenantPart = _currentTenant.Id is { } tenantId ? $"&{TenantResolverConsts.DefaultTenantKey}={tenantId}" : string.Empty;
        return $"{resetUrl}?userId={user.Id}{tenantPart}&resetToken={UrlEncoder.Default.Encode(token)}";
    }
}
