using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Features;
using OpenTms.MasterData.Fleet;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// The permission and feature gate of TransportCharacteristicsAppService.GetLookupPageAsync (design 43,
/// "Relation administration": the picker runs under the TransportCharacteristics permission, like the other
/// reads of that service), read by reflection. Risk class: Critical — authorization. The SQLite tests run with
/// authorization always allowed, so this is where a wrong or missing attribute is caught.
/// </summary>
public class TransportCharacteristicsLookupPageAuthorizationTests
{
    [Theory]
    [InlineData(nameof(TransportCharacteristicsAppService.GetListAsync))]
    [InlineData(nameof(TransportCharacteristicsAppService.GetAsync))]
    [InlineData(nameof(TransportCharacteristicsAppService.GetLookupAsync))]
    [InlineData(nameof(TransportCharacteristicsAppService.GetLookupPageAsync))]
    public void The_reads_including_the_lookup_page_carry_the_TransportCharacteristics_read_permission(string method)
    {
        PolicyOf(method).ShouldBe(MasterDataPermissions.TransportCharacteristics.Default);
    }

    [Fact]
    public void The_lookup_page_is_not_gated_by_the_relations_permission_or_a_write_permission()
    {
        var policy = PolicyOf(nameof(TransportCharacteristicsAppService.GetLookupPageAsync));

        policy.ShouldBe("MasterData.TransportCharacteristics");
        policy.ShouldNotBe(MasterDataPermissions.Relations.Default);
        policy.ShouldNotBe(MasterDataPermissions.TransportCharacteristics.Create);
        policy.ShouldNotBe(MasterDataPermissions.TransportCharacteristics.Update);
    }

    [Fact]
    public void The_service_sits_behind_the_Master_data_app_feature_and_the_lookup_page_adds_no_gate_of_its_own()
    {
        var feature = typeof(TransportCharacteristicsAppService).GetCustomAttribute<RequiresFeatureAttribute>();

        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.MasterData.Default]);
        AppFeatures.MasterData.Default.ShouldBe("Apps.MasterData");
        Method(nameof(TransportCharacteristicsAppService.GetLookupPageAsync)).GetCustomAttribute<RequiresFeatureAttribute>()
            .ShouldBeNull("a picker (PO 2026-10-14): only the class-level app feature applies, not the TransportCharacteristics node");
    }

    [Fact]
    public void No_public_method_of_the_service_is_without_a_permission()
    {
        typeof(TransportCharacteristicsAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
            .Select(m => m.Name)
            .ShouldBeEmpty("every public method of TransportCharacteristicsAppService needs [Authorize(<permission>)]");
    }

    private static MethodInfo Method(string name)
    {
        var info = typeof(TransportCharacteristicsAppService).GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        info.ShouldNotBeNull($"TransportCharacteristicsAppService.{name} must be a public method");
        return info;
    }

    private static string? PolicyOf(string method) => Method(method).GetCustomAttribute<AuthorizeAttribute>()?.Policy;
}
