using System;
using System.Collections.Generic;
using System.Linq;
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
public class PdokAddressLookupProvider : IAddressLookupProvider, IAddressSuggestionProvider
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
    private static readonly Regex FullPostcode = new(@"^(?<pc>[0-9]{4}[A-Z]{2})(?<nr>[0-9]+)?$", RegexOptions.Compiled);
    private static readonly Regex PartialPostcode = new(@"^[0-9]{4}[A-Z]?$", RegexOptions.Compiled);

    /// <summary>
    /// Four digits (and maybe a letter) list the postal codes with their street; a full postal code (maybe
    /// followed by a house number) lists the addresses on it in house-number order.
    /// </summary>
    public async Task<List<AddressSuggestionDto>> SuggestAsync(string countryCode, string term, int max, CancellationToken cancellationToken)
    {
        var key = term.Replace(" ", string.Empty).ToUpperInvariant();
        string q, type, sort;
        if (FullPostcode.Match(key) is { Success: true } full)
        {
            var number = full.Groups["nr"].Success ? full.Groups["nr"].Value : null;
            q = number == null ? $"postcode:{full.Groups["pc"].Value}" : $"postcode:{full.Groups["pc"].Value} and huisnummer:{number}";
            type = "adres";
            sort = "huisnummer asc,huisletter asc,huisnummertoevoeging asc";
        }
        else if (PartialPostcode.IsMatch(key))
        {
            q = $"postcode:{key}*";
            type = "postcode";
            sort = "postcode asc";
        }
        else
        {
            return [];
        }

        var url = $"{_options.PdokBaseUrl}?q={Uri.EscapeDataString(q)}&fq={Uri.EscapeDataString("type:" + type)}&rows={max}&sort={Uri.EscapeDataString(sort)}&fl={Uri.EscapeDataString("weergavenaam,postcode,straatnaam,woonplaatsnaam,huisnummer,huisletter,huisnummertoevoeging,centroide_ll")}";
        using var client = _clients.CreateClient(Name);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("response", out var body) || !body.TryGetProperty("docs", out var docs))
        {
            return [];
        }

        return docs.EnumerateArray().Select(doc =>
        {
            var letter = Text(doc, "huisletter");
            var addition = Text(doc, "huisnummertoevoeging");
            var suffix = (letter ?? string.Empty) + (addition == null ? string.Empty : (letter == null ? addition : "-" + addition));
            var suggestion = new AddressSuggestionDto
            {
                Label = Text(doc, "weergavenaam") ?? Text(doc, "postcode") ?? string.Empty,
                PostalCode = Text(doc, "postcode") ?? string.Empty,
                Street = Text(doc, "straatnaam"),
                City = Text(doc, "woonplaatsnaam"),
                HouseNumber = Text(doc, "huisnummer"),
                HouseNumberSuffix = suffix.Length == 0 ? null : suffix
            };
            var point = Text(doc, "centroide_ll");
            if (point != null && Point.Match(point) is { Success: true } m)
            {
                suggestion.Longitude = double.Parse(m.Groups["lon"].Value, CultureInfo.InvariantCulture);
                suggestion.Latitude = double.Parse(m.Groups["lat"].Value, CultureInfo.InvariantCulture);
            }

            return suggestion;
        }).Where(s => s.PostalCode.Length > 0).ToList();
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null }
            : null;
}
