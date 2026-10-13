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

public interface IAddressLookupAppService : IApplicationService
{
    /// <summary>GET /api/integrations/address-lookup?countryCode=NL&postalCode=1012AB&houseNumber=1 — never throws for a provider that is down: Found is false.</summary>
    Task<AddressLookupResultDto> GetAsync(string countryCode, string postalCode, string? houseNumber = null);
}
