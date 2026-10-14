using System;

namespace OpenTms.Platform.Impersonation;

/// <summary>
/// One step of "Log in as" (docs/designs/tenants-and-editions.md, "Log in as a tenant user"): the auth server may
/// switch the browser session from <see cref="ExpectedUserId"/> (in <see cref="ExpectedTenantId"/>) to
/// <see cref="TargetUserId"/> (in <see cref="TargetTenantId"/>). <see cref="IsReturn"/> marks the way back to the
/// impersonator, which carries no impersonator claims.
/// </summary>
public sealed record ImpersonationTicket(
    bool IsReturn,
    Guid ExpectedUserId,
    Guid? ExpectedTenantId,
    Guid TargetUserId,
    Guid? TargetTenantId);

/// <summary>
/// Turns a ticket into an opaque, signed, encrypted string that expires after five minutes, and back. The adapter lives
/// in the host (ASP.NET Core data protection, whose keys every instance shares), so the API instance that issues a
/// ticket and the auth-server instance that redeems it need not be the same.
/// </summary>
public interface IImpersonationTicketProtector
{
    string Protect(ImpersonationTicket ticket);

    /// <summary>Null when the string is not a ticket of ours, was tampered with, or has expired.</summary>
    ImpersonationTicket? Unprotect(string protectedTicket);
}
