using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Integrations.Features;
using OpenTms.Platform.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;

namespace OpenTms.Features;

/// <summary>
/// Test-side feature values. The application services of the Master Data module carry
/// <c>[RequiresFeature(AppFeatures.MasterData.Default)]</c> on the class and the group plus node feature on every
/// non-picker method (docs/designs/tenants-and-editions.md, "App features"); the real value providers answer the
/// definition's default ("false" for the twelve lists) in a test process with the dynamic feature store disabled,
/// which would block every service. This provider is registered last (so ABP consults it first) and answers "true"
/// for the Master Data app, its groups and its nodes, and for the Integrations address-lookup flag, unless a test
/// overrides a single feature for its own async flow through <see cref="Override"/>. Every other feature (the
/// Administration nodes, the placeholder apps) is left to the production providers. Nothing else is faked: the
/// check itself (interceptor, definition, checker) is the production code path.
/// </summary>
public sealed class TestFeatureValueProvider : FeatureValueProvider, ITransientDependency
{
    public const string ProviderName = "OpenTmsTest";

    /// <summary>Marks an override that makes this provider answer nothing, so the production providers decide.</summary>
    private const string Defer = "\0defer";

    private static readonly AsyncLocal<ImmutableDictionary<string, string>?> Overrides = new();

    public TestFeatureValueProvider(IFeatureStore featureStore)
        : base(featureStore)
    {
    }

    public override string Name => ProviderName;

    /// <summary>
    /// Overrides one feature for the current async flow; dispose to restore. A <c>null</c> value makes this provider
    /// step aside for that feature, so the stored tenant/edition values and the definition default decide.
    /// </summary>
    public static IDisposable Override(string feature, string? value)
    {
        var previous = Overrides.Value;
        Overrides.Value = (previous ?? ImmutableDictionary<string, string>.Empty).SetItem(feature, value ?? Defer);
        return new Restore(previous);
    }

    /// <summary>Overrides the Integrations address-lookup flag (design 44) for the current async flow; dispose to restore.</summary>
    public static IDisposable OverrideAddressLookup(string value) => Override(IntegrationsFeatures.AddressLookup, value);

    public override Task<string?> GetOrNullAsync(FeatureDefinition feature)
    {
        if (Overrides.Value != null && Overrides.Value.TryGetValue(feature.Name, out var value))
        {
            return Task.FromResult(value == Defer ? null : value);
        }

        if (IsMasterDataAppFeature(feature.Name) || feature.Name == IntegrationsFeatures.AddressLookup)
        {
            return Task.FromResult<string?>("true");
        }

        return Task.FromResult<string?>(null);
    }

    private static bool IsMasterDataAppFeature(string name) =>
        name == AppFeatures.MasterData.Default || name.StartsWith(AppFeatures.MasterData.Default + ".", StringComparison.Ordinal);

    private sealed class Restore : IDisposable
    {
        private readonly ImmutableDictionary<string, string>? _previous;

        public Restore(ImmutableDictionary<string, string>? previous)
        {
            _previous = previous;
        }

        public void Dispose() => Overrides.Value = _previous;
    }
}
