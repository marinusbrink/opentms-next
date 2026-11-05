using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.AiConnector;

/// <summary>What the connect screen writes (design 74). Runs in the current tenant, in a unit of work of its own. In-process only.</summary>
public interface IAiConnectionRecorder
{
    /// <summary>
    /// Allow on the connect screen: creates the connection of this authorization, or makes the existing one live again, so
    /// connecting twice with one authorization reuses one row.
    /// </summary>
    Task UpsertAsync(Guid userId, string clientId, string authorizationId, CancellationToken cancellationToken = default);
}
