using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataReferenceLookup"/>. In-process only (<c>RemoteService(IsEnabled = false)</c>); one
/// <c>IN</c> query per call (the address read adds one per distinct country and postcode for the region match); the
/// tenant filter scopes every read.
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

    [Authorize]
    public virtual async Task<List<MasterDataAddressReferenceDto>> GetRelationAddressesAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var addresses = await LazyServiceProvider.LazyGetRequiredService<IRepository<RelationAddress, Guid>>().GetListAsync(a => keys.Contains(a.Id));
        return await DescribeAsync(addresses);
    }

    [Authorize]
    public virtual async Task<List<MasterDataAddressReferenceDto>> GetRelationDefaultAddressesAsync(IReadOnlyCollection<Guid> relationIds)
    {
        var keys = Bound(relationIds);
        if (keys.Count == 0)
        {
            return [];
        }

        var rows = await LazyServiceProvider.LazyGetRequiredService<IRepository<RelationAddress, Guid>>().GetListAsync(a => keys.Contains(a.RelationId));
        if (rows.Count == 0)
        {
            return [];
        }

        /* The relation list's rule (RelationPlaces): the visiting address by kind, else the default, else the first by city. */
        var visiting = (await LazyServiceProvider.LazyGetRequiredService<IRepository<AddressType, Guid>>().GetListAsync(t => t.Kind == AddressTypeKind.Visiting)).Select(t => t.Id).ToHashSet();
        var chosen = rows
            .GroupBy(a => a.RelationId)
            .Select(g => g.FirstOrDefault(a => a.AddressTypeId != null && visiting.Contains(a.AddressTypeId.Value))
                         ?? g.FirstOrDefault(a => a.AddressTypeId == null)
                         ?? g.OrderBy(a => a.City).First())
            .ToList();
        return await DescribeAsync(chosen);
    }

    private async Task<List<MasterDataAddressReferenceDto>> DescribeAsync(List<RelationAddress> addresses)
    {
        if (addresses.Count == 0)
        {
            return [];
        }

        var relationIds = addresses.Select(a => a.RelationId).Distinct().ToList();
        var relations = (await _relations.GetListAsync(r => relationIds.Contains(r.Id))).ToDictionary(r => r.Id);
        var countryIds = addresses.Select(a => a.CountryId).Distinct().ToList();
        var countries = (await _countries.GetListAsync(c => countryIds.Contains(c.Id))).ToDictionary(c => c.Id);
        var relationRegions = (await LazyServiceProvider.LazyGetRequiredService<IRepository<RelationGeographicalRegion>>().GetListAsync(r => relationIds.Contains(r.RelationId)))
            .ToLookup(r => r.RelationId, r => r.GeographicalRegionId);

        /* The region match of design 41: one query per distinct (country, postcode) with the candidate ranges, matched in memory. */
        var ranges = LazyServiceProvider.LazyGetRequiredService<IRepository<GeographicalRegionPostalCodeRange, Guid>>();
        var matched = new Dictionary<(Guid CountryId, string PostalKey), List<Guid>>();
        foreach (var (countryId, postalKey) in addresses.Select(a => (a.CountryId, a.PostalCodeKey)).Distinct())
        {
            var leading = PostalCodeRangeMatcher.LeadingNumber(postalKey);
            var candidates = await ranges.GetListAsync(r => r.CountryId == countryId && (r.Prefix != null || (leading != null && r.FromNumeric <= leading && r.TillNumeric >= leading)));
            matched[(countryId, postalKey)] = candidates.Where(r => PostalCodeRangeMatcher.Matches(r, postalKey)).Select(r => r.GeographicalRegionId).Distinct().ToList();
        }

        return addresses
            .Where(a => relations.ContainsKey(a.RelationId))
            .Select(a =>
            {
                var relation = relations[a.RelationId];
                countries.TryGetValue(a.CountryId, out var country);
                return new MasterDataAddressReferenceDto
                {
                    Id = a.Id,
                    RelationId = a.RelationId,
                    RelationName = relation.Name,
                    AddressTypeId = a.AddressTypeId,
                    Street = a.Street,
                    HouseNumber = string.IsNullOrEmpty(a.HouseNumber) ? a.HouseNumberSuffix : a.HouseNumber + a.HouseNumberSuffix,
                    PostalCode = a.PostalCode,
                    City = a.City,
                    CountryCode = country?.IsoAlpha2 ?? string.Empty,
                    Latitude = a.Latitude,
                    Longitude = a.Longitude,
                    TimeZoneId = a.TimeZoneId ?? country?.PrimaryTimeZoneId,
                    GeographicalRegionIds = relationRegions[a.RelationId].Concat(matched[(a.CountryId, a.PostalCodeKey)]).Distinct().ToList()
                };
            })
            .ToList();
    }

    [Authorize]
    public virtual async Task<List<MasterDataReferenceDto>> GetReferencesAsync(MasterDataReferenceKind kind, IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        switch (kind)
        {
            case MasterDataReferenceKind.TransportUnit:
                return await ReadAsync<TransportUnit>(keys, u => new MasterDataReferenceDto { Id = u.Id, Name = u.Name, IsArchived = u.IsArchived });
            case MasterDataReferenceKind.Product:
                return await ReadAsync<Product>(keys, p => new MasterDataReferenceDto { Id = p.Id, Name = p.Name, IsArchived = p.IsArchived });
            case MasterDataReferenceKind.ProductRelationCode:
                return await ReadAsync<ProductRelationCode>(keys, c => new MasterDataReferenceDto { Id = c.Id, Name = c.Code, IsArchived = c.IsArchived });
            case MasterDataReferenceKind.Temperature:
                return await ReadAsync<Temperature>(keys, t => new MasterDataReferenceDto { Id = t.Id, Name = t.Name, IsArchived = t.IsArchived });
            case MasterDataReferenceKind.VehicleType:
                return await ReadAsync<VehicleType>(keys, v => new MasterDataReferenceDto { Id = v.Id, Name = v.Name });
            case MasterDataReferenceKind.TransportCharacteristic:
                return await ReadAsync<TransportCharacteristics>(keys, c => new MasterDataReferenceDto { Id = c.Id, Name = c.Name });
            case MasterDataReferenceKind.Vehicle:
                return await ReadAsync<Vehicle>(keys, v => new MasterDataReferenceDto { Id = v.Id, Name = v.LicencePlate == null ? v.Name : v.Name + " (" + v.LicencePlate + ")", IsArchived = v.IsArchived });
            case MasterDataReferenceKind.VehicleCombination:
                return await ReadAsync<VehicleCombination>(keys, c => new MasterDataReferenceDto { Id = c.Id, Name = c.Name, IsArchived = c.IsArchived });
            case MasterDataReferenceKind.Driver:
                return await ReadAsync<Driver>(keys, d => new MasterDataReferenceDto { Id = d.Id, Name = d.FullName, IsArchived = d.IsArchived });
            case MasterDataReferenceKind.ContactPerson:
                return await ReadAsync<RelationContactPerson>(keys, p => new MasterDataReferenceDto { Id = p.Id, Name = string.Join(" ", new[] { p.FirstName, p.MiddleName, p.LastName }.Where(part => !string.IsNullOrEmpty(part))) });
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown reference kind.");
        }
    }

    [Authorize]
    public virtual async Task<MasterDataReferenceDto?> GetOwnCompanyAsync()
    {
        var own = (await _relations.GetListAsync(r => (r.Roles & RelationRoles.OwnCompany) == RelationRoles.OwnCompany && !r.IsArchived)).FirstOrDefault();
        return own == null ? null : new MasterDataReferenceDto { Id = own.Id, Name = own.Name, IsArchived = own.IsArchived };
    }

    private async Task<List<MasterDataReferenceDto>> ReadAsync<TEntity>(List<Guid> keys, Func<TEntity, MasterDataReferenceDto> map)
        where TEntity : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var rows = await LazyServiceProvider.LazyGetRequiredService<IRepository<TEntity, Guid>>().GetListAsync(e => keys.Contains(e.Id));
        return rows.Select(map).ToList();
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
