using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.DerivedValues;

/// <summary>
/// The test side of design 49: a probe target type whose calculator leaves a visible, transactional trace (it takes a
/// number from a per-record sequence, so a rolled-back calculation leaves the counter untouched), can be told to fail
/// or to wait, and a transport that records every push.
/// </summary>
public static class DerivedValueProbe
{
    public const string TargetType = "Test.Probe";

    /// <summary>A permission every test principal holds (AlwaysAllowAuthorization).</summary>
    public const string WatchPermission = "Platform.DerivedValues.Recalculate";

    public static string SequenceKey(Guid targetId) => "Test.Probe:" + targetId;

    public static readonly NumberSequenceFormat Format = NumberSequenceFormat.Create(null, false, false, "", 3);

    public static void Register(DerivedValueOptions options)
    {
        if (options.Find(TargetType) == null)
        {
            options.AddCalculator<ProbeCalculator>(TargetType, WatchPermission);
        }
    }
}

/// <summary>Per-record behaviour of the probe calculator; a singleton shared by the test and the job.</summary>
public class ProbeCalculatorControl : ISingletonDependency
{
    public ConcurrentDictionary<Guid, string> FailWith { get; } = new();

    /// <summary>The calculation of a record waits for this before it writes its trace.</summary>
    public ConcurrentDictionary<Guid, TaskCompletionSource> Gates { get; } = new();

    /// <summary>Signalled when a calculation of a record has started (it is then waiting at its gate).</summary>
    public ConcurrentDictionary<Guid, TaskCompletionSource> Started { get; } = new();

    public ConcurrentQueue<(Guid TargetId, long Version)> Calls { get; } = new();
}

public class ProbeCalculator : IDerivedValueCalculator, ITransientDependency
{
    private readonly ProbeCalculatorControl _control;
    private readonly INumberSequenceGenerator _sequences;

    public ProbeCalculator(ProbeCalculatorControl control, INumberSequenceGenerator sequences)
    {
        _control = control;
        _sequences = sequences;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        _control.Calls.Enqueue((context.TargetId, context.Version));
        if (_control.Started.TryGetValue(context.TargetId, out var started))
        {
            started.TrySetResult();
        }

        if (_control.Gates.TryRemove(context.TargetId, out var gate))
        {
            await gate.Task;
        }

        if (_control.FailWith.TryGetValue(context.TargetId, out var code))
        {
            throw new BusinessException(code);
        }

        await _sequences.NextAsync(DerivedValueProbe.SequenceKey(context.TargetId), DerivedValueProbe.Format);
    }
}

/// <summary>Records every push instead of sending it (replaces the null transport in the test processes).</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IRecordUpdateTransport), typeof(CapturingRecordUpdateTransport))]
public class CapturingRecordUpdateTransport : IRecordUpdateTransport, ISingletonDependency
{
    private readonly ConcurrentQueue<RecordUpdate> _sent = new();

    public IReadOnlyList<RecordUpdate> Sent => _sent.ToList();

    public IReadOnlyList<RecordUpdate> For(Guid targetId) => _sent.Where(u => u.TargetId == targetId).ToList();

    public Task SendAsync(RecordUpdate update)
    {
        _sent.Enqueue(update);
        return Task.CompletedTask;
    }
}
