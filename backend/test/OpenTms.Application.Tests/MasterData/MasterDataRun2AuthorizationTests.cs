using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.Platform.Features;
using OpenTms.MasterData.Reference;
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
    public void Both_services_sit_behind_the_Master_data_app_feature(Type service)
    {
        var feature = service.GetCustomAttribute<RequiresFeatureAttribute>();

        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.MasterData.Default]);
    }

    [Fact]
    public void Relations_and_Drivers_have_their_own_nodes_so_each_list_can_be_switched_separately()
    {
        /* Was one switch (MasterData.Relations) so run 1 could be live first; PO 2026-10-14 gave every list its node. */
        MethodFeatures(typeof(RelationAppService), nameof(RelationAppService.GetListAsync))
            .ShouldBe([AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList]);
        MethodFeatures(typeof(DriverAppService), nameof(DriverAppService.GetListAsync))
            .ShouldBe([AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Drivers]);
        AppFeatures.MasterData.General.Relations.RelationsList.ShouldBe("Apps.MasterData.General.Relations.Relations");
        AppFeatures.MasterData.General.Fleet.Drivers.ShouldBe("Apps.MasterData.General.Fleet.Drivers");
    }

    [Fact]
    public void The_run_2_error_codes_travel_as_409_or_400()
    {
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.OwnCompanyAlreadyExists);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.AnonymisedRecordIsReadOnly);
        /* PO 2026-10-16: archiving the own company conflicts with its state, like a second own company. */
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.OwnCompanyCannotBeArchived);
        MasterDataErrorCodes.BadRequestCodes.ShouldNotContain(MasterDataErrorCodes.OwnCompanyCannotBeArchived);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.DuplicateKey);
        MasterDataErrorCodes.ConflictCodes.ShouldContain(MasterDataErrorCodes.InUse);

        foreach (var code in new[]
                 {
                     MasterDataErrorCodes.RolesRequired, MasterDataErrorCodes.EmployerRoleRequired, MasterDataErrorCodes.InvalidIban,
                     MasterDataErrorCodes.InvalidVatNumber, MasterDataErrorCodes.InvalidEmail, MasterDataErrorCodes.InvalidCulture,
                     MasterDataErrorCodes.UserNotFound, MasterDataErrorCodes.TooManyChildren, MasterDataErrorCodes.InvalidTimeZone,
                     MasterDataErrorCodes.BlockReasonRequired, MasterDataErrorCodes.InvalidCode
                 })
        {
            MasterDataErrorCodes.BadRequestCodes.ShouldContain(code);
        }

        /* PO 2026-10-12: a block without a reason is a wrong request, not a state conflict. */
        MasterDataErrorCodes.BlockReasonRequired.ShouldBe("OpenTms.MasterData:BlockReasonRequired");
        MasterDataErrorCodes.ConflictCodes.ShouldNotContain(MasterDataErrorCodes.BlockReasonRequired);

        MasterDataErrorCodes.ConflictCodes.Intersect(MasterDataErrorCodes.BadRequestCodes).ShouldBeEmpty();
    }

    /* PO 2026-10-13: the default country, mirroring the currency default of run 1. Critical — authorization. */
    [Theory]
    [InlineData(nameof(CountryAppService.SetDefaultAsync), MasterDataPermissions.Countries.Update)]
    [InlineData(nameof(CountryAppService.GetDefaultAsync), MasterDataPermissions.Countries.Default)]
    [InlineData(nameof(CountryAppService.DeleteAsync), MasterDataPermissions.Countries.Delete)]
    public void The_default_country_commands_carry_the_update_and_read_permissions(string method, string permission)
    {
        PolicyOf(typeof(CountryAppService), method).ShouldBe(permission);
    }

    [Fact]
    public void SetDefault_is_not_reachable_through_the_read_or_create_permission_and_sits_behind_the_Countries_node()
    {
        var policy = PolicyOf(typeof(CountryAppService), nameof(CountryAppService.SetDefaultAsync));
        policy.ShouldBe("MasterData.Countries.Update");
        policy.ShouldNotBe(MasterDataPermissions.Countries.Default);
        policy.ShouldNotBe(MasterDataPermissions.Countries.Create);
        policy.ShouldNotBe(MasterDataPermissions.Currencies.Update, "the country default is gated by the country permission, not the currency one");

        var feature = typeof(CountryAppService).GetCustomAttribute<RequiresFeatureAttribute>();
        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.MasterData.Default]);
        MethodFeatures(typeof(CountryAppService), nameof(CountryAppService.SetDefaultAsync))
            .ShouldBe([AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.Countries]);
        /* GetDefault is a picker (PO 2026-10-14): the app feature only. */
        typeof(CountryAppService).GetMethod(nameof(CountryAppService.GetDefaultAsync))!.GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull();

        typeof(CountryAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
            .Select(m => m.Name)
            .ShouldBeEmpty("every public method of CountryAppService needs [Authorize(<permission>)]");
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
