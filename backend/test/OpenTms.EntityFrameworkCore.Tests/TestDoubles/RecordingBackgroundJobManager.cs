using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace OpenTms.TestDoubles;

/// <summary>
/// The test processes run no Hangfire: jobs an event handler enqueues (design 49's <c>DerivedValuesRequestedHandler</c>)
/// are recorded here, and a test runs them itself through the job class. Replaces ABP's null manager.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IBackgroundJobManager), typeof(RecordingBackgroundJobManager))]
public class RecordingBackgroundJobManager : IBackgroundJobManager, ISingletonDependency
{
    private readonly ConcurrentQueue<object> _enqueued = new();

    public IReadOnlyList<object> Enqueued => _enqueued.ToList();

    public IReadOnlyList<TArgs> Of<TArgs>() => _enqueued.OfType<TArgs>().ToList();

    /// <summary>Forgets every recorded job (the leftovers of earlier tests in the same process).</summary>
    public void Clear() => _enqueued.Clear();

    /// <summary>Takes every recorded job of one argument type out of the queue, in order.</summary>
    public List<TArgs> Drain<TArgs>()
    {
        var kept = new List<object>();
        var taken = new List<TArgs>();
        while (_enqueued.TryDequeue(out var args))
        {
            if (args is TArgs match)
            {
                taken.Add(match);
            }
            else
            {
                kept.Add(args);
            }
        }

        foreach (var k in kept)
        {
            _enqueued.Enqueue(k);
        }

        return taken;
    }

    public Task<string> EnqueueAsync<TArgs>(TArgs args, BackgroundJobPriority priority = BackgroundJobPriority.Normal, TimeSpan? delay = null)
    {
        _enqueued.Enqueue(args!);
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }
}
