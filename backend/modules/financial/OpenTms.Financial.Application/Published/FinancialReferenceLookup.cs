using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Financial.Published;

/// <summary>
/// <see cref="IFinancialReferenceLookup"/>: existence and names of Financial's reference data for Orders' financial terms
/// (design 56). In-process only, tenant-filtered, at most 500 ids per call; unknown ids are absent, archived rows carry
/// their flag. Anonymous in-process (outbox handlers and jobs call it): it returns ids, codes and names of the current tenant only.
/// </summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class FinancialReferenceLookup : FinancialAppServiceBase, IFinancialReferenceLookup
{
    private readonly IRepository<VatPercentage, Guid> _vat;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discounts;
    private readonly IRepository<AdditionalService, Guid> _services;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<RelationFinancialSettings, Guid> _relationSettings;

    public FinancialReferenceLookup(IRepository<VatPercentage, Guid> vat, IRepository<DiscountOrSurcharge, Guid> discounts, IRepository<AdditionalService, Guid> services, IRepository<PriceList, Guid> priceLists,
        IRepository<RelationFinancialSettings, Guid> relationSettings)
    {
        _vat = vat;
        _discounts = discounts;
        _services = services;
        _priceLists = priceLists;
        _relationSettings = relationSettings;
    }

    public virtual async Task<RelationFinancialSettingsReferenceDto?> GetRelationFinancialSettingsAsync(Guid relationId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (relationId == Guid.Empty)
        {
            return null;
        }

        var settings = await _relationSettings.FindAsync(s => s.RelationId == relationId, includeDetails: true, cancellationToken: cancellationToken);
        if (settings == null)
        {
            return null;
        }

        return new RelationFinancialSettingsReferenceDto
        {
            RelationId = settings.RelationId,
            RevenuePriceListIds = settings.PriceListIdsFor(RateSide.Revenue).ToList(),
            RevenueMultipleMatches = settings.RevenueMultipleMatches,
            DiscountOrSurchargeIds = settings.DiscountOrSurchargeIdsOn(asOf).ToList(),
            VatPercentageId = settings.VatPercentageId,
            PaymentTermDays = settings.PaymentTermDays,
            ChargeFixedPriceByDefault = settings.ChargeFixedPriceByDefault,
            SendFreightDocumentsWithInvoice = settings.SendFreightDocumentsWithInvoice,
            CurrencyCode = settings.CurrencyCode
        };
    }

    public virtual async Task<IReadOnlyList<FinancialReferenceDto>> GetVatPercentagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = Bounded(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var rows = await _vat.GetListAsync(v => keys.Contains(v.Id), cancellationToken: cancellationToken);
        var culture = CultureInfo.CurrentUICulture.Name;
        return rows.Select(v => new FinancialReferenceDto { Id = v.Id, Code = v.Code, Name = v.Name.Resolve(culture), IsArchived = v.IsArchived }).ToList();
    }

    public virtual async Task<IReadOnlyList<FinancialReferenceDto>> GetDiscountsOrSurchargesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = Bounded(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var rows = await _discounts.GetListAsync(d => keys.Contains(d.Id), cancellationToken: cancellationToken);
        return rows.Select(d => new FinancialReferenceDto { Id = d.Id, Code = d.Code, Name = d.Name, IsArchived = d.IsArchived }).ToList();
    }

    public virtual async Task<IReadOnlyList<FinancialReferenceDto>> GetAdditionalServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = Bounded(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var rows = await _services.GetListAsync(s => keys.Contains(s.Id), cancellationToken: cancellationToken);
        return rows.Select(s => new FinancialReferenceDto { Id = s.Id, Code = s.Code, Name = s.Name, IsArchived = s.IsArchived }).ToList();
    }

    public virtual async Task<IReadOnlyList<PriceListReferenceDto>> GetPriceListsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = Bounded(ids);
        if (keys.Count == 0)
        {
            return [];
        }

        var rows = await _priceLists.GetListAsync(p => keys.Contains(p.Id), cancellationToken: cancellationToken);
        return rows.Select(p => new PriceListReferenceDto { Id = p.Id, Code = p.Code, Name = p.Name, IsArchived = p.IsArchived, Side = p.Side, CurrencyCode = p.CurrencyCode }).ToList();
    }

    private static List<Guid> Bounded(IReadOnlyCollection<Guid> ids)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count > IFinancialReferenceLookup.MaxKeys)
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.TooManyRows).WithData("max", IFinancialReferenceLookup.MaxKeys);
        }

        return distinct;
    }
}
