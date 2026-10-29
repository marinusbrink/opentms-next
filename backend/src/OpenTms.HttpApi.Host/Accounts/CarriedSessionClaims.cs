using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using OpenTms.Impersonation;
using OpenTms.Platform.Accounts;

namespace OpenTms.Accounts;

/// <summary>
/// The claims a session carries beyond what ABP rebuilds from the user: ABP's impersonator claims ("Log in as") and the
/// places whose password the session knows (the user menu's account switch, docs/designs/63-user-menu.md). They go from
/// the auth cookie into the tokens and survive Identity's cookie refresh.
/// </summary>
public static class CarriedSessionClaims
{
    public static IReadOnlyList<string> Types { get; } = [.. ImpersonationClaims.Types, AccountSwitchClaims.PasswordTenants];

    public static IReadOnlyList<Claim> From(ClaimsPrincipal? principal) =>
        principal == null ? [] : Types.SelectMany(type => principal.FindAll(type)).ToList();
}
