using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// The permission and feature attributes of the run-3 services and the published resolver, read by reflection
/// (design 48, "Feature flags and permissions" and the security quickscan: twelve permissions, every public method
/// attributed, the resolver behind MasterData.ProductRelationCodes and only the app feature). Risk class: Critical —
/// authorization. The SQLite tests run with authorization always allowed, so this is where a wrong or missing
/// permission attribute is caught; the feature gate itself is exercised through the interceptor in the SQLite tests.
/// </summary>
public class MasterDataRun3AuthorizationTests
{
    [Theory]
    [InlineData(nameof(VehicleAppService.GetListAsync), MasterDataPermissions.Vehicles.Default)]
    [InlineData(nameof(VehicleAppService.GetAsync), MasterDataPermissions.Vehicles.Default)]
    [InlineData(nameof(VehicleAppService.GetLookupAsync), MasterDataPermissions.Vehicles.Default)]
    [InlineData(nameof(VehicleAppService.GetLookupPageAsync), MasterDataPermissions.Vehicles.Default)]
    [InlineData(nameof(VehicleAppService.GetTypeDefaultsAsync), MasterDataPermissions.Vehicles.Default)]
    [InlineData(nameof(VehicleAppService.CreateAsync), MasterDataPermissions.Vehicles.Create)]
    [InlineData(nameof(VehicleAppService.UpdateAsync), MasterDataPermissions.Vehicles.Update)]
    [InlineData(nameof(VehicleAppService.ArchiveAsync), MasterDataPermissions.Vehicles.Update)]
    [InlineData(nameof(VehicleAppService.UnarchiveAsync), MasterDataPermissions.Vehicles.Update)]
    [InlineData(nameof(VehicleAppService.DeleteAsync), MasterDataPermissions.Vehicles.Delete)]
    [InlineData(nameof(VehicleAppService.BulkDeleteAsync), MasterDataPermissions.Vehicles.Delete)]
    public void Every_vehicle_service_method_carries_its_permission(string method, string permission)
    {
        PolicyOf(typeof(VehicleAppService), method).ShouldBe(permission);
    }

    [Theory]
    [InlineData(nameof(VehicleCombinationAppService.GetListAsync), MasterDataPermissions.VehicleCombinations.Default)]
    [InlineData(nameof(VehicleCombinationAppService.GetAsync), MasterDataPermissions.VehicleCombinations.Default)]
    [InlineData(nameof(VehicleCombinationAppService.GetLookupAsync), MasterDataPermissions.VehicleCombinations.Default)]
    [InlineData(nameof(VehicleCombinationAppService.CreateAsync), MasterDataPermissions.VehicleCombinations.Create)]
    [InlineData(nameof(VehicleCombinationAppService.UpdateAsync), MasterDataPermissions.VehicleCombinations.Update)]
    [InlineData(nameof(VehicleCombinationAppService.ArchiveAsync), MasterDataPermissions.VehicleCombinations.Update)]
    [InlineData(nameof(VehicleCombinationAppService.UnarchiveAsync), MasterDataPermissions.VehicleCombinations.Update)]
    [InlineData(nameof(VehicleCombinationAppService.DeleteAsync), MasterDataPermissions.VehicleCombinations.Delete)]
    [InlineData(nameof(VehicleCombinationAppService.BulkDeleteAsync), MasterDataPermissions.VehicleCombinations.Delete)]
    public void Every_vehicle_combination_service_method_carries_its_permission(string method, string permission)
    {
        PolicyOf(typeof(VehicleCombinationAppService), method).ShouldBe(permission);
    }

    [Theory]
    [InlineData(nameof(ProductRelationCodeAppService.GetListAsync), MasterDataPermissions.ProductRelationCodes.Default)]
    [InlineData(nameof(ProductRelationCodeAppService.GetAsync), MasterDataPermissions.ProductRelationCodes.Default)]
    [InlineData(nameof(ProductRelationCodeAppService.GetLookupAsync), MasterDataPermissions.ProductRelationCodes.Default)]
    [InlineData(nameof(ProductRelationCodeAppService.CreateAsync), MasterDataPermissions.ProductRelationCodes.Create)]
    [InlineData(nameof(ProductRelationCodeAppService.UpdateAsync), MasterDataPermissions.ProductRelationCodes.Update)]
    [InlineData(nameof(ProductRelationCodeAppService.ArchiveAsync), MasterDataPermissions.ProductRelationCodes.Update)]
    [InlineData(nameof(ProductRelationCodeAppService.UnarchiveAsync), MasterDataPermissions.ProductRelationCodes.Update)]
    [InlineData(nameof(ProductRelationCodeAppService.DeleteAsync), MasterDataPermissions.ProductRelationCodes.Delete)]
    [InlineData(nameof(ProductRelationCodeAppService.BulkDeleteAsync), MasterDataPermissions.ProductRelationCodes.Delete)]
    public void Every_product_relation_code_service_method_carries_its_permission(string method, string permission)
    {
        PolicyOf(typeof(ProductRelationCodeAppService), method).ShouldBe(permission);
    }

