using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// The PDOK provider against recorded Locatieserver answers (design 44, "Providers"; the test plan's
/// "PDOK parsing from recorded responses"). Risk class: High — external integration; these are the
/// contract tests on the provider's request and response shape, run on a fake transport.
/// </summary>
public class PdokAddressLookupProviderTests
{
    private readonly FakeHttpMessageHandler _transport = new();
    private readonly AddressLookupOptions _options = new();
    private readonly PdokAddressLookupProvider _provider;

    public PdokAddressLookupProviderTests()
    {
        _provider = new PdokAddressLookupProvider(new FakeHttpClientFactory(_transport), Options.Create(_options));
    }

    [Fact]
    public void Pdok_answers_the_Netherlands_only()
    {
        _provider.Name.ShouldBe("pdok");
        _provider.Supports("NL").ShouldBeTrue();
        _provider.Supports("DE").ShouldBeFalse();
        _provider.Supports("BE").ShouldBeFalse();
    }

    [Fact]
    public async Task An_address_document_gives_street_city_house_number_with_letter_and_coordinates_longitude_first()
    {
        _transport.RespondJson(Recorded.PdokAdres);

        var result = await _provider.LookupAsync("NL", "1012AB", "1", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Found.ShouldBeTrue();
        result.Provider.ShouldBe("pdok");
        result.Street.ShouldBe("Dam");
        result.City.ShouldBe("Amsterdam");
        result.HouseNumber.ShouldBe("1A", "the house letter follows the number");
        /* WKT "POINT(lon lat)": 4.89 is the longitude of Amsterdam, 52.37 its latitude. */
        result.Longitude.ShouldBe(4.89);
        result.Latitude.ShouldBe(52.37);
    }

    [Fact]
    public async Task A_textual_house_number_without_letter_is_taken_as_is()
    {
        _transport.RespondJson(Recorded.PdokAdresTextNumber);

        var result = await _provider.LookupAsync("NL", "1012AB", "12", CancellationToken.None);

        result.ShouldNotBeNull();
        result.HouseNumber.ShouldBe("12");
        result.Longitude.ShouldBe(4.891234);
        result.Latitude.ShouldBe(52.373456);
    }

    [Fact]
    public async Task With_a_house_number_the_query_asks_for_one_adres_document_by_postcode_and_huisnummer()
    {
        _transport.RespondJson(Recorded.PdokAdres);

        await _provider.LookupAsync("NL", "1012 ab", " 1 ", CancellationToken.None);

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Uri.GetLeftPart(System.UriPartial.Path).ShouldBe("https://api.pdok.nl/bzk/locatieserver/search/v3_1/free");
        var query = request.Query;
        query["q"].ShouldBe("postcode:1012AB and huisnummer:1", "the postal code is normalised (no space, upper case) and the number trimmed");
        query["fq"].ShouldBe("type:adres");
        query["rows"].ShouldBe("1");
        query["fl"].ShouldBe("straatnaam,woonplaatsnaam,huisnummer,huisletter,huisnummertoevoeging,centroide_ll");
        request.UserAgent.ShouldBe(_options.UserAgent);
    }

    [Fact]
    public async Task Without_a_house_number_the_query_asks_for_a_postcode_document_and_the_answer_has_no_house_number()
    {
        _transport.RespondJson(Recorded.PdokPostcode);

        var result = await _provider.LookupAsync("NL", "1012AB", null, CancellationToken.None);

        var query = _transport.Requests.ShouldHaveSingleItem().Query;
        query["q"].ShouldBe("postcode:1012AB");
        query["fq"].ShouldBe("type:postcode");
        result.ShouldNotBeNull();
        result.Found.ShouldBeTrue();
        result.Street.ShouldBe("Dam");
        result.City.ShouldBe("Amsterdam");
        result.HouseNumber.ShouldBeNull();
        result.Longitude.ShouldBe(4.8925);
        result.Latitude.ShouldBe(52.3731);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_house_number_counts_as_none(string houseNumber)
    {
        _transport.RespondJson(Recorded.PdokPostcode);

        await _provider.LookupAsync("NL", "1012AB", houseNumber, CancellationToken.None);

        _transport.Requests.ShouldHaveSingleItem().Query["fq"].ShouldBe("type:postcode");
    }

    [Fact]
    public async Task No_documents_means_null_not_an_empty_answer()
    {
        _transport.RespondJson(Recorded.PdokEmpty);

        (await _provider.LookupAsync("NL", "9999ZZ", "1", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task An_answer_without_the_response_envelope_means_null()
    {
        _transport.RespondJson("{\"error\":\"unexpected\"}");

        (await _provider.LookupAsync("NL", "1012AB", "1", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_document_without_a_centroid_leaves_the_coordinates_empty()
    {
        _transport.RespondJson("""{"response":{"docs":[{"woonplaatsnaam":"Amsterdam","straatnaam":"Dam"}]}}""");

        var result = await _provider.LookupAsync("NL", "1012AB", null, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Found.ShouldBeTrue();
        result.Latitude.ShouldBeNull();
        result.Longitude.ShouldBeNull();
    }

    [Fact]
    public async Task A_failing_status_is_thrown_to_the_service_not_swallowed()
    {
        _transport.RespondStatus(System.Net.HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<System.Net.Http.HttpRequestException>(() => _provider.LookupAsync("NL", "1012AB", "1", CancellationToken.None));
    }

    [Fact]
    public async Task The_configured_base_url_is_used()
    {
        _options.PdokBaseUrl = "http://pdok.internal/free";
        _transport.RespondJson(Recorded.PdokAdres);

        await _provider.LookupAsync("NL", "1012AB", "1", CancellationToken.None);

        _transport.Requests.ShouldHaveSingleItem().Uri.GetLeftPart(System.UriPartial.Path).ShouldBe("http://pdok.internal/free");
    }
}
