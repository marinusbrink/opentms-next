using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.MasterData.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;

namespace OpenTms.Features;

/// <summary>
/// Test-side feature values. The application services of the Master Data module carry
/// <c>[RequiresFeature(MasterDataFeatures.FirstRun)]</c>; the real value providers answer the
/// definition's default ("false") in a test process with the dynamic feature store disabled, which
/// would block every service. This provider is registered last (so ABP consults it first) and answers
/// "true" for the Master Data flag unless a test overrides it for its own async flow through
/// <see cref="Override"/>. Nothing else is faked: the check itself (interceptor, definition, checker)
/// is the production code path.
/// </summary>
public sealed class TestFeatureValueProvider : FeatureValueProvider, ITransientDependency
{
    public const string ProviderName = "OpenTmsTest";

    private static readonly AsyncLocal<string?> FirstRunOverride = new();

    public TestFeatureValueProvider(IFeatureStore featureStore)
        : base(featureStore)
    {
    }

    public override string Name => ProviderName;

    /// <summary>Overrides the Master Data flag for the current async flow; dispose to restore.</summary>
    public static IDisposable Override(string value)
    {
        var previous = FirstRunOverride.Value;
        FirstRunOverride.Value = value;
        return new Restore(previous);
    }

    public override Task<string?> GetOrNullAsync(FeatureDefinition feature)
    {
        if (feature.Name == MasterDataFeatures.FirstRun)
        {
            return Task.FromResult<string?>(FirstRunOverride.Value ?? "true");
        }

        return Task.FromResult<string?>(null);
    }

    private sealed class Restore : IDisposable
    {
        private readonly string? _previous;

        public Restore(string? previous)
        {
            _previous = previous;
        }

        public void Dispose() => FirstRunOverride.Value = _previous;
    }
}