    [Theory]
    [InlineData(typeof(VehicleAppService))]
    [InlineData(typeof(VehicleCombinationAppService))]
    [InlineData(typeof(ProductRelationCodeAppService))]
    [InlineData(typeof(ProductRelationCodeResolver))]
    public void No_public_method_is_without_a_permission_and_every_type_sits_behind_the_Master_data_app_feature(Type service)
    {
        service
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
            .Select(m => m.Name)
            .ShouldBeEmpty($"{service.Name}: every public method needs [Authorize(<permission>)]");

        var feature = service.GetCustomAttribute<RequiresFeatureAttribute>();
        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.MasterData.Default]);
    }

    [Fact]
    public void The_resolver_runs_under_the_product_relation_code_read_permission_and_only_the_app_feature()
    {
        /* Design 48: "The resolver is MasterData.ProductRelationCodes"; features: "ProductRelationCodeAppService, not the resolver". */
        typeof(ProductRelationCodeResolver).GetCustomAttribute<AuthorizeAttribute>()?.Policy.ShouldBe(MasterDataPermissions.ProductRelationCodes.Default);
        var resolve = typeof(ProductRelationCodeResolver).GetMethod(nameof(ProductRelationCodeResolver.ResolveAsync))!;
        resolve.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe("MasterData.ProductRelationCodes");
        resolve.GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull("the resolver needs only the app feature, not the ProductRelationCodes node");
        resolve.IsVirtual.ShouldBeTrue("the authorization and feature interceptors proxy the class through its virtual methods");
        typeof(IProductRelationCodeResolver).IsAssignableFrom(typeof(ProductRelationCodeResolver)).ShouldBeTrue();
    }

    [Fact]
    public void The_screen_methods_need_their_group_and_node_and_the_pickers_only_the_app()
    {
        MethodFeatures(typeof(VehicleAppService), nameof(VehicleAppService.GetListAsync)).ShouldBe([AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Vehicles]);
        MethodFeatures(typeof(VehicleAppService), nameof(VehicleAppService.GetTypeDefaultsAsync)).ShouldBe([AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.Vehicles]);
        MethodFeatures(typeof(VehicleCombinationAppService), nameof(VehicleCombinationAppService.GetListAsync)).ShouldBe([AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleCombinations]);
        MethodFeatures(typeof(ProductRelationCodeAppService), nameof(ProductRelationCodeAppService.GetListAsync)).ShouldBe([AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.ProductRelationCodes]);

        foreach (var (service, method) in new[]
                 {
                     (typeof(VehicleAppService), nameof(VehicleAppService.GetLookupAsync)),
                     (typeof(VehicleAppService), nameof(VehicleAppService.GetLookupPageAsync)),
                     (typeof(VehicleCombinationAppService), nameof(VehicleCombinationAppService.GetLookupAsync)),
                     (typeof(ProductRelationCodeAppService), nameof(ProductRelationCodeAppService.GetLookupAsync)),
                 })
        {
            service.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                .GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull($"{service.Name}.{method} is a picker (D12)");
        }
    }

    [Fact]
    public void The_run_3_error_codes_travel_as_409_or_400()
    {
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.VehicleAlreadyInCombination);
        foreach (var code in new[]
                 {
                     MasterDataErrorCodes.OwnerRoleRequired, MasterDataErrorCodes.HomeDepotRoleRequired, MasterDataErrorCodes.InvalidLicencePlate,
                     MasterDataErrorCodes.InvalidVin, MasterDataErrorCodes.RegistrationCountryRequired, MasterDataErrorCodes.CombinationMotorisedFirst,
                     MasterDataErrorCodes.CombinationMemberCount, MasterDataErrorCodes.ProductRelationCodeTargetRequired
                 })
        {
            MasterDataErrorCodes.BadRequestCodes.ShouldContain(code);
            code.ShouldStartWith("OpenTms.MasterData:");
        }

        MasterDataErrorCodes.ConflictCodes.Intersect(MasterDataErrorCodes.BadRequestCodes).ShouldBeEmpty();
    }

    private static string[] MethodFeatures(Type service, string method)
    {
        var attribute = service.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .GetCustomAttribute<RequiresFeatureAttribute>();
        attribute.ShouldNotBeNull($"{service.Name}.{method} needs its node feature");
        attribute.RequiresAll.ShouldBeTrue();
        return attribute.Features;
    }

    private static string? PolicyOf(Type service, string method)
    {
        var info = service.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        info.ShouldNotBeNull($"{service.Name}.{method} must be a public method");
        return info.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
    }
}
