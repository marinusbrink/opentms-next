using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Instances;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace OpenTms.HealthChecks;

/// <summary>The running instances (this one included) and how many of them dispatch the outbox.</summary>
public sealed record RunningInstances(int Count, int Dispatching);

/// <summary>
/// What the instance registry says about the running instances, for the checks whose verdict depends on how many run
/// (the in-memory cache, the live-update backplane, the outbox dispatcher). Null when the registry is switched off:
/// then only this instance is known.
/// </summary>
public interface IInstanceFleet
{
    Task<RunningInstances?> GetRunningAsync(CancellationToken cancellationToken = default);
}

public class InstanceFleet : IInstanceFleet, ITransientDependency
{
    private readonly IRepository<ApplicationInstance, Guid> _instances;
    private readonly CurrentApplicationInstance _current;
    private readonly InstanceRegistryOptions _options;
    private readonly OutboxOptions _outbox;
    private readonly IClock _clock;

    public InstanceFleet(
        IRepository<ApplicationInstance, Guid> instances,
        CurrentApplicationInstance current,
        IOptions<InstanceRegistryOptions> options,
        IOptions<OutboxOptions> outbox,
        IClock clock)
    {
        _instances = instances;
        _current = current;
        _options = options.Value;
        _outbox = outbox.Value;
        _clock = clock;
    }

    public async Task<RunningInstances?> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        /* Only rows that heartbeat recently can be running: the week of stopped rows stays in the database. */
        var now = _clock.Now.ToUniversalTime();
        var since = now - _options.UnresponsiveAfter;
        var running = (await _instances.GetListAsync(i => (i.StoppedAt == null && i.LastHeartbeatAt >= since) || i.Id == _current.Id, cancellationToken: cancellationToken))
            .Where(i => i.Id == _current.Id || ApplicationInstanceStatusResolver.Resolve(i, now, _options.UnresponsiveAfter) == ApplicationInstanceStatus.Running)
            .ToList();
        var count = running.Count;
        var dispatching = running.Count(i => i.OutboxDispatchEnabled);
        if (running.All(i => i.Id != _current.Id))
        {
            /* This instance runs whether or not its registration row exists yet (before the first heartbeat). */
            count++;
            dispatching += _outbox.DispatchEnabled ? 1 : 0;
        }

        return new RunningInstances(count, dispatching);
    }
}
