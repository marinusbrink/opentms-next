using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataReferenceLookup"/>. In-process only (<c>RemoteService(IsEnabled = false)</c>); one
/// <c>IN</c> query per call; the tenant filter scopes every read.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize]
public class MasterDataReferenceLookup : MasterDataAppServiceBase, IMasterDataReferenceLookup
{
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationCategory, Guid> _categories;
    private readonly IRepository<GeographicalRegion, Guid> _regions;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<Currency, Guid> _currencies;

    public MasterDataReferenceLookup(
        IRepository<Relation, Guid> relations,
        IRepository<RelationCategory, Guid> categories,
        IRepository<GeographicalRegion, Guid> regions,
        IRepository<Country, Guid> countries,
        IRepository<Currency, Guid> currencies)
    {
        _relations = relations;
        _categories = categories;
        _regions = regions;
        _countries = countries;
        _currencies = currencies;
    }

    [Authorize]
    public virtual async Task<List<MasterDataReferenceDto>> GetRelationsAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var query = (await _relations.GetQueryableAsync())
            .Where(r => keys.Contains(r.Id))
            .Select(r => new MasterDataReferenceDto { Id = r.Id, Name = r.Name, IsArchived = r.IsArchived });
        return await AsyncExecuter.ToListAsync(query);
    }

    [Authorize]
    public virtual async Task<List<MasterDataReferenceDto>> GetRelationCategoriesAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var query = (await _categories.GetQueryableAsync())
            .Where(c => keys.Contains(c.Id))
            .Select(c => new MasterDataReferenceDto { Id = c.Id, Name = c.Name });
        return await AsyncExecuter.ToListAsync(query);
    }

    [Authorize]
    public virtual async Task<List<MasterDataReferenceDto>> GetGeographicalRegionsAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var query = (await _regions.GetQueryableAsync())
            .Where(r => keys.Contains(r.Id))
            .Select(r => new MasterDataReferenceDto { Id = r.Id, Name = r.Name });
        return await AsyncExecuter.ToListAsync(query);
    }

    [Authorize]
    public virtual async Task<List<MasterDataCodeReferenceDto>> GetCountriesAsync(IReadOnlyCollection<string> isoAlpha2Codes)
    {
        var keys = BoundCodes(isoAlpha2Codes);
        if (keys.Count == 0)
        {
            return [];
        }

        var countries = await _countries.GetListAsync(c => keys.Contains(c.IsoAlpha2));
        return countries
            .Select(c => new MasterDataCodeReferenceDto { Id = c.Id, Code = c.IsoAlpha2, Name = c.Name.Resolve(CultureInfo.CurrentUICulture.Name) })
            .ToList();
    }

    [Authorize]
    public virtual async Task<List<MasterDataCodeReferenceDto>> GetCurrenciesAsync(IReadOnlyCollection<string> isoCodes)
    {
        var keys = BoundCodes(isoCodes);
        if (keys.Count == 0)
        {
            return [];
        }

        var currencies = await _currencies.GetListAsync(c => keys.Contains(c.IsoCode));
        return currencies
            .Select(c => new MasterDataCodeReferenceDto { Id = c.Id, Code = c.IsoCode, Name = c.Name.Resolve(CultureInfo.CurrentUICulture.Name), IsActive = c.IsActive })
            .ToList();
    }

    private static List<Guid> Bound(IReadOnlyCollection<Guid> ids)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        return keys.Count > IMasterDataReferenceLookup.MaxKeys
            ? throw new BusinessException(MasterDataErrorCodes.TooManyRows).WithData("max", IMasterDataReferenceLookup.MaxKeys)
            : keys;
    }

    private static List<string> BoundCodes(IReadOnlyCollection<string> codes)
    {
        var keys = (codes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        return keys.Count > IMasterDataReferenceLookup.MaxKeys
            ? throw new BusinessException(MasterDataErrorCodes.TooManyRows).WithData("max", IMasterDataReferenceLookup.MaxKeys)
            : keys;
    }
}
