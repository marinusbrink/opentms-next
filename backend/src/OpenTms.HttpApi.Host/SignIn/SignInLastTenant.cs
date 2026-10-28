using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Volo.Abp.DependencyInjection;

namespace OpenTms.SignIn;

/// <summary>The organisation last chosen in step 3 for an address (by its lookup hash); a null tenant is the host.</summary>
public record SignInLastTenant(string EmailHash, Guid? TenantId)
{
    /// <summary>
    /// The text step 3 starts with: the remembered organisation's name, when it was remembered for this address and is
    /// one of the places the password is valid in now; otherwise nothing.
    /// </summary>
    public static string? PrefillFor(SignInLastTenant? remembered, string emailHash, IEnumerable<SignInChoice> choices, string hostName)
    {
        if (remembered == null || remembered.EmailHash != emailHash)
        {
            return null;
        }

        var choice = choices.FirstOrDefault(c => c.TenantId == remembered.TenantId);
        return choice == null ? null : SignInTenantMatch.DisplayName(choice, hostName);
    }
}

/// <summary>
/// Seals the <see cref="SignInLastTenant"/> that pre-fills step 3 of the sign-in page on the next visit
/// (docs/designs/62-sign-in-tenant-detection.md). It travels in an HttpOnly cookie, encrypted and signed with the shared
/// data-protection keys, for <see cref="RememberFor"/>.
/// </summary>
public class SignInLastTenantProtector : ISingletonDependency
{
    public const string CookieName = ".OpenTms.SignInLastTenant";

    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(90);

    private readonly IDataProtector _protector;

    public SignInLastTenantProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("OpenTms.SignIn.LastTenant");
    }

    public string Protect(SignInLastTenant lastTenant) => _protector.Protect(JsonSerializer.Serialize(lastTenant));

    /// <summary>The remembered organisation, or null when the value is missing or tampered with.</summary>
    public SignInLastTenant? Unprotect(string? protectedLastTenant)
    {
        if (string.IsNullOrEmpty(protectedLastTenant))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SignInLastTenant>(_protector.Unprotect(protectedLastTenant));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
