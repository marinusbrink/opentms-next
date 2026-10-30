using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

// ── list ───────────────────────────────────────────────────────────────────────

/// <summary>The order list's request (design 58 "API contract"): the design-50 paging plus the typed filters and the view.</summary>
public class TransportOrderListRequestDto : ReferenceDataListRequestDto
{
    public List<TransportOrderStatus> Status { get; set; } = new();

    /// <summary>On the order date.</summary>
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public Guid? CustomerRelationId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Guid? SourceTemplateId { get; set; }

    public TransportOrderView View { get; set; } = TransportOrderView.All;
}

/// <summary>
/// One column of a list export (UI-GRID-013): the grid's column id (<c>field</c> or <c>colId</c>, e.g. "number",
/// "firstStop") and the header text the user sees. A missing header is localized on the server in the UI culture.
/// </summary>
public class ExportColumnDto
{
    [Required, MaxLength(OrdersConsts.MaxExportFieldLength)]
    public string Field { get; set; } = string.Empty;

    [MaxLength(OrdersConsts.MaxExportHeaderLength)]
    public string? Header { get; set; }
}

/// <summary>
/// The order list's export (UI-GRID-013, design 61): the list's own request (filters, view, search, sort; paging is
/// ignored) plus the columns in display order (empty = every exportable column) and <see cref="AllRows"/> (true = every
/// order of the tenant, the list's TotalCount, ignoring filters, view and search; the sort still applies). The answer is
/// an xlsx file; more rows than the setting <c>Orders:Export:MaxRows</c> (default 50,000) are refused.
/// </summary>
public class TransportOrderExportRequestDto : TransportOrderListRequestDto
{
    [MaxLength(OrdersConsts.MaxExportColumns)]
    public List<ExportColumnDto> Columns { get; set; } = new();

    public bool AllRows { get; set; }
}

