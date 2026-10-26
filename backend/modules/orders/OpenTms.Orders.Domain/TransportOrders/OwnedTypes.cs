using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/*
 * The owned value types the order and the template share (design 58). EF Core maps each as table-splitting owned
 * columns of its owner, so the same freight facts, stop facts and financial terms live on order lines, template lines,
 * default stops, template stops, orders and templates without two copies of the rules.
 */

/// <summary>The scalar facts of a freight line (OpenTMS freight): quantities, texts, container fields.</summary>
public class FreightDetails
{
    public Guid TransportUnitId { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? ProductRelationCodeId { get; private set; }
    public Guid? TemperatureId { get; private set; }
    public Guid? VehicleTypeId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? Positions { get; private set; }
    public decimal? Colli { get; private set; }
    public double? WeightKg { get; private set; }
    public double? VolumeM3 { get; private set; }
    public double? LoadingMeters { get; private set; }
    public double? LoadFactor { get; private set; }
    public double? Epq { get; private set; }
    public double? LengthCm { get; private set; }
    public double? WidthCm { get; private set; }
    public double? HeightCm { get; private set; }
    public string? InvoiceReference { get; private set; }
    public string? Remarks { get; private set; }
    public string? InternalRemarks { get; private set; }
    public string? ShipperInstructions { get; private set; }
    public string? SpecialAgreements { get; private set; }
    public string? AttachedDocuments { get; private set; }
    public PaymentInstruction PaymentInstruction { get; private set; }
    public string? ContainerNumber { get; private set; }
    public string? SizeOrIso { get; private set; }
    public string? PinCode { get; private set; }
    public string? ShippingCompany { get; private set; }
    public Guid? ShipperRelationId { get; private set; }
    public string? Barcode { get; private set; }

    protected FreightDetails()
    {
    }

    public static FreightDetails From(FreightInput input)
    {
        Check.NotNull(input, nameof(input));
        if (input.TransportUnitId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "transportUnitId");
        }

        OrderValues.NonNegative(input.Quantity, "quantity");
        OrderValues.NonNegative(input.Positions, "positions");
        OrderValues.NonNegative(input.Colli, "colli");
        OrderValues.NonNegative(input.WeightKg, "weightKg");
        OrderValues.NonNegative(input.VolumeM3, "volumeM3");
        OrderValues.NonNegative(input.LoadingMeters, "loadingMeters");
        OrderValues.NonNegative(input.LoadFactor, "loadFactor");
        OrderValues.NonNegative(input.Epq, "epq");
        OrderValues.NonNegative(input.LengthCm, "lengthCm");
        OrderValues.NonNegative(input.WidthCm, "widthCm");
        OrderValues.NonNegative(input.HeightCm, "heightCm");

        return new FreightDetails
        {
            TransportUnitId = input.TransportUnitId,
            ProductId = input.ProductId,
            ProductRelationCodeId = input.ProductRelationCodeId,
            TemperatureId = input.TemperatureId,
            VehicleTypeId = input.VehicleTypeId,
            Quantity = input.Quantity,
            Positions = input.Positions,
            Colli = input.Colli,
            WeightKg = input.WeightKg,
            VolumeM3 = input.VolumeM3,
            LoadingMeters = input.LoadingMeters,
            LoadFactor = input.LoadFactor,
            Epq = input.Epq,
            LengthCm = input.LengthCm,
            WidthCm = input.WidthCm,
            HeightCm = input.HeightCm,
            InvoiceReference = OrderValues.Text(input.InvoiceReference, OrdersConsts.MaxLineTextLength, "invoiceReference"),
            Remarks = OrderValues.Text(input.Remarks, OrdersConsts.MaxRemarksLength, "remarks"),
            InternalRemarks = OrderValues.Text(input.InternalRemarks, OrdersConsts.MaxRemarksLength, "internalRemarks"),
            ShipperInstructions = OrderValues.Text(input.ShipperInstructions, OrdersConsts.MaxRemarksLength, "shipperInstructions"),
            SpecialAgreements = OrderValues.Text(input.SpecialAgreements, OrdersConsts.MaxRemarksLength, "specialAgreements"),
            AttachedDocuments = OrderValues.Text(input.AttachedDocuments, OrdersConsts.MaxLineTextLength, "attachedDocuments"),
            PaymentInstruction = OrderValues.Defined(input.PaymentInstruction, "paymentInstruction"),
            ContainerNumber = OrderValues.Text(input.ContainerNumber, OrdersConsts.MaxContainerFieldLength, "containerNumber"),
            SizeOrIso = OrderValues.Text(input.SizeOrIso, OrdersConsts.MaxContainerFieldLength, "sizeOrIso"),
            PinCode = OrderValues.Text(input.PinCode, OrdersConsts.MaxContainerFieldLength, "pinCode"),
            ShippingCompany = OrderValues.Text(input.ShippingCompany, OrdersConsts.MaxContainerFieldLength, "shippingCompany"),
            ShipperRelationId = input.ShipperRelationId,
            Barcode = OrderValues.Text(input.Barcode, OrdersConsts.MaxBarcodeLength, "barcode")
        };
    }

