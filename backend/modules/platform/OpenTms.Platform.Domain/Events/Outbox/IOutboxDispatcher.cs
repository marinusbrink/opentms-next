using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Events.Outbox;

public interface IOutboxDispatcher
{
    /// <summary>
    /// Visits the database of <paramref name="tenantId"/> (null = the host database) and publishes
    /// what is waiting there. Returns the number of events published and deleted.
    /// </summary>
    Task<int> DispatchAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
