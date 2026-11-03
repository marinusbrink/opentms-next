using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Platform.Accounts;

public enum PasswordLinkKind
{
    /// <summary>A new user chooses a first password: valid for <see cref="PasswordLinkLifetimes.Invitation"/>.</summary>
    Invitation,

    /// <summary>A user chooses a new password: valid for <see cref="PasswordLinkLifetimes.Reset"/>.</summary>
    Reset,
}

/// <summary>The one-time tokens of the links to choose a password: made per kind, and accepted of either kind.</summary>
public class PasswordLinkTokens : ITransientDependency
{
    private readonly IdentityUserManager _userManager;

    public PasswordLinkTokens(IdentityUserManager userManager)
    {
        _userManager = userManager;
    }

    public Task<string> GenerateAsync(IdentityUser user, PasswordLinkKind kind) =>
        kind == PasswordLinkKind.Invitation
            ? _userManager.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, UserManager<IdentityUser>.ResetPasswordTokenPurpose)
            : _userManager.GeneratePasswordResetTokenAsync(user);

    /// <summary>
    /// A token Identity's <c>ResetPasswordAsync</c> accepts for the token of a link, or null when the link's token is
    /// expired, spent or not this user's. An invitation token is exchanged for a fresh reset token; both stop working once
    /// the password is set, because setting it renews the security stamp they carry.
    /// </summary>
    public async Task<string?> ToResetTokenAsync(IdentityUser user, string? linkToken)
    {
        if (string.IsNullOrEmpty(linkToken))
        {
            return null;
        }

        if (await _userManager.VerifyUserTokenAsync(
                user, _userManager.Options.Tokens.PasswordResetTokenProvider, UserManager<IdentityUser>.ResetPasswordTokenPurpose, linkToken))
        {
            return linkToken;
        }

        return await _userManager.VerifyUserTokenAsync(
            user, InvitationTokenProvider.ProviderName, UserManager<IdentityUser>.ResetPasswordTokenPurpose, linkToken)
            ? await _userManager.GeneratePasswordResetTokenAsync(user)
            : null;
    }
}
