using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>
/// Tells the dispatcher which tenant just committed outbox events, so delivery follows the commit
/// within milliseconds instead of waiting for the next sweep. In-process only: after the role split
/// the Jobs role needs a cross-process signal (open point AO-07 / proposal AP-13).
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Marks the tenant's database (null = host database) as having fresh events.</summary>
    void Notify(Guid? tenantId);

    /// <summary>Waits until at least one notification is pending. Returns false when the signal is completed.</summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);

    /// <summary>Takes the next pending notification without waiting.</summary>
    bool TryRead(out Guid? tenantId);
}
