using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Maintenance.Outbox;

/// <summary>What is waiting in one database's outbox right now.</summary>
public sealed record OutboxSnapshot(long PendingCount, DateTime? OldestPendingAt);

/// <summary>
/// Reads the outbox table of one database for the Maintenance view. This is the one reader of the
/// table besides the dispatcher: the Platform module owns the table, and the view needs the
/// counts the dispatcher does not keep. Modules still never touch it (CLAUDE.md, "Events and the
/// outbox").
/// </summary>
public interface IOutboxInspector
{
    /// <summary>Counts the waiting events in the database of <paramref name="tenantId"/> (null = host database).</summary>
    Task<OutboxSnapshot> InspectAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
