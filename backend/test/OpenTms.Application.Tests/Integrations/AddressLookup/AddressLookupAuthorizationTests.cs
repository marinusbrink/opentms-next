using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// The permission and feature attributes of the address lookup, read by reflection (design 44,
/// "API contract": permission Integrations.AddressLookup on both sides, feature default off), plus the
/// constants, the option defaults and the committed API contract. Risk class: Critical — authorization
/// and the per-tenant switch (constitution rule 4). The runtime check itself (feature off refuses the
/// call) is covered in OpenTms.EntityFrameworkCore.Tests AddressLookupAppServiceTests.
/// </summary>
public class AddressLookupAuthorizationTests
{
    [Fact]
    public void GetAsync_carries_the_AddressLookup_permission()
    {
        var method = typeof(AddressLookupAppService).GetMethod(nameof(AddressLookupAppService.GetAsync), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        method.ShouldNotBeNull();
        method.GetCustomAttribute<AuthorizeAttribute>()?.Policy.ShouldBe(IntegrationsPermissions.AddressLookup);
    }

    [Fact]
    public void No_public_method_of_the_service_is_without_a_permission()
    {
        typeof(AddressLookupAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
            .Select(m => m.Name)
            .ShouldBeEmpty("every public method of AddressLookupAppService needs [Authorize(<permission>)]");
    }

    [Fact]
    public void The_service_sits_behind_the_AddressLookup_feature()
    {
        var feature = typeof(AddressLookupAppService).GetCustomAttribute<RequiresFeatureAttribute>();

        feature.ShouldNotBeNull();
        feature.Features.ShouldBe([IntegrationsFeatures.AddressLookup]);
    }

    [Fact]
    public void The_feature_and_permission_names_sit_under_the_Integrations_group()
    {
        IntegrationsFeatures.GroupName.ShouldBe("Integrations");
        IntegrationsFeatures.AddressLookup.ShouldBe("Integrations.AddressLookup");
        IntegrationsPermissions.GroupName.ShouldBe("Integrations");
        IntegrationsPermissions.AddressLookup.ShouldBe("Integrations.AddressLookup");
    }

    [Fact]
    public void The_cache_item_is_shared_by_every_tenant_on_purpose()
    {
        /* Design 44, "Caching and privacy": a postal code's answer is public data, so the entry has no
         * tenant prefix. The attribute is the documented reason; the behaviour is proven in the container tests. */
        typeof(AddressLookupCacheItem).GetCustomAttribute<IgnoreMultiTenancyAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void The_options_default_to_the_public_endpoints_a_real_user_agent_a_short_timeout_and_a_month_of_cache()
    {
        var options = new AddressLookupOptions();

        options.PdokBaseUrl.ShouldBe("https://api.pdok.nl/bzk/locatieserver/search/v3_1/free");
        options.NominatimEnabled.ShouldBeTrue();
        options.NominatimBaseUrl.ShouldBe("https://nominatim.openstreetmap.org/search");
        options.UserAgent.ShouldNotBeNullOrWhiteSpace();
        options.UserAgent.ShouldContain("OpenTMS", Case.Sensitive, "Nominatim's usage policy asks for a real, identifying User-Agent");
        options.TimeoutSeconds.ShouldBe(5);
        options.CacheDays.ShouldBe(30);
    }

    [Fact]
    public void The_committed_api_contract_exposes_the_lookup_with_its_three_query_parameters()
    {
        var path = Path.Combine(FindRepositoryRoot(), "openapi", "opentms-next.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/integrations/address-lookup").GetProperty("get");
        operation.GetProperty("parameters").EnumerateArray()
            .Select(p => (p.GetProperty("name").GetString(), p.GetProperty("in").GetString()))
            .ShouldBe([("countryCode", "query"), ("postalCode", "query"), ("houseNumber", "query")]);
        operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/OpenTms.Integrations.AddressLookup.AddressLookupResultDto");

        var dto = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("OpenTms.Integrations.AddressLookup.AddressLookupResultDto").GetProperty("properties");
        dto.EnumerateObject().Select(p => p.Name).ShouldBe(["found", "street", "houseNumber", "city", "latitude", "longitude", "provider"], ignoreOrder: true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "backend", "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the test must run inside the repository");
        return directory.FullName;
    }
}
