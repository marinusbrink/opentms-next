using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Localization;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Aggregates → DTOs with the names of the referenced rows (design 58): every name set is one batched read per table,
/// never one per row; a referenced row that disappeared shows "(not found)" (design 51 precedent).
/// </summary>
public class TransportOrderDtoMapper : ITransientDependency
{
    private readonly OrdersReferences _references;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IDerivedValueStateStore _states;
    private readonly ITransportTripLookup _trips;
    private readonly IStringLocalizer<OrdersResource> _l;

    public TransportOrderDtoMapper(
        OrdersReferences references,
        IRepository<TransportOrderType, Guid> orderTypes,
        IDerivedValueStateStore states,
        ITransportTripLookup trips,
        IStringLocalizer<OrdersResource> l)
    {
        _references = references;
        _orderTypes = orderTypes;
        _states = states;
        _trips = trips;
        _l = l;
    }

    private string NotFound => _l["Orders:NotFound"];

    private static string Culture => CultureInfo.CurrentUICulture.Name;

    public async Task<List<TransportOrderRowDto>> ToRowsAsync(IReadOnlyList<TransportOrder> orders)
    {
        var typeIds = orders.Select(o => o.TransportOrderTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? [] : await _orderTypes.GetListAsync(t => typeIds.Contains(t.Id));
        var typeNames = types.ToDictionary(t => t.Id, t => t.Name.Resolve(Culture));
        return orders.Select(o => new TransportOrderRowDto
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            OrderDate = o.OrderDate,
            TransportOrderTypeId = o.TransportOrderTypeId,
            TransportOrderTypeName = typeNames.GetValueOrDefault(o.TransportOrderTypeId, NotFound),
            CustomerRelationId = o.CustomerRelationId,
            CustomerName = o.CustomerName,
            CustomerReference = o.CustomerReference,
            OrganizationUnitId = o.OrganizationUnitId,
            Source = o.Source,
            SourceTemplateId = o.SourceTemplateId,
            CurrencyCode = o.FinancialTerms.CurrencyCode,
            LineCount = o.LineCount,
            StopCount = o.StopCount,
            FirstStopName = o.FirstStopName,
            FirstStopCity = o.FirstStopCity,
            FirstStopDate = o.FirstStopDate,
            LastStopName = o.LastStopName,
            LastStopCity = o.LastStopCity,
            LastStopDate = o.LastStopDate,
            RevenueAmount = o.RevenueAmount,
            CostOfRevenueAmount = o.CostOfRevenueAmount,
            MarginAmount = o.MarginAmount,
            Co2Kg = o.Co2Kg,
            ExecutionProgress = o.ExecutionProgress,
            TripNumbers = o.TripNumbers,
            HasTripRequest = o.TripRequest != null,
            TripRequestWarningCode = o.TripRequest?.WarningCode,
            CreationTime = o.CreationTime,
            LastModificationTime = o.LastModificationTime
        }).ToList();
    }

