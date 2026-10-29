using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.Accounts;

/// <summary>One account of a user, as a link names it: the tenant (null: the host), the user there and its security stamp.</summary>
public record LinkedAccount(Guid? TenantId, Guid UserId, string SecurityStamp);

/// <summary>
/// The remembered account switches in the host database (<see cref="HostUserAccountLinkStore"/>). Cross-tenant by design
/// (docs/designs/63-user-menu.md): a link joins accounts in two tenants.
/// </summary>
public interface IUserAccountLinkStore
{
    /// <summary>The accounts the given user may switch to without a password, as remembered (stamps not yet checked).</summary>
    Task<IReadOnlyList<LinkedAccount>> GetLinkedAsync(Guid? tenantId, Guid userId);

    /// <summary>Remembers the switch both ways: each side keeps the other side's current security stamp.</summary>
    Task RememberAsync(LinkedAccount first, LinkedAccount second);

    /// <summary>Forgets the switch between the user and its account in <paramref name="linkedTenantId"/>, both ways.</summary>
    Task ForgetAsync(Guid? tenantId, Guid userId, Guid? linkedTenantId);

    /// <summary>Removes every link to or from a deleted user.</summary>
    Task DeleteUserAsync(Guid? tenantId, Guid userId);

    /// <summary>Removes every link to or from a deleted tenant.</summary>
    Task DeleteTenantAsync(Guid tenantId);
}
