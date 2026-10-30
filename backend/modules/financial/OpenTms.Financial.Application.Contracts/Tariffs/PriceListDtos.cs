using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace OpenTms.Financial.Tariffs;

// ── Price lists (design 57) ────────────────────────────────────────────────────

public class PriceListRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RateSide Side { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public QuantitySource Basis { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTill { get; set; }
    public int RuleCount { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class PriceListColumnDto
{
    public int Position { get; set; }

    /// <summary>Inclusive upper bound; null = the open last column.</summary>
    public decimal? UpperBound { get; set; }
}

public class PriceListDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RateSide Side { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public QuantitySource Basis { get; set; }
    public PriceMultiplier Multiplier { get; set; }
    public ColumnQuantitySource ColumnQuantity { get; set; }
    public StopMatching StopMatching { get; set; }
    public decimal UnitConversionFactor { get; set; } = 1m;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTill { get; set; }
    public bool IsArchived { get; set; }
    public List<PriceListColumnDto> Columns { get; set; } = new();
    /// <summary>The rules are paged through GetRulesAsync (a list may hold 10,000).</summary>
    public int RuleCount { get; set; }
}

public class PriceListCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public RateSide Side { get; set; } = RateSide.Revenue;

    [Required, MaxLength(ReferenceDataConsts.IsoCurrencyCodeLength)]
    public string CurrencyCode { get; set; } = "EUR";

    public QuantitySource Basis { get; set; } = QuantitySource.LineQuantity;

    public PriceMultiplier Multiplier { get; set; } = PriceMultiplier.PerUnit;

    public ColumnQuantitySource ColumnQuantity { get; set; } = ColumnQuantitySource.LineBasis;

    public StopMatching StopMatching { get; set; } = StopMatching.Both;

    public decimal UnitConversionFactor { get; set; } = 1m;

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTill { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>The upper bounds of the columns in order; the last is null (open); at most 50. Empty = one open column.</summary>
    [MaxLength(FinancialConsts.MaxPriceListColumns)]
    public List<decimal?> Columns { get; set; } = new();
}

public class PriceListUpdateDto : PriceListCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class PriceListListRequestDto : ArchivableListRequestDto
{
    public RateSide? Side { get; set; }
}

// ── Rules ──────────────────────────────────────────────────────────────────────

public class PriceListRuleListRequestDto : ReferenceDataListRequestDto
{
    /// <summary>0–3: only the rules of this specificity.</summary>
    [Range(0, 3)]
    public int? Tier { get; set; }

    /// <summary>
    /// Several tiers at once (design 57 UI: "Overrides" = tiers 1–3, "Defaults" = tier 0). Combined with <see cref="Tier"/>
    /// when both are given (a rule must match both); empty = no tier filter.
    /// </summary>
    [MaxLength(4)]
    public List<int> Tiers { get; set; } = new();
}

public class PriceListBracketInputDto
{
    /// <summary>Inclusive; null = the open row (exactly one, last).</summary>
    public decimal? UpperBound { get; set; }

    /// <summary>One unit price per column of the list, in column order; null = no price in that cell.</summary>
    [Required]
    public List<decimal?> UnitPrices { get; set; } = new();
}

public class PriceListRuleInputDto
{
    public Guid? FromRelationId { get; set; }
    public Guid? FromRegionId { get; set; }
    public Guid? ToRelationId { get; set; }
    public Guid? ToRegionId { get; set; }
    public Guid? TransportUnitId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VehicleTypeId { get; set; }

    /// <summary>The quantity the brackets grade on; Manual = no grading (one open row).</summary>
    public QuantitySource RowQuantity { get; set; } = QuantitySource.Manual;

    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }

    /// <summary>At least the open row; at most 30; strictly ascending.</summary>
    [Required, MinLength(1), MaxLength(FinancialConsts.MaxPriceListBrackets)]
    public List<PriceListBracketInputDto> Brackets { get; set; } = new();
}

public class PriceListRuleRowDto
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public int Tier { get; set; }
    public Guid? FromRelationId { get; set; }
    public string? FromRelationName { get; set; }
    public Guid? FromRegionId { get; set; }
    public string? FromRegionName { get; set; }
    public Guid? ToRelationId { get; set; }
    public string? ToRelationName { get; set; }
    public Guid? ToRegionId { get; set; }
    public string? ToRegionName { get; set; }
    /// <summary>Master Data ids; the screen names them through its own lookups.</summary>
    public Guid? TransportUnitId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VehicleTypeId { get; set; }
    public QuantitySource RowQuantity { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int BracketCount { get; set; }
}

public class PriceListBracketDto
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public decimal? UpperBound { get; set; }
    public List<decimal?> UnitPrices { get; set; } = new();
}

public class PriceListRuleDto : PriceListRuleRowDto
{
    public List<PriceListBracketDto> Brackets { get; set; } = new();
}

// ── Commands ───────────────────────────────────────────────────────────────────

public class PriceListCopyDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTill { get; set; }
}

