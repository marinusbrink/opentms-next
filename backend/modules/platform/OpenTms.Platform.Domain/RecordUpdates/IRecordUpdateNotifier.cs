using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>
/// Tells the screens that show a record that it changed (design 49, flow 2 D). Inside a unit of work the notice is sent
/// after it commits (a rolled-back change tells nobody); outside one, at once. A lost notice only delays a screen until
/// its next fetch, so delivery is best effort and never fails the caller.
/// <para>
/// Design 67: every notice also reaches the record type's group, and notices raised in one unit of work for the same
/// record are coalesced: the last one wins and it is sent once, after the commit.
/// </para>
/// </summary>
public interface IRecordUpdateNotifier
{
    Task NotifyAsync(string targetType, Guid targetId, long? version = null, DerivedValueStatus? status = null);

    /// <summary>A notice with routing hints for screens that watch the whole type (design 67).</summary>
    Task NotifyAsync(string targetType, Guid targetId, RecordUpdateHints? hints, long? version = null, DerivedValueStatus? status = null);
}

public class RecordUpdateNotifier : IRecordUpdateNotifier, ITransientDependency
{
    /// <summary>The unit-of-work item that holds the notices waiting for the commit.</summary>
    public const string PendingItemKey = "OpenTms.Platform.RecordUpdates.Pending";

    public ILogger<RecordUpdateNotifier> Logger { get; set; } = NullLogger<RecordUpdateNotifier>.Instance;

    private readonly IRecordUpdateTransport _transport;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public RecordUpdateNotifier(IRecordUpdateTransport transport, IUnitOfWorkManager unitOfWorkManager, ICurrentTenant currentTenant)
    {
        _transport = transport;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
    }

    public virtual Task NotifyAsync(string targetType, Guid targetId, long? version = null, DerivedValueStatus? status = null)
        => NotifyAsync(targetType, targetId, null, version, status);

    public virtual Task NotifyAsync(string targetType, Guid targetId, RecordUpdateHints? hints, long? version = null, DerivedValueStatus? status = null)
    {
        var update = new RecordUpdate(_currentTenant.Id, targetType, targetId, version, status, hints);
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            return SendAsync(update);
        }

        /* One pending set per unit of work (a child unit of work shares its parent's items and commit), flushed once
         * after the commit. A rolled-back unit of work never runs OnCompleted, so nothing leaves. */
        if (!unitOfWork.Items.TryGetValue(PendingItemKey, out var item) || item is not PendingRecordUpdates pending)
        {
            pending = new PendingRecordUpdates();
            unitOfWork.Items[PendingItemKey] = pending;
            unitOfWork.OnCompleted(() => FlushAsync(pending));
        }

        pending.Set(update);
        return Task.CompletedTask;
    }

    private async Task FlushAsync(PendingRecordUpdates pending)
    {
        foreach (var update in pending.TakeAll())
        {
            await SendAsync(update);
        }
    }

    private async Task SendAsync(RecordUpdate update)
    {
        try
        {
            await _transport.SendAsync(update);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Record update for {TargetType} {TargetId} of tenant {TenantId} was not delivered.", update.TargetType, update.TargetId, update.TenantId);
        }
    }

    /// <summary>The notices of one unit of work, one per (tenant, type, id), in the order each record was first touched.</summary>
    private sealed class PendingRecordUpdates
    {
        private readonly Dictionary<(Guid? TenantId, string TargetType, Guid TargetId), int> _index = new();
        private readonly List<RecordUpdate> _updates = new();
        private readonly object _lock = new();

        public void Set(RecordUpdate update)
        {
            lock (_lock)
            {
                var key = (update.TenantId, update.TargetType, update.TargetId);
                if (_index.TryGetValue(key, out var position))
                {
                    _updates[position] = update;
                }
                else
                {
                    _index[key] = _updates.Count;
                    _updates.Add(update);
                }
            }
        }

        public IReadOnlyList<RecordUpdate> TakeAll()
        {
            lock (_lock)
            {
                var updates = _updates.ToArray();
                _updates.Clear();
                _index.Clear();
                return updates;
            }
        }
    }
}
