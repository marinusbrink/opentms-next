using System;
using System.Collections.Concurrent;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Maintenance.Outbox;

/// <summary>The outcome of the last dispatcher visit to one database.</summary>
public sealed record OutboxDispatchRecord(DateTime LastRunAt, int LastPublishedCount, string? LastError, DateTime? LastErrorAt);

/// <summary>
/// Remembers, per database, what the <c>OutboxDispatcher</c> did last: when it ran, how many events
/// it published and the error that stopped it, if any. In memory and per process: after the role
/// split (AD-11) only the Jobs role fills it, which the Maintenance view states. It exists so that
/// a persistently failing event (open point AO-10) is visible to an administrator instead of only
/// to the log.
/// </summary>
public interface IOutboxDispatchRegistry
{
    void RecordRun(Guid? tenantId, int publishedCount, string? error);

    OutboxDispatchRecord? Find(Guid? tenantId);
}

public class OutboxDispatchRegistry : IOutboxDispatchRegistry, ISingletonDependency
{
    private static readonly Guid HostKey = Guid.Empty;

    private readonly ConcurrentDictionary<Guid, OutboxDispatchRecord> _records = new();

    public void RecordRun(Guid? tenantId, int publishedCount, string? error)
    {
        var now = DateTime.UtcNow;
        _records.AddOrUpdate(
            tenantId ?? HostKey,
            _ => new OutboxDispatchRecord(now, publishedCount, error, error == null ? null : now),
            (_, previous) => new OutboxDispatchRecord(
                now,
                publishedCount,
                error ?? previous.LastError,
                error == null ? previous.LastErrorAt : now));
    }

    public OutboxDispatchRecord? Find(Guid? tenantId) =>
        _records.TryGetValue(tenantId ?? HostKey, out var record) ? record : null;
}
