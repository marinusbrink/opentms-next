using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// What the host asks of the connector's tenant data (design 74, checks 3 and 6), so it never touches the entity or the
/// <c>DbContext</c>. Runs in the current tenant. In-process only.
/// </summary>
public interface IAiConnectionChecker
{
    /// <summary>
    /// True when a connection with this authorization id exists, belongs to this user and is not revoked. Stamps the
    /// connection's last use, at most once per <see cref="AiConnectorConsts.LastUsedThrottle"/>.
    /// </summary>
    Task<bool> IsActiveAsync(string authorizationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>True when the tenant administrator has accepted the data sharing.</summary>
    Task<bool> IsAcceptedAsync(CancellationToken cancellationToken = default);
}
