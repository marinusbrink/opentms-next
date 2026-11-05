using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// One AI tool a user connected (design 74): integrations."AiConnections" in the tenant's database. OpenIddict's
/// authorizations and tokens live in the host database and name a user but no tenant, so this tenant-owned row gives the
/// tenant its list of live connections without a cross-database read and gives <c>/mcp</c> a revocation check in the
/// tenant's own database. Holds a user id and timestamps only (art. 15: the connections endpoint; art. 17:
/// <c>AiConnectionUserDeletedHandler</c> removes a deleted user's rows).
/// </summary>
public class AiConnection : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>The user who connected.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The fixed client of the tool, for example <c>OpenTms_Mcp_Claude</c>.</summary>
    public string ClientId { get; private set; } = default!;

    /// <summary>The OpenIddict authorization in the host database; unique.</summary>
    public string AuthorizationId { get; private set; } = default!;

    public DateTime ConnectedAt { get; private set; }

    /// <summary>Updated at most once per <see cref="AiConnectorConsts.LastUsedThrottle"/>.</summary>
    public DateTime? LastUsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    protected AiConnection()
    {
    }

    public AiConnection(Guid id, Guid? tenantId, Guid userId, string clientId, string authorizationId, DateTime connectedAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        ClientId = Check.NotNullOrWhiteSpace(clientId, nameof(clientId), AiConnectorConsts.MaxClientIdLength);
        AuthorizationId = Check.NotNullOrWhiteSpace(authorizationId, nameof(authorizationId), AiConnectorConsts.MaxAuthorizationIdLength);
        ConnectedAt = connectedAt;
    }

    /// <summary>The user allowed the tool again on the same authorization: it is live from now on.</summary>
    public void Reconnect(DateTime at)
    {
        ConnectedAt = at;
        RevokedAt = null;
        RevokedByUserId = null;
    }

    /// <summary>Switches the connection off. A connection already off keeps its first revocation.</summary>
    public void Revoke(DateTime at, Guid? byUserId)
    {
        if (RevokedAt == null)
        {
            RevokedAt = at;
            RevokedByUserId = byUserId;
        }
    }
}