    public async Task<TransportOrderDto> ToDtoAsync(TransportOrder o)
    {
        var type = await _orderTypes.FindAsync(o.TransportOrderTypeId);
        var stops = o.Lines.SelectMany(l => l.Stops).ToList();
        var relationIds = stops.Select(s => s.Details.RelationId).OfType<Guid>()
            .Concat(o.DefaultStops.Select(s => s.Details.RelationId).OfType<Guid>())
            .Concat(o.Lines.Select(l => l.Freight.ShipperRelationId).OfType<Guid>());
        if (o.TripRequest != null)
        {
            relationIds = relationIds.Concat(new[] { o.TripRequest.DepartureRelationId, o.TripRequest.ArrivalRelationId }).Concat(new[] { o.TripRequest.CarrierRelationId }.OfType<Guid>());
        }

        var relations = await _references.RelationsAsync(relationIds, require: false);
        var addresses = await _references.AddressesAsync(stops.Select(s => s.Details.RelationAddressId).OfType<Guid>(), require: false);
        var waypointTypes = await _references.WaypointTypesAsync(stops.Select(s => s.Details.WaypointTypeId).Concat(o.DefaultStops.Select(s => s.Details.WaypointTypeId)), require: false);
        var slots = await _references.TimeSlotsAsync(stops.Select(s => s.Details.TimeSlotId).OfType<Guid>(), require: false);
        var units = await _references.NamesAsync(MasterDataReferenceKind.TransportUnit, o.Lines.Select(l => l.Freight.TransportUnitId), require: false, "transportUnitId");
        var products = await _references.NamesAsync(MasterDataReferenceKind.Product, o.Lines.Select(l => l.Freight.ProductId).OfType<Guid>(), require: false, "productId");
        var codes = await _references.NamesAsync(MasterDataReferenceKind.ProductRelationCode, o.Lines.Select(l => l.Freight.ProductRelationCodeId).OfType<Guid>(), require: false, "productRelationCodeId");
        var temperatures = await _references.NamesAsync(MasterDataReferenceKind.Temperature, o.Lines.Select(l => l.Freight.TemperatureId).OfType<Guid>(), require: false, "temperatureId");
        var vehicleTypes = await _references.NamesAsync(MasterDataReferenceKind.VehicleType, o.Lines.Select(l => l.Freight.VehicleTypeId).OfType<Guid>(), require: false, "vehicleTypeId");
        var characteristics = await _references.NamesAsync(MasterDataReferenceKind.TransportCharacteristic, o.Lines.SelectMany(l => l.RequiredTransportCharacteristics).Select(c => c.TransportCharacteristicId), require: false, "requiredTransportCharacteristicIds");
        var financial = await _references.FinancialNamesAsync(o.FinancialTermsInput());
        var tripNumber = o.TripRequest?.TripId == null ? null : (await _trips.GetAsync([o.TripRequest.TripId.Value])).FirstOrDefault()?.Number;

        string RelationName(Guid? id) => id != null && relations.TryGetValue(id.Value, out var r) ? r.Name : NotFound;

        var dto = new TransportOrderDto
        {
            Id = o.Id,
            Number = o.Number,
            Status = o.Status,
            TransportOrderTypeId = o.TransportOrderTypeId,
            TransportOrderTypeName = type?.Name.Resolve(Culture) ?? NotFound,
            RequiredFields = type?.RequiredFields ?? TransportOrderRequiredFields.None,
            CustomerRelationId = o.CustomerRelationId,
            CustomerName = o.CustomerName,
            CustomerContactPersonId = o.CustomerContactPersonId,
            OrderDate = o.OrderDate,
            CustomerReference = o.CustomerReference,
            OrganizationUnitId = o.OrganizationUnitId,
            InternalRemarks = o.InternalRemarks,
            InvoicingRemarks = o.InvoicingRemarks,
            Attributes = new Dictionary<string, string>(o.Attributes),
            Source = o.Source,
            DraftConfidence = o.DraftConfidence,
            SourceTemplateId = o.SourceTemplateId,
            CopyIndex = o.CopyIndex,
            FinancialTerms = TermsDto(o.FinancialTermsInput(), financial),
            TripRequest = o.TripRequest == null ? null : new TransportOrderTripRequestDto
            {
                TransportTripTypeId = o.TripRequest.TransportTripTypeId,
                DepartureRelationId = o.TripRequest.DepartureRelationId,
                DepartureAddressId = o.TripRequest.DepartureAddressId,
                ArrivalRelationId = o.TripRequest.ArrivalRelationId,
                ArrivalAddressId = o.TripRequest.ArrivalAddressId,
                PlannedDeparture = o.TripRequest.PlannedDeparture,
                PlannedArrival = o.TripRequest.PlannedArrival,
                CarrierRelationId = o.TripRequest.CarrierRelationId,
                CarrierContactPersonId = o.TripRequest.CarrierContactPersonId,
                VehicleCombinationId = o.TripRequest.VehicleCombinationId,
                VehicleIds = o.TripRequest.VehicleIds.ToList(),
                DriverIds = o.TripRequest.DriverIds.ToList(),
                AgreedPurchaseAmount = o.TripRequest.AgreedPurchaseAmount,
                AgreedPurchaseCurrencyCode = o.TripRequest.AgreedPurchaseCurrencyCode,
                LineIds = o.TripRequest.LineIds?.ToList(),
                DepartureRelationName = RelationName(o.TripRequest.DepartureRelationId),
                ArrivalRelationName = RelationName(o.TripRequest.ArrivalRelationId),
                CarrierRelationName = o.TripRequest.CarrierRelationId == null ? null : RelationName(o.TripRequest.CarrierRelationId),
                TripId = o.TripRequest.TripId,
                TripNumber = tripNumber,
                WarningCode = o.TripRequest.WarningCode,
                WarningField = o.TripRequest.WarningField,
                WarningAt = o.TripRequest.WarningAt
            },
            DefaultStops = o.DefaultStops.OrderBy(s => s.Position).Select(s => new TransportOrderDefaultStopDto
            {
                Position = s.Position,
                WaypointTypeId = s.Details.WaypointTypeId,
                WaypointTypeName = waypointTypes.TryGetValue(s.Details.WaypointTypeId, out var w) ? w.Name.Resolve(Culture) : NotFound,
                RelationId = s.Details.RelationId,
                RelationName = s.Details.RelationId == null ? null : RelationName(s.Details.RelationId),
                RelationAddressId = s.Details.RelationAddressId,
                ContactPersonId = s.Details.ContactPersonId,
                DayOffset = s.DayOffset,
                TimeSlotId = s.Details.TimeSlotId,
                WindowFrom = s.Details.WindowFrom,
                WindowTill = s.Details.WindowTill,
                PlannedDurationMin = s.Details.PlannedDurationMin
            }).ToList(),
            Lines = o.Lines.OrderBy(l => l.Position).Select(l =>
            {
                var dto = new TransportOrderLineDto
                {
                    Id = l.Id,
                    Position = l.Position,
                    Measurements = l.Measurements.OrderBy(m => m.Kind).ThenBy(m => m.Position).Select(m => new TransportOrderMeasurementDto { Kind = m.Kind, Position = m.Position, Value = m.Value }).ToList(),
                    RequiredTransportCharacteristicIds = l.RequiredTransportCharacteristics.Select(c => c.TransportCharacteristicId).ToList(),
                    RequiredTransportCharacteristicNames = l.RequiredTransportCharacteristics.Select(c => characteristics.GetValueOrDefault(c.TransportCharacteristicId, NotFound)).ToList(),
                    Attributes = new Dictionary<string, string>(l.Attributes),
                    TransportUnitName = units.GetValueOrDefault(l.Freight.TransportUnitId, NotFound),
                    ProductName = l.Freight.ProductId == null ? null : products.GetValueOrDefault(l.Freight.ProductId.Value, NotFound),
                    ProductRelationCodeName = l.Freight.ProductRelationCodeId == null ? null : codes.GetValueOrDefault(l.Freight.ProductRelationCodeId.Value, NotFound),
                    TemperatureName = l.Freight.TemperatureId == null ? null : temperatures.GetValueOrDefault(l.Freight.TemperatureId.Value, NotFound),
                    VehicleTypeName = l.Freight.VehicleTypeId == null ? null : vehicleTypes.GetValueOrDefault(l.Freight.VehicleTypeId.Value, NotFound),
                    ShipperRelationName = l.Freight.ShipperRelationId == null ? null : RelationName(l.Freight.ShipperRelationId),
                    DistanceM = l.DistanceM,
                    DurationS = l.DurationS,
                    Co2Kg = l.Co2Kg,
                    Stops = l.Stops.OrderBy(s => s.Position).Select(s =>
                    {
                        addresses.TryGetValue(s.Details.RelationAddressId!.Value, out var address);
                        return new TransportOrderStopDto
                        {
                            Id = s.Id,
                            Position = s.Position,
                            WaypointTypeId = s.Details.WaypointTypeId,
                            WaypointTypeName = waypointTypes.TryGetValue(s.Details.WaypointTypeId, out var w) ? w.Name.Resolve(Culture) : NotFound,
                            RelationId = s.Details.RelationId!.Value,
                            RelationName = RelationName(s.Details.RelationId),
                            RelationAddressId = s.Details.RelationAddressId!.Value,
                            AddressLine = address == null ? null : string.Join(" ", new[] { address.Street, address.HouseNumber }.Where(p => !string.IsNullOrEmpty(p))),
                            City = address?.City,
                            CountryCode = address?.CountryCode,
                            ContactPersonId = s.Details.ContactPersonId,
                            Date = s.Date,
                            TimeSlotId = s.Details.TimeSlotId,
                            TimeSlotName = s.Details.TimeSlotId != null && slots.TryGetValue(s.Details.TimeSlotId.Value, out var slot) ? slot.Name.Resolve(Culture) : null,
                            WindowFrom = s.Details.WindowFrom,
                            WindowTill = s.Details.WindowTill,
                            Reference = s.Details.Reference,
                            Remarks = s.Details.Remarks,
                            PlannedDurationMin = s.Details.PlannedDurationMin
                        };
                    }).ToList()
                };
                CopyFreight(l.Freight, dto);
                return dto;
            }).ToList(),
            References = o.References.OrderBy(r => r.Position).Select(r => new TransportOrderReferenceEntryDto { Type = r.Type, Value = r.Value, Description = r.Description }).ToList(),
            Remarks = o.Remarks.OrderBy(r => r.At).Select(r => new TransportOrderRemarkDto { Kind = r.Kind, Text = r.Text, At = r.At, UserId = r.UserId }).ToList(),
            Summary = await SummaryAsync(o)
        };
        dto.CreationTime = o.CreationTime;
        dto.CreatorId = o.CreatorId;
        dto.LastModificationTime = o.LastModificationTime;
        dto.LastModifierId = o.LastModifierId;
        dto.ConcurrencyStamp = o.ConcurrencyStamp;
        return dto;
    }