    public FreightInput ToInput() => new(
        TransportUnitId, ProductId, ProductRelationCodeId, TemperatureId, VehicleTypeId, Quantity, Positions, Colli, WeightKg, VolumeM3,
        LoadingMeters, LoadFactor, Epq, LengthCm, WidthCm, HeightCm, InvoiceReference, Remarks, InternalRemarks, ShipperInstructions,
        SpecialAgreements, AttachedDocuments, PaymentInstruction, ContainerNumber, SizeOrIso, PinCode, ShippingCompany, ShipperRelationId, Barcode);
}

/// <summary>The place and window of a stop without its date (the owner holds a date or a day offset).</summary>
public class StopDetails
{
    public Guid WaypointTypeId { get; private set; }
    public Guid? RelationId { get; private set; }
    public Guid? RelationAddressId { get; private set; }
    public Guid? ContactPersonId { get; private set; }
    public Guid? TimeSlotId { get; private set; }
    public TimeOnly? WindowFrom { get; private set; }
    public TimeOnly? WindowTill { get; private set; }
    public string? Reference { get; private set; }
    public string? Remarks { get; private set; }
    public int? PlannedDurationMin { get; private set; }

    protected StopDetails()
    {
    }

    public static StopDetails Create(
        Guid waypointTypeId, Guid? relationId, Guid? relationAddressId, Guid? contactPersonId, Guid? timeSlotId,
        TimeOnly? windowFrom, TimeOnly? windowTill, string? reference, string? remarks, int? plannedDurationMin, int position)
    {
        if (waypointTypeId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "waypointTypeId");
        }

        /* Assumption 2: a slot or an explicit window; both absent means the whole day. */
        if (timeSlotId != null && (windowFrom != null || windowTill != null))
        {
            throw new BusinessException(OrdersErrorCodes.StopWindowAmbiguous).WithData("position", position);
        }

        if ((windowFrom == null) != (windowTill == null))
        {
            throw new BusinessException(OrdersErrorCodes.StopWindowAmbiguous).WithData("position", position);
        }

        if (plannedDurationMin is < 0 or > OrdersConsts.MaxDefaultDurationMinutes)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "plannedDurationMin").WithData("value", plannedDurationMin.Value);
        }

        return new StopDetails
        {
            WaypointTypeId = waypointTypeId,
            RelationId = relationId,
            RelationAddressId = relationAddressId,
            ContactPersonId = contactPersonId,
            TimeSlotId = timeSlotId,
            WindowFrom = windowFrom,
            WindowTill = windowTill,
            Reference = OrderValues.Text(reference, OrdersConsts.MaxStopReferenceLength, "reference"),
            Remarks = OrderValues.Text(remarks, OrdersConsts.MaxStopRemarksLength, "remarks"),
            PlannedDurationMin = plannedDurationMin
        };
    }

    /// <summary>True when the place or window differs: what makes Planning regenerate the leg.</summary>
    public bool SamePlaceAndWindowAs(StopDetails other)
        => WaypointTypeId == other.WaypointTypeId && RelationId == other.RelationId && RelationAddressId == other.RelationAddressId
           && TimeSlotId == other.TimeSlotId && WindowFrom == other.WindowFrom && WindowTill == other.WindowTill
           && PlannedDurationMin == other.PlannedDurationMin;
}

