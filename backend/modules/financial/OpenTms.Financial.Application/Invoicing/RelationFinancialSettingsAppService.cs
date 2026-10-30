using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// Design 57: the financial defaults per relation, upserted by relation id (created on first edit). The list filters,
/// sorts and pages in the query on <see cref="RelationFinancialSettings.RelationName"/>, the copy of the relation's name
/// each save stores (review M2; the order's customer name is the precedent); only the page's live names are then read
/// through the Master Data lookup for display. A relation renamed in Master Data sorts and is found under its old name
/// until its settings are saved again (no Master Data rename event exists yet).
/// </summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class RelationFinancialSettingsAppService : FinancialAppServiceBase, IRelationFinancialSettingsAppService
{
    private readonly IRepository<RelationFinancialSettings, Guid> _settings;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discounts;
    private readonly VatPercentageReferences _vat;
    private readonly MasterDataReferences _masterData;

    public RelationFinancialSettingsAppService(
        IRepository<RelationFinancialSettings, Guid> settings,
        IRepository<PriceList, Guid> priceLists,
        IRepository<DiscountOrSurcharge, Guid> discounts,
        VatPercentageReferences vat,
        MasterDataReferences masterData)
    {
        _settings = settings;
        _priceLists = priceLists;
        _discounts = discounts;
        _vat = vat;
        _masterData = masterData;
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.RelationFinancialSettings, RequiresAll = true)]
    [Authorize(FinancialPermissions.RelationFinancialSettings.Default)]
    public virtual async Task<GridResponse<RelationFinancialSettingsRowDto>> GetListAsync(ReferenceDataListRequestDto input)
    {
        var all = await _settings.GetQueryableAsync();
        var totalCount = await AsyncExecuter.LongCountAsync(all);

        var filtered = all;
        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            filtered = filtered.Where(s => s.RelationName.ToLower().Contains(term));
        }

        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(filtered);
        var descending = input.Descending;
        /* Every order ends on the name and the id, so pages never overlap or skip a row. */
        var ordered = (input.SortBy?.ToLowerInvariant()) switch
        {
            null or "" or "relationname" => descending
                ? filtered.OrderByDescending(s => s.RelationName).ThenByDescending(s => s.Id)
                : filtered.OrderBy(s => s.RelationName).ThenBy(s => s.Id),
            "paymenttermdays" => descending
                ? filtered.OrderByDescending(s => s.PaymentTermDays).ThenBy(s => s.RelationName).ThenBy(s => s.Id)
                : filtered.OrderBy(s => s.PaymentTermDays).ThenBy(s => s.RelationName).ThenBy(s => s.Id),
            "currencycode" => descending
                ? filtered.OrderByDescending(s => s.CurrencyCode).ThenBy(s => s.RelationName).ThenBy(s => s.Id)
                : filtered.OrderBy(s => s.CurrencyCode).ThenBy(s => s.RelationName).ThenBy(s => s.Id),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", input.SortBy!)
        };

        var pageIds = await AsyncExecuter.ToListAsync(ordered.Skip(input.StartRow).Take(input.EndRow - input.StartRow).Select(s => s.Id));
        var loaded = pageIds.Count == 0 ? [] : (await _settings.GetListAsync(s => pageIds.Contains(s.Id), includeDetails: true)).ToDictionary(s => s.Id);
        var rows = pageIds.Where(loaded.ContainsKey).Select(id => loaded[id]).ToList();
        var names = await _masterData.GetRelationNamesAsync(rows.Select(s => s.RelationId).ToList());

        var vatNames = await _vat.GetNamesAsync(rows.Select(s => s.VatPercentageId));
        return new GridResponse<RelationFinancialSettingsRowDto>
        {
            Rows = rows.Select(s => new RelationFinancialSettingsRowDto
            {
                Id = s.Id,
                RelationId = s.RelationId,
                RelationName = names.GetValueOrDefault(s.RelationId) ?? s.RelationName,
                RevenuePriceListCount = s.PriceLists.Count(p => p.Side == RateSide.Revenue),
                CostPriceListCount = s.PriceLists.Count(p => p.Side == RateSide.Cost),
                VatPercentageName = s.VatPercentageId != null && vatNames.TryGetValue(s.VatPercentageId.Value, out var vatName) ? vatName : null,
                PaymentTermDays = s.PaymentTermDays,
                ChargeFixedPriceByDefault = s.ChargeFixedPriceByDefault,
                CurrencyCode = s.CurrencyCode
            }).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.RelationFinancialSettings, RequiresAll = true)]
    [Authorize(FinancialPermissions.RelationFinancialSettings.Default)]
    public virtual async Task<RelationFinancialSettingsDto?> GetByRelationAsync(Guid relationId)
    {
        var settings = await _settings.FindAsync(s => s.RelationId == relationId, includeDetails: true);
        return settings == null ? null : await ToDtoAsync(settings);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.RelationFinancialSettings, RequiresAll = true)]
    [Authorize(FinancialPermissions.RelationFinancialSettings.Update)]
    public virtual async Task<RelationFinancialSettingsDto> UpsertByRelationAsync(Guid relationId, RelationFinancialSettingsUpsertDto input)
    {
        var settings = await _settings.FindAsync(s => s.RelationId == relationId, includeDetails: true);
        var isNew = settings == null;
        if (settings == null)
        {
            await _masterData.EnsureRelationsExistAsync([relationId]);
            settings = new RelationFinancialSettings(GuidGenerator.Create(), CurrentTenant.Id, relationId);
        }


        /* The list sorts and searches on this copy; every save refreshes it from Master Data. */
        var relationName = (await _masterData.GetRelationNamesAsync([relationId])).GetValueOrDefault(relationId);
        if (relationName != null)
        {
            settings.SetRelationName(relationName);
        }

        /* The client's stamp is taken after the Master Data name lookup: taken before it, a stale stamp was not detected
         * on SQLite (RelationFinancialSettingsAppServiceTests covers both orders of checks after it). */
        if (!isNew && !string.IsNullOrEmpty(input.ConcurrencyStamp))
        {
            settings.ConcurrencyStamp = input.ConcurrencyStamp;
        }

        await EnsurePriceListsAsync(input.RevenuePriceListIds, RateSide.Revenue);
        await EnsurePriceListsAsync(input.CostPriceListIds, RateSide.Cost);
        await EnsureDiscountsAsync(input.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId).ToList());
        await _vat.EnsureExistsAsync(input.VatPercentageId);
        if (!string.IsNullOrWhiteSpace(input.CurrencyCode))
        {
            await _masterData.EnsureCurrenciesActiveAsync([input.CurrencyCode.Trim().ToUpperInvariant()]);
        }

        settings.SetPriceLists(RateSide.Revenue, input.RevenuePriceListIds, GuidGenerator.Create);
        settings.SetPriceLists(RateSide.Cost, input.CostPriceListIds, GuidGenerator.Create);
        settings.SetMultipleMatches(input.RevenueMultipleMatches, input.CostMultipleMatches);
        settings.SetRevenueChargedBases(input.RevenueChargedBases);
        settings.SetDiscountsOrSurcharges(input.DiscountsOrSurcharges.Select(d => (d.DiscountOrSurchargeId, d.ValidFrom, d.ValidTill)).ToList(), GuidGenerator.Create);
        settings.VatPercentageId = input.VatPercentageId;
        settings.SetPaymentTermDays(input.PaymentTermDays);
        settings.ChargeFixedPriceByDefault = input.ChargeFixedPriceByDefault;
        settings.SendFreightDocumentsWithInvoice = input.SendFreightDocumentsWithInvoice;
        settings.SetCurrencyCode(input.CurrencyCode);

        if (isNew)
        {
            await _settings.InsertAsync(settings, autoSave: true);
        }
        else
        {
            await _settings.UpdateAsync(settings, autoSave: true);
        }

        return await ToDtoAsync(await _settings.GetAsync(settings.Id, includeDetails: true));
    }

    private async Task EnsurePriceListsAsync(IReadOnlyCollection<Guid> ids, RateSide side)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var found = await _priceLists.GetListAsync(p => keys.Contains(p.Id));
        if (found.Count != keys.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", side == RateSide.Cost ? "costPriceListIds" : "revenuePriceListIds");
        }

        var wrongSide = found.FirstOrDefault(p => p.Side != side);
        if (wrongSide != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "side").WithData("value", wrongSide.Code);
        }
    }

    private async Task EnsureDiscountsAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count > 0 && await _discounts.CountAsync(d => keys.Contains(d.Id)) != keys.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "discountOrSurchargeId");
        }
    }

    private async Task<RelationFinancialSettingsDto> ToDtoAsync(RelationFinancialSettings s)
    {
        var relationNames = await _masterData.GetRelationNamesAsync([s.RelationId]);
        var listIds = s.PriceLists.Select(p => p.PriceListId).Distinct().ToList();
        var lists = listIds.Count == 0 ? [] : await _priceLists.GetListAsync(p => listIds.Contains(p.Id));
        var discountIds = s.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId).Distinct().ToList();
        var discounts = discountIds.Count == 0 ? [] : await _discounts.GetListAsync(d => discountIds.Contains(d.Id));
        var vatNames = await _vat.GetNamesAsync([s.VatPercentageId]);

        List<RelationPriceListDto> Lists(RateSide side) => s.PriceLists.Where(p => p.Side == side).OrderBy(p => p.Position).Select(p =>
        {
            var list = lists.FirstOrDefault(l => l.Id == p.PriceListId);
            return new RelationPriceListDto { PriceListId = p.PriceListId, Code = list?.Code, Name = list?.Name, Position = p.Position, Basis = list?.Basis };
        }).ToList();

        return new RelationFinancialSettingsDto
        {
            Id = s.Id,
            RelationId = s.RelationId,
            RelationName = relationNames.GetValueOrDefault(s.RelationId) ?? string.Empty,
            RevenuePriceLists = Lists(RateSide.Revenue),
            CostPriceLists = Lists(RateSide.Cost),
            RevenueMultipleMatches = s.RevenueMultipleMatches,
            CostMultipleMatches = s.CostMultipleMatches,
            RevenueChargedBases = s.RevenueChargedBases.ToList(),
            DiscountsOrSurcharges = s.DiscountsOrSurcharges.OrderBy(d => d.ValidFrom).Select(d => new RelationDiscountOrSurchargeDto
            {
                DiscountOrSurchargeId = d.DiscountOrSurchargeId,
                Name = discounts.FirstOrDefault(x => x.Id == d.DiscountOrSurchargeId)?.Name,
                ValidFrom = d.ValidFrom,
                ValidTill = d.ValidTill
            }).ToList(),
            VatPercentageId = s.VatPercentageId,
            VatPercentageName = s.VatPercentageId != null && vatNames.TryGetValue(s.VatPercentageId.Value, out var vatName) ? vatName : null,
            PaymentTermDays = s.PaymentTermDays,
            ChargeFixedPriceByDefault = s.ChargeFixedPriceByDefault,
            SendFreightDocumentsWithInvoice = s.SendFreightDocumentsWithInvoice,
            CurrencyCode = s.CurrencyCode,
            CreationTime = s.CreationTime,
            CreatorId = s.CreatorId,
            LastModificationTime = s.LastModificationTime,
            LastModifierId = s.LastModifierId,
            ConcurrencyStamp = s.ConcurrencyStamp
        };
    }
}
