using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial.Invoicing;

// ── Outbound invoice types ─────────────────────────────────────────────────────

public class OutboundInvoiceTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public OutboundInvoiceKind Kind { get; set; }
    /// <summary>The format applied to counter 1 today, e.g. "F-2026-00001".</summary>
    public string NumberingExample { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class OutboundInvoiceTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public OutboundInvoiceKind Kind { get; set; }
    public NumberSequenceFormatDto Numbering { get; set; } = new();
    /// <summary>The counter the next invoice of this type takes.</summary>
    public long NextNumber { get; set; }
    /// <summary>The number the next invoice of this type gets, e.g. "F-2026-00042".</summary>
    public string NextNumberPreview { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class OutboundInvoiceTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public OutboundInvoiceKind Kind { get; set; } = OutboundInvoiceKind.Invoice;

    [Required]
    public NumberSequenceFormatDto Numbering { get; set; } = new();

    /// <summary>Continue numbering at this counter (only upwards); null leaves it.</summary>
    [Range(1, long.MaxValue)]
    public long? NextNumber { get; set; }

    public bool IsArchived { get; set; }
}

public class OutboundInvoiceTypeUpdateDto : OutboundInvoiceTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IOutboundInvoiceTypeAppService : IApplicationService
{
    Task<GridResponse<OutboundInvoiceTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<OutboundInvoiceTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<OutboundInvoiceTypeDto> CreateAsync(OutboundInvoiceTypeCreateDto input);
    Task<OutboundInvoiceTypeDto> UpdateAsync(Guid id, OutboundInvoiceTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    /// <summary>Makes the type the default of its kind.</summary>
    Task SetDefaultAsync(Guid id);
}

// ── Inbound invoice types ──────────────────────────────────────────────────────

public class InboundInvoiceTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public InboundInvoiceKind Kind { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class InboundInvoiceTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public InboundInvoiceKind Kind { get; set; }
    public string? CostLedgerAccount { get; set; }
    public bool IsArchived { get; set; }
}

public class InboundInvoiceTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public InboundInvoiceKind Kind { get; set; } = InboundInvoiceKind.CarrierInvoice;

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? CostLedgerAccount { get; set; }

    public bool IsArchived { get; set; }
}

public class InboundInvoiceTypeUpdateDto : InboundInvoiceTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IInboundInvoiceTypeAppService : IApplicationService
{
    Task<GridResponse<InboundInvoiceTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<InboundInvoiceTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<InboundInvoiceTypeDto> CreateAsync(InboundInvoiceTypeCreateDto input);
    Task<InboundInvoiceTypeDto> UpdateAsync(Guid id, InboundInvoiceTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
