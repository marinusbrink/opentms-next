using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The app and navigation features on the Master Data services, through the production interceptor and feature
/// checker (docs/designs/tenants-and-editions.md, "App features"; PO 2026-10-14): every service needs the
/// Master data app feature; every non-picker method also needs its group and its node; the pickers
/// (GetLookupAsync and the named lookups) need only the app. Risk class: High (feature gating of every screen) on a
/// Critical boundary (authorization, tenant isolation of feature values).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataAppFeatureGatingTests : MasterDataAppServiceTestBase
{
    /// <summary>One row per service: its group, its node, a list call and its lookup call.</summary>
    public static IEnumerable<object[]> Services()
    {
        yield return Row("AddressTypes", AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes);
        yield return Row("RelationCategories", AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.RelationCategories);
        yield return Row("Relations", AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.RelationsList);
        yield return Row("Products", AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Products);
        yield return Row("TransportUnits", AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.TransportUnits);
        yield return Row("Temperatures", AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures);
        yield return Row("Drivers", AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Drivers);
        yield return Row("VehicleTypes", AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes);
        yield return Row("TransportCharacteristics", AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.TransportCharacteristics);
        yield return Row("Countries", AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.Countries);
        yield return Row("Currencies", AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.Currencies);
        yield return Row("GeographicalRegions", AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.GeographicalRegions);
        /* Design 48. */
        yield return Row("Vehicles", AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Vehicles);
        yield return Row("VehicleCombinations", AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleCombinations);
        yield return Row("ProductRelationCodes", AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.ProductRelationCodes);
    }

    private static object[] Row(string service, string group, string node) => [service, group, node];

    private Task ListAsync(string service) => service switch
    {
        "AddressTypes" => GetRequiredService<IAddressTypeAppService>().GetListAsync(List()),
        "RelationCategories" => GetRequiredService<IRelationCategoryAppService>().GetListAsync(List()),
        "Relations" => GetRequiredService<IRelationAppService>().GetListAsync(List<RelationListRequestDto>()),
        "Products" => GetRequiredService<IProductAppService>().GetListAsync(List<ArchivableListRequestDto>()),
        "TransportUnits" => GetRequiredService<ITransportUnitAppService>().GetListAsync(List<ArchivableListRequestDto>()),
        "Temperatures" => GetRequiredService<ITemperatureAppService>().GetListAsync(List<ArchivableListRequestDto>()),
        "Drivers" => GetRequiredService<IDriverAppService>().GetListAsync(List<DriverListRequestDto>()),
        "VehicleTypes" => GetRequiredService<IVehicleTypeAppService>().GetListAsync(List()),
        "TransportCharacteristics" => GetRequiredService<ITransportCharacteristicsAppService>().GetListAsync(List()),
        "Countries" => GetRequiredService<ICountryAppService>().GetListAsync(List()),
        "Currencies" => GetRequiredService<ICurrencyAppService>().GetListAsync(List<CurrencyListRequestDto>()),
        "GeographicalRegions" => GetRequiredService<IGeographicalRegionAppService>().GetListAsync(List<GeographicalRegionListRequestDto>()),
        "Vehicles" => GetRequiredService<IVehicleAppService>().GetListAsync(List<VehicleListRequestDto>()),
        "VehicleCombinations" => GetRequiredService<IVehicleCombinationAppService>().GetListAsync(List<ArchivableListRequestDto>()),
        "ProductRelationCodes" => GetRequiredService<IProductRelationCodeAppService>().GetListAsync(List<ArchivableListRequestDto>()),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    private Task<List<MasterDataLookupDto>> LookupAsync(string service) => service switch
    {
        "AddressTypes" => GetRequiredService<IAddressTypeAppService>().GetLookupAsync(),
        "RelationCategories" => GetRequiredService<IRelationCategoryAppService>().GetLookupAsync(),
        "Relations" => GetRequiredService<IRelationAppService>().GetLookupAsync(),
        "Products" => GetRequiredService<IProductAppService>().GetLookupAsync(),
        "TransportUnits" => GetRequiredService<ITransportUnitAppService>().GetLookupAsync(),
        "Temperatures" => GetRequiredService<ITemperatureAppService>().GetLookupAsync(),
        "Drivers" => GetRequiredService<IDriverAppService>().GetLookupAsync(),
        "VehicleTypes" => GetRequiredService<IVehicleTypeAppService>().GetLookupAsync(),
        "TransportCharacteristics" => GetRequiredService<ITransportCharacteristicsAppService>().GetLookupAsync(),
        "Countries" => GetRequiredService<ICountryAppService>().GetLookupAsync(),
        "Currencies" => GetRequiredService<ICurrencyAppService>().GetLookupAsync(),
        "GeographicalRegions" => GetRequiredService<IGeographicalRegionAppService>().GetLookupAsync(),
        "Vehicles" => GetRequiredService<IVehicleAppService>().GetLookupAsync(),
        "VehicleCombinations" => GetRequiredService<IVehicleCombinationAppService>().GetLookupAsync(),
        "ProductRelationCodes" => GetRequiredService<IProductRelationCodeAppService>().GetLookupAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    /// <summary>Passes when the call got past the feature gate (a not-found or validation error further in is fine).</summary>
    private static async Task ShouldPassTheFeatureGateAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (AbpAuthorizationException ex)
        {
            throw new ShouldAssertException($"The call was refused by the feature gate: {ex.Message}");
        }
        catch (Exception)
        {
            /* Past the gate: whatever the service itself throws for a made-up id is not this test's concern. */
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task With_its_node_off_the_list_is_refused_but_the_lookup_still_works(string service, string group, string node)
    {
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override(node, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => ListAsync(service));
            (await LookupAsync(service)).ShouldBeEmpty();
        }

        await ListAsync(service);
        group.ShouldStartWith(AppFeatures.MasterData.Default + ".");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task With_only_the_app_feature_on_the_lookup_works_and_the_list_does_not(string service, string group, string node)
    {
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override(group, "false"))
        using (TestFeatureValueProvider.Override(node, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Default)).ShouldBeTrue();
            (await LookupAsync(service)).ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => ListAsync(service));
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task With_its_group_off_the_list_is_refused_even_though_the_node_is_on(string service, string group, string node)
    {
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override(group, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(node)).ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => ListAsync(service));
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task With_the_app_off_even_the_lookup_is_refused(string service, string group, string node)
    {
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(group)).ShouldBeTrue();
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(node)).ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => LookupAsync(service));
            await Should.ThrowAsync<AbpAuthorizationException>(() => ListAsync(service));
        }
    }

    [Fact]
    public async Task The_named_pickers_need_only_the_app_feature()
    {
        using var _ = InTenantA();
        var relations = GetRequiredService<IRelationAppService>();
        var regions = GetRequiredService<IGeographicalRegionAppService>();
        var countries = GetRequiredService<ICountryAppService>();
        var characteristics = GetRequiredService<ITransportCharacteristicsAppService>();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Relations.Default, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Relations.RelationsList, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Reference.Default, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Reference.Countries, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Reference.GeographicalRegions, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Fleet.Default, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Fleet.TransportCharacteristics, "false"))
        {
            (await relations.GetLookupPageAsync()).TotalCount.ShouldBe(0);
            await ShouldPassTheFeatureGateAsync(() => relations.GetAddressesAsync(Guid.NewGuid()));
            await ShouldPassTheFeatureGateAsync(() => regions.GetMatchesAsync(Guid.NewGuid(), "1012"));
            (await countries.GetDefaultAsync()).ShouldBeNull();
            (await characteristics.GetLookupPageAsync()).TotalCount.ShouldBe(0);

            await Should.ThrowAsync<AbpAuthorizationException>(() => relations.MatchAsync(new RelationMatchRequestDto { Code = "X" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => countries.SetDefaultAsync(Guid.NewGuid()));
        }

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => relations.GetLookupPageAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => relations.GetAddressesAsync(Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => regions.GetMatchesAsync(Guid.NewGuid(), "1012"));
            await Should.ThrowAsync<AbpAuthorizationException>(() => countries.GetDefaultAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => characteristics.GetLookupPageAsync());
        }
    }

    [Fact]
    public async Task The_driver_user_lookup_page_is_not_a_picker_and_sits_behind_the_Drivers_node()
    {
        using var _ = InTenantA();
        var drivers = GetRequiredService<IDriverAppService>();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Fleet.Drivers, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => drivers.GetUserLookupPageAsync());
            (await drivers.GetLookupAsync()).ShouldBeEmpty();
        }

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Fleet.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => drivers.GetUserLookupPageAsync());
        }

        await ShouldPassTheFeatureGateAsync(() => drivers.GetUserLookupPageAsync());
    }

    // ── Tenant isolation of feature values (Critical) ─────────────────────────

    [Fact]
    public async Task A_node_switched_on_for_tenant_A_stays_off_for_tenant_B()
    {
        /* The test provider steps aside for the node, so the stored tenant value ("T") and the default ("false")
         * decide — the production path. */
        using var _ = TestFeatureValueProvider.Override(AppFeatures.MasterData.Reference.Countries, null);
        await WithUnitOfWorkAsync(() => GetRequiredService<IFeatureManagementStore>()
            .SetAsync(AppFeatures.MasterData.Reference.Countries, "true", TenantFeatureValueProvider.ProviderName, TenantA.ToString()));

        var countries = GetRequiredService<ICountryAppService>();

        using (InTenantA())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Reference.Countries)).ShouldBeTrue();
            (await countries.GetListAsync(List())).TotalCount.ShouldBe(0);
        }

        using (InTenantB())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Reference.Countries)).ShouldBeFalse();
            await Should.ThrowAsync<AbpAuthorizationException>(() => countries.GetListAsync(List()));
            (await countries.GetLookupAsync()).ShouldBeEmpty();
        }
    }
}
