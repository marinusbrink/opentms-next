using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace OpenTms.Orders.ConsignmentNotes;

// ── profiles (design-50 shape + set-default) ───────────────────────────────────

public class ConsignmentNoteProfileRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Copies { get; set; }
    public bool ShowBorders { get; set; }
    public bool ShowQrCode { get; set; }
    public bool GroupByLeg { get; set; }
    public string? ReportDefinitionName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class ConsignmentNoteProfileCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    [Range(ConsignmentNoteConsts.MinCopies, ConsignmentNoteConsts.MaxCopies)]
    public int Copies { get; set; } = ConsignmentNoteConsts.DefaultCopies;

    public bool ShowBorders { get; set; } = true;
    public bool ShowQrCode { get; set; } = true;
    public bool GroupByLeg { get; set; }

    public CmrPartySource Box1Sender { get; set; } = CmrPartySource.Customer;
    public CmrPartySource Box2Consignee { get; set; } = CmrPartySource.DestinationAddress;
    public CmrPartySource Box3DeliveryPlace { get; set; } = CmrPartySource.DestinationAddress;
    public CmrPartySource Box4TakingOverPlace { get; set; } = CmrPartySource.OriginAddress;
    public CmrPartySource Box16Carrier { get; set; } = CmrPartySource.Carrier;
    public CmrPartySource Box17SuccessiveCarrier { get; set; } = CmrPartySource.Empty;
    public CmrPartySource Box21EstablishedBy { get; set; } = CmrPartySource.OwnCompany;

    public List<CmrGoodsColumn> Box6Columns { get; set; } = new();
    public List<CmrInstructionItem> Box13Items { get; set; } = new();
    public List<CmrRemarkItem> Box18Items { get; set; } = new();
    public List<Guid> FixedDescriptionIds { get; set; } = new();

    /// <summary>A Platform report definition of purpose ConsignmentNote; null = the tenant's default of that purpose.</summary>
    public Guid? ReportDefinitionId { get; set; }

    public bool IsArchived { get; set; }
}

public class ConsignmentNoteProfileUpdateDto : ConsignmentNoteProfileCreateDto, IReferenceDataUpdateDto
{
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class ConsignmentNoteProfileDto : ReferenceDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public int Copies { get; set; }
    public bool ShowBorders { get; set; }
    public bool ShowQrCode { get; set; }
    public bool GroupByLeg { get; set; }
    public CmrPartySource Box1Sender { get; set; }
    public CmrPartySource Box2Consignee { get; set; }
    public CmrPartySource Box3DeliveryPlace { get; set; }
    public CmrPartySource Box4TakingOverPlace { get; set; }
    public CmrPartySource Box16Carrier { get; set; }
    public CmrPartySource Box17SuccessiveCarrier { get; set; }
    public CmrPartySource Box21EstablishedBy { get; set; }
    public List<CmrGoodsColumn> Box6Columns { get; set; } = new();
    public List<CmrInstructionItem> Box13Items { get; set; } = new();
    public List<CmrRemarkItem> Box18Items { get; set; } = new();
    public List<Guid> FixedDescriptionIds { get; set; } = new();
    public List<string> FixedDescriptionNames { get; set; } = new();
    public Guid? ReportDefinitionId { get; set; }
    public string? ReportDefinitionName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public interface IConsignmentNoteProfileAppService : IApplicationService
{
    Task<GridResponse<ConsignmentNoteProfileRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<ConsignmentNoteProfileDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<ConsignmentNoteProfileDto> CreateAsync(ConsignmentNoteProfileCreateDto input);
    Task<ConsignmentNoteProfileDto> UpdateAsync(Guid id, ConsignmentNoteProfileUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}

// ── fixed descriptions ─────────────────────────────────────────────────────────

public class ConsignmentNoteFixedDescriptionRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class ConsignmentNoteFixedDescriptionCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(ConsignmentNoteConsts.MaxFixedDescriptionTextLength)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }
}

public class ConsignmentNoteFixedDescriptionUpdateDto : ConsignmentNoteFixedDescriptionCreateDto, IReferenceDataUpdateDto
{
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class ConsignmentNoteFixedDescriptionDto : ReferenceDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}

public interface IConsignmentNoteFixedDescriptionAppService : IApplicationService
{
    Task<GridResponse<ConsignmentNoteFixedDescriptionRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<ConsignmentNoteFixedDescriptionDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<ConsignmentNoteFixedDescriptionDto> CreateAsync(ConsignmentNoteFixedDescriptionCreateDto input);
    Task<ConsignmentNoteFixedDescriptionDto> UpdateAsync(Guid id, ConsignmentNoteFixedDescriptionUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}

// ── printing and documents ─────────────────────────────────────────────────────

/// <summary>Per-print overrides of the profile's boxes and layout; null keeps the profile's value.</summary>
public class ConsignmentNoteOverridesDto
{
    [Range(ConsignmentNoteConsts.MinCopies, ConsignmentNoteConsts.MaxCopies)]
    public int? Copies { get; set; }
    public bool? ShowBorders { get; set; }
    public bool? ShowQrCode { get; set; }
    public bool? GroupByLeg { get; set; }
    public CmrPartySource? Box1Sender { get; set; }
    public CmrPartySource? Box2Consignee { get; set; }
    public CmrPartySource? Box3DeliveryPlace { get; set; }
    public CmrPartySource? Box4TakingOverPlace { get; set; }
    public CmrPartySource? Box16Carrier { get; set; }
    public CmrPartySource? Box17SuccessiveCarrier { get; set; }
    public CmrPartySource? Box21EstablishedBy { get; set; }
    public List<CmrGoodsColumn>? Box6Columns { get; set; }
    public List<CmrInstructionItem>? Box13Items { get; set; }
    public List<CmrRemarkItem>? Box18Items { get; set; }
    public List<Guid>? FixedDescriptionIds { get; set; }
}

/// <summary>Print for lines of one order, or for every line of a trip's legs (one document per order on the trip).</summary>
public class PrintConsignmentNoteDto
{
    [MaxLength(OrdersConsts.MaxLines)]
    public List<Guid> OrderLineIds { get; set; } = new();

    public Guid? TripId { get; set; }

    /// <summary>Null = the tenant's default profile.</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>Null = the profile's definition, else the tenant's default of purpose ConsignmentNote.</summary>
    public Guid? ReportDefinitionId { get; set; }

    public ConsignmentNoteOverridesDto? Overrides { get; set; }
}

public class ConsignmentNoteDocumentDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public List<Guid> LineIds { get; set; } = new();
    public Guid? TripId { get; set; }
    public Guid ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public DateTime PrintedAt { get; set; }
    public Guid? PrintedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public class ConsignmentNoteDocumentListRequestDto : ReferenceDataListRequestDto
{
    public Guid? OrderId { get; set; }
    public Guid? TripId { get; set; }
    public DateOnly? PrintedFrom { get; set; }
    public DateOnly? PrintedTo { get; set; }
}

public interface IConsignmentNoteAppService : IApplicationService
{
    /// <summary>Composes, renders, numbers and stores: one document per order among the selected lines.</summary>
    Task<List<ConsignmentNoteDocumentDto>> PrintAsync(PrintConsignmentNoteDto input);
    Task<GridResponse<ConsignmentNoteDocumentDto>> GetDocumentListAsync(ConsignmentNoteDocumentListRequestDto input);
    Task<ConsignmentNoteDocumentDto> GetDocumentAsync(Guid id);
    Task<IRemoteStreamContent> GetDownloadAsync(Guid id);
}
