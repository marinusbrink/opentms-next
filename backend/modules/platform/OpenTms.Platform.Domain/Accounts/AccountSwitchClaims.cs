using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// One account whose password the session knows: the tenant (null: the host), the user there, and a short hash of that
/// user's security stamp when the password was checked. A password change renews the stamp, so the place stops counting.
/// </summary>
public sealed record PasswordPlace(Guid? TenantId, Guid UserId, string StampHash)
{
    /// <summary>Whether this place lets the session enter <paramref name="userId"/> in <paramref name="tenantId"/> with its current stamp.</summary>
    public bool Holds(Guid? tenantId, Guid userId, string? securityStamp) =>
        TenantId == tenantId && UserId == userId && StampHash == AccountSwitchClaims.StampHash(securityStamp);
}

/// <summary>
/// The session claim that names the accounts whose password the session knows (docs/designs/63-user-menu.md): the
/// password it was signed in with, valid in several places, or a password entered while switching. Each entry binds a
/// tenant to a user id and a hash of that user's security stamp, so the claim never lets the session into another user
/// (an e-mail address can change) or past a password change. The sign-in page sets it, the switch page carries it on,
/// the tokens carry it to the API. Ids and short hashes of the user's own accounts only.
/// </summary>
public static class AccountSwitchClaims
{
    public const string PasswordTenants = "opentms_password_tenants";

    private const string Host = "host";

    public static string Format(IEnumerable<PasswordPlace> places) =>
        string.Join(' ', places
            .GroupBy(p => p.TenantId)
            .Select(g => g.Last())
            .Select(p => $"{p.TenantId?.ToString("N") ?? Host}|{p.UserId:N}|{p.StampHash}"));

    public static IReadOnlyList<PasswordPlace> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var places = new List<PasswordPlace>();
        foreach (var entry in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3 || !Guid.TryParse(parts[1], out var userId) || parts[2].Length == 0)
            {
                continue;
            }

            if (parts[0] == Host)
            {
                places.Add(new PasswordPlace(null, userId, parts[2]));
            }
            else if (Guid.TryParse(parts[0], out var tenantId))
            {
                places.Add(new PasswordPlace(tenantId, userId, parts[2]));
            }
        }

        return places;
    }

    /// <summary>The first 16 hex characters of the SHA-256 of a security stamp: enough to notice a change, not the stamp itself.</summary>
    public static string StampHash(string? securityStamp) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)))[..16];
}
