using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>
/// OpenStreetMap's Nominatim for every other country: a postal code gives the place and the postal-code
/// centroid, no street. Its usage policy asks for a real User-Agent and at most one request per second,
/// which this process honours with a throttle; a self-hosted instance is a configuration change.
/// </summary>
public class NominatimAddressLookupProvider : IAddressLookupProvider
{
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private readonly IHttpClientFactory _clients;
    private readonly AddressLookupOptions _options;

    public NominatimAddressLookupProvider(IHttpClientFactory clients, IOptions<AddressLookupOptions> options)
    {
        _clients = clients;
        _options = options.Value;
    }

    public string Name => "nominatim";

    public bool Supports(string countryCode) => _options.NominatimEnabled;

    public async Task<AddressLookupResultDto?> LookupAsync(string countryCode, string postalCode, string? houseNumber, CancellationToken cancellationToken)
    {
        var url = $"{_options.NominatimBaseUrl}?format=jsonv2&addressdetails=1&limit=1&countrycodes={countryCode.ToLowerInvariant()}&postalcode={Uri.EscapeDataString(postalCode.Trim())}";
        await Throttle.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastCall = DateTime.UtcNow;
            using var client = _clients.CreateClient(Name);
            client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var hit = json.RootElement[0];
            var address = hit.TryGetProperty("address", out var a) ? a : default;
            var city = address.ValueKind == JsonValueKind.Object
                ? Text(address, "city") ?? Text(address, "town") ?? Text(address, "village") ?? Text(address, "municipality")
                : null;
            var result = new AddressLookupResultDto { Found = true, Provider = Name, City = city, Street = address.ValueKind == JsonValueKind.Object ? Text(address, "road") : null };
            if (double.TryParse(Text(hit, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(Text(hit, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                result.Latitude = lat;
                result.Longitude = lon;
            }

            return result;
        }
        finally
        {
            Throttle.Release();
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
