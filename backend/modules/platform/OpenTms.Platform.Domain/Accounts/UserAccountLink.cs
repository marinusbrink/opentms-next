using System;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// A remembered switch between two accounts of one e-mail address (docs/designs/63-user-menu.md, "Remember password"):
/// from the user <see cref="UserId"/> in <see cref="UserTenantId"/> the menu may switch to <see cref="LinkedUserId"/> in
/// <see cref="LinkedTenantId"/> without asking for that account's password. Written in both directions when the user
/// proves the other account's password in a session. The linked account's security stamp at that moment is kept: a
/// password change (or anything else that renews the stamp) ends the link, so a password once known does not stay a
/// key. The user's own stamp is kept too: a link holds only while neither password has changed. Host-owned like the sign-in lookup: written and read in the host database only; ids and a stamp, no personal data.
/// </summary>
public class UserAccountLink : BasicAggregateRoot<Guid>
{
    public const int MaxSecurityStampLength = 256;

    /// <summary>Null for a host user. Not <c>TenantId</c>: the row is host data, never filtered.</summary>
    public Guid? UserTenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string UserSecurityStamp { get; private set; } = string.Empty;

    public Guid? LinkedTenantId { get; private set; }

    public Guid LinkedUserId { get; private set; }

    public string LinkedSecurityStamp { get; private set; } = string.Empty;

    public DateTime CreationTime { get; private set; }

    protected UserAccountLink()
    {
    }

    public UserAccountLink(
        Guid id, Guid? userTenantId, Guid userId, string userSecurityStamp,
        Guid? linkedTenantId, Guid linkedUserId, string linkedSecurityStamp, DateTime creationTime)
        : base(id)
    {
        UserTenantId = userTenantId;
        UserId = userId;
        UserSecurityStamp = userSecurityStamp;
        LinkedTenantId = linkedTenantId;
        LinkedUserId = linkedUserId;
        LinkedSecurityStamp = linkedSecurityStamp;
        CreationTime = creationTime;
    }

    public void Renew(string userSecurityStamp, Guid linkedUserId, string linkedSecurityStamp, DateTime time)
    {
        UserSecurityStamp = userSecurityStamp;
        LinkedUserId = linkedUserId;
        LinkedSecurityStamp = linkedSecurityStamp;
        CreationTime = time;
    }
}
