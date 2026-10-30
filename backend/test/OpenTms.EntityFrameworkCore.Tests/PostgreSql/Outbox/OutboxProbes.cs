using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Integrations.Routing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
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

/// <summary>An OpenTms.* event whose handler writes a row and publishes a follow-up event, then can be told to fail.</summary>
public class WritingProbeEto
{
    public Guid ProbeId { get; set; }
}

public record ProbeDelivery(Guid ProbeId, Guid? TenantId, string EventType);

/// <summary>Records every delivery with the tenant the handler ran in.</summary>
public class ProbeRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<ProbeDelivery> _deliveries = new();

    public volatile bool FailNextFailingProbe;

    public volatile bool FailNextWritingProbe;

    /// <summary>The follow-up probe id the writing handler publishes for a writing probe (kept here so the event carries one id only).</summary>
    public ConcurrentDictionary<Guid, Guid> FollowUps { get; } = new();

    /// <summary>The route-cache key the writing handler uses as its row (the table exists in every tenant database).</summary>
    public static string RowKey(Guid probeId) => "outbox-probe:" + probeId.ToString("N");

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

/// <summary>
/// Writes a row (a route-cache entry: any table of the tenant database would do) and publishes an OpenTms.* follow-up
/// event, both inside the dispatcher's unit of work, then throws when told to: the review-B4 failure path.
/// </summary>
public class WritingProbeHandler : IDistributedEventHandler<WritingProbeEto>, ITransientDependency
{
    private readonly ProbeRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<RouteCacheEntry, Guid> _rows;
    private readonly IDistributedEventBus _eventBus;

    public WritingProbeHandler(ProbeRecorder recorder, ICurrentTenant currentTenant, IRepository<RouteCacheEntry, Guid> rows, IDistributedEventBus eventBus)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
        _rows = rows;
        _eventBus = eventBus;
    }

    public async Task HandleEventAsync(WritingProbeEto eventData)
    {
        var now = DateTime.UtcNow;
        /* autoSave: the row is really in the database inside the transaction before the failure. */
        await _rows.InsertAsync(
            new RouteCacheEntry(Guid.NewGuid(), _currentTenant.Id, ProbeRecorder.RowKey(eventData.ProbeId), "Probe", 1, 1, null, null, null, null, null, now, now.AddDays(1)),
            autoSave: true);
        await _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = _recorder.FollowUps[eventData.ProbeId] });

        if (_recorder.FailNextWritingProbe)
        {
            _recorder.FailNextWritingProbe = false;
            throw new InvalidOperationException("Simulated handler failure after writing for " + eventData.ProbeId);
        }

        _recorder.Record(new ProbeDelivery(eventData.ProbeId, _currentTenant.Id, nameof(WritingProbeEto)));
    }
}