    public async Task<TransportOrderSummaryDto> SummaryAsync(TransportOrder o)
    {
        var state = await _states.FindAsync(OrdersConsts.OrderSummaryTargetType, o.Id);
        return new TransportOrderSummaryDto
        {
            Id = o.Id,
            RevenueAmount = o.RevenueAmount,
            CostOfRevenueAmount = o.CostOfRevenueAmount,
            MarginAmount = o.MarginAmount,
            Co2Kg = o.Co2Kg,
            ExecutionProgress = o.ExecutionProgress,
            LineCount = o.LineCount,
            StopCount = o.StopCount,
            FirstStopName = o.FirstStopName,
            FirstStopCity = o.FirstStopCity,
            FirstStopDate = o.FirstStopDate,
            LastStopName = o.LastStopName,
            LastStopCity = o.LastStopCity,
            LastStopDate = o.LastStopDate,
            TripNumbers = o.TripNumbers,
            CalculatedAt = o.CalculatedAt,
            State = state == null ? null : new DerivedValueStateDto
            {
                TargetId = state.TargetId,
                RequestedVersion = state.RequestedVersion,
                CalculatedVersion = state.CalculatedVersion,
                Status = state.Status,
                LastErrorCode = state.LastErrorCode,
                CalculatedAt = state.CalculatedAt
            }
        };
    }

