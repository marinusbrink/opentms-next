using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using OpenTms.Platform.Features;

namespace OpenTms.MasterData.Regions;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class GeographicalRegionAppService
    : MasterDataCrudAppServiceBase<GeographicalRegion, GeographicalRegionRowDto, GeographicalRegionDto, GeographicalRegionCreateDto, GeographicalRegionUpdateDto, GeographicalRegionListRequestDto>,
      IGeographicalRegionAppService
{
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<GeographicalRegionPostalCodeRange, Guid> _ranges;

    public GeographicalRegionAppService(
        IRepository<GeographicalRegion, Guid> repository,
        IRepository<Country, Guid> countries,
        IRepository<GeographicalRegionPostalCodeRange, Guid> ranges)
        : base(repository)
    {
        _countries = countries;
        _ranges = ranges;
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Default)]
    public Task<GridResponse<GeographicalRegionRowDto>> GetListAsync(GeographicalRegionListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Default)]
    public Task<GeographicalRegionDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.GeographicalRegions.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Create)]
    public Task<GeographicalRegionDto> CreateAsync(GeographicalRegionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Update)]
    public Task<GeographicalRegionDto> UpdateAsync(Guid id, GeographicalRegionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Reference.Default, AppFeatures.MasterData.General.Reference.GeographicalRegions, RequiresAll = true)]
    [Authorize(MasterDataPermissions.GeographicalRegions.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(GeographicalRegionBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyCountryFilter(q, input.CountryId));

    /// <summary>The regions with a range matching the postcode in the country; the rule is PostalCodeRangeMatcher, evaluated in memory.</summary>
    [Authorize(MasterDataPermissions.GeographicalRegions.Default)]
    public async Task<List<MasterDataLookupDto>> GetMatchesAsync(Guid countryId, string postalCode)
    {
        var code = MasterDataCodes.NormalizePostalCode(postalCode);
        if (code.Length == 0 || code.Length > MasterDataConsts.MaxPostalCodeLength)
        {
            return [];
        }

        var ranges = await _ranges.GetListAsync(r => r.CountryId == countryId);
        var regionIds = ranges.Where(r => PostalCodeRangeMatcher.Matches(r, code)).Select(r => r.GeographicalRegionId).Distinct().ToList();
        if (regionIds.Count == 0)
        {
            return [];
        }

        var regions = await Repository.GetListAsync(r => regionIds.Contains(r.Id));
        return regions.Select(ToLookup).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    protected override IQueryable<GeographicalRegion> ApplyFilters(IQueryable<GeographicalRegion> query, GeographicalRegionListRequestDto request)
        => ApplyCountryFilter(query, request.CountryId);

    private static IQueryable<GeographicalRegion> ApplyCountryFilter(IQueryable<GeographicalRegion> query, Guid? countryId)
        => countryId == null ? query : query.Where(r => r.PostalCodeRanges.Any(p => p.CountryId == countryId.Value));

    protected override IQueryable<GeographicalRegion> ApplySearch(IQueryable<GeographicalRegion> query, string term)
        => query.Where(r => r.Name.ToLower().Contains(term) || (r.Description != null && r.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<GeographicalRegion> ApplySort(IQueryable<GeographicalRegion> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, r => r.Name, descending),
            "description" => Order(query, r => r.Description, descending),
            "rangecount" => Order(query, r => r.PostalCodeRanges.Count, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override async Task<List<GeographicalRegionRowDto>> ToRowsAsync(List<GeographicalRegion> entities)
    {
        /* One grouped COUNT in SQL; loading the page's ranges as entities to count them cost 5,000 rows a page. */
        var ids = entities.Select(r => r.Id).ToList();
        var counts = new Dictionary<Guid, int>();
        if (ids.Count > 0)
        {
            var ranges = await _ranges.GetQueryableAsync();
            var grouped = await AsyncExecuter.ToListAsync(ranges
                .Where(p => ids.Contains(p.GeographicalRegionId))
                .GroupBy(p => p.GeographicalRegionId)
                .Select(g => new { RegionId = g.Key, Count = g.Count() }));
            counts = grouped.ToDictionary(g => g.RegionId, g => g.Count);
        }

        return entities.Select(r => new GeographicalRegionRowDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            RangeCount = counts.GetValueOrDefault(r.Id)
        }).ToList();
    }

    protected override async Task<GeographicalRegionDto> ToDtoAsync(GeographicalRegion r)
    {
        var countryIds = r.PostalCodeRanges.Select(p => p.CountryId).Distinct().ToList();
        var countries = countryIds.Count == 0 ? [] : await _countries.GetListAsync(c => countryIds.Contains(c.Id));
        var codes = countries.ToDictionary(c => c.Id, c => c.IsoAlpha2);

        var dto = new GeographicalRegionDto
        {
            Name = r.Name,
            PostalCodeRanges = r.PostalCodeRanges
                .Select(p => new GeographicalRegionPostalCodeRangeDto
                {
                    Id = p.Id,
                    CountryId = p.CountryId,
                    CountryCode = codes.GetValueOrDefault(p.CountryId),
                    FromNumeric = p.FromNumeric,
                    TillNumeric = p.TillNumeric,
                    Prefix = p.Prefix
                })
                .OrderBy(p => p.CountryCode).ThenBy(p => p.FromNumeric).ThenBy(p => p.Prefix)
                .ToList()
        };
        MapAudit(r, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(GeographicalRegion r) => new() { Id = r.Id, Name = r.Name };

    protected override string DisplayNameOf(GeographicalRegion r) => r.Name;

    protected override async Task<GeographicalRegion> CreateEntityAsync(GeographicalRegionCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(r => r.Name == name, "name", name);
        var region = new GeographicalRegion(GuidGenerator.Create(), CurrentTenant.Id, name);
        await ApplyAsync(region, input);
        return region;
    }

    protected override async Task ApplyUpdateAsync(GeographicalRegion region, GeographicalRegionUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != region.Name)
        {
            await EnsureUniqueAsync(r => r.Name == name && r.Id != region.Id, "name", name);
            region.SetName(name);
        }

        await ApplyAsync(region, input);
    }

    private async Task ApplyAsync(GeographicalRegion region, GeographicalRegionCreateDto input)
    {
        var countryIds = input.PostalCodeRanges.Select(p => p.CountryId).Distinct().ToList();
        if (countryIds.Count > 0 && await _countries.CountAsync(c => countryIds.Contains(c.Id)) != countryIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "postalCodeRanges.countryId");
        }

        region.SetDescription(input.Description);
        region.SetPostalCodeRanges(
            input.PostalCodeRanges.Select(p => (p.Id, p.CountryId, p.FromNumeric, p.TillNumeric, p.Prefix)),
            () => GuidGenerator.Create());
    }

    /// <summary>Ranges stay behind on a soft delete and would block deleting their country; clear them.</summary>
    protected override Task BeforeDeleteAsync(GeographicalRegion region)
    {
        region.SetPostalCodeRanges([], () => GuidGenerator.Create());
        return Task.CompletedTask;
    }
}
