using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Orders.TransportOrders;

/// <summary>DTOs → the aggregate's inputs (design 58). Pure; the aggregate validates shape, OrdersReferences the ids.</summary>
public static class TransportOrderInputMapper
{
    public static OrderHeaderInput Header(TransportOrderHeaderDto dto, string customerName, DateOnly orderDate)
        => new(dto.TransportOrderTypeId, dto.CustomerRelationId, dto.CustomerContactPersonId, customerName, orderDate, dto.CustomerReference, dto.OrganizationUnitId, dto.InternalRemarks, dto.InvoicingRemarks);

    public static FreightInput Freight(TransportOrderLineInputDto dto) => new(
        dto.TransportUnitId, dto.ProductId, dto.ProductRelationCodeId, dto.TemperatureId, dto.VehicleTypeId, dto.Quantity, dto.Positions, dto.Colli,
        dto.WeightKg, dto.VolumeM3, dto.LoadingMeters, dto.LoadFactor, dto.Epq, dto.LengthCm, dto.WidthCm, dto.HeightCm, dto.InvoiceReference, dto.Remarks,
        dto.InternalRemarks, dto.ShipperInstructions, dto.SpecialAgreements, dto.AttachedDocuments, dto.PaymentInstruction, dto.ContainerNumber, dto.SizeOrIso,
        dto.PinCode, dto.ShippingCompany, dto.ShipperRelationId, dto.Barcode);

    public static StopInput Stop(TransportOrderStopInputDto dto) => new(
        dto.Id, dto.WaypointTypeId, dto.RelationId, dto.RelationAddressId, dto.ContactPersonId, dto.Date, dto.TimeSlotId, dto.WindowFrom, dto.WindowTill,
        dto.Reference, dto.Remarks, dto.PlannedDurationMin);

    public static DefaultStopInput DefaultStop(TransportOrderDefaultStopInputDto dto) => new(
        dto.WaypointTypeId, dto.RelationId, dto.RelationAddressId, dto.ContactPersonId, dto.DayOffset, dto.TimeSlotId, dto.WindowFrom, dto.WindowTill, dto.PlannedDurationMin);

    public static IReadOnlyList<MeasurementInput> Measurements(IEnumerable<TransportOrderMeasurementDto> dtos)
        => dtos.Select(m => new MeasurementInput(m.Kind, m.Position, m.Value)).ToList();

    /// <summary>A line; <paramref name="patternStops"/> serve when the DTO sends no stops (a new line starts with the pattern).</summary>
    public static LineInput Line(TransportOrderLineInputDto dto, IReadOnlyList<StopInput>? patternStops) => new(
        dto.Id,
        Freight(dto),
        Measurements(dto.Measurements),
        dto.RequiredTransportCharacteristicIds,
        dto.Stops == null ? patternStops ?? [] : dto.Stops.Select(Stop).ToList(),
        dto.Attributes);

    public static ReferenceInput Reference(TransportOrderReferenceEntryDto dto) => new(dto.Type, dto.Value, dto.Description);

    public static FinancialTermsInput Terms(TransportOrderFinancialTermsInputDto? dto) => dto == null
        ? TransportOrderFinancialTerms.Default()
        : new FinancialTermsInput(
            dto.CurrencyCode, dto.VatPercentageId, dto.PaymentTermDays, dto.PriceListIds, dto.MultipleMatches,
            dto.DiscountsOrSurcharges.Select(d => new DiscountOrSurchargeInput(d.DiscountOrSurchargeId, d.Source)).ToList(),
            dto.FixedPriceAmount, dto.SendFreightDocumentsWithInvoice);

    public static TripRequestInput? TripRequest(TransportOrderTripRequestInputDto? dto) => dto == null ? null : new TripRequestInput(
        dto.TransportTripTypeId, dto.DepartureRelationId, dto.DepartureAddressId, dto.ArrivalRelationId, dto.ArrivalAddressId, dto.PlannedDeparture,
        dto.PlannedArrival, dto.CarrierRelationId, dto.CarrierContactPersonId, dto.VehicleCombinationId, dto.VehicleIds, dto.DriverIds,
        dto.AgreedPurchaseAmount, dto.AgreedPurchaseCurrencyCode);

    public static TemplateStopInput TemplateStop(TemplateStopInputDto dto) => new(
        dto.WaypointTypeId, dto.RelationId, dto.RelationAddressId, dto.ContactPersonId, dto.DayOffset, dto.TimeSlotId, dto.WindowFrom, dto.WindowTill,
        dto.Reference, dto.Remarks, dto.PlannedDurationMin);

    public static TemplateLineInput TemplateLine(TemplateLineInputDto dto) => new(
        Freight(dto), Measurements(dto.Measurements), dto.RequiredTransportCharacteristicIds, dto.Stops.Select(TemplateStop).ToList(), dto.Attributes);

    public static TemplateTripRequestInput? TemplateTripRequest(TemplateTripRequestInputDto? dto) => dto == null ? null : new TemplateTripRequestInput(
        dto.TransportTripTypeId, dto.DepartureRelationId, dto.DepartureAddressId, dto.ArrivalRelationId, dto.ArrivalAddressId, dto.DepartureDayOffset,
        dto.DepartureTime, dto.ArrivalDayOffset, dto.ArrivalTime, dto.CarrierRelationId, dto.CarrierContactPersonId, dto.VehicleCombinationId,
        dto.VehicleIds, dto.DriverIds, dto.AgreedPurchaseAmount, dto.AgreedPurchaseCurrencyCode);

    public static TemplateSchedule Schedule(TemplateScheduleDto dto)
        => TemplateSchedule.Create(dto.Recurrence, dto.Interval, dto.DaysOfWeek, dto.DayOfMonth, dto.HorizonDays, dto.ValidFrom, dto.ValidTill);
}
