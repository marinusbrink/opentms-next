using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using OpenTms.Platform.Features;

namespace OpenTms.MasterData.Relations;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class RelationAppService
    : MasterDataCrudAppServiceBase<Relation, RelationRowDto, RelationDto, RelationCreateDto, RelationUpdateDto, RelationListRequestDto>,
      IRelationAppService
{
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<RelationCategoryMembership> _memberships;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IRepository<RelationCategory, Guid> _categories;
    private readonly IRepository<Currency, Guid> _currencies;
    private readonly IRepository<TransportCharacteristics, Guid> _characteristics;
    private readonly IRepository<RelationExternalIdentifier, Guid> _externalIds;
    private readonly IRepository<GeographicalRegion, Guid> _regions;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IRepository<Vehicle, Guid> _vehicles;
    private readonly IRepository<Transport.ProductRelationCode, Guid> _productRelationCodes;
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
        IRepository<Currency, Guid> currencies,
        IRepository<TransportCharacteristics, Guid> characteristics,
        IRepository<RelationExternalIdentifier, Guid> externalIds,
        IRepository<GeographicalRegion, Guid> regions,
        IRepository<Driver, Guid> drivers,
        IRepository<Vehicle, Guid> vehicles,
        IRepository<Transport.ProductRelationCode, Guid> productRelationCodes,
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
        _currencies = currencies;
        _characteristics = characteristics;
        _externalIds = externalIds;
        _regions = regions;
        _drivers = drivers;
        _vehicles = vehicles;
        _productRelationCodes = productRelationCodes;
        _organizationUnits = organizationUnits;
        _matcher = matcher;
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Default)]
    public async Task<GridResponse<RelationRowDto>> GetListAsync(RelationListRequestDto input)
    {
        await EnsureVisitingTypesAsync();
        return await GetListInternalAsync(input);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
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

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Create)]
    public Task<RelationDto> CreateAsync(RelationCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Update)]
    public Task<RelationDto> UpdateAsync(Guid id, RelationUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(RelationBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.Roles, input.IncludeArchived, input.CategoryId, input.OrganizationUnitId));

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var relation = await Repository.GetAsync(id);
        relation.Archive();
        await Repository.UpdateAsync(relation, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Relations.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var relation = await Repository.GetAsync(id);
        relation.Unarchive();
        await Repository.UpdateAsync(relation, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Relations.Default, AppFeatures.MasterData.General.Relations.RelationsList, RequiresAll = true)]
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
            "fullname" => Order(query, r => r.FullName, descending),
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
            FullName = r.FullName,
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

    /// <summary>The relation's characteristics with code, name and description, in one query, ordered by name.</summary>
    private async Task<List<TransportCharacteristicsRowDto>> MapTransportCharacteristicsAsync(Relation r)
    {
        var ids = r.TransportCharacteristics.Select(c => c.TransportCharacteristicsId).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var characteristics = await _characteristics.GetListAsync(c => ids.Contains(c.Id));
        return characteristics.OrderBy(c => c.Name).ThenBy(c => c.Code)
            .Select(c => new TransportCharacteristicsRowDto { Id = c.Id, Code = c.Code, Name = c.Name, Description = c.Description }).ToList();
    }

    protected override async Task<RelationDto> ToDtoAsync(Relation r)
    {
        var dto = new RelationDto
        {
            Code = r.Code,
            Name = r.Name,
            FullName = r.FullName,
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
            DebtorNumber = r.DebtorNumber,
            CreditorNumber = r.CreditorNumber,
            CarrierCode = r.CarrierCode,
            CurrencyId = r.CurrencyId,
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
            OrganizationUnitIds = r.OrganizationUnits.Select(o => o.OrganizationUnitId).ToList(),
            ExternalIdentifiers = r.ExternalIdentifiers.OrderBy(e => e.Source).ThenBy(e => e.Identifier)
                .Select(e => new RelationExternalIdentifierDto { Id = e.Id, Source = e.Source, Identifier = e.Identifier }).ToList(),
            TransportCharacteristicIds = r.TransportCharacteristics.Select(c => c.TransportCharacteristicsId).ToList(),
            TransportCharacteristics = await MapTransportCharacteristicsAsync(r)
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

        /* Unarchive before the roles: an archived relation cannot take the OwnCompany role, but one that is being
         * unarchived in the same save can (PO 2026-10-16). Archiving happens in ApplyAsync, after the roles. */
        if (!input.IsArchived)
        {
            relation.Unarchive();
        }

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

        relation.SetAccountNumbers(input.DebtorNumber, input.CreditorNumber);
        if (relation.DebtorNumber != null)
        {
            var debtor = relation.DebtorNumber;
            await EnsureUniqueAsync(r => r.DebtorNumber == debtor && r.Id != relation.Id, "debtorNumber", debtor);
        }

        if (relation.CreditorNumber != null)
        {
            var creditor = relation.CreditorNumber;
            await EnsureUniqueAsync(r => r.CreditorNumber == creditor && r.Id != relation.Id, "creditorNumber", creditor);
        }

        relation.SetCarrierCode(input.CarrierCode);
        if (input.CurrencyId != null && !await _currencies.AnyAsync(c => c.Id == input.CurrencyId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "currencyId");
        }

        relation.SetCurrency(input.CurrencyId);

        relation.SetName(input.Name, input.FullName);
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

        var characteristicIds = input.TransportCharacteristicIds.Distinct().ToList();
        if (characteristicIds.Count > 0 && await _characteristics.CountAsync(c => characteristicIds.Contains(c.Id)) != characteristicIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "transportCharacteristicIds");
        }

        relation.SetTransportCharacteristics(characteristicIds);
        relation.SetExternalIdentifiers(input.ExternalIdentifiers.Select(e => new RelationExternalIdentifierInput(e.Id, e.Source, e.Identifier)), () => GuidGenerator.Create());

        /* An external id names one relation per tenant: the same (source, identifier) on another relation is a duplicate.
         * One query fetches the other relations' rows with any of these identifiers; the pairs are matched in memory. */
        if (relation.ExternalIdentifiers.Count > 0)
        {
            var identifiers = relation.ExternalIdentifiers.Select(e => e.Identifier).Distinct().ToList();
            var taken = (await _externalIds.GetListAsync(e => e.RelationId != relation.Id && identifiers.Contains(e.Identifier)))
                .Select(e => (e.Source, e.Identifier)).ToHashSet();
            var clash = relation.ExternalIdentifiers.FirstOrDefault(e => taken.Contains((e.Source, e.Identifier)));
            if (clash != null)
            {
                throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "externalIdentifiers").WithData("value", clash.Source + " " + clash.Identifier);
            }
        }

        relation.SetAddresses(
            input.Addresses.Select(a => new RelationAddressInput(a.Id, a.AddressTypeId, a.CountryId, a.Street, a.HouseNumber, a.HouseNumberSuffix, a.AddressLine2, a.PostalCode, a.City, a.Latitude, a.Longitude, a.TimeZoneId)),
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

        /* Design 48: the owner or home depot of a vehicle, the destination of a product relation code. */
        if (await _vehicles.AnyAsync(v => v.OwnerRelationId == relation.Id || v.HomeDepotRelationId == relation.Id))
        {
            return L["MasterData:InUseByVehicles"];
        }

        if (await _productRelationCodes.AnyAsync(c => c.RelationId == relation.Id))
        {
            return L["MasterData:InUseByProductRelationCodes"];
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
        relation.SetExternalIdentifiers([], () => GuidGenerator.Create());
        relation.SetTransportCharacteristics([]);
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
                HouseNumberSuffix = a.HouseNumberSuffix,
                AddressLine2 = a.AddressLine2,
                PostalCode = a.PostalCode,
                City = a.City,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                TimeZoneId = a.TimeZoneId
            });
    }
}
