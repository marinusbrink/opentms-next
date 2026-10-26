using System;
using System.Collections.Generic;
using OpenTms.Financial.Tariffs;

namespace OpenTms.Orders.TransportOrders;

/*
 * The inputs the order and template aggregates take (design 58). The application service maps DTOs to these records
 * after checking the cross-module ids through the published lookups; the aggregate validates bounds and shape. The
 * template materialiser produces the same records from a template, so one code path builds every order.
 */

/// <summary>The scalar facts of a line (OpenTMS freight); shared by order lines and template lines.</summary>
public sealed record FreightInput(
    Guid TransportUnitId,
    Guid? ProductId,
    Guid? ProductRelationCodeId,
    Guid? TemperatureId,
    Guid? VehicleTypeId,
    decimal Quantity,
    decimal? Positions,
    decimal? Colli,
    double? WeightKg,
    double? VolumeM3,
    double? LoadingMeters,
    double? LoadFactor,
    double? Epq,
    double? LengthCm,
    double? WidthCm,
    double? HeightCm,
    string? InvoiceReference,
    string? Remarks,
    string? InternalRemarks,
    string? ShipperInstructions,
    string? SpecialAgreements,
    string? AttachedDocuments,
    PaymentInstruction PaymentInstruction,
    string? ContainerNumber,
    string? SizeOrIso,
    string? PinCode,
    string? ShippingCompany,
    Guid? ShipperRelationId,
    string? Barcode);

public sealed record MeasurementInput(MeasurementKind Kind, int Position, double Value);

/// <summary>The place and time a line asks for (OpenTMS waypoint); the window is a slot or an explicit pair, never both.</summary>
public sealed record StopInput(
    Guid? Id,
    Guid WaypointTypeId,
    Guid RelationId,
    Guid RelationAddressId,
    Guid? ContactPersonId,
    DateOnly Date,
    Guid? TimeSlotId,
    TimeOnly? WindowFrom,
    TimeOnly? WindowTill,
    string? Reference,
    string? Remarks,
    int? PlannedDurationMin);

/// <summary>A stop of the order's pattern (the waypoint strip): the relation may still be open; the date is an offset.</summary>
public sealed record DefaultStopInput(
    Guid WaypointTypeId,
    Guid? RelationId,
    Guid? RelationAddressId,
    Guid? ContactPersonId,
    int DayOffset,
    Guid? TimeSlotId,
    TimeOnly? WindowFrom,
    TimeOnly? WindowTill,
    int? PlannedDurationMin);

public sealed record LineInput(
    Guid? Id,
    FreightInput Freight,
    IReadOnlyList<MeasurementInput> Measurements,
    IReadOnlyList<Guid> RequiredTransportCharacteristicIds,
    IReadOnlyList<StopInput> Stops,
    IReadOnlyDictionary<string, string>? Attributes);

public sealed record ReferenceInput(string Type, string Value, string? Description);

public sealed record DiscountOrSurchargeInput(Guid DiscountOrSurchargeId, DiscountOrSurchargeSource Source);

public sealed record FinancialTermsInput(
    string CurrencyCode,
    Guid? VatPercentageId,
    int? PaymentTermDays,
    IReadOnlyList<Guid> PriceListIds,
    MultipleMatches MultipleMatches,
    IReadOnlyList<DiscountOrSurchargeInput> DiscountsOrSurcharges,
    decimal? FixedPriceAmount,
    bool SendFreightDocumentsWithInvoice);

/// <summary>
/// "Tevens trip" (PO decision Q9): the trip Planning creates on Confirm. <paramref name="LineIds"/> (design 61 "Order page,
/// revised" point 7) are the lines whose legs go on the trip: null = every line; an empty list = none (a selection that
/// lost its last line; the API maps an empty list to null). In a draft they name the draft's lines by their input id.
/// </summary>
public sealed record TripRequestInput(
    Guid TransportTripTypeId,
    Guid DepartureRelationId,
    Guid DepartureAddressId,
    Guid ArrivalRelationId,
    Guid ArrivalAddressId,
    DateTimeOffset PlannedDeparture,
    DateTimeOffset? PlannedArrival,
    Guid? CarrierRelationId,
    Guid? CarrierContactPersonId,
    Guid? VehicleCombinationId,
    IReadOnlyList<Guid> VehicleIds,
    IReadOnlyList<Guid> DriverIds,
    decimal? AgreedPurchaseAmount,
    string? AgreedPurchaseCurrencyCode,
    IReadOnlyList<Guid>? LineIds = null);

/// <summary>The header of an order as created or updated.</summary>
public sealed record OrderHeaderInput(
    Guid TransportOrderTypeId,
    Guid CustomerRelationId,
    Guid? CustomerContactPersonId,
    string CustomerName,
    DateOnly OrderDate,
    string? CustomerReference,
    Guid? OrganizationUnitId,
    string? InternalRemarks,
    string? InvoicingRemarks);

/// <summary>Everything a new order is made of: the one shape Create, the template materialiser and a copy produce.</summary>
public sealed record TransportOrderDraft(
    OrderHeaderInput Header,
    FinancialTermsInput Terms,
    IReadOnlyList<DefaultStopInput> DefaultStops,
    IReadOnlyList<LineInput> Lines,
    IReadOnlyList<ReferenceInput> References,
    TripRequestInput? TripRequest,
    IReadOnlyDictionary<string, string>? Attributes);
