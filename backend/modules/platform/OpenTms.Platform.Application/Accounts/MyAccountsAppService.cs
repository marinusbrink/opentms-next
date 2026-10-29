using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.SignIn;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Platform.Accounts;

/* The user menu's accounts (docs/designs/63-user-menu.md; PO 2026-10-29): the tenants the signed-in user's e-mail
 * address has an account in, from the host's sign-in lookup (design 62), and the switch to one of them. A switch asks
 * for that account's password unless the session already knows it (the sign-in page checked the same password there,
 * or it was entered in an earlier switch: the opentms_password_tenants claim) or a "Remember password" link whose
 * security stamp is still current says so. This service decides and issues a five-minute ticket; the auth server's
 * /Account/SwitchAccount page redeems it in the browser, as "Log in as" does. */
/* Exposed only through its controller (the conventional route is switched off). */
[RemoteService(IsEnabled = false)]
[Authorize]
public class MyAccountsAppService : PlatformAppServiceBase, IMyAccountsAppService
{
    /// <summary>The auth server's page that redeems a ticket (HttpApi.Host, Pages/Account/SwitchAccount).</summary>
    public const string RedeemPath = "Account/SwitchAccount";

    /// <summary>The application name the host configures in <c>AppUrlOptions</c> for the auth server's pages.</summary>
    private const string AuthServerApplicationName = "MVC";

    private readonly IdentityUserManager _userManager;
    private readonly IUserSignInLookupStore _lookupStore;
    private readonly IUserAccountLinkStore _linkStore;
    private readonly ITenantStore _tenantStore;
    private readonly IAccountSwitchTicketProtector _ticketProtector;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public MyAccountsAppService(
        IdentityUserManager userManager,
        IUserSignInLookupStore lookupStore,
        IUserAccountLinkStore linkStore,
        ITenantStore tenantStore,
        IAccountSwitchTicketProtector ticketProtector,
        IAppUrlProvider appUrlProvider,
        IOptions<IdentityOptions> identityOptions)
    {
        _userManager = userManager;
        _lookupStore = lookupStore;
        _linkStore = linkStore;
        _tenantStore = tenantStore;
        _ticketProtector = ticketProtector;
        _appUrlProvider = appUrlProvider;
        _identityOptions = identityOptions;
    }

