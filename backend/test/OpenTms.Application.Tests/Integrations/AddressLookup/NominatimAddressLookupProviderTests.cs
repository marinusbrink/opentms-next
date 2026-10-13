using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// The Nominatim provider against recorded OpenStreetMap answers (design 44, "Providers": real
/// User-Agent, at most one request per second, place and centroid without street). Risk class: High —
/// external integration; contract tests on the request and response shape on a fake transport.
/// </summary>
public class NominatimAddressLookupProviderTests
{
    private readonly FakeHttpMessageHandler _transport = new();
    private readonly AddressLookupOptions _options = new();
    private readonly NominatimAddressLookupProvider _provider;

    public NominatimAddressLookupProviderTests()
    {
        _provider = new NominatimAddressLookupProvider(new FakeHttpClientFactory(_transport), Options.Create(_options));
    }

    [Fact]
    public void Nominatim_answers_every_country_while_enabled_and_none_when_disabled()
    {
        _provider.Name.ShouldBe("nominatim");
        _provider.Supports("DE").ShouldBeTrue();
        _provider.Supports("NL").ShouldBeTrue();
        _provider.Supports("BE").ShouldBeTrue();

        _options.NominatimEnabled = false;
        _provider.Supports("DE").ShouldBeFalse();
        _provider.Supports("NL").ShouldBeFalse();
    }

    [Fact]
    public async Task A_hit_gives_the_city_and_the_centroid_and_no_street_for_a_postcode_place()
    {
        _transport.RespondJson(Recorded.NominatimBerlin);

        var result = await _provider.LookupAsync("DE", "10115", null, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Found.ShouldBeTrue();
        result.Provider.ShouldBe("nominatim");
        result.City.ShouldBe("Berlin");
        result.Street.ShouldBeNull();
        result.HouseNumber.ShouldBeNull("Nominatim confirms no house number");
        result.Latitude.ShouldBe(52.5323);
        result.Longitude.ShouldBe(13.3846);
    }

    [Fact]
    public async Task The_request_carries_the_country_lower_cased_the_postal_code_and_the_configured_user_agent()
    {
        _options.UserAgent = "OpenTMS-test/9.9 (ops@example.test)";
        _transport.RespondJson(Recorded.NominatimBerlin);

        await _provider.LookupAsync("DE", " 10115 ", "7", CancellationToken.None);

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Uri.GetLeftPart(UriPartial.Path).ShouldBe("https://nominatim.openstreetmap.org/search");
        var query = request.Query;
        query["format"].ShouldBe("jsonv2");
        query["addressdetails"].ShouldBe("1");
        query["limit"].ShouldBe("1");
        query["countrycodes"].ShouldBe("de", "Nominatim wants ISO codes in lower case");
        query["postalcode"].ShouldBe("10115", "trimmed");
        query["street"].ShouldBeNull("the house number is not sent: Nominatim resolves a postal code to a place");
        request.UserAgent.ShouldBe("OpenTMS-test/9.9 (ops@example.test)");
    }

    [Theory]
    [InlineData(Recorded.NominatimTown, "Kleve", "Hauptstraße")]
    [InlineData(Recorded.NominatimVillage, "Dorfheim", null)]
    [InlineData(Recorded.NominatimMunicipality, "Gemeindeland", null)]
    public async Task The_place_falls_back_from_city_to_town_village_and_municipality_and_a_road_is_the_street(string json, string city, string? street)
    {
        _transport.RespondJson(json);

        var result = await _provider.LookupAsync("DE", "47533", null, CancellationToken.None);

        result.ShouldNotBeNull();
        result.City.ShouldBe(city);
        result.Street.ShouldBe(street);
    }

    [Fact]
    public async Task An_empty_array_means_null()
    {
        _transport.RespondJson(Recorded.NominatimEmpty);

        (await _provider.LookupAsync("DE", "00000", null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_non_array_answer_means_null()
    {
        _transport.RespondJson("{\"error\":{\"code\":400,\"message\":\"Unable to geocode\"}}");

        (await _provider.LookupAsync("DE", "00000", null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_hit_without_coordinates_still_counts_as_found()
    {
        _transport.RespondJson("""[{"place_id":5,"address":{"city":"Berlin"}}]""");

        var result = await _provider.LookupAsync("DE", "10115", null, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Found.ShouldBeTrue();
        result.City.ShouldBe("Berlin");
        result.Latitude.ShouldBeNull();
        result.Longitude.ShouldBeNull();
    }

    [Fact]
    public async Task A_failing_status_is_thrown_to_the_service_not_swallowed()
    {
        _transport.RespondStatus(System.Net.HttpStatusCode.TooManyRequests);

        await Should.ThrowAsync<System.Net.Http.HttpRequestException>(() => _provider.LookupAsync("DE", "10115", null, CancellationToken.None));
    }

    [Fact]
    public async Task Two_consecutive_requests_are_at_least_one_second_apart()
    {
        /* The usage policy's one request per second, honoured by the process-wide throttle. */
        _transport.RespondJson(Recorded.NominatimBerlin);

        await _provider.LookupAsync("DE", "10115", null, CancellationToken.None);
        await _provider.LookupAsync("DE", "10117", null, CancellationToken.None);

        _transport.Requests.Count.ShouldBe(2);
        (_transport.Requests[1].At - _transport.Requests[0].At).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950));
    }

    [Fact]
    public async Task The_configured_base_url_is_used()
    {
        _options.NominatimBaseUrl = "http://nominatim.internal/search";
        _transport.RespondJson(Recorded.NominatimBerlin);

        await _provider.LookupAsync("DE", "10115", null, CancellationToken.None);

        _transport.Requests.ShouldHaveSingleItem().Uri.GetLeftPart(UriPartial.Path).ShouldBe("http://nominatim.internal/search");
    }
}
