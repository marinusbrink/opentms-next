using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// One recorded outbound request: what the provider sent, kept as values because the message itself
/// is disposed with the client.
/// </summary>
public sealed record RecordedRequest(Uri Uri, string? UserAgent, DateTime At)
{
    public NameValueCollection Query => HttpUtility.ParseQueryString(Uri.Query);
}

/// <summary>
/// The fake transport of design 44's test plan: a <see cref="HttpMessageHandler"/> that answers with
/// recorded JSON (or a status, or an exception) and records every request it sees. Shared by the pure
/// provider tests (Application.Tests) and the container tests (EntityFrameworkCore.Tests).
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>What the next request gets; defaults to an empty PDOK answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => Json(Recorded.PdokEmpty);

    public void RespondJson(string json) => Responder = _ => Json(json);

    /// <summary>Each provider gets its own recorded answer, whichever of them is asked: for tests that are not about the provider order.</summary>
    public void RespondPerProvider(string pdokJson = Recorded.PdokAdres, string nominatimJson = Recorded.NominatimBerlin)
        => Responder = request => Json(request.RequestUri!.Host.Contains("pdok", StringComparison.OrdinalIgnoreCase) ? pdokJson : nominatimJson);

    public void RespondStatus(HttpStatusCode status) => Responder = _ => new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    public void Fail(Exception exception) => Responder = _ => throw exception;

    public static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!, request.Headers.UserAgent.ToString(), DateTime.UtcNow));
        }

        return Task.FromResult(Responder(request));
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> whose every client talks to the fake handler.</summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public FakeHttpClientFactory(FakeHttpMessageHandler handler)
    {
        Handler = handler;
    }

    public FakeHttpMessageHandler Handler { get; }

    public List<string> ClientNames { get; } = new();

    public HttpClient CreateClient(string name)
    {
        ClientNames.Add(name);
        return new HttpClient(Handler, disposeHandler: false);
    }
}

/// <summary>Recorded answers of the two providers (shapes as served on 2026-10-13).</summary>
public static class Recorded
{
    /* PDOK Locatieserver v3_1 "free", q=postcode:1012AB and huisnummer:1, fq=type:adres. huisnummer is a number in the
     * real answer; the house letter is a separate field. centroide_ll is WKT, longitude first. */
    public const string PdokAdres = """
        {"response":{"numFound":1,"start":0,"maxScore":11.2,"docs":[{"woonplaatsnaam":"Amsterdam","huisnummer":1,"huisletter":"A","straatnaam":"Dam","centroide_ll":"POINT(4.89 52.37)"}]}}
        """;

    /* The same, with huisnummer served as text and no house letter. */
    public const string PdokAdresTextNumber = """
        {"response":{"numFound":1,"start":0,"maxScore":11.2,"docs":[{"woonplaatsnaam":"Amsterdam","huisnummer":"12","straatnaam":"Dam","centroide_ll":"POINT(4.891234 52.373456)"}]}}
        """;

    /* fq=type:postcode: a postal-code document has street and city but no house number. */
    public const string PdokPostcode = """
        {"response":{"numFound":1,"start":0,"maxScore":9.1,"docs":[{"woonplaatsnaam":"Amsterdam","straatnaam":"Dam","centroide_ll":"POINT(4.8925 52.3731)"}]}}
        """;

    /* Suggestions (PO 2026-10-13), q=postcode:1012AB, fq=type:adres, sorted by number, letter, addition: one line per
     * address. huisnummer is a number; the letter and the addition are separate strings. The last document has no
     * postcode (a shape the provider must drop), the fourth no centroid. */
    public const string PdokSuggestAdres = """
        {"response":{"numFound":5,"start":0,"maxScore":12.0,"docs":[
          {"weergavenaam":"Stationsplein 7, 1012AB Amsterdam","postcode":"1012AB","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","huisnummer":7,"centroide_ll":"POINT(4.90012 52.37891)"},
          {"weergavenaam":"Stationsplein 7A, 1012AB Amsterdam","postcode":"1012AB","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","huisnummer":7,"huisletter":"A","centroide_ll":"POINT(4.90013 52.37892)"},
          {"weergavenaam":"Stationsplein 7P-1, 1012AB Amsterdam","postcode":"1012AB","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","huisnummer":7,"huisletter":"P","huisnummertoevoeging":"1","centroide_ll":"POINT(4.90014 52.37893)"},
          {"weergavenaam":"Stationsplein 9-2, 1012AB Amsterdam","postcode":"1012AB","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","huisnummer":9,"huisnummertoevoeging":"2"},
          {"weergavenaam":"Stationsplein, Amsterdam","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","huisnummer":11}
        ]}}
        """;

    /* Suggestions, q=postcode:1012*, fq=type:postcode, sort=postcode asc: one line per postal code with its street. */
    public const string PdokSuggestPostcode = """
        {"response":{"numFound":2,"start":0,"maxScore":8.0,"docs":[
          {"weergavenaam":"Stationsplein, 1012AB Amsterdam","postcode":"1012AB","straatnaam":"Stationsplein","woonplaatsnaam":"Amsterdam","centroide_ll":"POINT(4.9001 52.3789)"},
          {"weergavenaam":"Damrak, 1012AC Amsterdam","postcode":"1012AC","straatnaam":"Damrak","woonplaatsnaam":"Amsterdam","centroide_ll":"POINT(4.8945 52.3752)"}
        ]}}
        """;

    public const string PdokEmpty = """
        {"response":{"numFound":0,"start":0,"maxScore":0.0,"docs":[]}}
        """;

    /* Nominatim jsonv2 with addressdetails=1 for postalcode=10115&countrycodes=de. */
    public const string NominatimBerlin = """
        [{"place_id":12345,"licence":"Data © OpenStreetMap contributors, ODbL 1.0.","lat":"52.5323","lon":"13.3846","category":"place","type":"postcode","display_name":"10115, Berlin, Deutschland","address":{"postcode":"10115","city":"Berlin","state":"Berlin","ISO3166-2-lvl4":"DE-BE","country":"Deutschland","country_code":"de"}}]
        """;

    /* A place that is a town, not a city, with a road. */
    public const string NominatimTown = """
        [{"place_id":2,"lat":"51.79","lon":"6.14","address":{"road":"Hauptstraße","town":"Kleve","country_code":"de"}}]
        """;

    public const string NominatimVillage = """
        [{"place_id":3,"lat":"50.1","lon":"6.5","address":{"village":"Dorfheim","country_code":"de"}}]
        """;

    public const string NominatimMunicipality = """
        [{"place_id":4,"lat":"50.2","lon":"6.6","address":{"municipality":"Gemeindeland","country_code":"de"}}]
        """;

    public const string NominatimEmpty = "[]";
}