    public async Task<IReadOnlyList<MyAccountDto>> GetListAsync()
    {
        if (CurrentUser.FindImpersonatorUserId() != null)
        {
            /* While logged in as someone else the banner offers the way back; the impersonated user's other accounts are
             * not the impersonator's to enter. */
            return [];
        }

        var current = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var places = await FindOtherPlacesAsync(current);
        if (places.Count == 0)
        {
            return [];
        }

        var passwordTenants = PasswordTenants();
        var links = await _linkStore.GetLinkedAsync(CurrentTenant.Id, current.Id);
        return places
            .Select(p =>
            {
                var remembered = links.Any(l => l.TenantId == p.Candidate.TenantId && l.UserId == p.Candidate.UserId);
                return new MyAccountDto
                {
                    TenantId = p.Candidate.TenantId,
                    TenantName = p.TenantName,
                    NeedsPassword = !remembered && !passwordTenants.Contains(p.Candidate.TenantId),
                    IsRemembered = remembered
                };
            })
            .OrderBy(a => a.TenantId == null ? 0 : 1)
            .ThenBy(a => a.TenantName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AccountSwitchRedirectDto> SwitchAsync(SwitchAccountDto input)
    {
        if (CurrentUser.FindImpersonatorUserId() != null)
        {
            throw new BusinessException(PlatformErrorCodes.AccountSwitchWhileImpersonating);
        }

        var current = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var place = (await FindOtherPlacesAsync(current)).FirstOrDefault(p => p.Candidate.TenantId == input.TenantId)
                    ?? throw new BusinessException(PlatformErrorCodes.AccountNotAvailable);
        var candidate = place.Candidate;
        var password = string.IsNullOrEmpty(input.Password) ? null : input.Password;
        var passwordTenants = PasswordTenants();

        var target = await CheckTargetAsync(candidate, current.NormalizedEmail!, password);

        if (password == null && !passwordTenants.Contains(candidate.TenantId))
        {
            var links = await _linkStore.GetLinkedAsync(CurrentTenant.Id, current.Id);
            var linkHolds = links.Any(l =>
                l.TenantId == candidate.TenantId && l.UserId == candidate.UserId && l.SecurityStamp == target.SecurityStamp);
            if (!linkHolds)
            {
                throw new BusinessException(PlatformErrorCodes.AccountPasswordRequired);
            }
        }

        if (password != null && input.RememberPassword)
        {
            await _linkStore.RememberAsync(
                new LinkedAccount(CurrentTenant.Id, current.Id, current.SecurityStamp),
                new LinkedAccount(candidate.TenantId, candidate.UserId, target.SecurityStamp));
        }

        var newPasswordTenants = password == null ? passwordTenants : passwordTenants.Append(candidate.TenantId).ToList();

        Logger.LogInformation(
            "Account switch of user {UserId} (tenant {TenantId}) to user {TargetUserId} (tenant {TargetTenantId}), password entered: {PasswordEntered}, remembered: {Remembered}",
            current.Id, CurrentTenant.Id, candidate.UserId, candidate.TenantId, password != null, password != null && input.RememberPassword);

        var ticket = new AccountSwitchTicket(
            ExpectedUserId: current.Id,
            ExpectedTenantId: CurrentTenant.Id,
            TargetUserId: candidate.UserId,
            TargetTenantId: candidate.TenantId,
            PasswordTenants: AccountSwitchClaims.Format(newPasswordTenants));
        var root = (await _appUrlProvider.GetUrlAsync(AuthServerApplicationName)).EnsureEndsWith('/');
        return new AccountSwitchRedirectDto
        {
            Url = $"{root}{RedeemPath}?ticket={Uri.EscapeDataString(_ticketProtector.Protect(ticket))}"
        };
    }

    public async Task ForgetAsync(Guid? tenantId)
    {
        await _linkStore.ForgetAsync(CurrentTenant.Id, CurrentUser.GetId(), tenantId);
        Logger.LogInformation(
            "User {UserId} (tenant {TenantId}) forgot the remembered switch to tenant {LinkedTenantId}",
            CurrentUser.Id, CurrentTenant.Id, tenantId);
    }

    private IReadOnlyList<Guid?> PasswordTenants() =>
        AccountSwitchClaims.Parse(CurrentUser.FindClaim(AccountSwitchClaims.PasswordTenants)?.Value);

    private sealed record Place(SignInCandidate Candidate, string? TenantName);

    /// <summary>The other accounts of the current user's address: in active tenants and the host, not the current tenant.</summary>
    private async Task<List<Place>> FindOtherPlacesAsync(IdentityUser current)
    {
        var emailHash = UserSignInLookupHash.OfOptional(current.NormalizedEmail);
        if (emailHash == null)
        {
            return [];
        }

        // Cross-tenant by design: the host's sign-in lookup names the same address's accounts in other tenants; only
        // ids, and each account is read in its own tenant — docs/designs/63-user-menu.md, DEVIATION(constitution-1).
        var places = new List<Place>();
        foreach (var candidate in (await _lookupStore.FindAsync(emailHash)).Distinct())
        {
            if (candidate.TenantId == CurrentTenant.Id || places.Any(p => p.Candidate.TenantId == candidate.TenantId))
            {
                continue;
            }

            if (candidate.TenantId is not { } tenantId)
            {
                places.Add(new Place(candidate, null));
                continue;
            }

            var tenant = await _tenantStore.FindAsync(tenantId);
            if (tenant is { IsActive: true })
            {
                places.Add(new Place(candidate, tenant.Name));
            }
        }

        return places;
    }

    /// <summary>
    /// Reads the target account in its own tenant (with that tenant's identity settings, in a unit of work of its own so
    /// a counted failure is kept) and checks it may be entered, with its password when one is given.
    /// </summary>
    private async Task<IdentityUser> CheckTargetAsync(SignInCandidate candidate, string normalizedEmail, string? password)
    {
        string? failure = null;
        IdentityUser? user;
        using (CurrentTenant.Change(candidate.TenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _identityOptions.SetAsync();
            user = await _userManager.FindByIdAsync(candidate.UserId.ToString());
            if (user == null || user.NormalizedEmail != normalizedEmail || !user.IsActive || user.ShouldChangePasswordOnNextLogin)
            {
                failure = PlatformErrorCodes.AccountNotAvailable;
            }
            else if (await _userManager.IsLockedOutAsync(user))
            {
                failure = PlatformErrorCodes.AccountLockedOut;
            }
            else if (password != null)
            {
                if (await _userManager.CheckPasswordAsync(user, password))
                {
                    await _userManager.ResetAccessFailedCountAsync(user);
                }
                else
                {
                    /* A wrong password counts towards that account's lockout, as a sign-in there would. */
                    await _userManager.AccessFailedAsync(user);
                    failure = await _userManager.IsLockedOutAsync(user)
                        ? PlatformErrorCodes.AccountLockedOut
                        : PlatformErrorCodes.AccountPasswordIncorrect;
                }
            }

            await unitOfWork.CompleteAsync();
        }

        if (failure != null)
        {
            Logger.LogWarning(
                "Account switch to user {TargetUserId} (tenant {TargetTenantId}) refused: {Reason}",
                candidate.UserId, candidate.TenantId, failure);
            throw new BusinessException(failure);
        }

        return user!;
    }
}
