using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

public class TransportOrderTemplateListRequestDto : ReferenceDataListRequestDto
{
    public bool IncludeInactive { get; set; }

    public Guid? CustomerRelationId { get; set; }
}

public class TransportOrderTemplateRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CustomerRelationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string TransportOrderTypeName { get; set; } = string.Empty;
    public TemplateRecurrence Recurrence { get; set; }
    public int Interval { get; set; }
    public RecurrenceDays DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int HorizonDays { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTill { get; set; }
    public DateOnly? LastMaterialisedDate { get; set; }
    public int LineCount { get; set; }
    public bool HasTripRequest { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public class TemplateScheduleDto
{
    public TemplateRecurrence Recurrence { get; set; } = TemplateRecurrence.None;

    [Range(OrdersConsts.MinScheduleInterval, OrdersConsts.MaxScheduleInterval)]
    public int Interval { get; set; } = 1;

    public RecurrenceDays DaysOfWeek { get; set; }

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    [Range(OrdersConsts.MinHorizonDays, OrdersConsts.MaxHorizonDays)]
    public int HorizonDays { get; set; } = OrdersConsts.DefaultHorizonDays;

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTill { get; set; }
}

public class TemplateStopInputDto
{
    [Required] public Guid WaypointTypeId { get; set; }
    [Required] public Guid RelationId { get; set; }
    [Required] public Guid RelationAddressId { get; set; }
    public Guid? ContactPersonId { get; set; }
    /// <summary>Days after the order date.</summary>
    [Range(-365, 365)] public int DayOffset { get; set; }
    public Guid? TimeSlotId { get; set; }
    public TimeOnly? WindowFrom { get; set; }
    public TimeOnly? WindowTill { get; set; }
    [MaxLength(OrdersConsts.MaxStopReferenceLength)] public string? Reference { get; set; }
    [MaxLength(OrdersConsts.MaxStopRemarksLength)] public string? Remarks { get; set; }
    [Range(0, OrdersConsts.MaxDefaultDurationMinutes)] public int? PlannedDurationMin { get; set; }
}

public class TemplateStopDto : TemplateStopInputDto
{
    public int Position { get; set; }
    public string WaypointTypeName { get; set; } = string.Empty;
    public string RelationName { get; set; } = string.Empty;
}

/// <summary>A template line: the order line's freight facts with stops as offsets.</summary>
public class TemplateLineInputDto : TransportOrderLineInputDto
{
    /// <summary>Replaces the dated stops of the base: the template's stops carry day offsets.</summary>
    public new List<TemplateStopInputDto> Stops { get; set; } = new();
}

public class TemplateLineDto : TemplateLineInputDto
{
    public int Position { get; set; }
    public string TransportUnitName { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public new List<TemplateStopDto> Stops { get; set; } = new();
}

public class TemplateTripRequestInputDto
{
    [Required] public Guid TransportTripTypeId { get; set; }
    [Required] public Guid DepartureRelationId { get; set; }
    [Required] public Guid DepartureAddressId { get; set; }
    [Required] public Guid ArrivalRelationId { get; set; }
    [Required] public Guid ArrivalAddressId { get; set; }
    [Range(-365, 365)] public int DepartureDayOffset { get; set; }
    public TimeOnly DepartureTime { get; set; }
    [Range(-365, 365)] public int? ArrivalDayOffset { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public Guid? CarrierContactPersonId { get; set; }
    public Guid? VehicleCombinationId { get; set; }
    [MaxLength(OrdersConsts.MaxTripRequestVehicles)] public List<Guid> VehicleIds { get; set; } = new();
    [MaxLength(OrdersConsts.MaxTripRequestDrivers)] public List<Guid> DriverIds { get; set; } = new();
    public decimal? AgreedPurchaseAmount { get; set; }
    [MaxLength(OrdersConsts.MaxCurrencyCodeLength)] public string? AgreedPurchaseCurrencyCode { get; set; }

    /// <summary>
    /// The freight items whose legs go on the trip (design 61 point 7); null or empty = every line. An id names the line
    /// posted with that <c>id</c> in <c>lines</c> (the ids read back, or a client key for a new line); a save replaces the
    /// lines and the selection follows them to their new ids. Unknown: <c>Orders:TripRequestLineUnknown</c> (400).
    /// </summary>
    [MaxLength(OrdersConsts.MaxLines)] public List<Guid>? LineIds { get; set; }
}

public class TransportOrderTemplateCreateDto
{
    [Required, MaxLength(OrdersConsts.MaxTemplateNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    [Required] public Guid TransportOrderTypeId { get; set; }
    [Required] public Guid CustomerRelationId { get; set; }
    public Guid? CustomerContactPersonId { get; set; }
    [MaxLength(OrdersConsts.MaxCustomerReferenceLength)] public string? CustomerReference { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? InternalRemarks { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? InvoicingRemarks { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
    public TransportOrderFinancialTermsInputDto? FinancialTerms { get; set; }
    public List<TransportOrderDefaultStopInputDto> DefaultStops { get; set; } = new();
    public List<TemplateLineInputDto> Lines { get; set; } = new();
    public List<TransportOrderReferenceEntryDto> References { get; set; } = new();
    public TemplateTripRequestInputDto? TripRequest { get; set; }
    [Required] public TemplateScheduleDto Schedule { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class TransportOrderTemplateUpdateDto : TransportOrderTemplateCreateDto, IReferenceDataUpdateDto
{
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderTemplateDto : ReferenceDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public Guid TransportOrderTypeId { get; set; }
    public string TransportOrderTypeName { get; set; } = string.Empty;
    public Guid CustomerRelationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? CustomerContactPersonId { get; set; }
    public string? CustomerReference { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public string? InternalRemarks { get; set; }
    public string? InvoicingRemarks { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public TransportOrderFinancialTermsDto FinancialTerms { get; set; } = new();
    public List<TransportOrderDefaultStopDto> DefaultStops { get; set; } = new();
    public List<TemplateLineDto> Lines { get; set; } = new();
    public List<TransportOrderReferenceEntryDto> References { get; set; } = new();
    public TemplateTripRequestInputDto? TripRequest { get; set; }
    public TemplateScheduleDto Schedule { get; set; } = new();
    public DateOnly? LastMaterialisedDate { get; set; }
    public bool IsActive { get; set; }
    public Guid? SourceOrderId { get; set; }
    /// <summary>The next dates the schedule would materialise (the horizon from today).</summary>
    public List<DateOnly> UpcomingDates { get; set; } = new();
}

public class MaterialiseTemplateDto
{
    [Required, MaxLength(OrdersConsts.MaxMaterialisationDates)]
    public List<DateOnly> Dates { get; set; } = new();

    [Range(1, OrdersConsts.MaxMaterialisationCopies)]
    public int Copies { get; set; } = 1;

    /// <summary>
    /// Design 61 wizard step "Details": per generated order (date and copy index) the customer reference and per line the
    /// quantity and weight to use instead of the template's. Orders without an override get the template's values.
    /// </summary>
    [MaxLength(OrdersConsts.MaxMaterialisationOverrides)]
    public List<MaterialisationOverrideDto> Overrides { get; set; } = new();
}

/// <summary>The details of one generated order (design 61 "Details (reference, quantity, weight per generated order)").</summary>
public class MaterialisationOverrideDto
{
    /// <summary>One of the request's dates.</summary>
    [Required]
    public DateOnly Date { get; set; }

    /// <summary>0-based: 0 .. copies − 1.</summary>
    [Range(0, OrdersConsts.MaxMaterialisationCopies - 1)]
    public int CopyIndex { get; set; }

    /// <summary>Null keeps the template's reference; an empty text clears it.</summary>
    [MaxLength(OrdersConsts.MaxCustomerReferenceLength)]
    public string? CustomerReference { get; set; }

    [MaxLength(OrdersConsts.MaxLines)]
    public List<MaterialisationLineOverrideDto> Lines { get; set; } = new();
}

/// <summary>
/// A line of a generated order. <see cref="LineId"/> is the template line's id for "new from template" and the copied
/// order's line id for "copy order"; null values keep the source's.
/// </summary>
public class MaterialisationLineOverrideDto
{
    [Required]
    public Guid LineId { get; set; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal? Quantity { get; set; }

    [Range(0d, double.MaxValue)]
    public double? WeightKg { get; set; }
}

public class MaterialisationResultDto
{
    public List<Guid> CreatedOrderIds { get; set; } = new();

    /// <summary>Dates and copies that already existed (idempotent on template + date + copy index).</summary>
    public int SkippedCount { get; set; }
}

public class TransportOrderTemplateBulkDeleteRequestDto : ReferenceDataBulkDeleteRequestDto
{
    public bool IncludeInactive { get; set; }
}

public interface ITransportOrderTemplateAppService : IApplicationService
{
    Task<GridResponse<TransportOrderTemplateRowDto>> GetListAsync(TransportOrderTemplateListRequestDto input);
    Task<TransportOrderTemplateDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TransportOrderTemplateDto> CreateAsync(TransportOrderTemplateCreateDto input);
    Task<TransportOrderTemplateDto> UpdateAsync(Guid id, TransportOrderTemplateUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(TransportOrderTemplateBulkDeleteRequestDto input);
    Task<MaterialisationResultDto> MaterialiseAsync(Guid id, MaterialiseTemplateDto input);
    Task<TransportOrderTemplateDto> CreateFromOrderAsync(Guid orderId);
    Task<MaterialisationResultDto> CopyOrderAsync(Guid orderId, MaterialiseTemplateDto input);
}
