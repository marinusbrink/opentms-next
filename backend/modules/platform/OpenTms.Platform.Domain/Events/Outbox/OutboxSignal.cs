using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Events.Outbox;

public class OutboxSignal : IOutboxSignal, ISingletonDependency
{
    private const string HostKey = "host";

    private readonly Channel<Guid?> _channel = Channel.CreateUnbounded<Guid?>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /* One pending notification per database is enough: a burst of commits for the same tenant
     * collapses into a single visit, which reads everything that is waiting. */
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public void Notify(Guid? tenantId)
    {
        if (_pending.TryAdd(KeyOf(tenantId), 0))
        {
            _channel.Writer.TryWrite(tenantId);
        }
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.WaitToReadAsync(cancellationToken);
    }

    public bool TryRead(out Guid? tenantId)
    {
        if (_channel.Reader.TryRead(out tenantId))
        {
            _pending.TryRemove(KeyOf(tenantId), out _);
            return true;
        }

        return false;
    }

    private static string KeyOf(Guid? tenantId)
    {
        return tenantId?.ToString("N") ?? HostKey;
    }
}