/// <summary>
/// The financial terms of an order or template (design 58): copied from the customer's financial settings on the
/// screen and validated through Financial's published lookup. The price lists and discounts/surcharges are child
/// rows of the owner (positioned), the scalars live here.
/// </summary>
public class TransportOrderFinancialTerms
{
    public string CurrencyCode { get; private set; } = "EUR";
    public Guid? VatPercentageId { get; private set; }
    public int? PaymentTermDays { get; private set; }
    public MultipleMatches MultipleMatches { get; private set; } = MultipleMatches.All;
    public decimal? FixedPriceAmount { get; private set; }
    public bool SendFreightDocumentsWithInvoice { get; private set; }

    protected TransportOrderFinancialTerms()
    {
    }

    public static TransportOrderFinancialTerms From(FinancialTermsInput input)
    {
        Check.NotNull(input, nameof(input));
        if (input.PaymentTermDays is < 0 or > 365)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "paymentTermDays").WithData("value", input.PaymentTermDays.Value);
        }

        OrderValues.NonNegative(input.FixedPriceAmount, "fixedPriceAmount");
        if (input.PriceListIds.Count > OrdersConsts.MaxPriceLists)
        {
            throw OrderValues.TooMany("priceListIds", OrdersConsts.MaxPriceLists);
        }

        if (input.DiscountsOrSurcharges.Count > OrdersConsts.MaxDiscountsOrSurcharges)
        {
            throw OrderValues.TooMany("discountsOrSurcharges", OrdersConsts.MaxDiscountsOrSurcharges);
        }

        if (input.PriceListIds.Distinct().Count() != input.PriceListIds.Count
            || input.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId).Distinct().Count() != input.DiscountsOrSurcharges.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "financialTerms").WithData("value", "");
        }

        return new TransportOrderFinancialTerms
        {
            CurrencyCode = OrderValues.CurrencyCode(input.CurrencyCode),
            VatPercentageId = input.VatPercentageId,
            PaymentTermDays = input.PaymentTermDays,
            MultipleMatches = OrderValues.Defined(input.MultipleMatches, "multipleMatches"),
            FixedPriceAmount = input.FixedPriceAmount,
            SendFreightDocumentsWithInvoice = input.SendFreightDocumentsWithInvoice
        };
    }

    public static FinancialTermsInput Default(string currencyCode = "EUR")
        => new(currencyCode, null, null, [], MultipleMatches.All, [], null, false);
}

/// <summary>The "Tevens trip" request (Q9), owned by the order; <see cref="TripId"/> is written by Planning's event.</summary>
public class TransportOrderTripRequest
{
    public Guid TransportTripTypeId { get; private set; }
    public Guid DepartureRelationId { get; private set; }
    public Guid DepartureAddressId { get; private set; }
    public Guid ArrivalRelationId { get; private set; }
    public Guid ArrivalAddressId { get; private set; }
    public DateTimeOffset PlannedDeparture { get; private set; }
    public DateTimeOffset? PlannedArrival { get; private set; }
    public Guid? CarrierRelationId { get; private set; }
    public Guid? CarrierContactPersonId { get; private set; }
    public Guid? VehicleCombinationId { get; private set; }
    public List<Guid> VehicleIds { get; private set; } = new();
    public List<Guid> DriverIds { get; private set; } = new();
    public decimal? AgreedPurchaseAmount { get; private set; }
    public string? AgreedPurchaseCurrencyCode { get; private set; }
    public Guid? TripId { get; private set; }

    /// <summary>Planning could not honour the request on Confirm (design 59 build note 13): the warning code, else null.</summary>
    public string? WarningCode { get; private set; }

    /// <summary>The request field that did not resolve in Planning.</summary>
    public string? WarningField { get; private set; }

    public DateTimeOffset? WarningAt { get; private set; }

    protected TransportOrderTripRequest()
    {
    }

