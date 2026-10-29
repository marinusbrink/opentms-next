using System;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// One switch from the user menu to another account of the same e-mail address (docs/designs/63-user-menu.md): the auth
/// server may switch the browser session from <see cref="ExpectedUserId"/> (in <see cref="ExpectedTenantId"/>) to
/// <see cref="TargetUserId"/> (in <see cref="TargetTenantId"/>). <see cref="PasswordTenants"/> is the value the new
/// session's <see cref="AccountSwitchClaims.PasswordTenants"/> claim takes: the places whose password this session knows.
/// </summary>
public sealed record AccountSwitchTicket(
    Guid ExpectedUserId,
    Guid? ExpectedTenantId,
    Guid TargetUserId,
    Guid? TargetTenantId,
    string PasswordTenants);

/// <summary>
/// Turns a ticket into an opaque, signed, encrypted string that expires after five minutes, and back; the adapter lives
/// in the host (ASP.NET Core data protection), as the impersonation ticket's.
/// </summary>
public interface IAccountSwitchTicketProtector
{
    string Protect(AccountSwitchTicket ticket);

    /// <summary>Null when the string is not a ticket of ours, was tampered with, or has expired.</summary>
    AccountSwitchTicket? Unprotect(string protectedTicket);
}
