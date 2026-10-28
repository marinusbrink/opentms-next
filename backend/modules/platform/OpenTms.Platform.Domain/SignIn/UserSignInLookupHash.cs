using System;
using System.Security.Cryptography;
using System.Text;

namespace OpenTms.Platform.SignIn;

/// <summary>
/// The hash <see cref="UserSignInLookup"/> stores: SHA-256 of an already normalized e-mail address (ASP.NET Identity's
/// <c>ILookupNormalizer</c>, the same value as the user's <c>NormalizedEmail</c>), as lowercase hex.
/// </summary>
public static class UserSignInLookupHash
{
    private const string Prefix = "opentms-sign-in:";

    public static string Of(string normalizedEmail)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + normalizedEmail));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>The hash, or null for a user without an e-mail address (who cannot sign in by e-mail).</summary>
    public static string? OfOptional(string? normalizedEmail) =>
        string.IsNullOrEmpty(normalizedEmail) ? null : Of(normalizedEmail);
}
