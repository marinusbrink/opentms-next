using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Integrations.AddressLookup;
using OpenTms.Integrations.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Caching;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations;

/// <summary>
/// The address lookup service through the real container (design 44, "Tests": input validation,
/// provider order, cache hit, provider failure gives not found, feature off refused), on the fake
/// transport of <see cref="AddressLookupTestModule"/>. Risk classes: Critical for the feature gate and
/// the privacy rule on the log; High for the provider chain and the cache (external integration).
/// Tenant isolation: the service stores no tenant data; the one thing it keeps — the cache entry — is
/// shared across tenants by design, which one test pins down so a later change is deliberate.
/// Tests that are not about the provider order let the transport answer each provider with its own
/// recorded JSON (<see cref="FakeHttpMessageHandler.RespondPerProvider"/>) so they fail only for their
/// own reasons.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class AddressLookupAppServiceTests : OpenTmsTestBase<AddressLookupTestModule>
{
    private readonly IAddressLookupAppService _service;
    private readonly FakeHttpMessageHandler _transport;
    private readonly FakeHttpClientFactory _factory;
    private readonly AddressLookupOptions _options;
    private readonly CapturingLog _log;
    private readonly IDistributedCache<AddressLookupCacheItem> _cache;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedCache<AddressSuggestionsCacheItem> _suggestionCache;

    public AddressLookupAppServiceTests()
    {
        _service = GetRequiredService<IAddressLookupAppService>();
        _transport = GetRequiredService<FakeHttpMessageHandler>();
        _factory = GetRequiredService<FakeHttpClientFactory>();
        _options = GetRequiredService<IOptions<AddressLookupOptions>>().Value;
        _log = GetRequiredService<CapturingLog>();
        _cache = GetRequiredService<IDistributedCache<AddressLookupCacheItem>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _suggestionCache = GetRequiredService<IDistributedCache<AddressSuggestionsCacheItem>>();
    }

    private static readonly string SixteenChars = new('A', 16);

    /* ── Input validation (design 44, "API contract") ─────────────────────────────────────────── */

    [Theory]
    [InlineData("N", "1012AB", null)]
    [InlineData("NLD", "1012AB", null)]
    [InlineData("", "1012AB", null)]
    [InlineData("1A", "1012AB", null)]
    [InlineData("N1", "1012AB", null)]
    [InlineData("NL", "", "1")]
    [InlineData("NL", "   ", "1")]
    [InlineData("NL", "AAAAAAAAAAAAAAAAA", null)]
    [InlineData("NL", "1012AB", "AAAAAAAAAAAAAAAAA")]
    public async Task A_key_with_the_wrong_shape_is_refused_before_any_provider_is_asked(string country, string postalCode, string? houseNumber)
    {
        using var _ = _currentTenant.Change(Guid.NewGuid());

        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAsync(country, postalCode, houseNumber));

        _transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_country_or_postal_code_is_refused_like_an_empty_one()
    {
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAsync(null!, "1012AB"));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetAsync("NL", null!));
        _transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sixteen_character_boundary_is_inclusive_and_blanks_are_trimmed()
    {
        _transport.RespondPerProvider(Recorded.PdokEmpty, Recorded.NominatimEmpty);

        await _service.GetAsync(" nl ", SixteenChars, SixteenChars);
        await _service.GetAsync("NL", " 1012AB ", "   ");

        _transport.Requests.Count.ShouldBe(2, "sixteen characters pass, and blank parts are trimmed rather than refused");
        _transport.Requests[0].Uri.Query.ShouldContain(SixteenChars);
        _transport.Requests[1].Uri.Query.ShouldContain("1012AB");
        _transport.Requests[1].Uri.Query.ShouldNotContain("%201012AB", Case.Insensitive, "the postal code reaches the provider trimmed");
    }

    /* ── Provider order (design 44, "Providers") ───────────────────────────────────────────────── */

    [Fact]
    public async Task The_Netherlands_go_to_PDOK()
    {
        _transport.RespondJson(Recorded.PdokAdres);

        var result = await _service.GetAsync("NL", "1012AB", "1");

        _transport.Requests.ShouldHaveSingleItem().Uri.Host.ShouldBe("api.pdok.nl", "design 44: NL is answered by PDOK");
        _factory.ClientNames.ShouldBe(["pdok"]);
        result.Found.ShouldBeTrue();
        result.Provider.ShouldBe("pdok");
        result.Street.ShouldBe("Dam");
        result.City.ShouldBe("Amsterdam");
        result.HouseNumber.ShouldBe("1A");
        result.Longitude.ShouldBe(4.89);
        result.Latitude.ShouldBe(52.37);
    }

    [Fact]
    public async Task The_Netherlands_go_to_PDOK_even_while_Nominatim_is_enabled_for_every_country()
    {
        /* Nominatim says it supports every country; the registration order must still put PDOK first for NL. */
        _options.NominatimEnabled.ShouldBeTrue("the default configuration");
        _transport.RespondJson(Recorded.PdokAdres);

        var result = await _service.GetAsync("nl", "1012 ab", "1");

        _transport.Requests.ShouldHaveSingleItem().Uri.Host.ShouldBe("api.pdok.nl", "design 44: NL is answered by PDOK, not by the catch-all Nominatim");
        result.Provider.ShouldBe("pdok");
    }

    [Fact]
    public void Both_providers_are_registered_PDOK_before_Nominatim()
    {
        var providers = GetRequiredService<System.Collections.Generic.IEnumerable<IAddressLookupProvider>>().Select(p => p.Name).ToList();

        providers.ShouldBe(["pdok", "nominatim"], "the first provider that supports the country answers, and Nominatim supports every country");
    }

    [Fact]
    public async Task Germany_goes_to_Nominatim()
    {
        _transport.RespondJson(Recorded.NominatimBerlin);

        var result = await _service.GetAsync("DE", "10115");

        result.Found.ShouldBeTrue();
        result.Provider.ShouldBe("nominatim");
        result.City.ShouldBe("Berlin");
        result.Street.ShouldBeNull();
        result.Latitude.ShouldBe(52.5323);
        result.Longitude.ShouldBe(13.3846);
        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Uri.Host.ShouldBe("nominatim.openstreetmap.org");
        request.Query["countrycodes"].ShouldBe("de");
        request.UserAgent.ShouldBe(_options.UserAgent);
        _factory.ClientNames.ShouldBe(["nominatim"]);
    }

    [Fact]
    public async Task With_Nominatim_disabled_a_country_other_than_NL_has_no_provider_and_is_not_found()
    {
        _options.NominatimEnabled = false;
        try
        {
            var result = await _service.GetAsync("BE", "1000", "1");

            result.Found.ShouldBeFalse();
            result.Provider.ShouldBeNull();
            result.City.ShouldBeNull();
            _transport.Requests.ShouldBeEmpty("no provider, no request");
            _log.Entries.ShouldBeEmpty("no provider is not a failure");
        }
        finally
        {
            _options.NominatimEnabled = true;
        }
    }

    [Fact]
    public async Task With_Nominatim_disabled_the_Netherlands_still_go_to_PDOK()
    {
        _options.NominatimEnabled = false;
        try
        {
            _transport.RespondJson(Recorded.PdokAdres);

            (await _service.GetAsync("NL", "1012AB", "1")).Provider.ShouldBe("pdok");
            _transport.Requests.ShouldHaveSingleItem().Uri.Host.ShouldBe("api.pdok.nl");
        }
        finally
        {
            _options.NominatimEnabled = true;
        }
    }

    /* ── Cache (design 44, "Caching and privacy") ──────────────────────────────────────────────── */

    [Fact]
    public async Task A_found_answer_is_served_from_the_cache_without_asking_the_provider_again()
    {
        _transport.RespondPerProvider();

        var first = await _service.GetAsync("NL", "1012AB", "1");
        _transport.Fail(new HttpRequestException("the provider is gone"));
        var second = await _service.GetAsync("NL", "1012AB", "1");

        first.Found.ShouldBeTrue();
        _transport.Requests.Count.ShouldBe(1);
        second.Found.ShouldBeTrue();
        second.Provider.ShouldBe(first.Provider);
        second.Street.ShouldBe(first.Street);
        second.City.ShouldBe(first.City);
        second.HouseNumber.ShouldBe(first.HouseNumber);
        second.Latitude.ShouldBe(first.Latitude);
        second.Longitude.ShouldBe(first.Longitude);
    }

    [Fact]
    public async Task The_cache_key_ignores_spaces_and_case_in_the_postal_code_and_case_in_the_country()
    {
        _transport.RespondPerProvider();

        await _service.GetAsync("nl", "1012 ab", "1");
        await _service.GetAsync("NL", "1012AB", "1");
        await _service.GetAsync("Nl", " 1012 AB ", " 1 ");

        _transport.Requests.Count.ShouldBe(1);
        (await _cache.GetAsync("NL|1012AB|1")).ShouldNotBeNull("the key is COUNTRY|POSTCODE|number");
    }

    [Fact]
    public async Task A_different_house_number_or_no_house_number_is_a_different_key()
    {
        _transport.RespondPerProvider();

        await _service.GetAsync("NL", "1012AB", "1");
        await _service.GetAsync("NL", "1012AB", "2");
        await _service.GetAsync("NL", "1012AB");

        _transport.Requests.Count.ShouldBe(3);
        (await _cache.GetAsync("NL|1012AB|1")).ShouldNotBeNull();
        (await _cache.GetAsync("NL|1012AB|2")).ShouldNotBeNull();
        (await _cache.GetAsync("NL|1012AB|")).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_not_found_answer_is_not_cached()
    {
        _transport.RespondPerProvider(Recorded.PdokEmpty, Recorded.NominatimEmpty);

        var first = await _service.GetAsync("NL", "9999ZZ", "1");
        var second = await _service.GetAsync("NL", "9999ZZ", "1");

        first.Found.ShouldBeFalse();
        second.Found.ShouldBeFalse();
        _transport.Requests.Count.ShouldBe(2, "a miss is asked again next time");
        (await _cache.GetAsync("NL|9999ZZ|1")).ShouldBeNull();
        _log.Entries.ShouldBeEmpty("not found is not a failure");
    }

    [Fact]
    public async Task A_cached_answer_is_shared_by_every_tenant_because_it_is_public_data()
    {
        /* Design 44: the cache item carries [IgnoreMultiTenancy]; a postal code is the same for tenant A and B. */
        _transport.RespondPerProvider();
        AddressLookupResultDto first;
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            first = await _service.GetAsync("NL", "1012AB", "1");
        }

        first.Found.ShouldBeTrue();
        _transport.Fail(new HttpRequestException("the provider is gone"));
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var result = await _service.GetAsync("NL", "1012AB", "1");

            result.Found.ShouldBeTrue();
            result.City.ShouldBe(first.City);
            result.Provider.ShouldBe(first.Provider);
        }

        using (_currentTenant.Change(null))
        {
            (await _service.GetAsync("NL", "1012AB", "1")).Found.ShouldBeTrue();
        }

        _transport.Requests.Count.ShouldBe(1);
    }

    /* ── Provider failure (design 44: never an error for the user; rule 5 on the log) ─────────── */

    [Fact]
    public async Task A_throwing_transport_gives_not_found_and_logs_the_provider_and_the_country_only()
    {
        _transport.Fail(new HttpRequestException("connection refused"));

        var result = await _service.GetAsync("DE", "10115", "7");

        result.Found.ShouldBeFalse();
        result.Provider.ShouldBeNull();
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Category.ShouldBe(typeof(AddressLookupAppService).FullName);
        entry.Message.ShouldContain("nominatim");
        entry.Message.ShouldContain("DE");
        entry.Everything.ShouldNotContain("10115", Case.Insensitive, "no postal code in the log (rule 5)");
        entry.Message.ShouldNotContain("7", Case.Insensitive, "no house number in the log (rule 5)");
        entry.Everything.ShouldNotContain("postalcode=", Case.Insensitive, "no request URL in the log (rule 5)");
        entry.Exception.ShouldNotBeNull();
        entry.Exception.ShouldContain("connection refused");
    }

    [Fact]
    public async Task A_failing_status_from_the_provider_gives_not_found_and_logs_without_the_postal_code()
    {
        /* The real EnsureSuccessStatusCode path: whatever the exception says must not carry the request. */
        _transport.RespondStatus(HttpStatusCode.InternalServerError);

        var result = await _service.GetAsync("NL", "1012AB", "7");

        result.Found.ShouldBeFalse();
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldContain(_factory.ClientNames.ShouldHaveSingleItem(), Case.Sensitive, "the provider that was asked is named");
        entry.Message.ShouldContain("NL");
        entry.Everything.ShouldNotContain("1012", Case.Insensitive, "no postal code in the log (rule 5)");
        entry.Everything.ShouldNotContain("huisnummer", Case.Insensitive, "no house number in the log (rule 5)");
        entry.Exception.ShouldNotBeNull();
        entry.Exception.ShouldContain("500");
    }

    [Fact]
    public async Task A_failure_is_not_cached_so_the_next_call_asks_again()
    {
        _transport.Fail(new HttpRequestException("connection refused"));
        (await _service.GetAsync("NL", "1012AB", "1")).Found.ShouldBeFalse();

        _transport.RespondPerProvider();
        var result = await _service.GetAsync("NL", "1012AB", "1");

        result.Found.ShouldBeTrue();
        _transport.Requests.Count.ShouldBe(2);
    }

    /* ── Feature gate (constitution rule 4) ─────────────────────────────────────────────────────── */

    [Fact]
    public async Task With_the_feature_off_the_call_is_refused_before_any_provider_or_cache_is_touched()
    {
        _transport.RespondPerProvider();
        using var _ = _currentTenant.Change(Guid.NewGuid());

        using (TestFeatureValueProvider.OverrideAddressLookup("false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(IntegrationsFeatures.AddressLookup)).ShouldBeFalse();

            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync("NL", "1012AB", "1"));
            _transport.Requests.ShouldBeEmpty();
            (await _cache.GetAsync("NL|1012AB|1")).ShouldBeNull();
        }

        /* With the flag on (the test default) the same call reaches the provider. */
        (await _service.GetAsync("NL", "1012AB", "1")).Found.ShouldBeTrue();
        _transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_flag_is_read_through_the_production_feature_checker()
    {
        var checker = GetRequiredService<IFeatureChecker>();

        (await checker.IsEnabledAsync(IntegrationsFeatures.AddressLookup)).ShouldBeTrue();
        using (TestFeatureValueProvider.OverrideAddressLookup("false"))
        {
            (await checker.IsEnabledAsync(IntegrationsFeatures.AddressLookup)).ShouldBeFalse();
        }

        (await checker.IsEnabledAsync(IntegrationsFeatures.AddressLookup)).ShouldBeTrue();
    }

    /* ── Options ────────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public void The_options_are_bound_from_the_AddressLookup_section_with_their_defaults_where_absent()
    {
        /* The test appsettings carry no AddressLookup section: the defaults of the options class apply. */
        _options.PdokBaseUrl.ShouldBe("https://api.pdok.nl/bzk/locatieserver/search/v3_1/free");
        _options.NominatimBaseUrl.ShouldBe("https://nominatim.openstreetmap.org/search");
        _options.CacheDays.ShouldBe(30);
        _options.TimeoutSeconds.ShouldBe(5);
    }

    /* ══ Suggestions: the postal-code dropdown (PO 2026-10-13, extension of design 44) ════════════════════
     * Risk classes as for the lookup: Critical for the feature gate and the log rule, High for the provider
     * call, the validation and the cache. The provider chain with a recording source is in
     * AddressSuggestionChainTests (its own container: an extra provider must not leak into these tests). */

    /* ── Input validation ───────────────────────────────────────────────────────────────────────── */

    /* "1012 AB" + 25 spaces + "7" = 33 characters: the longest accepted term, and still a PDOK shape. */
    private static readonly string ThirtyThreeChars = "1012 AB" + new string(' ', 25) + "7";
    private static readonly string ThirtyFourChars = "1012 AB" + new string(' ', 26) + "7";

    [Theory]
    [InlineData("N", "1012")]
    [InlineData("NLD", "1012")]
    [InlineData("", "1012")]
    [InlineData("  ", "1012")]
    [InlineData("1A", "1012")]
    [InlineData("N1", "1012")]
    [InlineData("N-", "1012")]
    [InlineData(null, "1012")]
    public async Task Suggestions_with_a_country_of_the_wrong_shape_are_refused_before_any_provider_is_asked(string? country, string term)
    {
        using var _ = _currentTenant.Change(Guid.NewGuid());

        await Should.ThrowAsync<AbpValidationException>(() => _service.GetSuggestionsAsync(country!, term));

        _transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_term_longer_than_33_characters_is_refused_and_33_pass()
    {
        ThirtyThreeChars.Length.ShouldBe(33);
        ThirtyFourChars.Length.ShouldBe(34);
        _transport.RespondJson(Recorded.PdokSuggestAdres);

        await Should.ThrowAsync<AbpValidationException>(() => _service.GetSuggestionsAsync("NL", ThirtyFourChars));
        _transport.Requests.ShouldBeEmpty();

        var lines = await _service.GetSuggestionsAsync("NL", ThirtyThreeChars);

        lines.ShouldNotBeEmpty();
        _transport.Requests.ShouldHaveSingleItem().Query["q"].ShouldBe("postcode:1012AB and huisnummer:7");
    }

    [Fact]
    public async Task Blanks_around_the_term_do_not_count_towards_its_length()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        var lines = await _service.GetSuggestionsAsync(" nl ", new string(' ', 40) + "1012" + new string(' ', 40));

        lines.Count.ShouldBe(2);
        _transport.Requests.ShouldHaveSingleItem().Query["q"].ShouldBe("postcode:1012*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("1 0")]
    [InlineData(" 1   0 ")]
    public async Task A_term_under_three_characters_spaces_ignored_gives_no_lines_without_a_request(string? term)
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        var lines = await _service.GetSuggestionsAsync("NL", term!);

        lines.ShouldNotBeNull();
        lines.ShouldBeEmpty();
        _transport.Requests.ShouldBeEmpty();
        _factory.ClientNames.ShouldBeEmpty();
        _log.Entries.ShouldBeEmpty("a short term is not a failure");
    }

    /* ── Provider ───────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public void Only_PDOK_is_registered_as_a_suggestion_source()
    {
        GetRequiredService<IEnumerable<IAddressSuggestionProvider>>().Select(p => p.Name).ShouldBe(["pdok"], "Nominatim's usage policy forbids autocomplete");
    }

    [Fact]
    public async Task Four_digits_in_the_Netherlands_list_the_postal_codes_through_PDOK()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        var lines = await _service.GetSuggestionsAsync("nl", "1012");

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Uri.Host.ShouldBe("api.pdok.nl");
        request.Query["q"].ShouldBe("postcode:1012*");
        request.Query["fq"].ShouldBe("type:postcode");
        request.Query["rows"].ShouldBe("10", "at most ten lines");
        request.UserAgent.ShouldBe(_options.UserAgent);
        _factory.ClientNames.ShouldBe(["pdok"]);
        lines.Select(l => l.PostalCode).ShouldBe(["1012AB", "1012AC"]);
        lines[0].Label.ShouldBe("Stationsplein, 1012AB Amsterdam");
        lines[0].Street.ShouldBe("Stationsplein");
        lines[0].City.ShouldBe("Amsterdam");
        lines[0].HouseNumber.ShouldBeNull();
        lines[0].Latitude.ShouldBe(52.3789);
        lines[0].Longitude.ShouldBe(4.9001);
    }

    [Fact]
    public async Task A_full_postal_code_with_a_number_lists_its_addresses_through_PDOK()
    {
        _transport.RespondJson(Recorded.PdokSuggestAdres);

        var lines = await _service.GetSuggestionsAsync("NL", "1012 ab 7");

        var query = _transport.Requests.ShouldHaveSingleItem().Query;
        query["q"].ShouldBe("postcode:1012AB and huisnummer:7");
        query["fq"].ShouldBe("type:adres");
        query["sort"].ShouldBe("huisnummer asc,huisletter asc,huisnummertoevoeging asc");
        lines.Select(l => (l.HouseNumber, l.HouseNumberSuffix)).ShouldBe([("7", null), ("7", "A"), ("7", "P-1"), ("9", "2")]);
    }

    [Theory]
    [InlineData("DE", "10115")]
    [InlineData("BE", "1000")]
    [InlineData("FR", "75001")]
    public async Task A_country_without_a_suggestion_source_gives_no_lines_and_never_asks_Nominatim(string country, string term)
    {
        _options.NominatimEnabled.ShouldBeTrue("the default configuration: Nominatim answers the lookup for these countries");
        _transport.RespondPerProvider();

        var lines = await _service.GetSuggestionsAsync(country, term);

        lines.ShouldBeEmpty();
        _transport.Requests.ShouldBeEmpty("no suggestion source, no request; Nominatim is never asked on every keystroke");
        _log.Entries.ShouldBeEmpty("no source is not a failure");
        (await _suggestionCache.GetAsync($"{country}|{term}")).ShouldBeNull();
    }

    /* ── Cache ──────────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Lines_are_served_from_the_cache_without_asking_PDOK_again()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        var first = await _service.GetSuggestionsAsync("NL", "1012");
        _transport.Fail(new HttpRequestException("the provider is gone"));
        var second = await _service.GetSuggestionsAsync("NL", "1012");

        _transport.Requests.Count.ShouldBe(1);
        second.Select(l => (l.Label, l.PostalCode, l.Street, l.City, l.HouseNumber, l.HouseNumberSuffix, l.Latitude, l.Longitude))
            .ShouldBe(first.Select(l => (l.Label, l.PostalCode, l.Street, l.City, l.HouseNumber, l.HouseNumberSuffix, l.Latitude, l.Longitude)));
        _log.Entries.ShouldBeEmpty("the failing transport was never reached");
    }

    [Fact]
    public async Task The_suggestion_cache_key_is_country_and_term_upper_case_without_spaces()
    {
        _transport.RespondJson(Recorded.PdokSuggestAdres);

        await _service.GetSuggestionsAsync("nl", "1012 ab 7");
        await _service.GetSuggestionsAsync("NL", "1012AB7");
        await _service.GetSuggestionsAsync("Nl", "  1012 AB  7 ");

        _transport.Requests.Count.ShouldBe(1);
        (await _suggestionCache.GetAsync("NL|1012AB7")).ShouldNotBeNull("the key is COUNTRY|TERM, upper case, without spaces");
        (await _suggestionCache.GetAsync("NL|1012AB7"))!.Suggestions.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_longer_term_is_a_different_key()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        await _service.GetSuggestionsAsync("NL", "1012");
        await _service.GetSuggestionsAsync("NL", "1012A");

        _transport.Requests.Select(r => r.Query["q"]).ShouldBe(["postcode:1012*", "postcode:1012A*"]);
        (await _suggestionCache.GetAsync("NL|1012")).ShouldNotBeNull();
        (await _suggestionCache.GetAsync("NL|1012A")).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_empty_list_is_not_cached()
    {
        _transport.RespondJson(Recorded.PdokEmpty);

        (await _service.GetSuggestionsAsync("NL", "9999")).ShouldBeEmpty();
        (await _service.GetSuggestionsAsync("NL", "9999")).ShouldBeEmpty();

        _transport.Requests.Count.ShouldBe(2, "an empty answer is asked again next time");
        (await _suggestionCache.GetAsync("NL|9999")).ShouldBeNull();
        _log.Entries.ShouldBeEmpty("no lines is not a failure");
    }

    [Fact]
    public async Task The_suggestion_cache_and_the_lookup_cache_are_separate()
    {
        _transport.RespondJson(Recorded.PdokPostcode);
        (await _service.GetAsync("NL", "1012AB")).Found.ShouldBeTrue();

        _transport.RespondJson(Recorded.PdokSuggestAdres);
        var lines = await _service.GetSuggestionsAsync("NL", "1012AB");

        lines.Count.ShouldBe(4, "a cached lookup answer is no list of lines");
        _transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cached_lines_are_shared_by_every_tenant_because_they_are_public_data()
    {
        /* [IgnoreMultiTenancy] on AddressSuggestionsCacheItem: a postal code's lines are the same for tenant A and B,
         * and they carry nothing of either tenant. Pinned so a change to a per-tenant cache is deliberate. */
        _transport.RespondJson(Recorded.PdokSuggestPostcode);
        List<AddressSuggestionDto> first;
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            first = await _service.GetSuggestionsAsync("NL", "1012");
        }

        first.Count.ShouldBe(2);
        _transport.Fail(new HttpRequestException("the provider is gone"));
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            (await _service.GetSuggestionsAsync("NL", "1012")).Select(l => l.Label).ShouldBe(first.Select(l => l.Label));
        }

        using (_currentTenant.Change(null))
        {
            (await _service.GetSuggestionsAsync("NL", "1012")).Count.ShouldBe(2);
        }

        _transport.Requests.Count.ShouldBe(1);
    }

    /* ── Provider failure (never an error for the user; rule 5 on the log) ─────────────────────── */

    [Fact]
    public async Task A_throwing_transport_gives_no_lines_and_logs_the_provider_and_the_country_only()
    {
        _transport.Fail(new HttpRequestException("connection refused"));

        var lines = await _service.GetSuggestionsAsync("NL", "1012 AB 7");

        lines.ShouldBeEmpty();
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Category.ShouldBe(typeof(AddressLookupAppService).FullName);
        entry.Message.ShouldContain("pdok");
        entry.Message.ShouldContain("NL");
        entry.Everything.ShouldNotContain("1012", Case.Insensitive, "no postal code in the log (rule 5)");
        entry.Everything.ShouldNotContain("postcode:", Case.Insensitive, "no query in the log (rule 5)");
        entry.Exception.ShouldNotBeNull();
        entry.Exception.ShouldContain("connection refused");
    }

    [Fact]
    public async Task A_failing_status_gives_no_lines_and_logs_without_the_term()
    {
        _transport.RespondStatus(HttpStatusCode.InternalServerError);

        var lines = await _service.GetSuggestionsAsync("NL", "1012AB 7");

        lines.ShouldBeEmpty();
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("pdok");
        entry.Message.ShouldContain("NL");
        entry.Everything.ShouldNotContain("1012", Case.Insensitive, "no postal code in the log (rule 5)");
        entry.Everything.ShouldNotContain("huisnummer", Case.Insensitive, "no house number in the log (rule 5)");
        entry.Exception.ShouldNotBeNull();
        entry.Exception.ShouldContain("500");
    }

    [Fact]
    public async Task A_failure_is_not_cached_so_the_next_keystroke_asks_again()
    {
        _transport.Fail(new HttpRequestException("connection refused"));
        (await _service.GetSuggestionsAsync("NL", "1012")).ShouldBeEmpty();
        (await _suggestionCache.GetAsync("NL|1012")).ShouldBeNull();

        _transport.RespondJson(Recorded.PdokSuggestPostcode);
        var lines = await _service.GetSuggestionsAsync("NL", "1012");

        lines.Count.ShouldBe(2);
        _transport.Requests.Count.ShouldBe(2);
    }

    /* ── Feature gate (constitution rule 4) ─────────────────────────────────────────────────────── */

    [Fact]
    public async Task With_the_feature_off_suggestions_are_refused_before_any_provider_or_cache_is_touched()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);
        using var _ = _currentTenant.Change(Guid.NewGuid());

        using (TestFeatureValueProvider.OverrideAddressLookup("false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSuggestionsAsync("NL", "1012"));
            /* The gate comes before the method body: even a term that would short-circuit is refused. */
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSuggestionsAsync("NL", "10"));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSuggestionsAsync("DE", "10115"));
            _transport.Requests.ShouldBeEmpty();
            (await _suggestionCache.GetAsync("NL|1012")).ShouldBeNull();
        }

        /* With the flag on (the test default) the same call reaches PDOK. */
        (await _service.GetSuggestionsAsync("NL", "1012")).Count.ShouldBe(2);
        _transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_the_feature_off_a_cached_list_is_not_served_either()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);
        using var _ = _currentTenant.Change(Guid.NewGuid());
        (await _service.GetSuggestionsAsync("NL", "1012")).Count.ShouldBe(2);
        (await _suggestionCache.GetAsync("NL|1012")).ShouldNotBeNull();

        using (TestFeatureValueProvider.OverrideAddressLookup("false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSuggestionsAsync("NL", "1012"));
        }
    }
}
