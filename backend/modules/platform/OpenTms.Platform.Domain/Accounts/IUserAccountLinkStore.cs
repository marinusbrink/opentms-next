using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.Accounts;

/// <summary>One account of a user, as a link names it: the tenant (null: the host), the user there and its security stamp.</summary>
public record LinkedAccount(Guid? TenantId, Guid UserId, string SecurityStamp);

/// <summary>A remembered switch from a user: the linked account, and the user's own stamp when it was remembered.</summary>
public record AccountLink(LinkedAccount Linked, string UserSecurityStamp)
{
    /// <summary>Holds only while neither account's security stamp (password) has changed since it was remembered.</summary>
    public bool Holds(string? userSecurityStamp, Guid? linkedTenantId, Guid linkedUserId, string? linkedSecurityStamp) =>
        Linked.TenantId == linkedTenantId && Linked.UserId == linkedUserId
        && Linked.SecurityStamp == linkedSecurityStamp && UserSecurityStamp == userSecurityStamp;
}

/// <summary>
/// The remembered account switches in the host database (<see cref="HostUserAccountLinkStore"/>). Cross-tenant by design
/// (docs/designs/63-user-menu.md): a link joins accounts in two tenants.
/// </summary>
public interface IUserAccountLinkStore
{
    /// <summary>The remembered switches of the given user (stamps not yet checked: see <see cref="AccountLink.Holds"/>).</summary>
    Task<IReadOnlyList<AccountLink>> GetLinkedAsync(Guid? tenantId, Guid userId);

    /// <summary>Remembers the switch both ways: each row keeps both sides' current security stamps.</summary>
    Task RememberAsync(LinkedAccount first, LinkedAccount second);

    /// <summary>Forgets the switch between the user and its account in <paramref name="linkedTenantId"/>, both ways.</summary>
    Task ForgetAsync(Guid? tenantId, Guid userId, Guid? linkedTenantId);

    /// <summary>Removes every link to or from a deleted user.</summary>
    Task DeleteUserAsync(Guid? tenantId, Guid userId);

    /// <summary>Removes every link to or from a deleted tenant.</summary>
    Task DeleteTenantAsync(Guid tenantId);
}
