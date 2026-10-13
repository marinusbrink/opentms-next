using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    /* ── Suggestions: the postal-code dropdown (PO 2026-10-13, extension of design 44) ──────────────────── */

    [Fact]
    public void GetSuggestionsAsync_carries_the_AddressLookup_permission()
    {
        var method = typeof(AddressLookupAppService).GetMethod(nameof(AddressLookupAppService.GetSuggestionsAsync), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        method.ShouldNotBeNull();
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        authorize.ShouldNotBeNull("the dropdown needs the same permission as the lookup");
        authorize.Policy.ShouldBe(IntegrationsPermissions.AddressLookup);
        /* The feature gate sits on the class and so covers the new method: no method-level opt-out. */
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull("the class-level feature gate applies; a method-level one would be a second, divergent gate");
    }

    [Fact]
    public void The_contract_declares_GetSuggestionsAsync_with_country_and_term_returning_a_list_of_lines()
    {
        var method = typeof(IAddressLookupAppService).GetMethod(nameof(IAddressLookupAppService.GetSuggestionsAsync));

        method.ShouldNotBeNull();
        method.GetParameters().Select(p => (p.Name, p.ParameterType)).ShouldBe([("countryCode", typeof(string)), ("term", typeof(string))]);
        method.ReturnType.ShouldBe(typeof(Task<List<AddressSuggestionDto>>));
        typeof(AddressSuggestionDto).GetProperties().Select(p => p.Name)
            .ShouldBe(["Label", "PostalCode", "Street", "HouseNumber", "HouseNumberSuffix", "City", "Latitude", "Longitude"], ignoreOrder: true);
        new AddressSuggestionDto().Label.ShouldBe(string.Empty);
        new AddressSuggestionDto().PostalCode.ShouldBe(string.Empty);
    }

    [Fact]
    public void The_suggestions_cache_item_is_shared_by_every_tenant_on_purpose()
    {
        /* Public data like the lookup's answers: no tenant prefix on the key. Behaviour proven in the container tests. */
        typeof(AddressSuggestionsCacheItem).GetCustomAttribute<IgnoreMultiTenancyAttribute>().ShouldNotBeNull();
        new AddressSuggestionsCacheItem().Suggestions.ShouldNotBeNull();
    }

    [Fact]
    public void The_committed_api_contract_exposes_the_suggestions_with_country_and_term_and_an_array_of_lines()
    {
        var path = Path.Combine(FindRepositoryRoot(), "openapi", "opentms-next.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/integrations/address-lookup/suggestions", out var route).ShouldBeTrue("GET /api/integrations/address-lookup/suggestions is in the committed contract");
        route.EnumerateObject().Select(p => p.Name).ShouldBe(["get"], "a read: GET only");
        var operation = route.GetProperty("get");
        operation.GetProperty("parameters").EnumerateArray()
            .Select(p => (p.GetProperty("name").GetString(), p.GetProperty("in").GetString()))
            .ShouldBe([("countryCode", "query"), ("term", "query")]);
        operation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["AddressLookup"]);
        var schema = operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        schema.GetProperty("type").GetString().ShouldBe("array");
        schema.GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/OpenTms.Integrations.AddressLookup.AddressSuggestionDto");

        var dto = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("OpenTms.Integrations.AddressLookup.AddressSuggestionDto").GetProperty("properties");
        dto.EnumerateObject().Select(p => p.Name).ShouldBe(["label", "postalCode", "street", "houseNumber", "houseNumberSuffix", "city", "latitude", "longitude"], ignoreOrder: true);
        dto.GetProperty("houseNumber").GetProperty("type").GetString().ShouldBe("string", "the number travels as text, like the lookup's");
        dto.GetProperty("latitude").GetProperty("format").GetString().ShouldBe("double");
        dto.GetProperty("longitude").GetProperty("format").GetString().ShouldBe("double");
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
