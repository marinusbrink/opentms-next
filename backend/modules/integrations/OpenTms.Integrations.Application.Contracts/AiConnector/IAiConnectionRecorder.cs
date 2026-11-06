using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.AiConnector;

/// <summary>What the connect screen writes (design 74). Runs in the current tenant, in a unit of work of its own. In-process only.</summary>
public interface IAiConnectionRecorder
{
    /// <summary>
    /// Of these authorizations that are valid in the host, the one whose connection of this user and tool is on, newest first, or
    /// null: a connection that is off, or an authorization without a connection, is never reused. Those that are off are revoked
    /// in the host once more on the way, because a host-side failure of switching off may have left them valid; a failure there is
    /// logged and does not stop the connect.
    /// </summary>
    Task<string?> FindReusableAsync(Guid userId, string clientId, IReadOnlyCollection<string> validAuthorizationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allow on the connect screen: creates the connection of this authorization, or marks one that is on as connected again, and
    /// answers true. Answers false and changes nothing when the connection of this authorization is off, because it never comes back.
    /// </summary>
    Task<bool> TryRecordAsync(Guid userId, string clientId, string authorizationId, CancellationToken cancellationToken = default);
}
