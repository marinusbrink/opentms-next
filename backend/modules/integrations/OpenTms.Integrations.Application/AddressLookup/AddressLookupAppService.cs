using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation;

namespace OpenTms.Integrations.AddressLookup;

/// <summary>A cached answer; public data, the same for every tenant, hence outside the tenant cache prefix.</summary>
[IgnoreMultiTenancy]
public class AddressLookupCacheItem
{
    public AddressLookupResultDto Result { get; set; } = new();
}

/// <summary>
/// The postal-code lookup of design 44: the anti-corruption layer in front of the providers. Keys, rate
/// limits and parsing stay here; the editor only ever sees <see cref="AddressLookupResultDto"/>. A provider
/// that is down means "not found", never an error for the user.
/// </summary>
[RequiresFeature(IntegrationsFeatures.AddressLookup)]
public class AddressLookupAppService : IntegrationsAppServiceBase, IAddressLookupAppService
{
    private const int MaxPostalCodeLength = 16;
    private const int MaxHouseNumberLength = 16;

    private readonly IEnumerable<IAddressLookupProvider> _providers;
    private readonly IDistributedCache<AddressLookupCacheItem> _cache;
    private readonly AddressLookupOptions _options;

    public AddressLookupAppService(IEnumerable<IAddressLookupProvider> providers, IDistributedCache<AddressLookupCacheItem> cache, IOptions<AddressLookupOptions> options)
    {
        _providers = providers;
        _cache = cache;
        _options = options.Value;
    }

    [Authorize(IntegrationsPermissions.AddressLookup)]
    public async Task<AddressLookupResultDto> GetAsync(string countryCode, string postalCode, string? houseNumber = null)
    {
        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        var postal = (postalCode ?? string.Empty).Trim();
        var number = string.IsNullOrWhiteSpace(houseNumber) ? null : houseNumber.Trim();
        if (country.Length != 2 || !country.All(char.IsLetter) || postal.Length is 0 or > MaxPostalCodeLength || (number?.Length ?? 0) > MaxHouseNumberLength)
        {
            throw new AbpValidationException("countryCode, postalCode and houseNumber have the shape of an address key.");
        }

        var key = $"{country}|{postal.Replace(" ", string.Empty).ToUpperInvariant()}|{number ?? string.Empty}";
        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            return cached.Result;
        }

        var provider = _providers.FirstOrDefault(p => p.Supports(country));
        if (provider == null)
        {
            return new AddressLookupResultDto();
        }

        AddressLookupResultDto result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds + 2));
            result = await provider.LookupAsync(country, postal, number, cts.Token) ?? new AddressLookupResultDto();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            /* The provider, not the request, failed: the editor simply gets nothing to fill in. No address data in the log (rule 5). */
            Logger.LogWarning(ex, "Address lookup through {Provider} failed for country {Country}.", provider.Name, country);
            return new AddressLookupResultDto();
        }

        if (result.Found)
        {
            await _cache.SetAsync(key, new AddressLookupCacheItem { Result = result }, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(_options.CacheDays) });
        }

        return result;
    }
}
