using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Integrations.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <summary>The published <see cref="IAiConnectionChecker"/> and <see cref="IAiConnectionRecorder"/>; tenant-filtered.</summary>
[ExposeServices(typeof(IAiConnectionChecker), typeof(IAiConnectionRecorder))]
public class AiConnectionStore : IAiConnectionChecker, IAiConnectionRecorder, ITransientDependency
{
    private readonly IAiConnectionRepository _connections;
    private readonly ISettingProvider _settings;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly IClock _clock;
    private readonly IAsyncQueryableExecuter _queries;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly AiConnectionAuthorizationRevoker _revoker;

    public AiConnectionStore(
        IAiConnectionRepository connections,
        ISettingProvider settings,
        ICurrentTenant currentTenant,
        IGuidGenerator guids,
        IClock clock,
        IAsyncQueryableExecuter queries,
        IUnitOfWorkManager unitOfWorkManager,
        AiConnectionAuthorizationRevoker revoker)
    {
        _connections = connections;
        _settings = settings;
        _currentTenant = currentTenant;
        _guids = guids;
        _clock = clock;
        _queries = queries;
        _unitOfWorkManager = unitOfWorkManager;
        _revoker = revoker;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<bool> IsActiveAsync(string authorizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var query = (await _connections.GetQueryableAsync())
            .Where(c => c.AuthorizationId == authorizationId && c.UserId == userId && c.RevokedAt == null)
            .Select(c => new { c.Id, c.LastUsedAt });
        var connection = await _queries.FirstOrDefaultAsync(query, cancellationToken);
        if (connection == null)
        {
            return false;
        }

        var now = _clock.Now;
        var staleBefore = now - AiConnectorConsts.LastUsedThrottle;
        if (connection.LastUsedAt == null || connection.LastUsedAt < staleBefore)
        {
            await _connections.RecordUseAsync(connection.Id, now, staleBefore, cancellationToken);
        }

        return true;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<bool> IsAcceptedAsync(CancellationToken cancellationToken = default)
        => !string.IsNullOrEmpty(await _settings.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedAt));

    public virtual async Task<string?> FindReusableAsync(Guid userId, string clientId, IReadOnlyCollection<string> validAuthorizationIds, CancellationToken cancellationToken = default)
    {
        if (validAuthorizationIds.Count == 0)
        {
            return null;
        }

        /* Bounded: the authorizations of one user and one tool. */
        var ids = validAuthorizationIds.ToList();
        var query = (await _connections.GetQueryableAsync())
            .Where(c => ids.Contains(c.AuthorizationId) && c.UserId == userId && c.ClientId == clientId)
            .Select(c => new { c.AuthorizationId, c.ConnectedAt, c.RevokedAt });
        var rows = await _queries.ToListAsync(query, cancellationToken);

        foreach (var switchedOff in rows.Where(r => r.RevokedAt != null))
        {
            await _revoker.RevokeAsync(switchedOff.AuthorizationId);
        }

        return rows.Where(r => r.RevokedAt == null).OrderByDescending(r => r.ConnectedAt).FirstOrDefault()?.AuthorizationId;
    }

    public virtual async Task<bool> TryRecordAsync(Guid userId, string clientId, string authorizationId, CancellationToken cancellationToken = default)
    {
        /* A unit of work of its own: the row must be committed before the authorization code leaves for the AI tool. */
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var now = _clock.Now;
        var existing = await _connections.FirstOrDefaultAsync(c => c.AuthorizationId == authorizationId, cancellationToken);
        if (existing == null)
        {
            var inserted = await _connections.TryInsertAsync(
                new AiConnection(_guids.Create(), _currentTenant.Id, userId, clientId, authorizationId, now), cancellationToken);
            existing = inserted ? null : await _connections.FirstAsync(c => c.AuthorizationId == authorizationId, cancellationToken);
        }

        if (existing?.RevokedAt != null)
        {
            return false;
        }

        /* Conditional in the database: a switch-off committing after the read above is seen here, never overwritten. */
        if (existing != null && !await _connections.TryConnectAgainAsync(existing.Id, now, cancellationToken))
        {
            return false;
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return true;
    }
}
