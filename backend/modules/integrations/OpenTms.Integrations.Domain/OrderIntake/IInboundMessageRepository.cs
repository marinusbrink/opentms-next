using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>The inbound message repository: the dedupe-safe insert (review minor 7) and the Received list's search (review M2).</summary>
public interface IInboundMessageRepository : IRepository<InboundMessage, Guid>
{
    /// <summary>
    /// Inserts and saves <paramref name="message"/>. When a concurrent recording of the same (tenant, channel, source,
    /// external id) won the unique index, nothing is written, the caller's transaction stays usable (the insert ran under a
    /// savepoint) and false is returned.
    /// </summary>
    Task<bool> TryInsertAsync(InboundMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="query"/> narrowed to the messages whose sender, subject or external id contains
    /// <paramref name="term"/>, case-insensitively: on PostgreSQL an <c>ILIKE '%term%'</c> on the raw columns, which carry
    /// trigram indexes; the term's wildcard characters are matched literally.
    /// </summary>
    Task<IQueryable<InboundMessage>> WhereSearchMatchesAsync(IQueryable<InboundMessage> query, string term);
}
