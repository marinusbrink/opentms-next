using System.Linq;
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

    /* ── Suggestions: the postal-code dropdown (PO 2026-10-13, extension of design 44) ──────────────────── */

    private const string SuggestionSort = "huisnummer asc,huisletter asc,huisnummertoevoeging asc";

    private static readonly string[] SuggestionFields = ["weergavenaam", "postcode", "straatnaam", "woonplaatsnaam", "huisnummer", "huisletter", "huisnummertoevoeging", "centroide_ll"];

    [Fact]
    public void Pdok_is_a_suggestion_source_and_Nominatim_is_not()
    {
        /* Nominatim's usage policy forbids autocomplete: it must never be asked on every keystroke. */
        typeof(IAddressSuggestionProvider).IsAssignableFrom(typeof(PdokAddressLookupProvider)).ShouldBeTrue();
        typeof(IAddressSuggestionProvider).IsAssignableFrom(typeof(NominatimAddressLookupProvider)).ShouldBeFalse();
        typeof(PdokAddressLookupProvider).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAddressSuggestionProvider).IsAssignableFrom(t))
            .ShouldBe([typeof(PdokAddressLookupProvider)], "PDOK is the only suggestion source");
    }

    [Theory]
    [InlineData("1012AB", "postcode:1012AB")]
    [InlineData("1012 ab", "postcode:1012AB")]
    [InlineData("1012AB7", "postcode:1012AB and huisnummer:7")]
    [InlineData("1012 AB 7", "postcode:1012AB and huisnummer:7")]
    [InlineData(" 1012ab 123 ", "postcode:1012AB and huisnummer:123")]
    public async Task A_full_postal_code_with_or_without_a_number_asks_for_the_addresses_on_it_in_house_number_order(string term, string q)
    {
        _transport.RespondJson(Recorded.PdokSuggestAdres);

        await _provider.SuggestAsync("NL", term, 10, CancellationToken.None);

        var request = _transport.Requests.ShouldHaveSingleItem();
        request.Uri.GetLeftPart(System.UriPartial.Path).ShouldBe("https://api.pdok.nl/bzk/locatieserver/search/v3_1/free");
        var query = request.Query;
        query["q"].ShouldBe(q);
        query["fq"].ShouldBe("type:adres");
        query["sort"].ShouldBe(SuggestionSort);
        query["rows"].ShouldBe("10");
        query["fl"].ShouldNotBeNull();
        query["fl"]!.Split(',').ShouldBe(SuggestionFields, ignoreOrder: true, "every field the parser reads must be asked for");
        request.UserAgent.ShouldBe(_options.UserAgent);
    }

    [Theory]
    [InlineData("1012", "postcode:1012*")]
    [InlineData("1012a", "postcode:1012A*")]
    [InlineData("1012 A", "postcode:1012A*")]
    [InlineData(" 1 0 1 2 ", "postcode:1012*")]
    public async Task Four_digits_with_at_most_one_letter_ask_for_the_postal_codes_that_start_with_them(string term, string q)
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        await _provider.SuggestAsync("NL", term, 10, CancellationToken.None);

        var query = _transport.Requests.ShouldHaveSingleItem().Query;
        query["q"].ShouldBe(q);
        query["fq"].ShouldBe("type:postcode");
        query["sort"].ShouldBe("postcode asc");
        query["rows"].ShouldBe("10");
    }

    [Theory]
    [InlineData("")]
    [InlineData("101")]
    [InlineData("10123")]
    [InlineData("1012ABC")]
    [InlineData("1012AB7A")]
    [InlineData("1012AB 7-1")]
    [InlineData("1012-AB")]
    [InlineData("AB1012")]
    [InlineData("Damrak")]
    [InlineData("1012*")]
    [InlineData("1012AB or postcode:9999ZZ")]
    public async Task Anything_else_is_no_suggestion_and_no_request(string term)
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        (await _provider.SuggestAsync("NL", term, 10, CancellationToken.None)).ShouldBeEmpty();

        _transport.Requests.ShouldBeEmpty("PDOK is only asked for a postal-code shape; nothing else reaches its query language");
    }

    [Fact]
    public async Task The_number_of_rows_is_the_maximum_asked_for()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        await _provider.SuggestAsync("NL", "1012", 5, CancellationToken.None);

        _transport.Requests.ShouldHaveSingleItem().Query["rows"].ShouldBe("5");
    }

    [Fact]
    public async Task Address_documents_become_lines_with_label_number_suffix_and_coordinates_longitude_first()
    {
        _transport.RespondJson(Recorded.PdokSuggestAdres);

        var lines = await _provider.SuggestAsync("NL", "1012AB", 10, CancellationToken.None);

        lines.Count.ShouldBe(4, "the document without a postcode is dropped");
        lines.Select(l => l.Label).ShouldBe(["Stationsplein 7, 1012AB Amsterdam", "Stationsplein 7A, 1012AB Amsterdam", "Stationsplein 7P-1, 1012AB Amsterdam", "Stationsplein 9-2, 1012AB Amsterdam"]);
        lines.ShouldAllBe(l => l.PostalCode == "1012AB" && l.Street == "Stationsplein" && l.City == "Amsterdam");
        lines.Select(l => l.HouseNumber).ShouldBe(["7", "7", "7", "9"], "PDOK serves huisnummer as a number");
        lines.Select(l => l.HouseNumberSuffix).ShouldBe([null, "A", "P-1", "2"], "letter alone, letter-addition, addition alone");
        lines[0].Longitude.ShouldBe(4.90012);
        lines[0].Latitude.ShouldBe(52.37891);
        lines[2].Longitude.ShouldBe(4.90014);
        lines[2].Latitude.ShouldBe(52.37893);
        lines[3].Latitude.ShouldBeNull("no centroid, no coordinates");
        lines[3].Longitude.ShouldBeNull();
    }

    [Fact]
    public async Task Postal_code_documents_become_lines_without_number_or_suffix()
    {
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        var lines = await _provider.SuggestAsync("NL", "1012", 10, CancellationToken.None);

        lines.Count.ShouldBe(2);
        lines[0].Label.ShouldBe("Stationsplein, 1012AB Amsterdam");
        lines[0].PostalCode.ShouldBe("1012AB");
        lines[0].Street.ShouldBe("Stationsplein");
        lines[0].City.ShouldBe("Amsterdam");
        lines[0].HouseNumber.ShouldBeNull();
        lines[0].HouseNumberSuffix.ShouldBeNull();
        lines[0].Longitude.ShouldBe(4.9001);
        lines[0].Latitude.ShouldBe(52.3789);
        lines[1].PostalCode.ShouldBe("1012AC");
        lines[1].Street.ShouldBe("Damrak");
    }

    [Fact]
    public async Task A_document_with_an_empty_postcode_is_dropped_too()
    {
        _transport.RespondJson("""{"response":{"docs":[{"weergavenaam":"x","postcode":"","straatnaam":"Dam"},{"weergavenaam":"Dam, 1012JS Amsterdam","postcode":"1012JS","straatnaam":"Dam","woonplaatsnaam":"Amsterdam"}]}}""");

        var lines = await _provider.SuggestAsync("NL", "1012", 10, CancellationToken.None);

        lines.ShouldHaveSingleItem().PostalCode.ShouldBe("1012JS");
    }

    [Fact]
    public async Task No_documents_or_no_envelope_mean_no_lines()
    {
        _transport.RespondJson(Recorded.PdokEmpty);
        (await _provider.SuggestAsync("NL", "9999", 10, CancellationToken.None)).ShouldBeEmpty();

        _transport.RespondJson("{\"error\":\"unexpected\"}");
        (await _provider.SuggestAsync("NL", "9999", 10, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_status_on_a_suggestion_is_thrown_to_the_service_not_swallowed()
    {
        _transport.RespondStatus(System.Net.HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<System.Net.Http.HttpRequestException>(() => _provider.SuggestAsync("NL", "1012", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Suggestions_use_the_configured_base_url()
    {
        _options.PdokBaseUrl = "http://pdok.internal/free";
        _transport.RespondJson(Recorded.PdokSuggestPostcode);

        await _provider.SuggestAsync("NL", "1012", 10, CancellationToken.None);

        _transport.Requests.ShouldHaveSingleItem().Uri.GetLeftPart(System.UriPartial.Path).ShouldBe("http://pdok.internal/free");
    }
}
