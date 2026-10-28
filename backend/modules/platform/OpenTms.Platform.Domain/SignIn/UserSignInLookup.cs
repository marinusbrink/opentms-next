using System;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.SignIn;

/// <summary>
/// Where one user can sign in: the user's tenant (null for a host user) under a hash of the normalized e-mail address
/// (docs/designs/62-sign-in-tenant-detection.md). The sign-in page reads it before any tenant is known, so it is
/// host-owned: written and read in the host database only (the table exists in every database because the schema is
/// shared; tenant databases keep it empty). It holds no readable personal data and is only a hint: the sign-in page
/// checks the credentials in the tenant's own database. The id is the user's id.
/// </summary>
public class UserSignInLookup : BasicAggregateRoot<Guid>
{
    /// <summary>A hex SHA-256 hash.</summary>
    public const int HashLength = 64;

    /// <summary>The user's tenant; null for a host user. Not <c>TenantId</c>: the row is host data, never filtered.</summary>
    public Guid? UserTenantId { get; private set; }

    public string EmailHash { get; private set; } = string.Empty;

    protected UserSignInLookup()
    {
    }

    public UserSignInLookup(Guid userId, Guid? userTenantId, string emailHash)
        : base(userId)
    {
        UserTenantId = userTenantId;
        EmailHash = emailHash;
    }

    public void ChangeEmailHash(string emailHash) => EmailHash = emailHash;
}
