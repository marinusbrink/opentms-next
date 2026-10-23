using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Published;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Financial;

/// <summary>
/// The Master Data references of the Financial master data (design 52, module boundary rule 3: the stated synchronous
/// call through the published <see cref="IMasterDataReferenceLookup"/>): countries and currencies by ISO code
/// (assessment §3.7), geographical regions by id. Unknown keys are refused (ReferenceNotFound), an inactive currency
/// too (InvalidCode). Only keys that are new or changed are checked: a row that refers to a country, currency or
/// region deactivated or deleted later stays as it is.
/// </summary>
public class MasterDataReferences : ITransientDependency
{
    private readonly IMasterDataReferenceLookup _lookup;

    public MasterDataReferences(IMasterDataReferenceLookup lookup)
    {
        _lookup = lookup;
    }

    public async Task EnsureCountryExistsAsync(string isoAlpha2)
    {
        var found = await _lookup.GetCountriesAsync([isoAlpha2]);
        if (found.All(c => c.Code != isoAlpha2))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "countryCode");
        }
    }

    public async Task EnsureCurrenciesActiveAsync(IReadOnlyCollection<string> isoCodes)
    {
        var keys = isoCodes.Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var found = (await _lookup.GetCurrenciesAsync(keys)).ToDictionary(c => c.Code);
        foreach (var key in keys)
        {
            if (!found.TryGetValue(key, out var currency))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "currencyCode");
            }

            if (!currency.IsActive)
            {
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "currencyCode").WithData("value", key);
            }
        }
    }

    public async Task EnsureRegionsExistAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var found = (await _lookup.GetGeographicalRegionsAsync(keys)).Select(r => r.Id).ToHashSet();
        if (keys.Any(id => !found.Contains(id)))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "geographicalRegionId");
        }
    }

    /// <summary>Region id → name; a region deleted in Master Data is absent.</summary>
    public async Task<Dictionary<Guid, string>> GetRegionNamesAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        return keys.Count == 0 ? [] : (await _lookup.GetGeographicalRegionsAsync(keys)).ToDictionary(r => r.Id, r => r.Name);
    }
}
