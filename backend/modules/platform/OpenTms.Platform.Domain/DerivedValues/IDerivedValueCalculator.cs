using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// Implemented by a module for one target type (design 49): recalculates the derived values of one record and writes
/// them as plain stored columns in the module's own tables (AD-07). It runs inside the job's transaction, under the
/// record's tenant; if a newer save arrives meanwhile, everything it wrote is rolled back and a later job writes. It
/// must therefore be deterministic for a given state of the facts, and never call out of process inside the transaction.
/// Register it with <see cref="DerivedValueOptions.AddCalculator{TCalculator}"/>.
/// </summary>
public interface IDerivedValueCalculator
{
    Task CalculateAsync(DerivedValueCalculationContext context);
}

public sealed record DerivedValueCalculationContext(string TargetType, Guid TargetId, long Version, CancellationToken CancellationToken);
