using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The registry of design 49: which target types have a calculator, and which permission lets a user watch a type's
/// records live. A module registers in its own <c>ConfigureServices</c>:
/// <code>Configure&lt;DerivedValueOptions&gt;(o =&gt; o.AddCalculator&lt;TripCostCalculator&gt;("PlanningExecution.TransportTrip", PlanningExecutionPermissions.Trips.Default));</code>
/// A malformed or duplicate target type fails at start-up.
/// </summary>
public partial class DerivedValueOptions
{
    private readonly Dictionary<string, DerivedValueTargetDefinition> _targets = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, DerivedValueTargetDefinition> Targets => _targets;

    /// <summary>A target type whose derived values <typeparamref name="TCalculator"/> recalculates.</summary>
    public DerivedValueOptions AddCalculator<TCalculator>(string targetType, string watchPermission)
        where TCalculator : class, IDerivedValueCalculator
        => Add(new DerivedValueTargetDefinition(targetType, watchPermission, typeof(TCalculator)));

    /// <summary>A target type without derived values whose records screens may still watch for change notices.</summary>
    public DerivedValueOptions AddWatchable(string targetType, string watchPermission)
        => Add(new DerivedValueTargetDefinition(targetType, watchPermission, null));

    public DerivedValueTargetDefinition? Find(string? targetType)
        => targetType != null && _targets.TryGetValue(targetType, out var definition) ? definition : null;

    private DerivedValueOptions Add(DerivedValueTargetDefinition definition)
    {
        if (!IsValidTargetType(definition.TargetType))
        {
            throw new ArgumentException($"'{definition.TargetType}' is not a target type: use '<Module>.<Entity>' (letters and digits, at most {DerivedValueConsts.MaxTargetTypeLength} characters).", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.WatchPermission))
        {
            throw new ArgumentException($"Target type '{definition.TargetType}' needs a watch permission.", nameof(definition));
        }

        if (!_targets.TryAdd(definition.TargetType, definition))
        {
            throw new InvalidOperationException($"Target type '{definition.TargetType}' is registered twice.");
        }

        return this;
    }

    public static bool IsValidTargetType(string? targetType)
        => targetType != null && targetType.Length <= DerivedValueConsts.MaxTargetTypeLength && TargetTypeShape().IsMatch(targetType);

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*\\.[A-Z][A-Za-z0-9]*$")]
    private static partial Regex TargetTypeShape();
}

/// <param name="CalculatorType">Null for a watch-only type.</param>
public sealed record DerivedValueTargetDefinition(string TargetType, string WatchPermission, Type? CalculatorType);
