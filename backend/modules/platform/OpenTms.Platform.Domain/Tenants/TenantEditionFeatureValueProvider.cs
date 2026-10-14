using System.Threading.Tasks;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Replaces ABP's <see cref="EditionFeatureValueProvider"/> (same name <c>"E"</c>, same place in the
/// chain). ABP reads the edition from an <c>editionid</c> claim, which is frozen at login and absent in
/// background jobs and event handlers; a feature flag (constitution rule 4) must evaluate the same
/// everywhere, so the edition is resolved from the current tenant instead
/// (docs/designs/tenants-and-editions.md, "Edition features").
/// </summary>
public class TenantEditionFeatureValueProvider : FeatureValueProvider
{
    public override string Name => EditionFeatureValueProvider.ProviderName;

    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantEditionStore _editionStore;

    public TenantEditionFeatureValueProvider(
        IFeatureStore featureStore,
        ICurrentTenant currentTenant,
        ITenantEditionStore editionStore)
        : base(featureStore)
    {
        _currentTenant = currentTenant;
        _editionStore = editionStore;
    }

    public override async Task<string?> GetOrNullAsync(FeatureDefinition feature)
    {
        if (_currentTenant.Id is not { } tenantId)
        {
            return null;
        }

        var editionId = await _editionStore.GetEditionIdAsync(tenantId);
        return editionId == null
            ? null
            : await FeatureStore.GetOrNullAsync(feature.Name, Name, editionId.Value.ToString());
    }
}
