using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs;

namespace OpenTms.Financial.Published;

/// <summary>
/// Existence and names of Financial's reference data for the modules that store its ids (Orders' financial terms;
/// design 56 "The three flows, applied"). The Financial counterpart of Master Data's <c>IMasterDataReferenceLookup</c>:
/// in-process only, tenant-filtered, at most <see cref="MaxKeys"/> ids per call, archived rows included with their flag.
/// </summary>
public interface IFinancialReferenceLookup
{
    Task<IReadOnlyList<FinancialReferenceDto>> GetVatPercentagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinancialReferenceDto>> GetDiscountsOrSurchargesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinancialReferenceDto>> GetAdditionalServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PriceListReferenceDto>> GetPriceListsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// The financial defaults of a Master Data relation (design 57 <c>RelationFinancialSettings</c>) as Orders copies them
    /// into an order's terms and checks them on approval (design 58, rule <c>FinancialDefaultsMismatch</c>): the revenue
    /// lists in order, how several matching lists combine, the default discounts and surcharges valid on <paramref name="asOf"/>,
    /// VAT, payment term, the fixed-price and documents flags, the currency. Null when the relation has no settings.
    /// </summary>
    Task<RelationFinancialSettingsReferenceDto?> GetRelationFinancialSettingsAsync(Guid relationId, DateOnly asOf, CancellationToken cancellationToken = default);

    const int MaxKeys = 500;
}

public class RelationFinancialSettingsReferenceDto
{
    public Guid RelationId { get; set; }

    /// <summary>The revenue price lists in position order (QP5).</summary>
    public List<Guid> RevenuePriceListIds { get; set; } = new();

    public MultipleMatches RevenueMultipleMatches { get; set; } = MultipleMatches.All;

    /// <summary>The revenue bases charged by default (design 61 point 3), in the legacy order; Orders copies them into an order's terms.</summary>
    public List<QuantitySource> RevenueChargedBases { get; set; } = new();

    /// <summary>The default discounts and surcharges valid on the date asked for.</summary>
    public List<Guid> DiscountOrSurchargeIds { get; set; } = new();

    public Guid? VatPercentageId { get; set; }

    public int? PaymentTermDays { get; set; }

    public bool ChargeFixedPriceByDefault { get; set; }

    public bool SendFreightDocumentsWithInvoice { get; set; }

    /// <summary>ISO 4217; null = the relation's currency from Master Data (Orders then keeps its default).</summary>
    public string? CurrencyCode { get; set; }
}

public class FinancialReferenceDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsArchived { get; set; }
}

public class PriceListReferenceDto : FinancialReferenceDto
{
    public RateSide Side { get; set; }

    public string CurrencyCode { get; set; } = "EUR";

    /// <summary>The fact the list prices by (design 61 point 3: the order's lists are shown per basis).</summary>
    public QuantitySource Basis { get; set; }
}
