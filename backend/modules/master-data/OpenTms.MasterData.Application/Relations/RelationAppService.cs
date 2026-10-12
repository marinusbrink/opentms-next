using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.Localization;

namespace OpenTms.MasterData.Relations;

[RequiresFeature(MasterDataFeatures.Relations)]
public class RelationAppService
    : MasterDataCrudAppServiceBase<Relation, RelationRowDto, RelationDto, RelationCreateDto, RelationUpdateDto, RelationListRequestDto>,
      IRelationAppService
{
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<RelationCategoryMembership> _memberships;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IRepository<RelationCategory, Guid> _categories;
    private readonly IRepository<GeographicalRegion, Guid> _regions;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IOrganizationUnitRepository _organizationUnits;
    private readonly IRelationMatcher _matcher;
    private readonly IReadOnlyCollection<string> _cultures;

    /* The visiting address type ids of the tenant, loaded once per request for the city column. */
    private List<Guid>? _visitingTypeIds;

    public RelationAppService(
        IRepository<Relation, Guid> repository,
        IRepository<RelationAddress, Guid> addresses,
        IRepository<RelationCategoryMembership> memberships,
        IRepository<Country, Guid> countries,
        IRepository<AddressType, Guid> addressTypes,
        IRepository<RelationCategory, Guid> categories,
        IRepository<GeographicalRegion, Guid> regions,
        IRepository<Driver, Guid> drivers,
        IOrganizationUnitRepository organizationUnits,
        IRelationMatcher matcher,
        IOptions<AbpLocalizationOptions> localization)
        : base(repository)
    {
        _addresses = addresses;
        _memberships = memberships;
        _countries = countries;
        _addressTypes = addressTypes;
        _categories = categories;
        _regions = regions;
        _drivers = drivers;
        _organizationUnits = organizationUnits;
        _matcher = matcher;
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
    }

    [Authorize(MasterDataPermissions.Relations.Default)]
    public async Task<GridResponse<RelationRowDto>> GetListAsync(RelationListRequestDto input)
    {
        await EnsureVisitingTypesAsync();
        return await GetListInternalAsync(input);
    }

    [Authorize(MasterDataPermissions.Relations.Default)]
    public Task<RelationDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Relations.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.Relations.Default)]
    public async Task<RelationLookupPageDto> GetLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize, RelationRoles? roles = null)
    {
        var all = await Repository.GetQueryableAsync();
        var query = all.Where(r => !r.IsArchived);
        if (roles is { } wanted && wanted != RelationRoles.None)
        {
            query = query.Where(r => (r.Roles & wanted) != 0);
        }

        var key = MasterDataValues.SearchKey(term);
        if (key.Length > 0)
        {
            query = query.Where(r => r.SearchKey.Contains(key));
        }

        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var page = await AsyncExecuter.ToListAsync(query.OrderBy(r => r.Name).ThenBy(r => r.Code).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MasterDataConsts.LookupPageSize)));
        var visiting = await VisitingAddressesAsync(page.Select(r => r.Id).ToList());

        return new RelationLookupPageDto
        {
            TotalCount = totalCount,
            Items = page.Select(r => new RelationLookupDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                Roles = r.Roles,
                City = visiting.GetValueOrDefault(r.Id)?.City,
                CountryCode = visiting.GetValueOrDefault(r.Id)?.CountryCode
            }).ToList()
        };
    }

    [Authorize(MasterDataPermissions.Relations.Default)]
    public async Task<List<RelationAddressDto>> GetAddressesAsync(Guid id)
    {
        var relation = await Repository.GetAsync(id, includeDetails: true);
        return (await MapAddressesAsync(relation.Addresses)).ToList();
    }

    [Authorize(MasterDataPermissions.Relations.Create)]
    public Task<RelationDto> CreateAsync(RelationCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.Relations.Update)]
    public Task<RelationDto> UpdateAsync(Guid id, RelationUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.Relations.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.Relations.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(RelationBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.Roles, input.IncludeArchived, input.CategoryId, input.OrganizationUnitId));

    [Authorize(MasterDataPermissions.Relations.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var relation = await Repository.GetAsync(id);
        relation.Archive();
        await Repository.UpdateAsync(relation, autoSave: true);
    }

    [Authorize(MasterDataPermissions.Relations.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var relation = await Repository.GetAsync(id);
        relation.Unarchive();
        await Repository.UpdateAsync(relation, autoSave: true);
    }

    [Authorize(MasterDataPermissions.Relations.Default)]
    public Task<RelationMatchResultDto> MatchAsync(RelationMatchRequestDto input) => _matcher.MatchAsync(input);

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<Relation> ApplyFilters(IQueryable<Relation> query, RelationListRequestDto request)
        => ApplyFilters(query, request.Roles, request.IncludeArchived, request.CategoryId, request.OrganizationUnitId);

    private static IQueryable<Relation> ApplyFilters(IQueryable<Relation> query, RelationRoles? roles, bool includeArchived, Guid? categoryId, Guid? organizationUnitId)
    {
        if (!includeArchived)
        {
            query = query.Where(r => !r.IsArchived);
        }

        if (roles is { } wanted && wanted != RelationRoles.None)
        {
            query = query.Where(r => (r.Roles & wanted) != 0);
        }

        if (categoryId != null)
        {
            query = query.Where(r => r.Categories.Any(c => c.RelationCategoryId == categoryId.Value));
        }

        if (organizationUnitId != null)
        {
            query = query.Where(r => r.OrganizationUnits.Any(o => o.OrganizationUnitId == organizationUnitId.Value));
        }

        return query;
    }

    protected override IQueryable<Relation> ApplySearch(IQueryable<Relation> query, string term)
    {
        var key = MasterDataValues.SearchKey(term);
        var legal = MasterDataValues.NormalizeLegalNumber(term) ?? string.Empty;
        return query.Where(r =>
            r.SearchKey.Contains(key) ||
            (r.VatNumber != null && r.VatNumber.Contains(legal)) ||
            (r.ChamberOfCommerceNumber != null && r.ChamberOfCommerceNumber.Contains(legal)) ||
            r.Addresses.Any(a => a.City.ToLower().Contains(term)));
    }

    protected override IOrderedQueryable<Relation> ApplySort(IQueryable<Relation> query, string? sortBy, bool descending)
    {
        var visiting = _visitingTypeIds ?? [];
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, r => r.Name, descending),
            "code" => Order(query, r => r.Code, descending),
            "vatnumber" => Order(query, r => r.VatNumber, descending),
            "chamberofcommercenumber" => Order(query, r => r.ChamberOfCommerceNumber, descending),
            "isarchived" => Order(query, r => r.IsArchived, descending),
            "isblocked" => Order(query, r => r.IsBlocked, descending),
            "email" => Order(query, r => r.Email, descending),
            "phone" => Order(query, r => r.Phone, descending),
            "city" => Order(query, r => r.Addresses.Where(a => a.AddressTypeId == null || visiting.Contains(a.AddressTypeId.Value)).OrderBy(a => a.AddressTypeId == null ? 1 : 0).Select(a => a.City).FirstOrDefault(), descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<Relation> ApplyLookupFilter(IQueryable<Relation> query) => query.Where(r => !r.IsArchived);

    protected override async Task<List<RelationRowDto>> ToRowsAsync(List<Relation> entities)
    {
        var ids = entities.Select(r => r.Id).ToList();
        var visiting = await VisitingAddressesAsync(ids);
        var categoryNames = await CategoryNamesAsync(ids);

        return entities.Select(r => new RelationRowDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Roles = r.Roles,
            IsArchived = r.IsArchived,
            IsBlocked = r.IsBlocked,
            BlockReason = r.BlockReason,
            VatNumber = r.VatNumber,
            ChamberOfCommerceNumber = r.ChamberOfCommerceNumber,
            Phone = r.Phone,
            Email = r.Email,
            City = visiting.GetValueOrDefault(r.Id)?.City,
            CountryCode = visiting.GetValueOrDefault(r.Id)?.CountryCode,
            CategoryNames = categoryNames.GetValueOrDefault(r.Id) ?? []
        }).ToList();
    }

    protected override async Task<RelationDto> ToDtoAsync(Relation r)
    {
        var dto = new RelationDto
        {
            Code = r.Code,
            Name = r.Name,
            Roles = r.Roles,
            IsArchived = r.IsArchived,
            IsBlocked = r.IsBlocked,
            BlockReason = r.BlockReason,
            Phone = r.Phone,
            Mobile = r.Mobile,
            Email = r.Email,
            Website = r.Website,
            ChamberOfCommerceNumber = r.ChamberOfCommerceNumber,
            VatNumber = r.VatNumber,
            PreferredCulture = r.PreferredCulture,
            Addresses = (await MapAddressesAsync(r.Addresses)).ToList(),
            ContactPersons = r.ContactPersons.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Select(c => new RelationContactPersonDto
            {
                Id = c.Id,
                FirstName = c.FirstName,
                MiddleName = c.MiddleName,
                LastName = c.LastName,
                Role = c.Role,
                JobTitle = c.JobTitle,
                Department = c.Department,
                Email = c.Email,
                Phone = c.Phone,
                Mobile = c.Mobile
            }).ToList(),
            BankAccounts = r.BankAccounts.OrderByDescending(b => b.IsDefault).ThenBy(b => b.Iban).Select(b => new RelationBankAccountDto
            {
                Id = b.Id,
                Iban = b.Iban,
                Bic = b.Bic,
                BankName = b.BankName,
                AccountHolder = b.AccountHolder,
                IsDefault = b.IsDefault
            }).ToList(),
            CategoryIds = r.Categories.Select(c => c.RelationCategoryId).ToList(),
            GeographicalRegionIds = r.GeographicalRegions.Select(g => g.GeographicalRegionId).ToList(),
            OrganizationUnitIds = r.OrganizationUnits.Select(o => o.OrganizationUnitId).ToList()
        };
        MapAudit(r, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Relation r) => new() { Id = r.Id, Name = r.Name, Code = r.Code };

    protected override string DisplayNameOf(Relation r) => r.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<Relation> CreateEntityAsync(RelationCreateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        await EnsureUniqueAsync(r => r.Code == code, "code", code);

        var relation = new Relation(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, input.Roles);
        await ApplyAsync(relation, input);
        return relation;
    }

    protected override async Task ApplyUpdateAsync(Relation relation, RelationUpdateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        if (code != relation.Code)
        {
            await EnsureUniqueAsync(r => r.Code == code && r.Id != relation.Id, "code", code);
            relation.SetCode(code);
        }

        relation.SetName(input.Name);
        relation.SetRoles(input.Roles);
        await ApplyAsync(relation, input);
    }

    private async Task ApplyAsync(Relation relation, RelationCreateDto input)
    {
        if (relation.HasRole(RelationRoles.OwnCompany)
            && await Repository.AnyAsync(r => (r.Roles & RelationRoles.OwnCompany) != 0 && r.Id != relation.Id))
        {
            throw new BusinessException(MasterDataErrorCodes.OwnCompanyAlreadyExists);
        }

        relation.SetLegal(input.ChamberOfCommerceNumber, input.VatNumber);
        if (relation.VatNumber != null)
        {
            var vat = relation.VatNumber;
            await EnsureUniqueAsync(r => r.VatNumber == vat && r.Id != relation.Id, "vatNumber", vat);
        }

        if (relation.ChamberOfCommerceNumber != null)
        {
            var coc = relation.ChamberOfCommerceNumber;
            await EnsureUniqueAsync(r => r.ChamberOfCommerceNumber == coc && r.Id != relation.Id, "chamberOfCommerceNumber", coc);
        }

        relation.SetContact(input.Phone, input.Mobile, input.Email, input.Website);
        relation.SetPreferredCulture(input.PreferredCulture, _cultures);
        relation.SetDescription(input.Description);
        relation.SetBlocked(input.IsBlocked, input.BlockReason);
        if (input.IsArchived)
        {
            relation.Archive();
        }
        else
        {
            relation.Unarchive();
        }

        /* Referenced rows of this tenant only (constitution rule 1: the filtered repositories see one tenant). */
        var countryIds = input.Addresses.Select(a => a.CountryId).Distinct().ToList();
        var countries = countryIds.Count == 0 ? [] : await _countries.GetListAsync(c => countryIds.Contains(c.Id));
        if (countries.Count != countryIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "addresses.countryId");
        }

        var typeIds = input.Addresses.Where(a => a.AddressTypeId != null).Select(a => a.AddressTypeId!.Value).Distinct().ToList();
        if (typeIds.Count > 0 && await _addressTypes.CountAsync(t => typeIds.Contains(t.Id)) != typeIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "addresses.addressTypeId");
        }

        var categoryIds = input.CategoryIds.Distinct().ToList();
        if (categoryIds.Count > 0 && await _categories.CountAsync(c => categoryIds.Contains(c.Id)) != categoryIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "categoryIds");
        }

        var regionIds = input.GeographicalRegionIds.Distinct().ToList();
        if (regionIds.Count > 0 && await _regions.CountAsync(g => regionIds.Contains(g.Id)) != regionIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "geographicalRegionIds");
        }

        var unitIds = input.OrganizationUnitIds.Distinct().ToList();
        if (unitIds.Count > 0 && (await _organizationUnits.GetListAsync(unitIds)).Count != unitIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "organizationUnitIds");
        }

        relation.SetAddresses(
            input.Addresses.Select(a => new RelationAddressInput(a.Id, a.AddressTypeId, a.CountryId, a.Street, a.HouseNumber, a.AddressLine2, a.PostalCode, a.City, a.Latitude, a.Longitude, a.TimeZoneId)),
            () => GuidGenerator.Create());
        var zones = countries.ToDictionary(c => c.Id, c => c.PrimaryTimeZoneId);
        foreach (var address in relation.Addresses)
        {
            address.DefaultTimeZone(zones.GetValueOrDefault(address.CountryId));
        }

        relation.SetContactPersons(
            input.ContactPersons.Select(c => new RelationContactPersonInput(c.Id, c.FirstName, c.MiddleName, c.LastName, c.Role, c.JobTitle, c.Department, c.Email, c.Phone, c.Mobile)),
            () => GuidGenerator.Create());
        relation.SetBankAccounts(
            input.BankAccounts.Select(b => new RelationBankAccountInput(b.Id, b.Iban, b.Bic, b.BankName, b.AccountHolder, b.IsDefault)),
            () => GuidGenerator.Create());
        relation.SetCategories(categoryIds);
        relation.SetGeographicalRegions(regionIds);
        relation.SetOrganizationUnits(unitIds);
    }

    protected override async Task<string?> GetDeleteBlockerAsync(Relation relation)
    {
        if (await _drivers.AnyAsync(d => d.EmployerRelationId == relation.Id))
        {
            return L["MasterData:InUseByDrivers"];
        }

        return null;
    }

    /// <summary>Children and join rows stay behind on a soft delete; clear them so nothing points at a deleted relation.</summary>
    protected override Task BeforeDeleteAsync(Relation relation)
    {
        relation.SetAddresses([], () => GuidGenerator.Create());
        relation.SetContactPersons([], () => GuidGenerator.Create());
        relation.SetBankAccounts([], () => GuidGenerator.Create());
        relation.SetCategories([]);
        relation.SetGeographicalRegions([]);
        relation.SetOrganizationUnits([]);
        return Task.CompletedTask;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task EnsureVisitingTypesAsync()
    {
        _visitingTypeIds ??= (await _addressTypes.GetListAsync(t => t.Kind == AddressTypeKind.Visiting)).Select(t => t.Id).ToList();
    }

    private sealed record CityAndCountry(string City, string? CountryCode);

    /// <summary>The visiting address (by kind) of each relation, else its default address, else its first address, as city and country code.</summary>
    private async Task<Dictionary<Guid, CityAndCountry>> VisitingAddressesAsync(List<Guid> relationIds)
    {
        if (relationIds.Count == 0)
        {
            return [];
        }

        await EnsureVisitingTypesAsync();
        var visiting = _visitingTypeIds!;
        var addresses = await _addresses.GetListAsync(a => relationIds.Contains(a.RelationId));
        var countryIds = addresses.Select(a => a.CountryId).Distinct().ToList();
        var codes = countryIds.Count == 0 ? new Dictionary<Guid, string>() : (await _countries.GetListAsync(c => countryIds.Contains(c.Id))).ToDictionary(c => c.Id, c => c.IsoAlpha2);

        return addresses
            .GroupBy(a => a.RelationId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var chosen = g.FirstOrDefault(a => a.AddressTypeId != null && visiting.Contains(a.AddressTypeId.Value))
                        ?? g.FirstOrDefault(a => a.AddressTypeId == null)
                        ?? g.OrderBy(a => a.City).First();
                    return new CityAndCountry(chosen.City, codes.GetValueOrDefault(chosen.CountryId));
                });
    }

    private async Task<Dictionary<Guid, List<string>>> CategoryNamesAsync(List<Guid> relationIds)
    {
        if (relationIds.Count == 0)
        {
            return [];
        }

        var memberships = await _memberships.GetListAsync(m => relationIds.Contains(m.RelationId));
        var categoryIds = memberships.Select(m => m.RelationCategoryId).Distinct().ToList();
        var names = categoryIds.Count == 0 ? new Dictionary<Guid, string>() : (await _categories.GetListAsync(c => categoryIds.Contains(c.Id))).ToDictionary(c => c.Id, c => c.Name);
        return memberships
            .GroupBy(m => m.RelationId)
            .ToDictionary(g => g.Key, g => g.Select(m => names.GetValueOrDefault(m.RelationCategoryId)).Where(n => n != null).Select(n => n!).OrderBy(n => n).ToList());
    }

    private async Task<IEnumerable<RelationAddressDto>> MapAddressesAsync(ICollection<RelationAddress> addresses)
    {
        var countryIds = addresses.Select(a => a.CountryId).Distinct().ToList();
        var typeIds = addresses.Where(a => a.AddressTypeId != null).Select(a => a.AddressTypeId!.Value).Distinct().ToList();
        var codes = countryIds.Count == 0 ? new Dictionary<Guid, string>() : (await _countries.GetListAsync(c => countryIds.Contains(c.Id))).ToDictionary(c => c.Id, c => c.IsoAlpha2);
        var types = typeIds.Count == 0 ? new Dictionary<Guid, string>() : (await _addressTypes.GetListAsync(t => typeIds.Contains(t.Id))).ToDictionary(t => t.Id, t => Resolve(t.Name));

        return addresses
            .OrderBy(a => a.AddressTypeId == null ? 0 : 1)
            .ThenBy(a => a.AddressTypeId == null ? null : types.GetValueOrDefault(a.AddressTypeId.Value))
            .Select(a => new RelationAddressDto
            {
                Id = a.Id,
                AddressTypeId = a.AddressTypeId,
                AddressTypeName = a.AddressTypeId == null ? null : types.GetValueOrDefault(a.AddressTypeId.Value),
                CountryId = a.CountryId,
                CountryCode = codes.GetValueOrDefault(a.CountryId),
                Street = a.Street,
                HouseNumber = a.HouseNumber,
                AddressLine2 = a.AddressLine2,
                PostalCode = a.PostalCode,
                City = a.City,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                TimeZoneId = a.TimeZoneId
            });
    }
}
