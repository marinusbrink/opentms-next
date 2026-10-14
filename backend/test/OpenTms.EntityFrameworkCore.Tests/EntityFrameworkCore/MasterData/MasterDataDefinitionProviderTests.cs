using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.Platform.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation.StringValues;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The Master Data permission and feature definitions through the real definition managers, and the
/// feature gate on the services (design 41, "Feature flag and permissions").
/// Risk class: Critical — authorization: 49 permissions (design 41 plus design 43) on both sides, the flag default off, and the
/// flag blocking every service when off.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataDefinitionProviderTests : MasterDataAppServiceTestBase
{
    [Fact]
    public async Task The_permission_provider_defines_forty_nine_permissions_on_both_sides_in_one_group()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        var groups = await manager.GetGroupsAsync();
        var group = groups.Single(g => g.Name == MasterDataPermissions.GroupName);

        group.Permissions.Count.ShouldBe(12);
        var all = group.GetPermissionsWithChildren();
        all.Count.ShouldBe(49);
        all.Select(p => p.Name).ShouldBe(
            MasterDataPermissions.All.SelectMany(p => new[] { p.Default, p.Create, p.Update, p.Delete })
                .Append(MasterDataPermissions.Drivers.Anonymise),
            ignoreOrder: true);
        all.ShouldAllBe(p => p.MultiTenancySide == MultiTenancySides.Both);
        all.ShouldAllBe(p => p.IsEnabled);

        foreach (var view in group.Permissions)
        {
            view.Parent.ShouldBeNull();
            /* Drivers carries the extra Anonymise child (design 43, GDPR art. 17). */
            string[] expected = view.Name == MasterDataPermissions.Drivers.Default
                ? [view.Name + ".Create", view.Name + ".Update", view.Name + ".Delete", view.Name + ".Anonymise"]
                : [view.Name + ".Create", view.Name + ".Update", view.Name + ".Delete"];
            view.Children.Select(c => c.Name).ShouldBe(expected);
            view.Children.ShouldAllBe(c => c.Parent == view);
        }

        foreach (var name in all.Select(p => p.Name))
        {
            (await manager.GetOrNullAsync(name)).ShouldNotBeNull(name);
        }
    }

    [Fact]
    public async Task The_master_data_features_are_app_tree_toggles_with_the_lists_off_by_default()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();

        /* PO 2026-10-14: the run-1 and run-2 flags became the Master data app tree; the app and its groups are on,
         * the twelve lists (which were behind a flag) stay off until an edition or tenant switches them on. */
        foreach (var name in new[]
                 {
                     AppFeatures.MasterData.Default, AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Transport.Default,
                     AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Reference.Default,
                 })
        {
            var feature = await manager.GetAsync(name);
            feature.DefaultValue.ShouldBe("true", name);
            feature.ValueType.ShouldBeOfType<ToggleStringValueType>();
            feature.IsVisibleToClients.ShouldBeTrue();
            feature.IsAvailableToHost.ShouldBeTrue();
        }

        foreach (var name in new[]
                 {
                     AppFeatures.MasterData.Relations.RelationsList, AppFeatures.MasterData.Relations.AddressTypes, AppFeatures.MasterData.Relations.RelationCategories,
                     AppFeatures.MasterData.Transport.Products, AppFeatures.MasterData.Transport.TransportUnits, AppFeatures.MasterData.Transport.Temperatures,
                     AppFeatures.MasterData.Fleet.Drivers, AppFeatures.MasterData.Fleet.VehicleTypes, AppFeatures.MasterData.Fleet.TransportCharacteristics,
                     AppFeatures.MasterData.Reference.Countries, AppFeatures.MasterData.Reference.Currencies, AppFeatures.MasterData.Reference.GeographicalRegions,
                 })
        {
            var feature = await manager.GetAsync(name);
            feature.DefaultValue.ShouldBe("false", name);
            feature.ValueType.ShouldBeOfType<ToggleStringValueType>();
            feature.IsVisibleToClients.ShouldBeTrue();
            feature.IsAvailableToHost.ShouldBeTrue();
        }

        /* The old flags and their group are gone. */
        (await manager.GetOrNullAsync("MasterData.FirstRun")).ShouldBeNull();
        (await manager.GetOrNullAsync("MasterData.Relations")).ShouldBeNull();
        (await manager.GetGroupsAsync()).ShouldNotContain(g => g.Name == "MasterData");
    }

    [Fact]
    public async Task With_the_app_feature_off_every_service_method_is_refused_before_touching_data()
    {
        var currencies = GetRequiredService<ICurrencyAppService>();
        var products = GetRequiredService<IProductAppService>();
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Default)).ShouldBeFalse();

            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.GetListAsync(List<CurrencyListRequestDto>()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.GetAsync(Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.GetLookupAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", Names = Names("Euro") }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.DeleteAsync(Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.SetDefaultAsync(Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => currencies.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = Explicit(Guid.NewGuid()) }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => products.CreateAsync(new ProductCreateDto { Name = "Pallet" }));
        }

        /* With the flag on (the test default) the same calls reach the service. */
        (await currencies.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(0);
        (await products.CreateAsync(new ProductCreateDto { Name = "Pallet" })).Name.ShouldBe("Pallet");
        (await currencies.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_flag_is_read_through_the_production_feature_checker()
    {
        var checker = GetRequiredService<IFeatureChecker>();

        (await checker.IsEnabledAsync(AppFeatures.MasterData.Reference.Countries)).ShouldBeTrue();
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Reference.Countries, "false"))
        {
            (await checker.IsEnabledAsync(AppFeatures.MasterData.Reference.Countries)).ShouldBeFalse();
        }

        (await checker.IsEnabledAsync(AppFeatures.MasterData.Reference.Countries)).ShouldBeTrue();
    }
}
