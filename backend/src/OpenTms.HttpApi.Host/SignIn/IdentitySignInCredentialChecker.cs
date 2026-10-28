using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenTms.Platform.SignIn;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.SignIn;

/// <summary>
/// The credential check of ABP's own sign-in (<see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/>: lockout, the
/// pre-sign-in rules, the password), run inside the candidate's tenant with that tenant's identity settings and in a
/// unit of work of its own, so one tenant's database failing never takes another's work with it.
/// </summary>
public class IdentitySignInCredentialChecker : ISignInCredentialChecker, ITransientDependency
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IOptions<IdentityOptions> _identityOptions;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public IdentitySignInCredentialChecker(
        SignInManager<IdentityUser> signInManager,
        IOptions<IdentityOptions> identityOptions,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _signInManager = signInManager;
        _identityOptions = identityOptions;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<SignInCredentialOutcome> CheckAsync(SignInCandidate candidate, string normalizedEmail, string password)
    {
        using (_currentTenant.Change(candidate.TenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _identityOptions.SetAsync();
            var outcome = await CheckInTenantAsync(candidate, normalizedEmail, password);
            await unitOfWork.CompleteAsync();
            return outcome;
        }
    }

    public async Task<bool> RecordFailureAsync(SignInCandidate candidate)
    {
        using (_currentTenant.Change(candidate.TenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _identityOptions.SetAsync();
            var userManager = _signInManager.UserManager;
            var user = await userManager.FindByIdAsync(candidate.UserId.ToString());
            var lockedOut = false;
            if (user != null && userManager.SupportsUserLockout)
            {
                await userManager.AccessFailedAsync(user);
                lockedOut = await userManager.IsLockedOutAsync(user);
            }

            await unitOfWork.CompleteAsync();
            return lockedOut;
        }
    }

    private async Task<SignInCredentialOutcome> CheckInTenantAsync(SignInCandidate candidate, string normalizedEmail, string password)
    {
        var user = await _signInManager.UserManager.FindByIdAsync(candidate.UserId.ToString());
        if (user == null || user.NormalizedEmail != normalizedEmail)
        {
            return SignInCredentialOutcome.NotFound;
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            return SignInCredentialOutcome.Succeeded;
        }

        if (result.IsLockedOut)
        {
            return SignInCredentialOutcome.LockedOut;
        }

        /* The pre-sign-in rules run before the password check: "not allowed" only tells a user who knows the password. */
        if (result.IsNotAllowed)
        {
            return await _signInManager.UserManager.CheckPasswordAsync(user, password)
                ? SignInCredentialOutcome.NotAllowed
                : SignInCredentialOutcome.Failed;
        }

        return SignInCredentialOutcome.Failed;
    }
}
