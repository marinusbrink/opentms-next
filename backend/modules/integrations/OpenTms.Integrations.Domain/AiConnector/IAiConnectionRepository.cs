using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Integrations.AiConnector;

/// <summary>The connection repository with the throttled use stamp and a duplicate-safe insert.</summary>
public interface IAiConnectionRepository : IRepository<AiConnection, Guid>
{
    /// <summary>
    /// Sets <see cref="AiConnection.LastUsedAt"/> to <paramref name="at"/> in one set-based update, only when it is null or
    /// older than <paramref name="staleBefore"/>, so parallel tool calls neither conflict nor write twice.
    /// </summary>
    Task RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <see cref="AiConnection.ConnectedAt"/> in one set-based update, only while the connection is not switched off.
    /// False when it is, including when a switch-off committed while this update waited for the row.
    /// </summary>
    Task<bool> TryConnectAgainAsync(Guid id, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="connection"/> unless a row with its authorization id exists (a double click on Allow). False
    /// when it lost that race; the caller's transaction stays usable and it reads the winner.
    /// </summary>
    Task<bool> TryInsertAsync(AiConnection connection, CancellationToken cancellationToken = default);
}
