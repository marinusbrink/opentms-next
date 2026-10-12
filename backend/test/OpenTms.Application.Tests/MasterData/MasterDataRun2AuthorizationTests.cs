using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// The permission and feature attributes of the run-2 services, read by reflection (design 43,
/// "Feature flag and permissions" and the security quickscan: every public method attributed, the
/// matcher behind Relations, anonymise behind its own permission). Risk class: Critical — authorization.
/// The run-1 architecture test proves an attribute exists; this test proves it is the right one.
/// </summary>
public class MasterDataRun2AuthorizationTests
{
    [Theory]
    [InlineData(nameof(RelationAppService.GetListAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.GetAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.GetLookupAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.GetLookupPageAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.GetAddressesAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.MatchAsync), MasterDataPermissions.Relations.Default)]
    [InlineData(nameof(RelationAppService.CreateAsync), MasterDataPermissions.Relations.Create)]
    [InlineData(nameof(RelationAppService.UpdateAsync), MasterDataPermissions.Relations.Update)]
    [InlineData(nameof(RelationAppService.ArchiveAsync), MasterDataPermissions.Relations.Update)]
    [InlineData(nameof(RelationAppService.UnarchiveAsync), MasterDataPermissions.Relations.Update)]
    [InlineData(nameof(RelationAppService.DeleteAsync), MasterDataPermissions.Relations.Delete)]
    [InlineData(nameof(RelationAppService.BulkDeleteAsync), MasterDataPermissions.Relations.Delete)]
    public void Every_relation_service_method_carries_its_permission(string method, string permission)
    {
        PolicyOf(typeof(RelationAppService), method).ShouldBe(permission);
    }

    [Theory]
    [InlineData(nameof(DriverAppService.GetListAsync), MasterDataPermissions.Drivers.Default)]
    [InlineData(nameof(DriverAppService.GetAsync), MasterDataPermissions.Drivers.Default)]
    [InlineData(nameof(DriverAppService.GetLookupAsync), MasterDataPermissions.Drivers.Default)]
    [InlineData(nameof(DriverAppService.GetUserLookupPageAsync), MasterDataPermissions.Drivers.Update)]
    [InlineData(nameof(DriverAppService.CreateAsync), MasterDataPermissions.Drivers.Create)]
    [InlineData(nameof(DriverAppService.UpdateAsync), MasterDataPermissions.Drivers.Update)]
    [InlineData(nameof(DriverAppService.ArchiveAsync), MasterDataPermissions.Drivers.Update)]
    [InlineData(nameof(DriverAppService.UnarchiveAsync), MasterDataPermissions.Drivers.Update)]
    [InlineData(nameof(DriverAppService.DeleteAsync), MasterDataPermissions.Drivers.Delete)]
    [InlineData(nameof(DriverAppService.BulkDeleteAsync), MasterDataPermissions.Drivers.Delete)]
    [InlineData(nameof(DriverAppService.AnonymiseAsync), MasterDataPermissions.Drivers.Anonymise)]
    public void Every_driver_service_method_carries_its_permission(string method, string permission)
    {
        PolicyOf(typeof(DriverAppService), method).ShouldBe(permission);
    }

    [Fact]
    public void Anonymise_is_not_reachable_through_the_update_or_delete_permission()
    {
        MasterDataPermissions.Drivers.Anonymise.ShouldBe("MasterData.Drivers.Anonymise");
        MasterDataPermissions.Drivers.Anonymise.ShouldNotBe(MasterDataPermissions.Drivers.Update);
        MasterDataPermissions.Drivers.Anonymise.ShouldNotBe(MasterDataPermissions.Drivers.Delete);
        PolicyOf(typeof(DriverAppService), nameof(DriverAppService.AnonymiseAsync)).ShouldBe(MasterDataPermissions.Drivers.Anonymise);
    }

    [Fact]
    public void No_public_method_of_the_two_services_is_without_a_permission()
    {
        foreach (var service in new[] { typeof(RelationAppService), typeof(DriverAppService) })
        {
            var unattributed = service
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
                .Select(m => m.Name)
                .ToList();

            unattributed.ShouldBeEmpty($"{service.Name}: every public method needs [Authorize(<permission>)]");
        }
    }

    [Theory]
    [InlineData(typeof(RelationAppService))]
    [InlineData(typeof(DriverAppService))]
    public void Both_services_sit_behind_the_Relations_feature(Type service)
    {
        var feature = service.GetCustomAttribute<RequiresFeatureAttribute>();

        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([MasterDataFeatures.Relations]);
        MasterDataFeatures.Relations.ShouldBe("MasterData.Relations");
        MasterDataFeatures.Relations.ShouldNotBe(MasterDataFeatures.FirstRun, "the second run has its own switch so run 1 can be live first");
    }

    [Fact]
    public void The_run_2_error_codes_travel_as_409_or_400()
    {
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.OwnCompanyAlreadyExists);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.AnonymisedRecordIsReadOnly);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.DuplicateKey);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.InUse);

        foreach (var code in new[]
                 {
                     MasterDataErrorCodes.RolesRequired, MasterDataErrorCodes.EmployerRoleRequired, MasterDataErrorCodes.InvalidIban,
                     MasterDataErrorCodes.InvalidVatNumber, MasterDataErrorCodes.InvalidEmail, MasterDataErrorCodes.InvalidCulture,
                     MasterDataErrorCodes.UserNotFound, MasterDataErrorCodes.TooManyChildren, MasterDataErrorCodes.InvalidTimeZone
                 })
        {
            MasterDataErrorCodes.BadRequestCodes.ShouldContain(code);
        }

        MasterDataErrorCodes.ConflictCodes.Intersect(MasterDataErrorCodes.BadRequestCodes).ShouldBeEmpty();
    }

    private static string? PolicyOf(Type service, string method)
    {
        var info = service.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        info.ShouldNotBeNull($"{service.Name}.{method} must be a public method");
        return info.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
    }
}
