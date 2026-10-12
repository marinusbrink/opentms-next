using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Features;
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
    public async Task The_feature_definition_is_a_toggle_that_defaults_to_off()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();

        var feature = await manager.GetAsync(MasterDataFeatures.FirstRun);

        feature.Name.ShouldBe("MasterData.FirstRun");
        feature.DefaultValue.ShouldBe("false");
        feature.ValueType.ShouldBeOfType<ToggleStringValueType>();
        feature.IsVisibleToClients.ShouldBeTrue();
        feature.IsAvailableToHost.ShouldBeTrue();

        /* Run 2 (design 43) adds a second toggle with the same defaults. */
        var relations = await manager.GetAsync(MasterDataFeatures.Relations);
        relations.Name.ShouldBe("MasterData.Relations");
        relations.DefaultValue.ShouldBe("false");
        relations.ValueType.ShouldBeOfType<ToggleStringValueType>();
        relations.IsVisibleToClients.ShouldBeTrue();
        relations.IsAvailableToHost.ShouldBeTrue();

        var groups = await manager.GetGroupsAsync();
        groups.Single(g => g.Name == MasterDataFeatures.GroupName).Features.Select(f => f.Name).ShouldBe(["MasterData.FirstRun", "MasterData.Relations"]);
    }

    [Fact]
    public async Task With_the_flag_off_every_service_method_is_refused_before_touching_data()
    {
        var currencies = GetRequiredService<ICurrencyAppService>();
        var products = GetRequiredService<IProductAppService>();
        using var _ = InTenantA();

        using (TestFeatureValueProvider.Override("false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(MasterDataFeatures.FirstRun)).ShouldBeFalse();

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

        (await checker.IsEnabledAsync(MasterDataFeatures.FirstRun)).ShouldBeTrue();
        using (TestFeatureValueProvider.Override("false"))
        {
            (await checker.IsEnabledAsync(MasterDataFeatures.FirstRun)).ShouldBeFalse();
        }

        (await checker.IsEnabledAsync(MasterDataFeatures.FirstRun)).ShouldBeTrue();
    }
}