    public TransportOrderFinancialTermsDto TermsDto(FinancialTermsInput terms, FinancialNames names) => new()
    {
        CurrencyCode = terms.CurrencyCode,
        VatPercentageId = terms.VatPercentageId,
        VatPercentageName = terms.VatPercentageId == null ? null : names.VatPercentages.GetValueOrDefault(terms.VatPercentageId.Value, NotFound),
        PaymentTermDays = terms.PaymentTermDays,
        PriceListIds = terms.PriceListIds.ToList(),
        PriceListNames = terms.PriceListIds.Select(id => names.PriceLists.GetValueOrDefault(id, NotFound)).ToList(),
        MultipleMatches = terms.MultipleMatches,
        DiscountsOrSurcharges = terms.DiscountsOrSurcharges.Select(d => new TransportOrderDiscountOrSurchargeDto
        {
            DiscountOrSurchargeId = d.DiscountOrSurchargeId,
            Source = d.Source,
            Name = names.DiscountsOrSurcharges.GetValueOrDefault(d.DiscountOrSurchargeId, NotFound)
        }).ToList(),
        FixedPriceAmount = terms.FixedPriceAmount,
        SendFreightDocumentsWithInvoice = terms.SendFreightDocumentsWithInvoice
    };

    public static void CopyFreight(FreightDetails f, TransportOrderLineInputDto dto)
    {
        dto.TransportUnitId = f.TransportUnitId;
        dto.ProductId = f.ProductId;
        dto.ProductRelationCodeId = f.ProductRelationCodeId;
        dto.TemperatureId = f.TemperatureId;
        dto.VehicleTypeId = f.VehicleTypeId;
        dto.Quantity = f.Quantity;
        dto.Positions = f.Positions;
        dto.Colli = f.Colli;
        dto.WeightKg = f.WeightKg;
        dto.VolumeM3 = f.VolumeM3;
        dto.LoadingMeters = f.LoadingMeters;
        dto.LoadFactor = f.LoadFactor;
        dto.Epq = f.Epq;
        dto.LengthCm = f.LengthCm;
        dto.WidthCm = f.WidthCm;
        dto.HeightCm = f.HeightCm;
        dto.InvoiceReference = f.InvoiceReference;
        dto.Remarks = f.Remarks;
        dto.InternalRemarks = f.InternalRemarks;
        dto.ShipperInstructions = f.ShipperInstructions;
        dto.SpecialAgreements = f.SpecialAgreements;
        dto.AttachedDocuments = f.AttachedDocuments;
        dto.PaymentInstruction = f.PaymentInstruction;
        dto.ContainerNumber = f.ContainerNumber;
        dto.SizeOrIso = f.SizeOrIso;
        dto.PinCode = f.PinCode;
        dto.ShippingCompany = f.ShippingCompany;
        dto.ShipperRelationId = f.ShipperRelationId;
        dto.Barcode = f.Barcode;
    }
}
