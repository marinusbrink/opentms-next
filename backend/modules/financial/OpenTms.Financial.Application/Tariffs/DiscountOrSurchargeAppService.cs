using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Tariffs;

/* Design 52. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class DiscountOrSurchargeAppService
    : ReferenceDataCrudAppServiceBase<DiscountOrSurcharge, DiscountOrSurchargeRowDto, DiscountOrSurchargeDto, DiscountOrSurchargeCreateDto, DiscountOrSurchargeUpdateDto, ArchivableListRequestDto>,
      IDiscountOrSurchargeAppService
{
    private readonly IRepository<DiscountOrSurchargeValue, Guid> _values;
    private readonly IRepository<DiscountOrSurchargeRegion, Guid> _regions;
    private readonly VatPercentageReferences _vat;
    private readonly MasterDataReferences _masterData;
    private readonly FinancialToday _today;

    public DiscountOrSurchargeAppService(
        IRepository<DiscountOrSurcharge, Guid> repository,
        IRepository<DiscountOrSurchargeValue, Guid> values,
        IRepository<DiscountOrSurchargeRegion, Guid> regions,
        VatPercentageReferences vat,
        MasterDataReferences masterData,
        FinancialToday today)
        : base(repository)
    {
        _values = values;
        _regions = regions;
        _vat = vat;
        _masterData = masterData;
        _today = today;
        LocalizationResource = typeof(FinancialResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Default)]
    public Task<GridResponse<DiscountOrSurchargeRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Default)]
    public Task<DiscountOrSurchargeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Create)]
    public Task<DiscountOrSurchargeDto> CreateAsync(DiscountOrSurchargeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Update)]
    public Task<DiscountOrSurchargeDto> UpdateAsync(Guid id, DiscountOrSurchargeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, RequiresAll = true)]
    [Authorize(FinancialPermissions.DiscountsOrSurcharges.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(d => !d.IsArchived));

    protected override IQueryable<DiscountOrSurcharge> ApplyFilters(IQueryable<DiscountOrSurcharge> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(d => !d.IsArchived);

    protected override IQueryable<DiscountOrSurcharge> ApplySearch(IQueryable<DiscountOrSurcharge> query, string term)
        => query.Where(d => d.Code.ToLower().Contains(term) || d.Name.ToLower().Contains(term));

    protected override IOrderedQueryable<DiscountOrSurcharge> ApplySort(IQueryable<DiscountOrSurcharge> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, d => d.Code, descending),
            "name" => Order(query, d => d.Name, descending),
            "direction" => Order(query, d => d.Direction, descending),
            "valuekind" => Order(query, d => d.ValueKind, descending),
            "side" => Order(query, d => d.Side, descending),
            "regioncount" => Order(query, d => d.Regions.Count, descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<DiscountOrSurcharge> ApplyLookupFilter(IQueryable<DiscountOrSurcharge> query) => query.Where(d => !d.IsArchived);

    protected override async Task<List<DiscountOrSurchargeRowDto>> ToRowsAsync(List<DiscountOrSurcharge> entities)
    {
        /* The value valid today and the region count for the whole page: one query each (Cost & SLO), not per row. */
        var today = _today.Get();
        var ids = entities.Select(d => d.Id).ToList();
        var values = ids.Count == 0
            ? []
            : await AsyncExecuter.ToListAsync((await _values.GetQueryableAsync()).Where(v => ids.Contains(v.DiscountOrSurchargeId) && v.ValidFrom <= today));
        var current = values.GroupBy(v => v.DiscountOrSurchargeId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.ValidFrom)!);
        var regionCounts = ids.Count == 0
            ? []
            : (await AsyncExecuter.ToListAsync((await _regions.GetQueryableAsync())
                    .Where(r => ids.Contains(r.DiscountOrSurchargeId))
                    .GroupBy(r => r.DiscountOrSurchargeId)
                    .Select(g => new { Id = g.Key, Count = g.Count() })))
                .ToDictionary(r => r.Id, r => r.Count);

        return entities.Select(d =>
        {
            current.TryGetValue(d.Id, out var value);
            return new DiscountOrSurchargeRowDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                Direction = d.Direction,
                ValueKind = d.ValueKind,
                Side = d.Side,
                CurrentValue = value?.Value,
                CurrentCurrencyCode = value?.CurrencyCode,
                RegionCount = regionCounts.GetValueOrDefault(d.Id),
                IsArchived = d.IsArchived,
                Description = d.Description
            };
        }).ToList();
    }

    protected override async Task<DiscountOrSurchargeDto> ToDtoAsync(DiscountOrSurcharge d)
    {
        var today = _today.Get();
        var vatNames = await _vat.GetNamesAsync([d.VatPercentageId]);
        var regionNames = await _masterData.GetRegionNamesAsync(d.Regions.Select(r => r.GeographicalRegionId).ToList());
        var dto = new DiscountOrSurchargeDto
        {
            Code = d.Code,
            Name = d.Name,
            Direction = d.Direction,
            ValueKind = d.ValueKind,
            Side = d.Side,
            DaysOfWeek = d.DaysOfWeek,
            VatPercentageId = d.VatPercentageId,
            VatPercentageName = d.VatPercentageId != null && vatNames.TryGetValue(d.VatPercentageId.Value, out var vatName) ? vatName : null,
            LedgerAccount = d.LedgerAccount,
            AppliesAfterOthers = d.AppliesAfterOthers,
            KilometreBasis = d.KilometreBasis,
            BillableKilometrePercentage = d.BillableKilometrePercentage,
            RoundTrip = d.RoundTrip,
            Values = d.Values.OrderBy(v => v.ValidFrom)
                .Select(v => new DiscountOrSurchargeValueDto { ValidFrom = v.ValidFrom, Value = v.Value, CurrencyCode = v.CurrencyCode, IsFixed = v.ValidFrom <= today })
                .ToList(),
            Regions = d.Regions
                .Select(r => new DiscountOrSurchargeRegionDto { GeographicalRegionId = r.GeographicalRegionId, AppliesAt = r.AppliesAt, Name = regionNames.GetValueOrDefault(r.GeographicalRegionId) ?? string.Empty })
                .OrderBy(r => r.Name)
                .ToList(),
            IsArchived = d.IsArchived
        };
        MapAudit(d, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(DiscountOrSurcharge d) => new() { Id = d.Id, Name = d.Name, Code = d.Code };

    protected override string DisplayNameOf(DiscountOrSurcharge d) => d.Name;

    protected override async Task<DiscountOrSurcharge> CreateEntityAsync(DiscountOrSurchargeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(d => d.Code == code, "code", code);
        var item = new DiscountOrSurcharge(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, input.Direction, input.ValueKind, input.Side);

        /* Nothing has taken effect before the row exists: values of any date are accepted on create. */
        await ApplyAsync(item, input, fixedOn: null);
        return item;
    }

    protected override async Task ApplyUpdateAsync(DiscountOrSurcharge item, DiscountOrSurchargeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != item.Code)
        {
            await EnsureUniqueAsync(d => d.Code == code && d.Id != item.Id, "code", code);
            item.SetCode(code);
        }

        item.SetName(input.Name);
        item.SetDirection(input.Direction);
        item.SetValueKind(input.ValueKind);
        item.SetSide(input.Side);
        await ApplyAsync(item, input, fixedOn: _today.Get());
    }

    /// <summary>
    /// The shared part of create and update. The values and regions are set on the aggregate, so they are written with
    /// the parent in one unit of work (OpenTMS lost the region links of a new surcharge, assessment §6).
    /// </summary>
    private async Task ApplyAsync(DiscountOrSurcharge item, DiscountOrSurchargeCreateDto input, DateOnly? fixedOn)
    {
        item.SetDaysOfWeek(input.DaysOfWeek);
        if (input.VatPercentageId != item.VatPercentageId)
        {
            await _vat.EnsureExistsAsync(input.VatPercentageId);
            item.VatPercentageId = input.VatPercentageId;
        }

        item.SetLedgerAccount(input.LedgerAccount);
        item.AppliesAfterOthers = input.AppliesAfterOthers;
        item.SetKilometreRule(input.KilometreBasis, input.BillableKilometrePercentage, input.RoundTrip);
        item.SetDescription(input.Description);

        var values = (input.Values ?? []).Select(v => (v.ValidFrom, v.Value, CurrencyCode: string.IsNullOrWhiteSpace(v.CurrencyCode) ? null : v.CurrencyCode.Trim().ToUpperInvariant())).ToList();
        item.SetValues(values, fixedOn, GuidGenerator.Create);

        /* Only currencies on new or changed rows are checked: a fixed row keeps a currency deactivated later. */
        var unchanged = item.Values.Where(v => fixedOn != null && v.ValidFrom <= fixedOn.Value).Select(v => v.ValidFrom).ToHashSet();
        await _masterData.EnsureCurrenciesActiveAsync(values
            .Where(v => v.CurrencyCode != null && !unchanged.Contains(v.ValidFrom))
            .Select(v => v.CurrencyCode!)
            .ToList());

        var regions = (input.Regions ?? []).Select(r => (r.GeographicalRegionId, r.AppliesAt)).ToList();
        var known = item.Regions.Select(r => r.GeographicalRegionId).ToHashSet();
        await _masterData.EnsureRegionsExistAsync(regions.Select(r => r.GeographicalRegionId).Where(id => !known.Contains(id)).ToList());
        item.SetRegions(regions, GuidGenerator.Create);

        item.IsArchived = input.IsArchived;
    }
}
