using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial.Invoicing;

// ── Relation financial settings (design 57) ───────────────────────────────────

public class RelationFinancialSettingsRowDto
{
    public Guid Id { get; set; }
    public Guid RelationId { get; set; }
    /// <summary>From Master Data; empty when the relation was deleted there.</summary>
    public string RelationName { get; set; } = string.Empty;
    public int RevenuePriceListCount { get; set; }
    public int CostPriceListCount { get; set; }
    public string? VatPercentageName { get; set; }
    public int? PaymentTermDays { get; set; }
    public bool ChargeFixedPriceByDefault { get; set; }
    public string? CurrencyCode { get; set; }
}

public class RelationPriceListDto
{
    public Guid PriceListId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public int Position { get; set; }
    /// <summary>The fact the list prices by (design 61 point 3: the revenue lists are shown per basis); null when the list no longer exists.</summary>
    public QuantitySource? Basis { get; set; }
}

public class RelationDiscountOrSurchargeInputDto
{
    public Guid DiscountOrSurchargeId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTill { get; set; }
}

public class RelationDiscountOrSurchargeDto : RelationDiscountOrSurchargeInputDto
{
    public string? Name { get; set; }
}

public class RelationFinancialSettingsDto : ReferenceDataDetailDtoBase
{
    public Guid RelationId { get; set; }
    public string RelationName { get; set; } = string.Empty;
    public List<RelationPriceListDto> RevenuePriceLists { get; set; } = new();
    public List<RelationPriceListDto> CostPriceLists { get; set; } = new();
    public MultipleMatches RevenueMultipleMatches { get; set; }
    public MultipleMatches CostMultipleMatches { get; set; }
    /// <summary>The revenue bases charged by default (design 61 point 3), in the legacy order of the block "Price calculation".</summary>
    public List<QuantitySource> RevenueChargedBases { get; set; } = new();
    public List<RelationDiscountOrSurchargeDto> DiscountsOrSurcharges { get; set; } = new();
    public Guid? VatPercentageId { get; set; }
    public string? VatPercentageName { get; set; }
    public int? PaymentTermDays { get; set; }
    public bool ChargeFixedPriceByDefault { get; set; }
    public bool SendFreightDocumentsWithInvoice { get; set; }
    public string? CurrencyCode { get; set; }
}

/// <summary>Upsert by relation: created on first edit (the stamp is then absent), updated with the stamp afterwards.</summary>
public class RelationFinancialSettingsUpsertDto
{
    [MaxLength(FinancialConsts.MaxRelationPriceLists)]
    public List<Guid> RevenuePriceListIds { get; set; } = new();

    [MaxLength(FinancialConsts.MaxRelationPriceLists)]
    public List<Guid> CostPriceListIds { get; set; } = new();

    public MultipleMatches RevenueMultipleMatches { get; set; } = MultipleMatches.All;

    public MultipleMatches CostMultipleMatches { get; set; } = MultipleMatches.All;

    /// <summary>
    /// The revenue bases switched on (design 61 point 3): each once, revenue bases only (not <c>Manual</c>, not the cost-only
    /// <c>TripDistanceKm</c>). A basis with one of <see cref="RevenuePriceListIds"/> counts as on either way. The cost side
    /// keeps its plain list.
    /// </summary>
    public List<QuantitySource> RevenueChargedBases { get; set; } = new();

    [MaxLength(FinancialConsts.MaxRelationDiscountsOrSurcharges)]
    public List<RelationDiscountOrSurchargeInputDto> DiscountsOrSurcharges { get; set; } = new();

    public Guid? VatPercentageId { get; set; }

    [Range(0, 366)]
    public int? PaymentTermDays { get; set; }

    public bool ChargeFixedPriceByDefault { get; set; }

    public bool SendFreightDocumentsWithInvoice { get; set; }

    [MaxLength(ReferenceDataConsts.IsoCurrencyCodeLength)]
    public string? CurrencyCode { get; set; }

    /// <summary>Required when the settings exist already (assessment §3.14); absent on the first edit.</summary>
    public string? ConcurrencyStamp { get; set; }
}

public interface IRelationFinancialSettingsAppService : IApplicationService
{
    /// <summary>Lists the relations with settings; the wildcard search matches the relation name (through the Master Data lookup).</summary>
    Task<GridResponse<RelationFinancialSettingsRowDto>> GetListAsync(ReferenceDataListRequestDto input);

    /// <summary>The settings of one relation, or null when none were saved yet.</summary>
    Task<RelationFinancialSettingsDto?> GetByRelationAsync(Guid relationId);

    Task<RelationFinancialSettingsDto> UpsertByRelationAsync(Guid relationId, RelationFinancialSettingsUpsertDto input);
}