    public static TransportOrderTripRequest From(TripRequestInput input, Guid? keepTripId, TransportOrderTripRequest? previous = null)
    {
        Check.NotNull(input, nameof(input));
        foreach (var (id, field) in new[] { (input.TransportTripTypeId, "transportTripTypeId"), (input.DepartureRelationId, "departureRelationId"), (input.DepartureAddressId, "departureAddressId"), (input.ArrivalRelationId, "arrivalRelationId"), (input.ArrivalAddressId, "arrivalAddressId") })
        {
            if (id == Guid.Empty)
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field);
            }
        }

        if (input.PlannedArrival != null && input.PlannedArrival < input.PlannedDeparture)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "plannedArrival").WithData("value", input.PlannedArrival.Value);
        }

        var vehicles = input.VehicleIds.Where(v => v != Guid.Empty).Distinct().ToList();
        if (vehicles.Count > OrdersConsts.MaxTripRequestVehicles)
        {
            throw new BusinessException(OrdersErrorCodes.TooManyTripRequestMembers).WithData("field", "vehicleIds").WithData("max", OrdersConsts.MaxTripRequestVehicles);
        }

        var drivers = input.DriverIds.Where(d => d != Guid.Empty).Distinct().ToList();
        if (drivers.Count > OrdersConsts.MaxTripRequestDrivers)
        {
            throw new BusinessException(OrdersErrorCodes.TooManyTripRequestMembers).WithData("field", "driverIds").WithData("max", OrdersConsts.MaxTripRequestDrivers);
        }

        OrderValues.NonNegative(input.AgreedPurchaseAmount, "agreedPurchaseAmount");
        return new TransportOrderTripRequest
        {
            TransportTripTypeId = input.TransportTripTypeId,
            DepartureRelationId = input.DepartureRelationId,
            DepartureAddressId = input.DepartureAddressId,
            ArrivalRelationId = input.ArrivalRelationId,
            ArrivalAddressId = input.ArrivalAddressId,
            PlannedDeparture = input.PlannedDeparture,
            PlannedArrival = input.PlannedArrival,
            CarrierRelationId = input.CarrierRelationId,
            CarrierContactPersonId = input.CarrierContactPersonId,
            VehicleCombinationId = input.VehicleCombinationId,
            VehicleIds = vehicles,
            DriverIds = drivers,
            AgreedPurchaseAmount = input.AgreedPurchaseAmount,
            AgreedPurchaseCurrencyCode = input.AgreedPurchaseAmount == null ? null : OrderValues.CurrencyCode(input.AgreedPurchaseCurrencyCode ?? "EUR"),
            TripId = keepTripId,
            /* The request is honoured once, on Confirm: a refusal stays the fact of that confirm until a trip exists. */
            WarningCode = keepTripId == null ? previous?.WarningCode : null,
            WarningField = keepTripId == null ? previous?.WarningField : null,
            WarningAt = keepTripId == null ? previous?.WarningAt : null
        };
    }

    /// <summary>Planning reported the trip it created from this request (idempotent: the same id again changes nothing).</summary>
    public void SetTrip(Guid tripId)
    {
        TripId = tripId;
        WarningCode = null;
        WarningField = null;
        WarningAt = null;
    }

    /// <summary>Planning reported it could not honour the request; true when this changed the stored warning (idempotent).</summary>
    public bool SetNotHonoured(string code, string? field, DateTimeOffset at)
    {
        var trimmedField = string.IsNullOrWhiteSpace(field) ? null : field.Trim();
        if (trimmedField is { Length: > OrdersConsts.MaxTripRequestWarningLength })
        {
            trimmedField = trimmedField[..OrdersConsts.MaxTripRequestWarningLength];
        }

        if (TripId != null || (WarningCode == code && WarningField == trimmedField))
        {
            return false;
        }

        WarningCode = code;
        WarningField = trimmedField;
        WarningAt = at;
        return true;
    }

    public TripRequestInput ToInput() => new(
        TransportTripTypeId, DepartureRelationId, DepartureAddressId, ArrivalRelationId, ArrivalAddressId, PlannedDeparture, PlannedArrival,
        CarrierRelationId, CarrierContactPersonId, VehicleCombinationId, VehicleIds.ToList(), DriverIds.ToList(), AgreedPurchaseAmount, AgreedPurchaseCurrencyCode);
}