public class PriceListAdjustDto
{
    /// <summary>Relative: every cell × (1 + percentage / 100). Exactly one of percentage and amount.</summary>
    public decimal? Percentage { get; set; }

    /// <summary>Absolute: every cell + amount (in the list's currency).</summary>
    public decimal? Amount { get; set; }

    public PriceRounding Rounding { get; set; } = PriceRounding.None;

    /// <summary>True = report what would change without saving.</summary>
    public bool Preview { get; set; }
}

public class PriceListAdjustSampleDto
{
    public Guid RuleId { get; set; }
    public int BracketPosition { get; set; }
    public int ColumnPosition { get; set; }
    public decimal Before { get; set; }
    public decimal After { get; set; }
}

public class PriceListAdjustResultDto
{
    public int ChangedCellCount { get; set; }

    /// <summary>With preview: the first cells that would change.</summary>
    public List<PriceListAdjustSampleDto> Sample { get; set; } = new();
}

/// <summary>One line as the Simulate panel describes it: the lane, the dimensions and the quantities.</summary>
public class PriceListSimulateDto
{
    public Guid FromRelationId { get; set; }
    public List<Guid> FromRegionIds { get; set; } = new();
    public Guid ToRelationId { get; set; }
    public List<Guid> ToRegionIds { get; set; } = new();
    /// <summary>
    /// Null simulates a line without a transport unit: only rules that name no unit ("any unit") can match, as a rule's
    /// dimension set to "any" matches every line (design 57 step 3; build note 20).
    /// </summary>
    public Guid? TransportUnitId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VehicleTypeId { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public decimal? Positions { get; set; }
    public decimal? Colli { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public decimal? LoadingMeters { get; set; }
    public decimal? LoadFactor { get; set; }
    public decimal? Epq { get; set; }
    public decimal? DistanceKm { get; set; }
    public decimal? TollDistanceKm { get; set; }
    public decimal? DurationH { get; set; }
    public DateOnly? AsOf { get; set; }
}

public class PriceListRuleCandidateDto
{
    public Guid RuleId { get; set; }
    public int Tier { get; set; }
    public int LaneScore { get; set; }
    public int Position { get; set; }
    /// <summary>Winner, Candidate, Lane (lane did not match) or Dimensions (a dimension did not match).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class PriceListSimulateResultDto
{
    public Guid? WinningRuleId { get; set; }
    public List<PriceListRuleCandidateDto> Candidates { get; set; } = new();
    public Guid? BracketId { get; set; }
    public int? ColumnPosition { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Amount { get; set; }
    public string? Warning { get; set; }
    /// <summary>False when the list is not valid on the as-of date or its currency was not checked.</summary>
    public bool IsValidOnAsOf { get; set; }
}

public class PriceListImportResultDto
{
    public int RuleCount { get; set; }
    public int BracketCount { get; set; }
    public int CellCount { get; set; }
}

public interface IPriceListAppService : IApplicationService
{
    Task<GridResponse<PriceListRowDto>> GetListAsync(PriceListListRequestDto input);
    Task<PriceListDto> GetAsync(Guid id);
    /// <summary>The active lists for a picker; <paramref name="basis"/> narrows them to one basis (design 61 point 3: a row of the block "Price calculation").</summary>
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(RateSide? side = null, string? term = null, int take = ReferenceDataConsts.MaxLookupTake, QuantitySource? basis = null);
    Task<PriceListDto> CreateAsync(PriceListCreateDto input);
    Task<PriceListDto> UpdateAsync(Guid id, PriceListUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);

    Task<GridResponse<PriceListRuleRowDto>> GetRulesAsync(Guid id, PriceListRuleListRequestDto input);
    Task<PriceListRuleDto> GetRuleAsync(Guid id, Guid ruleId);
    Task<PriceListRuleDto> CreateRuleAsync(Guid id, PriceListRuleInputDto input);
    Task<PriceListRuleDto> UpdateRuleAsync(Guid id, Guid ruleId, PriceListRuleInputDto input);
    Task DeleteRuleAsync(Guid id, Guid ruleId);

    Task<PriceListDto> CopyAsync(Guid id, PriceListCopyDto input);
    Task<PriceListAdjustResultDto> AdjustAsync(Guid id, PriceListAdjustDto input);
    Task<PriceListSimulateResultDto> SimulateAsync(Guid id, PriceListSimulateDto input);
    Task<IRemoteStreamContent> ExportAsync(Guid id);
    Task<PriceListImportResultDto> ImportAsync(Guid id, IRemoteStreamContent file);
}
