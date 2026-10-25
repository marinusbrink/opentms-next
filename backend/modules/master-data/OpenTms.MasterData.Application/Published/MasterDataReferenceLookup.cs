using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataReferenceLookup"/>. In-process only (<c>RemoteService(IsEnabled = false)</c>); one
/// <c>IN</c> query per call (two for the fleet reads that join the type); the tenant filter scopes every read.
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
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<Vehicle, Guid> _vehicles;
    private readonly IRepository<VehicleType, Guid> _vehicleTypes;
    private readonly IRepository<VehicleCombination, Guid> _combinations;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IRepository<TemperatureIncompatibility> _temperaturePairs;
    private readonly IRepository<ProductIncompatibility> _productPairs;
    private readonly IRepository<TransportUnitIncompatibility> _unitPairs;

    public MasterDataReferenceLookup(
        IRepository<Relation, Guid> relations,
        IRepository<RelationCategory, Guid> categories,
        IRepository<GeographicalRegion, Guid> regions,
        IRepository<Country, Guid> countries,
        IRepository<Currency, Guid> currencies,
        IRepository<RelationAddress, Guid> addresses,
        IRepository<Vehicle, Guid> vehicles,
        IRepository<VehicleType, Guid> vehicleTypes,
        IRepository<VehicleCombination, Guid> combinations,
        IRepository<Driver, Guid> drivers,
        IRepository<TemperatureIncompatibility> temperaturePairs,
        IRepository<ProductIncompatibility> productPairs,
        IRepository<TransportUnitIncompatibility> unitPairs)
    {
        _relations = relations;
        _categories = categories;
        _regions = regions;
        _countries = countries;
        _currencies = currencies;
        _addresses = addresses;
        _vehicles = vehicles;
        _vehicleTypes = vehicleTypes;
        _combinations = combinations;
        _drivers = drivers;
        _temperaturePairs = temperaturePairs;
        _productPairs = productPairs;
        _unitPairs = unitPairs;
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
    public virtual async Task<List<MasterDataAddressReferenceDto>> GetRelationAddressesAsync(IReadOnlyCollection<Guid> addressIds)
    {
        var keys = Bound(addressIds);
        if (keys.Count == 0)
        {
            return [];
        }

        return await AddressesAsync(await _addresses.GetQueryableAsync(), a => keys.Contains(a.Id));
    }

    [Authorize]
    public virtual async Task<List<MasterDataAddressReferenceDto>> GetAddressesOfRelationsAsync(IReadOnlyCollection<Guid> relationIds)
    {
        var keys = Bound(relationIds);
        if (keys.Count == 0)
        {
            return [];
        }

        return await AddressesAsync(await _addresses.GetQueryableAsync(), a => keys.Contains(a.RelationId));
    }

    private async Task<List<MasterDataAddressReferenceDto>> AddressesAsync(IQueryable<RelationAddress> addresses, System.Linq.Expressions.Expression<Func<RelationAddress, bool>> filter)
    {
        var relations = await _relations.GetQueryableAsync();
        var countries = await _countries.GetQueryableAsync();
        var query = addresses.Where(filter)
            .Join(relations, a => a.RelationId, r => r.Id, (a, r) => new { Address = a, Relation = r })
            .GroupJoin(countries, x => x.Address.CountryId, c => c.Id, (x, cs) => new { x.Address, x.Relation, Countries = cs })
            .SelectMany(x => x.Countries.DefaultIfEmpty(), (x, c) => new MasterDataAddressReferenceDto
            {
                Id = x.Address.Id,
                RelationId = x.Address.RelationId,
                RelationName = x.Relation.Name,
                AddressTypeId = x.Address.AddressTypeId,
                Street = x.Address.Street,
                HouseNumber = x.Address.HouseNumber,
                PostalCode = x.Address.PostalCode,
                City = x.Address.City,
                CountryCode = c != null ? c.IsoAlpha2 : null,
                Latitude = x.Address.Latitude,
                Longitude = x.Address.Longitude,
                TimeZoneId = x.Address.TimeZoneId
            })
            .OrderBy(a => a.RelationId).ThenBy(a => a.Id);
        return await AsyncExecuter.ToListAsync(query);
    }

    [Authorize]
    public virtual async Task<List<MasterDataVehicleReferenceDto>> GetVehiclesAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var vehicles = await AsyncExecuter.ToListAsync((await _vehicles.WithDetailsAsync())
            .Where(v => keys.Contains(v.Id)));
        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var types = await AsyncExecuter.ToListAsync((await _vehicleTypes.WithDetailsAsync())
            .Where(t => typeIds.Contains(t.Id)));
        var typeById = types.ToDictionary(t => t.Id);

        return vehicles.Select(v =>
        {
            var type = typeById.GetValueOrDefault(v.VehicleTypeId);
            return new MasterDataVehicleReferenceDto
            {
                Id = v.Id,
                Name = v.Name,
                LicencePlate = v.LicencePlate,
                VehicleTypeId = v.VehicleTypeId,
                VehicleTypeName = type?.Name ?? string.Empty,
                Kind = type?.Kind ?? VehicleTypeKind.Rigid,
                GrossVehicleWeightKg = v.GrossVehicleWeightKg ?? type?.DefaultGrossVehicleWeightKg,
                EuroClass = v.EuroClass,
                FuelType = v.FuelType,
                LengthCm = v.LengthCm ?? type?.DefaultLengthCm,
                WidthCm = v.WidthCm ?? type?.DefaultWidthCm,
                HeightCm = v.HeightCm ?? type?.DefaultHeightCm,
                MaxEpq = v.MaxEpq ?? type?.MaxEpq,
                MaxLoadingMeters = v.MaxLoadingMeters ?? type?.MaxLoadingMeters,
                MaxPayloadKg = v.MaxPayloadKg,
                TransportCharacteristicIds = v.TransportCharacteristics.Select(c => c.TransportCharacteristicsId)
                    .Concat(type?.Characteristics.Select(c => c.TransportCharacteristicsId) ?? [])
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList(),
                IsArchived = v.IsArchived
            };
        }).OrderBy(v => v.Name).ToList();
    }

    [Authorize]
    public virtual async Task<List<MasterDataVehicleCombinationReferenceDto>> GetVehicleCombinationsAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var combinations = await AsyncExecuter.ToListAsync((await _combinations.WithDetailsAsync())
            .Where(c => keys.Contains(c.Id)));
        return combinations.Select(c => new MasterDataVehicleCombinationReferenceDto
        {
            Id = c.Id,
            Name = c.Name,
            VehicleIds = c.Members.Where(m => m.IsActive).OrderBy(m => m.Position).Select(m => m.VehicleId).ToList(),
            IsArchived = c.IsArchived
        }).ToList();
    }

    [Authorize]
    public virtual async Task<List<MasterDataReferenceDto>> GetDriversAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = Bound(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var drivers = await _drivers.GetListAsync(d => keys.Contains(d.Id));
        return drivers.Select(d => new MasterDataReferenceDto { Id = d.Id, Name = d.FullName, IsArchived = d.IsArchived }).ToList();
    }

    [Authorize]
    public virtual async Task<MasterDataIncompatibilitiesDto> GetIncompatibilitiesAsync(IReadOnlyCollection<Guid> temperatureIds, IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> transportUnitIds)
    {
        return new MasterDataIncompatibilitiesDto
        {
            Temperatures = await PairsAsync(_temperaturePairs, Bound(temperatureIds)),
            Products = await PairsAsync(_productPairs, Bound(productIds)),
            TransportUnits = await PairsAsync(_unitPairs, Bound(transportUnitIds))
        };
    }

    private async Task<List<MasterDataIncompatiblePairDto>> PairsAsync<TPair>(IRepository<TPair> pairs, List<Guid> keys)
        where TPair : Incompatibility
    {
        if (keys.Count < 2)
        {
            return [];
        }

        var query = (await pairs.GetQueryableAsync())
            .Where(p => keys.Contains(p.LeftId) && keys.Contains(p.RightId))
            .Select(p => new MasterDataIncompatiblePairDto { LeftId = p.LeftId, RightId = p.RightId });
        return await AsyncExecuter.ToListAsync(query);
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
