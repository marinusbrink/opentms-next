using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// The Dutch government's Locatieserver (BAG data, free, no key): postal code plus house number gives the
/// street, the city and the coordinates; a postal code alone gives the street and the city.
/// </summary>
public class PdokAddressLookupProvider : IAddressLookupProvider
{
    private static readonly Regex Point = new(@"POINT\((?<lon>-?[0-9.]+) (?<lat>-?[0-9.]+)\)", RegexOptions.Compiled);
    private readonly IHttpClientFactory _clients;
    private readonly AddressLookupOptions _options;

    public PdokAddressLookupProvider(IHttpClientFactory clients, IOptions<AddressLookupOptions> options)
    {
        _clients = clients;
        _options = options.Value;
    }

    public string Name => "pdok";

    public bool Supports(string countryCode) => countryCode == "NL";

    public async Task<AddressLookupResultDto?> LookupAsync(string countryCode, string postalCode, string? houseNumber, CancellationToken cancellationToken)
    {
        var postcode = postalCode.Replace(" ", string.Empty).ToUpperInvariant();
        var number = houseNumber?.Trim();
        var query = string.IsNullOrEmpty(number)
            ? $"postcode:{postcode}"
            : $"postcode:{postcode} and huisnummer:{number}";
        var type = string.IsNullOrEmpty(number) ? "postcode" : "adres";
        var url = $"{_options.PdokBaseUrl}?q={Uri.EscapeDataString(query)}&fq={Uri.EscapeDataString("type:" + type)}&rows=1&fl={Uri.EscapeDataString("straatnaam,woonplaatsnaam,huisnummer,huisletter,huisnummertoevoeging,centroide_ll")}";

        using var client = _clients.CreateClient(Name);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("response", out var body) || !body.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
        {
            return null;
        }

        var doc = docs[0];
        var result = new AddressLookupResultDto
        {
            Found = true,
            Provider = Name,
            Street = Text(doc, "straatnaam"),
            City = Text(doc, "woonplaatsnaam"),
            HouseNumber = string.IsNullOrEmpty(number) ? null : (Text(doc, "huisnummer") ?? number) + (Text(doc, "huisletter") ?? string.Empty)
        };
        var point = Text(doc, "centroide_ll");
        if (point != null && Point.Match(point) is { Success: true } m)
        {
            result.Longitude = double.Parse(m.Groups["lon"].Value, CultureInfo.InvariantCulture);
            result.Latitude = double.Parse(m.Groups["lat"].Value, CultureInfo.InvariantCulture);
        }

        return result;
    }

    /// <summary>PDOK serves the house number as a number and the rest as strings; both read as text.</summary>
    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null }
            : null;
}
