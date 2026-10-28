using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.SignIn;

public enum SignInTenantMatchStatus
{
    /// <summary>Fewer characters than <see cref="SignInTenantMatch.MinQueryLength"/> and no exact name: nothing is said.</summary>
    TooShort,
    None,
    /// <summary>More than one tenant contains the text: type more. Their names are not given.</summary>
    Ambiguous,
    One,
}

public record SignInTenantMatchResult(SignInTenantMatchStatus Status, SignInChoice? Choice);

/// <summary>
/// Picks the tenant a user means from part of its name (docs/designs/62-sign-in-tenant-detection.md): a case-insensitive
/// "contains" over the tenants where the credentials were valid only, so "opem" finds "filogic-opemtms". The full name
/// always wins, so "acme" next to "acme-transport", or a two-letter name, can still be chosen. A name is given back only
/// when exactly one tenant matches, and a partial name only after a few characters, so the page never lists them.
/// </summary>
public static class SignInTenantMatch
{
    public const int MinQueryLength = 3;

    /// <param name="hostName">What the host is called in the choice (localized "Host").</param>
    public static SignInTenantMatchResult Find(IReadOnlyCollection<SignInChoice> choices, string? query, string hostName)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return new SignInTenantMatchResult(SignInTenantMatchStatus.TooShort, null);
        }

        var exact = choices.Where(c => string.Equals(DisplayName(c, hostName), text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            return new SignInTenantMatchResult(SignInTenantMatchStatus.One, exact[0]);
        }

        if (text.Length < MinQueryLength)
        {
            return new SignInTenantMatchResult(SignInTenantMatchStatus.TooShort, null);
        }

        var matches = choices.Where(c => DisplayName(c, hostName).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            0 => new SignInTenantMatchResult(SignInTenantMatchStatus.None, null),
            1 => new SignInTenantMatchResult(SignInTenantMatchStatus.One, matches[0]),
            _ => new SignInTenantMatchResult(SignInTenantMatchStatus.Ambiguous, null),
        };
    }

    public static string DisplayName(SignInChoice choice, string hostName) => choice.TenantName ?? hostName;
}
