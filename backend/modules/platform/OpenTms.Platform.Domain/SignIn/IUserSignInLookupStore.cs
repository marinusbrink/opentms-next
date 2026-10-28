using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.SignIn;

/// <summary>One user an e-mail address may belong to: the tenant (null for the host) and the user's id there.</summary>
public record SignInCandidate(Guid? TenantId, Guid UserId);

/// <summary>A user's lookup row as the rebuild writes it.</summary>
public record UserSignInLookupEntry(Guid UserId, string EmailHash);

/// <summary>
/// The sign-in lookup in the host database (<see cref="HostUserSignInLookupStore"/>). Cross-tenant by design
/// (docs/designs/62-sign-in-tenant-detection.md): the sign-in page asks it before any tenant is known.
/// </summary>
public interface IUserSignInLookupStore
{
    /// <summary>Every user, in any tenant or the host, whose e-mail address hashes to <paramref name="emailHash"/>.</summary>
    Task<IReadOnlyList<SignInCandidate>> FindAsync(string emailHash);

    /// <summary>Writes one user's row, or removes it when <paramref name="emailHash"/> is null (user deleted, no e-mail). Writes nothing when the row is current.</summary>
    Task SyncUserAsync(Guid? tenantId, Guid userId, string? emailHash);

    /// <summary>Replaces every row of one tenant (null: the host) by <paramref name="entries"/>.</summary>
    Task ReplaceTenantAsync(Guid? tenantId, IReadOnlyCollection<UserSignInLookupEntry> entries);

    /// <summary>Removes every row of a deleted tenant.</summary>
    Task DeleteTenantAsync(Guid tenantId);
}
