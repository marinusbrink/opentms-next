using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PostgreSql.Outbox;

/// <summary>An OpenTms.* event: travels through the outbox.</summary>
public class OutboxProbeEto
{
    public Guid ProbeId { get; set; }
}

/// <summary>An OpenTms.* event whose handler can be told to fail once (at-least-once delivery).</summary>
public class FailingProbeEto
{
    public Guid ProbeId { get; set; }
}

public record ProbeDelivery(Guid ProbeId, Guid? TenantId, string EventType);

/// <summary>Records every delivery with the tenant the handler ran in.</summary>
public class ProbeRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<ProbeDelivery> _deliveries = new();

    public volatile bool FailNextFailingProbe;

    public IReadOnlyList<ProbeDelivery> Deliveries => _deliveries.ToList();

    public void Record(ProbeDelivery delivery) => _deliveries.Enqueue(delivery);

    public IReadOnlyList<ProbeDelivery> For(Guid probeId) => _deliveries.Where(d => d.ProbeId == probeId).ToList();
}

public class OutboxProbeHandler : IDistributedEventHandler<OutboxProbeEto>, ITransientDependency
{
    private readonly ProbeRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public OutboxProbeHandler(ProbeRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(OutboxProbeEto eventData)
    {
        _recorder.Record(new ProbeDelivery(eventData.ProbeId, _currentTenant.Id, nameof(OutboxProbeEto)));
        return Task.CompletedTask;
    }
}

public class FailingProbeHandler : IDistributedEventHandler<FailingProbeEto>, ITransientDependency
{
    private readonly ProbeRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public FailingProbeHandler(ProbeRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(FailingProbeEto eventData)
    {
        if (_recorder.FailNextFailingProbe)
        {
            _recorder.FailNextFailingProbe = false;
            throw new InvalidOperationException("Simulated handler failure for " + eventData.ProbeId);
        }

        _recorder.Record(new ProbeDelivery(eventData.ProbeId, _currentTenant.Id, nameof(FailingProbeEto)));
        return Task.CompletedTask;
    }
}

public class ExternalProbeHandler : IDistributedEventHandler<ThirdParty.Probe.ExternalProbeEto>, ITransientDependency
{
    private readonly ProbeRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public ExternalProbeHandler(ProbeRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(ThirdParty.Probe.ExternalProbeEto eventData)
    {
        _recorder.Record(new ProbeDelivery(eventData.ProbeId, _currentTenant.Id, nameof(ThirdParty.Probe.ExternalProbeEto)));
        return Task.CompletedTask;
    }
}
