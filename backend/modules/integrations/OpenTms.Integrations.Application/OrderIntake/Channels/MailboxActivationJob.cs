using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>Ids only: Hangfire stores job arguments in plain text.</summary>
[BackgroundJobName("OpenTms.Integrations.MailboxActivation")]
public class MailboxActivationArgs
{
    public Guid? TenantId { get; set; }

    public Guid ConnectionId { get; set; }

    public string? CorrelationId { get; set; }
}

/// <summary>
/// Takes the provider's current cursor of an Activating connection's folder and activates it (design 72 "Choose folder",
/// build note 18): Gmail's <c>historyId</c>, or Graph's delta link after paging the folder's current contents without
/// importing them — too slow for the request that chose the folder. Runs in the tenant of its arguments under the sync's
/// distributed lock <c>mailbox-sync:{id}</c>. Idempotent: a connection that is no longer Activating, or moved to another
/// folder meanwhile, is left alone. <c>invalid_grant</c> → Reconnect (no retry); any other failure → LastErrorCode, the
/// connection stays Activating and the job throws, so Hangfire retries it.
/// </summary>
public class MailboxActivationJob : AsyncBackgroundJob<MailboxActivationArgs>, ITransientDependency
{
    /// <summary>How long the job waits for a running sync or activation of the same connection.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private readonly IRepository<MailboxConnection, Guid> _connections;
    private readonly MailProviderClientResolver _clients;
    private readonly MailboxTokenService _tokens;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IFeatureChecker _features;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICorrelationIdProvider _correlationIds;
    private readonly IClock _clock;

    public MailboxActivationJob(
        IRepository<MailboxConnection, Guid> connections,
        MailProviderClientResolver clients,
        MailboxTokenService tokens,
        IAbpDistributedLock distributedLock,
        IUnitOfWorkManager unitOfWorkManager,
        IFeatureChecker features,
        ICurrentTenant currentTenant,
        ICorrelationIdProvider correlationIds,
        IClock clock)
    {
        _connections = connections;
        _clients = clients;
        _tokens = tokens;
        _distributedLock = distributedLock;
        _unitOfWorkManager = unitOfWorkManager;
        _features = features;
        _currentTenant = currentTenant;
        _correlationIds = correlationIds;
        _clock = clock;
    }

    public override async Task ExecuteAsync(MailboxActivationArgs args)
    {
        using var correlation = _correlationIds.Change(string.IsNullOrWhiteSpace(args.CorrelationId) ? null : args.CorrelationId);
        using var tenant = _currentTenant.Change(args.TenantId);
        if (!await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntake))
        {
            Logger.LogInformation("Mailbox connection {ConnectionId} (tenant {TenantId}) not activated: order intake is off.", args.ConnectionId, args.TenantId);
            return;
        }

        await using var handle = await _distributedLock.TryAcquireAsync("mailbox-sync:" + args.ConnectionId, LockTimeout);
        if (handle == null)
        {
            /* Busy elsewhere: the job fails and Hangfire retries it later. */
            throw new MailboxActivationRetryException(args.ConnectionId);
        }

        try
        {
            await ActivateLockedAsync(args.ConnectionId);
        }
        catch (MailboxReconnectRequiredException)
        {
            await UpdateAsync(args.ConnectionId, c => c.MarkReconnect(MailboxReconnectRequiredException.ErrorCode, _clock.Now));
            Logger.LogInformation("Mailbox connection {ConnectionId} needs a new consent (invalid_grant); it waits in Reconnect.", args.ConnectionId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var code = e is MailProviderException provider ? provider.ErrorCode : "activation_failed";
            await UpdateAsync(args.ConnectionId, c =>
            {
                if (c.Status == MailboxStatus.Activating)
                {
                    c.RecordError(code, _clock.Now);
                }
            });
            Logger.LogWarning("Mailbox connection {ConnectionId} (tenant {TenantId}) could not be activated ({ErrorCode}); Hangfire retries.", args.ConnectionId, args.TenantId, code);
            throw;
        }
    }

    private async Task ActivateLockedAsync(Guid connectionId)
    {
        /* 1. A fresh access token; a rotated refresh token is committed at once, whatever happens next. */
        string accessToken, folderId;
        MailProvider provider;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            var connection = await _connections.FindAsync(connectionId);
            if (connection is not { Status: MailboxStatus.Activating, FolderId: not null })
            {
                Logger.LogInformation("Mailbox connection {ConnectionId} is no longer activating; nothing to do.", connectionId);
                return;
            }

            accessToken = await _tokens.GetAccessTokenAsync(connection);
            await _connections.UpdateAsync(connection, autoSave: true);
            await unitOfWork.CompleteAsync();
            (provider, folderId) = (connection.Provider, connection.FolderId);
        }

        /* 2. The provider's cursor, outside any transaction (Graph pages the folder's whole current contents). */
        var cursor = await _clients.Get(provider).GetCurrentCursorAsync(accessToken, folderId);

        /* 3. Activate, unless the connection changed meanwhile. */
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            var connection = await _connections.FindAsync(connectionId);
            if (connection is { Status: MailboxStatus.Activating } && connection.FolderId == folderId)
            {
                /* From here on: only mail that arrives after activation is imported (design 72, assumption 2). */
                connection.Activate(cursor, _clock.Now);
                await _connections.UpdateAsync(connection, autoSave: true);
                Logger.LogInformation("Mailbox connection {ConnectionId} activated.", connectionId);
            }

            await unitOfWork.CompleteAsync();
        }
    }

    private async Task UpdateAsync(Guid connectionId, Action<MailboxConnection> change)
    {
        try
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var connection = await _connections.FindAsync(connectionId);
            if (connection != null)
            {
                change(connection);
                await _connections.UpdateAsync(connection, autoSave: true);
            }

            await unitOfWork.CompleteAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogWarning(e, "Mailbox connection {ConnectionId}: its activation status could not be saved.", connectionId);
        }
    }
}

/// <summary>Another sync or activation holds the connection's lock: the job throws so Hangfire retries it.</summary>
public class MailboxActivationRetryException : Exception
{
    public MailboxActivationRetryException(Guid connectionId)
        : base($"Mailbox connection {connectionId} is busy; activation retried.")
    {
    }
}
