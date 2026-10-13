using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>What a provider knows about a postal code (and house number): the editor fills only what is still empty.</summary>
public class AddressLookupResultDto
{
    public bool Found { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>The provider that answered ("pdok", "nominatim"); null when nothing was found.</summary>
    public string? Provider { get; set; }
}

/// <summary>One line of the postal-code dropdown: a postal code with its street, or one address on it.</summary>
public class AddressSuggestionDto
{
    /// <summary>What the dropdown shows, e.g. "Stationsplein 7A, 1012AB Amsterdam".</summary>
    public string Label { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string? Street { get; set; }
    /// <summary>Null for a postal-code line; set for an address line.</summary>
    public string? HouseNumber { get; set; }
    public string? HouseNumberSuffix { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public interface IAddressLookupAppService : IApplicationService
{
    /// <summary>
    /// GET /api/integrations/address-lookup/suggestions?countryCode=NL&amp;term=1012 — the postal-code dropdown (PO 2026-10-13).
    /// A partial postal code lists postal codes with their street, a full one the addresses on it; at most ten lines.
    /// Empty for a country without a suggestion source and for a term under three characters; never throws for a provider that is down.
    /// </summary>
    Task<List<AddressSuggestionDto>> GetSuggestionsAsync(string countryCode, string term);

    /// <summary>GET /api/integrations/address-lookup?countryCode=NL&postalCode=1012AB&houseNumber=1 — never throws for a provider that is down: Found is false.</summary>
    Task<AddressLookupResultDto> GetAsync(string countryCode, string postalCode, string? houseNumber = null);
}
