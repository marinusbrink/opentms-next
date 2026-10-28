using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Volo.Abp.DependencyInjection;

namespace OpenTms.SignIn;

/// <summary>The e-mail address step 1 accepted, and whether the user asked to be remembered.</summary>
public record SignInEmail(string Email, bool RememberMe);

/// <summary>
/// Seals the <see cref="SignInEmail"/> that carries the sign-in page from step 1 to step 2
/// (docs/designs/62-sign-in-tenant-detection.md). It travels in an HttpOnly cookie, encrypted and signed with the shared
/// data-protection keys: for the browser session, or for <see cref="RememberFor"/> when the user ticked "Remember me",
/// so the next visit starts at the password.
/// </summary>
public class SignInEmailProtector : ISingletonDependency
{
    public const string CookieName = ".OpenTms.SignInEmail";

    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(30);

    private readonly IDataProtector _protector;

    public SignInEmailProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("OpenTms.SignIn.Email");
    }

    public string Protect(SignInEmail email) => _protector.Protect(JsonSerializer.Serialize(email));

    /// <summary>The address, or null when the value is missing or tampered with.</summary>
    public SignInEmail? Unprotect(string? protectedEmail)
    {
        if (string.IsNullOrEmpty(protectedEmail))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SignInEmail>(_protector.Unprotect(protectedEmail));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
