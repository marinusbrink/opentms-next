using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// "Forgot password?" on the sign-in page, for one account of the address: the page runs before any tenant is known and
/// finds every account the address has (docs/designs/62-sign-in-tenant-detection.md, "Password recovery"); each gets its
/// own mail, sent here in the account's own tenant and transaction (design 64: the mail is queued with it).
/// </summary>
public class ForgotPasswordMailer : ITransientDependency
{
    public ILogger<ForgotPasswordMailer> Logger { get; set; } = NullLogger<ForgotPasswordMailer>.Instance;

    private readonly IdentityUserManager _userManager;
    private readonly UserPasswordMailSender _mails;
    private readonly EmailDeliveryAvailability _availability;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILookupNormalizer _normalizer;

    public ForgotPasswordMailer(
        IdentityUserManager userManager,
        UserPasswordMailSender mails,
        EmailDeliveryAvailability availability,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILookupNormalizer normalizer)
    {
        _userManager = userManager;
        _mails = mails;
        _availability = availability;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _normalizer = normalizer;
    }

    /// <summary>
    /// Sends the link to the user, read again in its tenant (null: the host). False when no mail went out: the user is
    /// gone, inactive or has another address now, or e-mail delivery is not on for the tenant.
    /// </summary>
    public async Task<bool> SendAsync(Guid? tenantId, string? tenantName, Guid userId, string email)
    {
        using (_currentTenant.Change(tenantId, tenantName))
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null || !user.IsActive || user.NormalizedEmail != _normalizer.NormalizeEmail(email.Trim()))
            {
                return false;
            }

            if (!await _availability.IsAvailableAsync())
            {
                Logger.LogWarning("Forgot-password link for user {UserId} of tenant {TenantId} not sent: e-mail delivery is off", userId, tenantId);
                return false;
            }

            await _mails.SendForgotPasswordLinkAsync(user);
            await unitOfWork.CompleteAsync();
            return true;
        }
    }
}
