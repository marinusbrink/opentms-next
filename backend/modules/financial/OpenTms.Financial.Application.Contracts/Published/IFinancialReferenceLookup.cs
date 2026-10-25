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

    const int MaxKeys = 500;
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
}
