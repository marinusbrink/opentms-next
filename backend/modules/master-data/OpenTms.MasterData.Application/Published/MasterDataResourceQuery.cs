using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataResourceQuery"/> (design 69). In-process only (<c>RemoteService(IsEnabled = false)</c>).
/// Each page is one SQL projection plus one count, served by the sort indexes <c>Vehicles (TenantId, NormalizedName)</c>
/// and <c>Drivers (TenantId, Code)</c>; the tenant and soft-delete filters scope every read.
/// </summary>
// Published in-process reader: called by Planning's HTTP endpoints (which authorize) and possibly by jobs without a user, so it is anonymous.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class MasterDataResourceQuery : MasterDataAppServiceBase, IMasterDataResourceQuery
{
    private readonly IRepository<Vehicle, Guid> _vehicles;
    private readonly IRepository<VehicleType, Guid> _vehicleTypes;
    private readonly IRepository<Driver, Guid> _drivers;

    public MasterDataResourceQuery(IRepository<Vehicle, Guid> vehicles, IRepository<VehicleType, Guid> vehicleTypes, IRepository<Driver, Guid> drivers)
    {
        _vehicles = vehicles;
        _vehicleTypes = vehicleTypes;
        _drivers = drivers;
    }

    public virtual async Task<MasterDataResourcePageDto<MasterDataVehicleResourceDto>> GetVehiclesAsync(MasterDataVehicleResourceQuery query)
    {
        Check.NotNull(query, nameof(query));
        var kinds = (query.Kinds ?? []).Distinct().ToList();
        if (kinds.Count == 0)
        {
            throw new ArgumentException("At least one vehicle type kind is required.", nameof(query));
        }

        var ids = BoundIds(query.Ids);
        var organizationUnitIds = (query.OrganizationUnitIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

        var vehicles = await _vehicles.GetQueryableAsync();
        var types = await _vehicleTypes.GetQueryableAsync();
        var filtered = from v in vehicles
                       join t in types on v.VehicleTypeId equals t.Id
                       where kinds.Contains(t.Kind)
                       select new { Vehicle = v, Type = t };

        if (!query.IncludeArchived)
        {
            filtered = filtered.Where(x => !x.Vehicle.IsArchived);
        }

        if (ids.Count > 0)
        {
            filtered = filtered.Where(x => ids.Contains(x.Vehicle.Id));
        }

        if (organizationUnitIds.Count > 0)
        {
            filtered = filtered.Where(x => x.Vehicle.OrganizationUnits.Any(o => organizationUnitIds.Contains(o.OrganizationUnitId)));
        }

        var term = Term(query.Search);
        if (term != null)
        {
            var plate = VehicleRegistration.SearchKey(term);
            var hasPlate = plate.Length > 0;
            filtered = filtered.Where(x =>
                x.Vehicle.Name.ToLower().Contains(term) ||
                (hasPlate && x.Vehicle.LicencePlate != null && x.Vehicle.LicencePlate.Contains(plate)));
        }

        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var items = await AsyncExecuter.ToListAsync(filtered
            .OrderBy(x => x.Vehicle.NormalizedName)
            .ThenBy(x => x.Vehicle.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Take(query.Take))
            .Select(x => new MasterDataVehicleResourceDto
            {
                Id = x.Vehicle.Id,
                Name = x.Vehicle.Name,
                LicencePlate = x.Vehicle.LicencePlate,
                Kind = x.Type.Kind,
                VehicleTypeName = x.Type.Name,
                HomeDepotRelationId = x.Vehicle.HomeDepotRelationId,
                OwnerRelationId = x.Vehicle.OwnerRelationId,
                DefaultDriverIds = x.Vehicle.DefaultDrivers.OrderBy(d => d.SortOrder).Select(d => d.DriverId).ToList(),
                OrganizationUnitIds = x.Vehicle.OrganizationUnits.Select(o => o.OrganizationUnitId).ToList(),
                IsArchived = x.Vehicle.IsArchived
            }));

        return new MasterDataResourcePageDto<MasterDataVehicleResourceDto> { Items = items, TotalCount = totalCount };
    }

    public virtual async Task<MasterDataResourcePageDto<MasterDataDriverResourceDto>> GetDriversAsync(MasterDataDriverResourceQuery query)
    {
        Check.NotNull(query, nameof(query));
        var ids = BoundIds(query.Ids);

        var filtered = await _drivers.GetQueryableAsync();
        if (!query.IncludeArchived)
        {
            filtered = filtered.Where(d => !d.IsArchived);
        }

        if (ids.Count > 0)
        {
            filtered = filtered.Where(d => ids.Contains(d.Id));
        }

        var term = Term(query.Search);
        if (term != null)
        {
            /* The search key holds the folded first, middle and last name and the code. */
            var key = MasterDataValues.SearchKey(term);
            filtered = filtered.Where(d => d.SearchKey.Contains(key));
        }

        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var rows = await AsyncExecuter.ToListAsync(filtered
            .OrderBy(d => d.Code)
            .ThenBy(d => d.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Take(query.Take))
            .Select(d => new { d.Id, d.Code, d.FirstName, d.MiddleName, d.LastName, d.EmployerRelationId, d.IsArchived }));

        var items = rows.Select(d => new MasterDataDriverResourceDto
        {
            Id = d.Id,
            Code = d.Code,
            Name = string.Join(" ", new[] { d.FirstName, d.MiddleName, d.LastName }.Where(p => !string.IsNullOrEmpty(p))),
            EmployerRelationId = d.EmployerRelationId,
            IsArchived = d.IsArchived
        }).ToList();

        return new MasterDataResourcePageDto<MasterDataDriverResourceDto> { Items = items, TotalCount = totalCount };
    }

    private static int Take(int take) => Math.Clamp(take, 1, IMasterDataResourceQuery.MaxTake);

    private static string? Term(string? search)
    {
        var value = search?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static List<Guid> BoundIds(IReadOnlyCollection<Guid>? ids)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        return keys.Count > IMasterDataResourceQuery.MaxTake
            ? throw new BusinessException(MasterDataErrorCodes.TooManyRows).WithData("max", IMasterDataResourceQuery.MaxTake)
            : keys;
    }
}