public class TransportOrderRowDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public TransportOrderStatus Status { get; set; }
    public DateOnly OrderDate { get; set; }
    public Guid TransportOrderTypeId { get; set; }
    public string TransportOrderTypeName { get; set; } = string.Empty;
    public Guid CustomerRelationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerReference { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public OrderSource Source { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public int LineCount { get; set; }
    public int StopCount { get; set; }
    public string? FirstStopName { get; set; }
    public string? FirstStopCity { get; set; }
    public DateOnly? FirstStopDate { get; set; }
    public string? LastStopName { get; set; }
    public string? LastStopCity { get; set; }
    public DateOnly? LastStopDate { get; set; }
    public decimal? RevenueAmount { get; set; }
    public decimal? CostOfRevenueAmount { get; set; }
    public decimal? MarginAmount { get; set; }
    public decimal? Co2Kg { get; set; }
    public ExecutionProgress ExecutionProgress { get; set; }
    public string? TripNumbers { get; set; }
    public bool HasTripRequest { get; set; }
    /// <summary>Set when Planning could not honour the order's trip request ("OpenTms.Orders:TripRequestNotHonoured", OrdersWarningCodes); the legs stay unplanned.</summary>
    public string? TripRequestWarningCode { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

// ── children ───────────────────────────────────────────────────────────────────

public class TransportOrderStopInputDto
{
    /// <summary>The stop's id when it exists; null adds a stop. Stops not sent again are removed.</summary>
    public Guid? Id { get; set; }

    [Required]
    public Guid WaypointTypeId { get; set; }

    [Required]
    public Guid RelationId { get; set; }

    /// <summary>The address of the relation this stop is at (explicit, assessment §3.4).</summary>
    [Required]
    public Guid RelationAddressId { get; set; }

    public Guid? ContactPersonId { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>A time slot or an explicit window (<see cref="WindowFrom"/>/<see cref="WindowTill"/>), never both.</summary>
    public Guid? TimeSlotId { get; set; }

    public TimeOnly? WindowFrom { get; set; }

    public TimeOnly? WindowTill { get; set; }

    [MaxLength(OrdersConsts.MaxStopReferenceLength)]
    public string? Reference { get; set; }

    [MaxLength(OrdersConsts.MaxStopRemarksLength)]
    public string? Remarks { get; set; }

    [Range(0, OrdersConsts.MaxDefaultDurationMinutes)]
    public int? PlannedDurationMin { get; set; }
}

public class TransportOrderStopDto : TransportOrderStopInputDto
{
    public int Position { get; set; }
    public string WaypointTypeName { get; set; } = string.Empty;
    public string RelationName { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public string? TimeSlotName { get; set; }
}

public class TransportOrderDefaultStopInputDto
{
    [Required]
    public Guid WaypointTypeId { get; set; }

    public Guid? RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }

    public Guid? ContactPersonId { get; set; }

    /// <summary>Days after the order date a stop of this position is planned.</summary>
    [Range(-365, 365)]
    public int DayOffset { get; set; }

    public Guid? TimeSlotId { get; set; }

    public TimeOnly? WindowFrom { get; set; }

    public TimeOnly? WindowTill { get; set; }

    [Range(0, OrdersConsts.MaxDefaultDurationMinutes)]
    public int? PlannedDurationMin { get; set; }
}

public class TransportOrderDefaultStopDto : TransportOrderDefaultStopInputDto
{
    public int Position { get; set; }
    public string WaypointTypeName { get; set; } = string.Empty;
    public string? RelationName { get; set; }
}

public class TransportOrderMeasurementDto
{
    public MeasurementKind Kind { get; set; }

    [Range(1, OrdersConsts.MaxMeasurementsPerKind)]
    public int Position { get; set; }

    public double Value { get; set; }
}

/// <summary>The freight facts of a line as the editor sends them (the order line's input).</summary>
public class TransportOrderLineInputDto
{
    /// <summary>The line's id when it exists; null adds a line. Lines not sent again are removed (PUT lines).</summary>
    public Guid? Id { get; set; }

    [Required]
    public Guid TransportUnitId { get; set; }

    public Guid? ProductId { get; set; }
    public Guid? ProductRelationCodeId { get; set; }
    public Guid? TemperatureId { get; set; }
    public Guid? VehicleTypeId { get; set; }

    public decimal Quantity { get; set; } = 1;
    public decimal? Positions { get; set; }
    public decimal? Colli { get; set; }
    public double? WeightKg { get; set; }
    public double? VolumeM3 { get; set; }
    public double? LoadingMeters { get; set; }
    public double? LoadFactor { get; set; }
    public double? Epq { get; set; }
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }

    [MaxLength(OrdersConsts.MaxLineTextLength)] public string? InvoiceReference { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? Remarks { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? InternalRemarks { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? ShipperInstructions { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? SpecialAgreements { get; set; }
    [MaxLength(OrdersConsts.MaxLineTextLength)] public string? AttachedDocuments { get; set; }

    public PaymentInstruction PaymentInstruction { get; set; }

    [MaxLength(OrdersConsts.MaxContainerFieldLength)] public string? ContainerNumber { get; set; }
    [MaxLength(OrdersConsts.MaxContainerFieldLength)] public string? SizeOrIso { get; set; }
    [MaxLength(OrdersConsts.MaxContainerFieldLength)] public string? PinCode { get; set; }
    [MaxLength(OrdersConsts.MaxContainerFieldLength)] public string? ShippingCompany { get; set; }
    public Guid? ShipperRelationId { get; set; }
    [MaxLength(OrdersConsts.MaxBarcodeLength)] public string? Barcode { get; set; }

    public List<TransportOrderMeasurementDto> Measurements { get; set; } = new();

    public List<Guid> RequiredTransportCharacteristicIds { get; set; } = new();

    /// <summary>Null on add: the line starts with the order's stop pattern (Assumption 1).</summary>
    public List<TransportOrderStopInputDto>? Stops { get; set; }

    public Dictionary<string, string>? Attributes { get; set; }
}

public class TransportOrderLineDto : TransportOrderLineInputDto
{
    public int Position { get; set; }
    public string TransportUnitName { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? ProductRelationCodeName { get; set; }
    public string? TemperatureName { get; set; }
    public string? VehicleTypeName { get; set; }
    public string? ShipperRelationName { get; set; }
    public List<string> RequiredTransportCharacteristicNames { get; set; } = new();
    public new List<TransportOrderStopDto> Stops { get; set; } = new();
    public int? DistanceM { get; set; }
    public int? DurationS { get; set; }
    public decimal? Co2Kg { get; set; }
}

public class TransportOrderReferenceEntryDto
{
    [Required, MaxLength(OrdersConsts.MaxReferenceTypeLength)]
    public string Type { get; set; } = string.Empty;

    [Required, MaxLength(OrdersConsts.MaxReferenceValueLength)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(OrdersConsts.MaxReferenceDescriptionLength)]
    public string? Description { get; set; }
}

public class TransportOrderDiscountOrSurchargeInputDto
{
    [Required]
    public Guid DiscountOrSurchargeId { get; set; }

    public DiscountOrSurchargeSource Source { get; set; } = DiscountOrSurchargeSource.Manual;
}

public class TransportOrderDiscountOrSurchargeDto : TransportOrderDiscountOrSurchargeInputDto
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>The terms as created or updated (the screen copies them from the customer's financial settings).</summary>
public class TransportOrderFinancialTermsInputDto
{
    [Required, MaxLength(OrdersConsts.MaxCurrencyCodeLength)]
    public string CurrencyCode { get; set; } = "EUR";

    public Guid? VatPercentageId { get; set; }

    [Range(0, 365)]
    public int? PaymentTermDays { get; set; }

    /// <summary>In position order (QP5).</summary>
    public List<Guid> PriceListIds { get; set; } = new();

    public MultipleMatches MultipleMatches { get; set; } = MultipleMatches.All;

    public List<TransportOrderDiscountOrSurchargeInputDto> DiscountsOrSurcharges { get; set; } = new();

    public decimal? FixedPriceAmount { get; set; }

    public bool SendFreightDocumentsWithInvoice { get; set; }

    /// <summary>
    /// The bases the order is charged by (design 61 "Order page review" point 3): the rows of the block "Price calculation"
    /// that are switched on, each once, revenue bases only (not <c>Manual</c>, not the cost-only <c>TripDistanceKm</c>). A
    /// basis with one of <see cref="PriceListIds"/> counts as on either way; a basis in here without such a list makes the
    /// calculation write an open line per freight item for it. Reads return them in the legacy order of the block.
    /// </summary>
    public List<QuantitySource> ChargedBases { get; set; } = new();
}

public class TransportOrderFinancialTermsDto : TransportOrderFinancialTermsInputDto
{
    public string? VatPercentageName { get; set; }
    public List<string> PriceListNames { get; set; } = new();

    /// <summary>The basis of each of <see cref="TransportOrderFinancialTermsInputDto.PriceListIds"/>, in the same order (null for a list that no longer exists).</summary>
    public List<QuantitySource?> PriceListBases { get; set; } = new();
    public new List<TransportOrderDiscountOrSurchargeDto> DiscountsOrSurcharges { get; set; } = new();
}

public class TransportOrderTripRequestInputDto
{
    [Required] public Guid TransportTripTypeId { get; set; }
    [Required] public Guid DepartureRelationId { get; set; }
    [Required] public Guid DepartureAddressId { get; set; }
    [Required] public Guid ArrivalRelationId { get; set; }
    [Required] public Guid ArrivalAddressId { get; set; }
    public DateTimeOffset PlannedDeparture { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public Guid? CarrierContactPersonId { get; set; }
    public Guid? VehicleCombinationId { get; set; }
    [MaxLength(OrdersConsts.MaxTripRequestVehicles)] public List<Guid> VehicleIds { get; set; } = new();
    [MaxLength(OrdersConsts.MaxTripRequestDrivers)] public List<Guid> DriverIds { get; set; } = new();
    public decimal? AgreedPurchaseAmount { get; set; }
    [MaxLength(OrdersConsts.MaxCurrencyCodeLength)] public string? AgreedPurchaseCurrencyCode { get; set; }

    /// <summary>
    /// The freight items (line ids) whose legs go on the trip (design 61 point 7); null or empty = every line. Each must be a
    /// line of the order (<c>Orders:TripRequestLineUnknown</c>, 400). On create the lines have no ids yet: an id names the
    /// line posted with that <c>id</c> (a client key; the line gets its own id). Read back, null = every line and an empty
    /// list = the selection lost its last line (no trip is created; posting it back unchanged means every line again).
    /// </summary>
    [MaxLength(OrdersConsts.MaxLines)] public List<Guid>? LineIds { get; set; }
}

public class TransportOrderTripRequestDto : TransportOrderTripRequestInputDto
{
    public string DepartureRelationName { get; set; } = string.Empty;
    public string ArrivalRelationName { get; set; } = string.Empty;
    public string? CarrierRelationName { get; set; }
    /// <summary>Set once Planning reported the trip it created from this request.</summary>
    public Guid? TripId { get; set; }
    public string? TripNumber { get; set; }
    /// <summary>
    /// Set when Planning could not honour the request on Confirm (design 59 build note 13): the error code
    /// "OpenTms.Orders:TripRequestNotHonoured", localized by the client like any error code with <see cref="WarningField"/>
    /// as {field}. The legs stay unplanned; the planner plans them on the Planboard.
    /// </summary>
    public string? WarningCode { get; set; }
    /// <summary>The request field that did not resolve (transportTripTypeId, departureRelationId, vehicleIds, driverIds, …).</summary>
    public string? WarningField { get; set; }
    public DateTimeOffset? WarningAt { get; set; }
}

public class TransportOrderRemarkDto
{
    public OrderRemarkKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public Guid? UserId { get; set; }
}

// ── the order ──────────────────────────────────────────────────────────────────

/// <summary>The header fields of an order as created or updated.</summary>
public class TransportOrderHeaderDto
{
    [Required] public Guid TransportOrderTypeId { get; set; }
    [Required] public Guid CustomerRelationId { get; set; }
    public Guid? CustomerContactPersonId { get; set; }
    /// <summary>Null on create = today in the tenant's zone.</summary>
    public DateOnly? OrderDate { get; set; }
    [MaxLength(OrdersConsts.MaxCustomerReferenceLength)] public string? CustomerReference { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? InternalRemarks { get; set; }
    [MaxLength(OrdersConsts.MaxRemarksLength)] public string? InvoicingRemarks { get; set; }
}

public class TransportOrderCreateDto : TransportOrderHeaderDto
{
    /// <summary>Null: EUR without lists; the screen posts the customer's financial settings.</summary>
    public TransportOrderFinancialTermsInputDto? FinancialTerms { get; set; }

    /// <summary>Null: the type's default waypoints (dated on the order date, offset 0).</summary>
    public List<TransportOrderDefaultStopInputDto>? DefaultStops { get; set; }

    public List<TransportOrderLineInputDto> Lines { get; set; } = new();

    public List<TransportOrderReferenceEntryDto> References { get; set; } = new();

    public TransportOrderTripRequestInputDto? TripRequest { get; set; }

    public Dictionary<string, string>? Attributes { get; set; }

    public OrderSource Source { get; set; } = OrderSource.Screen;

    [Range(0, 1)]
    public decimal? DraftConfidence { get; set; }

    /// <summary>Create as Open at once (the number is drawn; the Confirm rules apply).</summary>
    public bool Confirm { get; set; }
}

public class TransportOrderUpdateDto : TransportOrderHeaderDto, IReferenceDataUpdateDto
{
    /// <summary>
    /// The terms to apply with the header. Left out on a customer change, the new customer's defaults are copied
    /// server-side (design 58); given, they are kept as posted. Ignored when neither the customer changes nor terms are posted.
    /// </summary>
    public TransportOrderFinancialTermsInputDto? FinancialTerms { get; set; }

    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderLinesUpdateDto : IReferenceDataUpdateDto
{
    [Required] public List<TransportOrderLineInputDto> Lines { get; set; } = new();
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderDefaultStopsUpdateDto : IReferenceDataUpdateDto
{
    [Required] public List<TransportOrderDefaultStopInputDto> DefaultStops { get; set; } = new();
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderReferencesUpdateDto : IReferenceDataUpdateDto
{
    [Required] public List<TransportOrderReferenceEntryDto> References { get; set; } = new();
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderFinancialTermsUpdateDto : TransportOrderFinancialTermsInputDto, IReferenceDataUpdateDto
{
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderTripRequestUpdateDto : IReferenceDataUpdateDto
{
    /// <summary>Null removes the request.</summary>
    public TransportOrderTripRequestInputDto? TripRequest { get; set; }
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderAttributesUpdateDto : IReferenceDataUpdateDto
{
    [Required] public Dictionary<string, string> Attributes { get; set; } = new();
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TransportOrderReviseDto
{
    [Required, MaxLength(OrdersConsts.MaxRevisionReasonLength)]
    public string Reason { get; set; } = string.Empty;
}

public class TransportOrderApproveDto
{
    /// <summary>The hash the validation returned; a different evaluation now is ApprovalResultsStale.</summary>
    [Required, MaxLength(64)]
    public string Hash { get; set; } = string.Empty;
}

public class TransportOrderBulkDeleteRequestDto : ReferenceDataBulkDeleteRequestDto
{
    public List<TransportOrderStatus> Status { get; set; } = new();
    public TransportOrderView View { get; set; } = TransportOrderView.All;
}

public class TransportOrderDto : ReferenceDataDetailDtoBase
{
    public string? Number { get; set; }
    public TransportOrderStatus Status { get; set; }
    public Guid TransportOrderTypeId { get; set; }
    public string TransportOrderTypeName { get; set; } = string.Empty;
    public TransportOrderRequiredFields RequiredFields { get; set; }
    public Guid CustomerRelationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid? CustomerContactPersonId { get; set; }
    public DateOnly OrderDate { get; set; }
    public string? CustomerReference { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public string? InternalRemarks { get; set; }
    public string? InvoicingRemarks { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public OrderSource Source { get; set; }
    public decimal? DraftConfidence { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public int CopyIndex { get; set; }
    public TransportOrderFinancialTermsDto FinancialTerms { get; set; } = new();
    public TransportOrderTripRequestDto? TripRequest { get; set; }
    public List<TransportOrderDefaultStopDto> DefaultStops { get; set; } = new();
    public List<TransportOrderLineDto> Lines { get; set; } = new();
    public List<TransportOrderReferenceEntryDto> References { get; set; } = new();
    public List<TransportOrderRemarkDto> Remarks { get; set; } = new();
    public TransportOrderSummaryDto Summary { get; set; } = new();
}

/// <summary>The stored derived values and their calculation state (design 49, 56).</summary>
public class TransportOrderSummaryDto
{
    public Guid Id { get; set; }
    public decimal? RevenueAmount { get; set; }
    public decimal? CostOfRevenueAmount { get; set; }
    public decimal? MarginAmount { get; set; }
    public decimal? Co2Kg { get; set; }
    public ExecutionProgress ExecutionProgress { get; set; }
    public int LineCount { get; set; }
    public int StopCount { get; set; }
    public string? FirstStopName { get; set; }
    public string? FirstStopCity { get; set; }
    public DateOnly? FirstStopDate { get; set; }
    public string? LastStopName { get; set; }
    public string? LastStopCity { get; set; }
    public DateOnly? LastStopDate { get; set; }
    public string? TripNumbers { get; set; }
    public DateTime? CalculatedAt { get; set; }
    /// <summary>Null when never requested.</summary>
    public DerivedValueStateDto? State { get; set; }
}

// ── approval ───────────────────────────────────────────────────────────────────

public class ApprovalValidateRequestDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxBulkDeleteRows)]
    public List<Guid> Ids { get; set; } = new();
}

public class ApprovalApproveRequestDto : ApprovalValidateRequestDto
{
    [Required, MaxLength(64)]
    public string Hash { get; set; } = string.Empty;
}

public class ApprovalResultDto
{
    public ApprovalRule Rule { get; set; }
    public ApprovalSeverity Severity { get; set; }
    public TransportOrderStatus ProposedStatus { get; set; }
    public string MessageKey { get; set; } = string.Empty;
    /// <summary>The message in the request culture.</summary>
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new();
}

public class OrderApprovalDto
{
    public Guid OrderId { get; set; }
    public string? Number { get; set; }
    public TransportOrderStatus CurrentStatus { get; set; }
    public TransportOrderStatus ProposedStatus { get; set; }
    public bool CanApprove { get; set; }
    public List<ApprovalResultDto> Results { get; set; } = new();
}

public class ApprovalValidationDto
{
    public List<OrderApprovalDto> Orders { get; set; } = new();
    /// <summary>Send back with Approve.</summary>
    public string Hash { get; set; } = string.Empty;
}

public class ApprovalOutcomeDto
{
    public List<Guid> ApprovedIds { get; set; } = new();
    public List<OrderApprovalDto> Skipped { get; set; } = new();
}

// ── preview ────────────────────────────────────────────────────────────────────

/// <summary>The unsaved state the preview prices: null parts fall back to the stored order.</summary>
public class PricePreviewRequestDto
{
    public List<TransportOrderLineInputDto>? Lines { get; set; }
    public TransportOrderFinancialTermsInputDto? FinancialTerms { get; set; }
    public DateOnly? OrderDate { get; set; }
    public Guid? CustomerRelationId { get; set; }
}

/// <summary>A new, never-saved order as it stands on the screen: nothing falls back to a stored order; terms left out
/// are the customer's defaults and a missing date is today, as Create has them.</summary>
public class NewOrderPricePreviewRequestDto
{
    [Required]
    public Guid? CustomerRelationId { get; set; }
    public DateOnly? OrderDate { get; set; }
    public TransportOrderFinancialTermsInputDto? FinancialTerms { get; set; }
    public List<TransportOrderLineInputDto> Lines { get; set; } = new();
}

public interface ITransportOrderAppService : IApplicationService
{
    Task<GridResponse<TransportOrderRowDto>> GetListAsync(TransportOrderListRequestDto input);
    Task<Volo.Abp.Content.IRemoteStreamContent> ExportAsync(TransportOrderExportRequestDto input);
    Task<TransportOrderDto> GetAsync(Guid id);
    Task<TransportOrderDto> CreateAsync(TransportOrderCreateDto input);
    Task<TransportOrderDto> UpdateAsync(Guid id, TransportOrderUpdateDto input);
    Task<TransportOrderDto> UpdateLinesAsync(Guid id, TransportOrderLinesUpdateDto input);
    Task<TransportOrderDto> AddLineAsync(Guid id, TransportOrderLineInputDto input);
    Task<TransportOrderDto> UpdateLineAsync(Guid id, Guid lineId, TransportOrderLineInputDto input);
    Task<TransportOrderDto> DeleteLineAsync(Guid id, Guid lineId);
    Task<TransportOrderDto> UpdateDefaultStopsAsync(Guid id, TransportOrderDefaultStopsUpdateDto input);
    Task<TransportOrderDto> UpdateReferencesAsync(Guid id, TransportOrderReferencesUpdateDto input);
    Task<TransportOrderDto> UpdateFinancialTermsAsync(Guid id, TransportOrderFinancialTermsUpdateDto input);
    Task<TransportOrderDto> UpdateTripRequestAsync(Guid id, TransportOrderTripRequestUpdateDto input);
    Task<TransportOrderDto> UpdateAttributesAsync(Guid id, TransportOrderAttributesUpdateDto input);
    Task<TransportOrderDto> ConfirmAsync(Guid id);
    Task<TransportOrderDto> CancelAsync(Guid id);
    Task<TransportOrderDto> ApproveAsync(Guid id, TransportOrderApproveDto input);
    Task<TransportOrderDto> ReviseAsync(Guid id, TransportOrderReviseDto input);
    Task<TransportOrderDto> ArchiveAsync(Guid id);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(TransportOrderBulkDeleteRequestDto input);
    Task<ApprovalValidationDto> ValidateApprovalAsync(ApprovalValidateRequestDto input);
    Task<ApprovalOutcomeDto> ApproveManyAsync(ApprovalApproveRequestDto input);
    Task<PricePreviewDto> PreviewPricesAsync(Guid id, PricePreviewRequestDto input);
    Task<PricePreviewDto> PreviewNewOrderPricesAsync(NewOrderPricePreviewRequestDto input);
    Task<TransportOrderSummaryDto> GetSummaryAsync(Guid id);
}
