using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// The session claim that names the places (tenants, "host" for the host) where the password this session was signed in
/// with, or a password entered while switching, is valid (docs/designs/63-user-menu.md). The sign-in page sets it from
/// the places it checked; the switch page carries it on; the tokens carry it to the API, which lets the menu switch to
/// those places without asking for a password. Ids only, of the user's own accounts.
/// </summary>
public static class AccountSwitchClaims
{
    public const string PasswordTenants = "opentms_password_tenants";

    private const string Host = "host";

    public static string Format(IEnumerable<Guid?> tenantIds) =>
        string.Join(' ', tenantIds.Distinct().Select(id => id?.ToString("N") ?? Host));

    public static IReadOnlyList<Guid?> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var places = new List<Guid?>();
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == Host)
            {
                places.Add(null);
            }
            else if (Guid.TryParse(part, out var id))
            {
                places.Add(id);
            }
        }

        return places;
    }
}
