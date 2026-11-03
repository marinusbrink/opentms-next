using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Linq;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// Synchronises the mailbox connections of the current tenant (design 72 "Sync"). Nothing runs for a tenant without
/// <see cref="IntegrationsFeatures.OrderIntake"/>. Per Active connection, under the distributed lock
/// <c>mailbox-sync:{id}</c>: refresh the access token (a rotated refresh token is stored at once), list the new messages
/// since the cursor, fetch each raw message and hand it to <see cref="IMailMessageRecorder"/>, and advance the cursor in
/// the same transaction as the stored message, so a crash never skips mail and never stores it twice (the recorder is
/// idempotent on the external id). <c>invalid_grant</c> → Reconnect (the cursor stays); any other failure →
/// LastErrorCode, retried next period. One failing connection never stops the others. Logs carry ids, never addresses.
/// </summary>
public class MailboxSynchronizer : ITransientDependency
{
    /// <summary>The Active connections one sweep visits per tenant (design 72: 1–20 per tenant).</summary>
    public const int MaxConnectionsPerSweep = 200;

    private readonly IRepository<MailboxConnection, Guid> _connections;
    private readonly MailProviderClientResolver _clients;
    private readonly MailboxTokenService _tokens;
    private readonly IMailMessageRecorder _recorder;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IFeatureChecker _features;
    private readonly IClock _clock;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ILogger<MailboxSynchronizer> Logger { get; set; } = NullLogger<MailboxSynchronizer>.Instance;

    public MailboxSynchronizer(
        IRepository<MailboxConnection, Guid> connections,
        MailProviderClientResolver clients,
        MailboxTokenService tokens,
        IMailMessageRecorder recorder,
        IUnitOfWorkManager unitOfWorkManager,
        IAbpDistributedLock distributedLock,
        IFeatureChecker features,
        IClock clock,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _connections = connections;
        _clients = clients;
        _tokens = tokens;
        _recorder = recorder;
        _unitOfWorkManager = unitOfWorkManager;
        _distributedLock = distributedLock;
        _features = features;
        _clock = clock;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Every Active connection of the current tenant; returns the number of messages stored.</summary>
    public async Task<int> SyncCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        if (!await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntake))
        {
            return 0;
        }

        List<Guid> ids;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            var query = (await _connections.GetQueryableAsync())
                .Where(c => c.Status == MailboxStatus.Active)
                .OrderBy(c => c.LastSyncAt)
                .Select(c => c.Id)
                .Take(MaxConnectionsPerSweep);
            ids = await _asyncExecuter.ToListAsync(query, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }

        var stored = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stored += await SyncAsync(id, cancellationToken);
        }

        return stored;
    }

    /// <summary>One connection; returns the number of new messages stored. Never throws except on cancellation.</summary>
    public async Task<int> SyncAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        await using var handle = await _distributedLock.TryAcquireAsync("mailbox-sync:" + connectionId, cancellationToken: cancellationToken);
        if (handle == null)
        {
            Logger.LogDebug("Mailbox connection {ConnectionId} is being synchronised elsewhere; skipping.", connectionId);
            return 0;
        }

        try
        {
            return await SyncLockedAsync(connectionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailboxReconnectRequiredException)
        {
            await UpdateConnectionAsync(connectionId, c => c.MarkReconnect(MailboxReconnectRequiredException.ErrorCode, _clock.Now), cancellationToken);
            Logger.LogInformation("Mailbox connection {ConnectionId} needs a new consent (invalid_grant); it waits in Reconnect.", connectionId);
            return 0;
        }
        catch (Exception e)
        {
            var code = e is MailProviderException provider ? provider.ErrorCode : "sync_failed";
            await UpdateConnectionAsync(connectionId, c => c.RecordError(code, _clock.Now), cancellationToken);
            if (e is MailProviderException)
            {
                Logger.LogWarning("Mailbox connection {ConnectionId} could not be synchronised ({ErrorCode}); retried next period.", connectionId, code);
            }
            else
            {
                Logger.LogError(e, "Mailbox connection {ConnectionId} failed to synchronise; retried next period.", connectionId);
            }

            return 0;
        }
    }

    private async Task<int> SyncLockedAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        /* 1. A fresh access token; a rotated refresh token is committed at once, whatever happens next. */
        string accessToken, folderId, cursor;
        MailProvider provider;
        DateTime? lastSyncAt;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            var connection = await _connections.FindAsync(connectionId, cancellationToken: cancellationToken);
            if (connection is not { IsSyncable: true } || connection.FolderId == null || connection.SyncCursor == null)
            {
                return 0;
            }

            accessToken = await _tokens.GetAccessTokenAsync(connection, cancellationToken);
            await _connections.UpdateAsync(connection, autoSave: true, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            (provider, folderId, cursor, lastSyncAt) = (connection.Provider, connection.FolderId, connection.SyncCursor, connection.LastSyncAt);
        }

        var client = _clients.Get(provider);
        var channel = provider == MailProvider.Gmail ? InboundChannel.Gmail : InboundChannel.Microsoft365;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stored = 0;

        /* 2. The new messages, each stored with the cursor that follows it; checkpoints advance the cursor alone. */
        await foreach (var change in client.GetChangesAsync(accessToken, folderId, cursor, lastSyncAt, cancellationToken))
        {
            MailRawMessage? raw = null;
            string? externalId = null;
            if (change.Message != null && seen.Add(change.Message.ExternalId))
            {
                raw = await client.FetchRawAsync(accessToken, change.Message, cancellationToken);
                externalId = change.Message.ExternalId;
            }

            if (raw == null && change.Cursor == null)
            {
                continue;
            }

            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var connection = await _connections.FindAsync(connectionId, cancellationToken: cancellationToken);
            if (connection is not { IsSyncable: true } || connection.FolderId != folderId)
            {
                /* Paused, deleted or moved to another folder meanwhile: stop without touching it. */
                return stored;
            }

            if (raw != null)
            {
                var result = await _recorder.RecordMailAsync(
                    channel, connectionId, externalId!, raw.Content, raw.ReceivedAt ?? change.Message!.ReceivedAt ?? _clock.Now, cancellationToken);
                if (result.IsNew)
                {
                    stored++;
                }
            }

            if (change.Cursor != null)
            {
                connection.RecordSync(change.Cursor, _clock.Now);
                await _connections.UpdateAsync(connection, autoSave: true, cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }

        if (stored > 0)
        {
            Logger.LogInformation("Mailbox connection {ConnectionId}: {Count} new messages stored.", connectionId, stored);
        }

        return stored;
    }

    private async Task UpdateConnectionAsync(Guid connectionId, Action<MailboxConnection> change, CancellationToken cancellationToken)
    {
        try
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var connection = await _connections.FindAsync(connectionId, cancellationToken: cancellationToken);
            if (connection != null)
            {
                change(connection);
                await _connections.UpdateAsync(connection, autoSave: true, cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogWarning(e, "Mailbox connection {ConnectionId}: its sync status could not be saved.", connectionId);
        }
    }
}
