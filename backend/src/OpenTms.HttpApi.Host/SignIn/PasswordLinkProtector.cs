using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Volo.Abp.DependencyInjection;

namespace OpenTms.SignIn;

/// <summary>
/// A link to choose a password, as the reset-password page holds it: the user, the tenant key of the link, the one-time
/// token, and whether the sign-in page sent the user (with the sign-in to return to).
/// </summary>
public record PasswordLink(Guid UserId, string? TenantKey, string Token, bool Change, string? ReturnUrl);

/// <summary>
/// Seals the <see cref="PasswordLink"/> the reset-password page works with (docs/designs/62-sign-in-tenant-detection.md,
/// "Password recovery"). The token arrives once in the mail link's query string; the page moves it into this HttpOnly
/// cookie and goes on at its own address without it, so the token is not in the address bar, the history, a Referer or
/// the log lines of the requests that follow. The sign-in page hands over a forced password change the same way, without
/// the token ever being in a URL. Valid for <see cref="Lifetime"/>; the token keeps its own, longer, lifetime.
/// </summary>
public class PasswordLinkProtector : ISingletonDependency
{
    public const string CookieName = ".OpenTms.PasswordLink";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ITimeLimitedDataProtector _protector;

    public PasswordLinkProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("OpenTms.SignIn.PasswordLink").ToTimeLimitedDataProtector();
    }

    public string Protect(PasswordLink link) => _protector.Protect(JsonSerializer.Serialize(link), DateTimeOffset.UtcNow.Add(Lifetime));

    /// <summary>The link, or null when the value is missing, expired or tampered with.</summary>
    public PasswordLink? Unprotect(string? protectedLink)
    {
        if (string.IsNullOrEmpty(protectedLink))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PasswordLink>(_protector.Unprotect(protectedLink));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
