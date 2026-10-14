using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantUpdateDto = OpenTms.Platform.Tenants.TenantUpdateDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// Edition feature resolution (docs/designs/tenants-and-editions.md, "Edition features"): the edition of the
/// <em>current tenant</em> decides, not a login claim, so a feature flag (constitution rule 4) evaluates the same in a
/// request, a job and an event handler. Uses <see cref="AppFeatures.Administration.Maintenance"/> (default "false"), which the
/// test-side provider does not answer for.
/// Risk class: Critical — a wrong resolution switches features on for the wrong tenant (tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TenantEditionFeatureValueProviderTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private const string Feature = AppFeatures.Administration.Maintenance;

    private readonly ICurrentTenant _currentTenant;
    private readonly IFeatureChecker _featureChecker;
    private readonly IFeatureManagementStore _featureStore;
    private readonly IEditionAdministrationAppService _editions;

    public TenantEditionFeatureValueProviderTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _featureChecker = GetRequiredService<IFeatureChecker>();
        _featureStore = GetRequiredService<IFeatureManagementStore>();
        _editions = GetRequiredService<IEditionAdministrationAppService>();
    }

    /// <summary>A shared-tier tenant (no connection string), so its data stays in the SQLite test database.</summary>
    private async Task<Guid> CreateTenantAsync(string name, Guid? editionId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync(name);
            tenant.SetProperty(TenantProperties.EditionId, editionId);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });
    }

    private Task SetEditionValueAsync(Guid editionId, string value) =>
        WithUnitOfWorkAsync(() => _featureStore.SetAsync(Feature, value, EditionConsts.FeatureProviderName, editionId.ToString()));

    private Task SetTenantValueAsync(Guid tenantId, string value) =>
        WithUnitOfWorkAsync(() => _featureStore.SetAsync(Feature, value, TenantFeatureValueProvider.ProviderName, tenantId.ToString()));

    [Fact]
    public void The_provider_replaces_abps_edition_provider_under_the_same_name_and_position()
    {
        var providers = GetRequiredService<IOptions<AbpFeatureOptions>>().Value.ValueProviders;

        providers.ShouldContain(typeof(TenantEditionFeatureValueProvider));
        providers.ShouldNotContain(typeof(EditionFeatureValueProvider));
        providers.IndexOf(typeof(TenantEditionFeatureValueProvider))
            .ShouldBeLessThan(providers.IndexOf(typeof(TenantFeatureValueProvider)), "the tenant value must win over the edition value");
        providers.IndexOf(typeof(TenantEditionFeatureValueProvider))
            .ShouldBeGreaterThan(providers.IndexOf(typeof(DefaultValueFeatureValueProvider)));

        GetRequiredService<TenantEditionFeatureValueProvider>().Name.ShouldBe("E");
    }

    [Fact]
    public async Task A_tenant_gets_the_value_of_its_own_edition()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        await SetEditionValueAsync(gold.Id, "true");
        var acme = await CreateTenantAsync("acme", gold.Id);

        using (_currentTenant.Change(acme))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Another_tenant_on_another_edition_or_none_does_not_get_that_value()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        var basic = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Basic" });
        await SetEditionValueAsync(gold.Id, "true");
        await CreateTenantAsync("acme", gold.Id);
        var onBasic = await CreateTenantAsync("beta", basic.Id);
        var onNone = await CreateTenantAsync("gamma", null);

        using (_currentTenant.Change(onBasic))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeFalse();
        }

        using (_currentTenant.Change(onNone))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task On_the_host_the_provider_answers_nothing()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        await SetEditionValueAsync(gold.Id, "true");
        await CreateTenantAsync("acme", gold.Id);

        var provider = GetRequiredService<TenantEditionFeatureValueProvider>();
        var definition = await GetRequiredService<IFeatureDefinitionManager>().GetAsync(Feature);

        using (_currentTenant.Change(null))
        {
            (await provider.GetOrNullAsync(definition)).ShouldBeNull();
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_unknown_tenant_resolves_to_no_edition()
    {
        var provider = GetRequiredService<TenantEditionFeatureValueProvider>();
        var definition = await GetRequiredService<IFeatureDefinitionManager>().GetAsync(Feature);

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            (await provider.GetOrNullAsync(definition)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task The_tenants_own_value_wins_over_its_editions_value()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        await SetEditionValueAsync(gold.Id, "true");
        var acme = await CreateTenantAsync("acme", gold.Id);
        await SetTenantValueAsync(acme, "false");

        using (_currentTenant.Change(acme))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Changing_the_edition_evicts_the_entry_the_feature_checker_cached_inside_the_tenant()
    {
        /* Feature checks run inside the tenant (requests, jobs, handlers), so that is where the edition gets cached.
         * Design: "cached per tenant ... removed when a tenant's edition changes". */
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        var basic = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Basic" });
        var acme = await CreateTenantAsync("acme", basic.Id);
        var store = GetRequiredService<ITenantEditionStore>();

        using (_currentTenant.Change(acme))
        {
            (await store.GetEditionIdAsync(acme)).ShouldBe(basic.Id);
        }

        await GetRequiredService<ITenantAdministrationAppService>()
            .UpdateAsync(acme, new TenantUpdateDto { Name = "acme", EditionId = gold.Id });

        using (_currentTenant.Change(acme))
        {
            (await store.GetEditionIdAsync(acme)).ShouldBe(gold.Id);
        }
    }

    [Fact]
    public async Task Moving_a_tenant_to_another_edition_changes_its_value_at_once_on_this_instance()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        var basic = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Basic" });
        await SetEditionValueAsync(gold.Id, "true");
        var tenants = GetRequiredService<ITenantAdministrationAppService>();
        var acme = await CreateTenantAsync("acme", basic.Id);

        using (_currentTenant.Change(acme))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeFalse();
        }

        await tenants.UpdateAsync(acme, new TenantUpdateDto { Name = "acme", EditionId = gold.Id });

        using (_currentTenant.Change(acme))
        {
            (await _featureChecker.IsEnabledAsync(Feature)).ShouldBeTrue();
        }
    }
}
