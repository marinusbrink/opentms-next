using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// The app and navigation feature attributes of every Master Data service, read by reflection (PO 2026-10-14,
/// docs/designs/tenants-and-editions.md, "App features"): the class needs the app; every public method needs its
/// group and node (RequiresAll), except the pickers, which need only the app. Risk class: High (feature gating of
/// every screen) on a Critical boundary (authorization). The SQLite tests prove the gate works for one method per
/// service; this proves every method carries the right one.
/// </summary>
public class MasterDataAppFeatureAttributeTests
{
    public static IEnumerable<object[]> Services() =>
    [
        [typeof(AddressTypeAppService), AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes],
        [typeof(RelationCategoryAppService), AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.RelationCategories],
        [typeof(RelationAppService), AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.RelationsList],
        [typeof(ProductAppService), AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Products],
        [typeof(TransportUnitAppService), AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.TransportUnits],
        [typeof(TemperatureAppService), AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures],
        [typeof(DriverAppService), AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Drivers],
        [typeof(VehicleTypeAppService), AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes],
        [typeof(TransportCharacteristicsAppService), AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.TransportCharacteristics],
        [typeof(CountryAppService), AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.Countries],
        [typeof(CurrencyAppService), AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.Currencies],
        [typeof(GeographicalRegionAppService), AppFeatures.MasterData.Reference.Default, AppFeatures.MasterData.Reference.GeographicalRegions],
    ];

    /// <summary>The pickers per PO decision 2026-10-14; Driver.GetUserLookupPageAsync is deliberately not one.</summary>
    private static readonly HashSet<string> Pickers =
    [
        "*." + nameof(CountryAppService.GetLookupAsync),
        nameof(RelationAppService) + "." + nameof(RelationAppService.GetLookupPageAsync),
        nameof(RelationAppService) + "." + nameof(RelationAppService.GetAddressesAsync),
        nameof(GeographicalRegionAppService) + "." + nameof(GeographicalRegionAppService.GetMatchesAsync),
        nameof(CountryAppService) + "." + nameof(CountryAppService.GetDefaultAsync),
        nameof(TransportCharacteristicsAppService) + "." + nameof(TransportCharacteristicsAppService.GetLookupPageAsync),
    ];

    private static bool IsPicker(Type service, MethodInfo method) =>
        Pickers.Contains("*." + method.Name) || Pickers.Contains(service.Name + "." + method.Name);

    private static IEnumerable<MethodInfo> PublicMethods(Type service) => service
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName);

    [Theory]
    [MemberData(nameof(Services))]
    public void The_service_needs_the_Master_data_app_feature(Type service, string group, string node)
    {
        var attributes = service.GetCustomAttributes<RequiresFeatureAttribute>().ToList();
        attributes.ShouldHaveSingleItem().Features.ShouldBe([AppFeatures.MasterData.Default]);
        node.ShouldStartWith(group + ".");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_non_picker_method_needs_its_group_and_node_and_every_picker_only_the_app(Type service, string group, string node)
    {
        var methods = PublicMethods(service).ToList();
        methods.ShouldNotBeEmpty();

        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes<RequiresFeatureAttribute>().ToList();
            if (IsPicker(service, method))
            {
                attributes.ShouldBeEmpty($"{service.Name}.{method.Name} is a picker: the app feature only");
            }
            else
            {
                var attribute = attributes.ShouldHaveSingleItem($"{service.Name}.{method.Name} needs its node feature");
                attribute.Features.ShouldBe([group, node], $"{service.Name}.{method.Name}");
                attribute.RequiresAll.ShouldBeTrue($"{service.Name}.{method.Name}: the group AND the node");
            }
        }
    }

    [Fact]
    public void The_driver_user_lookup_page_is_not_a_picker()
    {
        var attribute = typeof(DriverAppService).GetMethod(nameof(DriverAppService.GetUserLookupPageAsync))!
            .GetCustomAttribute<RequiresFeatureAttribute>();
        attribute.ShouldNotBeNull();
        attribute.Features.ShouldBe([AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Drivers]);
        attribute.RequiresAll.ShouldBeTrue();
    }

    [Fact]
    public void Every_service_has_a_GetLookupAsync_picker()
    {
        foreach (var row in Services())
        {
            var service = (Type)row[0];
            var lookup = service.GetMethod("GetLookupAsync", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            lookup.ShouldNotBeNull(service.Name);
            lookup.GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull(service.Name);
        }
    }
}
