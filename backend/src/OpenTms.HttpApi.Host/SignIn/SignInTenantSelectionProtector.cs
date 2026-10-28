using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Volo.Abp.DependencyInjection;

namespace OpenTms.SignIn;

/// <summary>The places a user proved the password for, waiting for the tenant choice.</summary>
public record SignInTenantSelection(IReadOnlyList<SignInChoice> Choices, bool RememberMe);

/// <summary>
/// Seals a <see cref="SignInTenantSelection"/> for the tenant step of the sign-in page
/// (docs/designs/62-sign-in-tenant-detection.md). It travels in an HttpOnly cookie, encrypted and signed with the shared
/// data-protection keys, and expires after <see cref="Lifetime"/>; the password itself is never kept.
/// </summary>
public class SignInTenantSelectionProtector : ISingletonDependency
{
    public const string CookieName = ".OpenTms.SignInTenantSelection";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ITimeLimitedDataProtector _protector;

    public SignInTenantSelectionProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("OpenTms.SignIn.TenantSelection").ToTimeLimitedDataProtector();
    }

    public string Protect(SignInTenantSelection selection) =>
        _protector.Protect(JsonSerializer.Serialize(selection), Lifetime);

    /// <summary>The selection, or null when the value is missing, tampered with or expired.</summary>
    public SignInTenantSelection? Unprotect(string? protectedSelection)
    {
        if (string.IsNullOrEmpty(protectedSelection))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SignInTenantSelection>(_protector.Unprotect(protectedSelection));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
