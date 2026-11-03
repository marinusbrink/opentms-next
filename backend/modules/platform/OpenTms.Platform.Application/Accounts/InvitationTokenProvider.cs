using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.DependencyInjection;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Platform.Accounts;

/// <summary>How long the links to choose a password stay valid (PO decision 2026-11-03).</summary>
public static class PasswordLinkLifetimes
{
    /// <summary>An invitation often waits in an inbox over a weekend.</summary>
    public static readonly TimeSpan Invitation = TimeSpan.FromDays(7);

    /// <summary>
    /// A reset link (asked for by the user or sent by an administrator) carries Identity's own password-reset token, whose
    /// default lifetime this is (<c>DataProtectionTokenProviderOptions.TokenLifespan</c>; nothing here changes it).
    /// </summary>
    public static readonly TimeSpan Reset = TimeSpan.FromDays(1);
}

/// <summary>
/// The token of an invitation link, valid for <see cref="PasswordLinkLifetimes.Invitation"/> instead of a reset link's
/// day. It is sealed with the shared data-protection keys and, like Identity's reset token, binds the user, the purpose
/// and the security stamp: setting a password renews the stamp, so the token is spent once the password is chosen.
/// <see cref="PasswordLinkTokens"/> turns it into a reset token when the user chooses the password.
/// </summary>
public class InvitationTokenProvider : IUserTwoFactorTokenProvider<IdentityUser>
{
    public const string ProviderName = "OpenTmsInvitation";

    /// <summary>The data-protection purpose its tokens are sealed under.</summary>
    public const string ProtectorPurpose = "OpenTms.Platform.Accounts.Invitation";

    private readonly IAbpLazyServiceProvider _services;

    /// <summary>
    /// Data protection is looked up on first use, not here: Identity builds every registered token provider with each user
    /// manager, also in an application without data protection (seeding, test containers), which never makes a token.
    /// </summary>
    public InvitationTokenProvider(IAbpLazyServiceProvider services)
    {
        _services = services;
    }

    private ITimeLimitedDataProtector Protector =>
        _services.LazyGetRequiredService<IDataProtectionProvider>().CreateProtector(ProtectorPurpose).ToTimeLimitedDataProtector();

    public async Task<string> GenerateAsync(string purpose, UserManager<IdentityUser> manager, IdentityUser user)
    {
        var payload = new Payload(await manager.GetUserIdAsync(user), purpose, await manager.GetSecurityStampAsync(user) ?? string.Empty);
        return Protector.Protect(JsonSerializer.Serialize(payload), DateTimeOffset.UtcNow.Add(PasswordLinkLifetimes.Invitation));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<IdentityUser> manager, IdentityUser user)
    {
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Protector.Unprotect(token));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return false;
        }

        return payload != null
               && payload.UserId == await manager.GetUserIdAsync(user)
               && payload.Purpose == purpose
               && payload.SecurityStamp == (await manager.GetSecurityStampAsync(user) ?? string.Empty);
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<IdentityUser> manager, IdentityUser user) => Task.FromResult(false);

    private sealed record Payload(string UserId, string Purpose, string SecurityStamp);
}
