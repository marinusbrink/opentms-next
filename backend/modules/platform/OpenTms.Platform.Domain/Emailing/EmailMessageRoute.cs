using System;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Which tenant's send log holds the mail the mail service knows by <see cref="ProviderMessageId"/> (design 64). The mail
/// service posts every delivery report of the whole server to one URL, so the report cannot be trusted to name its tenant;
/// the send job writes this row after the service took the mail, and the webhook finds the tenant here, in our own record.
/// Host-owned like <c>UserSignInLookup</c>: written and read in the host database only (tenant databases keep the table
/// empty). Ids only.
/// </summary>
public class EmailMessageRoute : BasicAggregateRoot<Guid>
{
    public string ProviderMessageId { get; private set; } = default!;

    /// <summary>The mail's tenant; null for host mail. Not <c>TenantId</c>: the row is host data, never filtered.</summary>
    public Guid? MessageTenantId { get; private set; }

    public Guid EmailMessageId { get; private set; }

    protected EmailMessageRoute()
    {
    }

    public EmailMessageRoute(Guid id, string providerMessageId, Guid? messageTenantId, Guid emailMessageId)
        : base(id)
    {
        ProviderMessageId = providerMessageId;
        MessageTenantId = messageTenantId;
        EmailMessageId = emailMessageId;
    }
}
