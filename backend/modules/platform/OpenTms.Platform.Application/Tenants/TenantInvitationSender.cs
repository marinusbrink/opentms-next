using System;
using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Invites a new tenant's administrator (PO decision D2): the password the seed set is random and never sent;
/// the email carries a one-time link to ABP's own reset-password page on the auth server, where the administrator
/// chooses a password. The link expires with Identity's token lifespan; "Resend invitation" makes a new one.
/// </summary>
public class TenantInvitationSender : ITransientDependency
{
    /// <summary>ABP Account's <c>AccountUrlNames.PasswordReset</c>; the Account module maps it to <c>Account/ResetPassword</c>.</summary>
    private const string PasswordResetUrlName = "Abp.Account.PasswordReset";

    /// <summary>The application name the host configures in <c>AppUrlOptions</c> for the auth server's pages.</summary>
    private const string AuthServerApplicationName = "MVC";

    public ILogger<TenantInvitationSender> Logger { get; set; } = NullLogger<TenantInvitationSender>.Instance;

    private readonly IdentityUserManager _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IStringLocalizer<PlatformResource> _localizer;

    public TenantInvitationSender(
        IdentityUserManager userManager,
        IEmailSender emailSender,
        IAppUrlProvider appUrlProvider,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IStringLocalizer<PlatformResource> localizer)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _appUrlProvider = appUrlProvider;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _localizer = localizer;
    }

    public async Task SendAsync(Guid tenantId, string tenantName)
    {
        using (_currentTenant.Change(tenantId, tenantName))
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var admin = await _userManager.FindByNameAsync(IdentityDataSeedContributor.AdminUserNameDefaultValue)
                        ?? throw new InvalidOperationException("The tenant has no administrator to invite.");
            if (string.IsNullOrWhiteSpace(admin.Email))
            {
                throw new InvalidOperationException("The tenant administrator has no email address.");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(admin);
            var resetUrl = await _appUrlProvider.GetUrlAsync(AuthServerApplicationName, PasswordResetUrlName);
            var link = $"{resetUrl}?userId={admin.Id}&{TenantResolverConsts.DefaultTenantKey}={tenantId}" +
                       $"&resetToken={UrlEncoder.Default.Encode(token)}";

            await _emailSender.SendAsync(
                admin.Email,
                _localizer["Tenants:InvitationSubject", tenantName],
                _localizer["Tenants:InvitationBody", WebUtility.HtmlEncode(tenantName), WebUtility.HtmlEncode(admin.UserName), link],
                isBodyHtml: true);

            await unitOfWork.CompleteAsync();

            /* No address in the log (constitution rule 5): the tenant and user ids identify the invitation. */
            Logger.LogInformation("Invitation sent to the administrator {UserId} of tenant {TenantId}", admin.Id, tenantId);
        }
    }
}
