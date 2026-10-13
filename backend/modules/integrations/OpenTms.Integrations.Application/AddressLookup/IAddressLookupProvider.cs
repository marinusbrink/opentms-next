using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>One external address source (design 44). Providers are tried in registration order; the first that supports the country answers.</summary>
public interface IAddressLookupProvider
{
    string Name { get; }

    bool Supports(string countryCode);

    /// <summary>Null when the provider has no answer; throws only for transport errors, which the service turns into "not found".</summary>
    Task<AddressLookupResultDto?> LookupAsync(string countryCode, string postalCode, string? houseNumber, CancellationToken cancellationToken);
}

/// <summary>Configuration section "AddressLookup".</summary>
public class AddressLookupOptions
{
    public string PdokBaseUrl { get; set; } = "https://api.pdok.nl/bzk/locatieserver/search/v3_1/free";

    /// <summary>OpenStreetMap's Nominatim for countries without a national source; its usage policy asks for a real User-Agent and at most one request per second.</summary>
    public bool NominatimEnabled { get; set; } = true;

    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org/search";

    public string UserAgent { get; set; } = "OpenTMS/1.0 (address lookup)";

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Answers are public data that rarely change; they are cached for every tenant alike.</summary>
    public int CacheDays { get; set; } = 30;
}
