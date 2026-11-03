using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.Intake;

/*
 * The canonical intake model (design 72 "Mapping, submit, trace"): the one shape every channel maps to — a mail
 * attachment read by an import profile or the AI, and the intake API's JSON body. Values arrive as the document states
 * them (names, codes, texts); ids are optional everywhere, so an API party that knows our ids may send them. Dates are
 * "yyyy-MM-dd" and times "HH:mm" strings, kept as received so the resolver, not the deserializer, reports a bad one on
 * its field path. Field paths use camel case: order.customerReference, lines[0].stops[1].address.postalCode.
 */

public class OrderIntakeDto
{
    [MaxLength(OrdersConsts.MaxCustomerReferenceLength)]
    public string? CustomerReference { get; set; }

    /// <summary>yyyy-MM-dd.</summary>
    [MaxLength(IntakeConsts.DateLength)]
    public string? OrderDate { get; set; }

    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? OrderTypeName { get; set; }

    public Guid? TransportOrderTypeId { get; set; }

    [MaxLength(OrdersConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    public OrderIntakeCustomerDto? Customer { get; set; }

    [MaxLength(OrdersConsts.MaxReferences)]
    public List<OrderIntakeReferenceDto> References { get; set; } = new();

    [MaxLength(OrdersConsts.MaxLines)]
    public List<OrderIntakeLineDto> Lines { get; set; } = new();
}

/// <summary>The customer as the document names it; the resolver tries the fields in the design's order.</summary>
public class OrderIntakeCustomerDto
{
    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? Name { get; set; }

    [MaxLength(IntakeConsts.MaxCodeLength)]
    public string? Code { get; set; }

    [MaxLength(IntakeConsts.MaxEmailAddressLength)]
    public string? EmailAddress { get; set; }

    [MaxLength(IntakeConsts.MaxVatNumberLength)]
    public string? VatNumber { get; set; }

    [MaxLength(IntakeConsts.MaxChamberOfCommerceNumberLength)]
    public string? ChamberOfCommerceNumber { get; set; }

    public Guid? RelationId { get; set; }
}

public class OrderIntakeReferenceDto
{
    [Required, MaxLength(OrdersConsts.MaxReferenceTypeLength)]
    public string Type { get; set; } = string.Empty;

    [Required, MaxLength(OrdersConsts.MaxReferenceValueLength)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>One freight item with its own stops (Assumption 3: one Pickup and one Delivery unless the profile defines more).</summary>
public class OrderIntakeLineDto
{
    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? TransportUnitName { get; set; }

    public Guid? TransportUnitId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Quantity { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal? Colli { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? Positions { get; set; }

    [Range(0, double.MaxValue)]
    public double? WeightKg { get; set; }

    [Range(0, double.MaxValue)]
    public double? VolumeM3 { get; set; }

    [Range(0, double.MaxValue)]
    public double? LoadingMeters { get; set; }

    [Range(0, double.MaxValue)]
    public double? LengthCm { get; set; }

    [Range(0, double.MaxValue)]
    public double? WidthCm { get; set; }

    [Range(0, double.MaxValue)]
    public double? HeightCm { get; set; }

    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? ProductName { get; set; }

    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? TemperatureName { get; set; }

    [MaxLength(OrdersConsts.MaxLineTextLength)]
    public string? Description { get; set; }

    [MaxLength(OrdersConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    [MaxLength(OrdersConsts.MaxContainerFieldLength)]
    public string? ContainerNumber { get; set; }

    [MaxLength(OrdersConsts.MaxBarcodeLength)]
    public string? Barcode { get; set; }

    [MaxLength(IntakeConsts.MaxStopsPerLine)]
    public List<OrderIntakeStopDto> Stops { get; set; } = new();
}

public class OrderIntakeStopDto
{
    public IntakeStopRole Role { get; set; }

    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? WaypointTypeName { get; set; }

    /// <summary>yyyy-MM-dd.</summary>
    [MaxLength(IntakeConsts.DateLength)]
    public string? Date { get; set; }

    /// <summary>HH:mm.</summary>
    [MaxLength(IntakeConsts.TimeLength)]
    public string? TimeFrom { get; set; }

    /// <summary>HH:mm.</summary>
    [MaxLength(IntakeConsts.TimeLength)]
    public string? TimeTill { get; set; }

    [MaxLength(OrdersConsts.MaxStopReferenceLength)]
    public string? Reference { get; set; }

    [MaxLength(OrdersConsts.MaxStopRemarksLength)]
    public string? Remarks { get; set; }

    [MaxLength(IntakeConsts.MaxContactNameLength)]
    public string? ContactName { get; set; }

    [MaxLength(IntakeConsts.MaxContactPhoneLength)]
    public string? ContactPhone { get; set; }

    public OrderIntakeAddressDto Address { get; set; } = new();

    public Guid? RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }
}

public class OrderIntakeAddressDto
{
    [MaxLength(IntakeConsts.MaxNameLength)]
    public string? Name { get; set; }

    [MaxLength(IntakeConsts.MaxStreetLength)]
    public string? Street { get; set; }

    [MaxLength(IntakeConsts.MaxHouseNumberLength)]
    public string? HouseNumber { get; set; }

    [MaxLength(IntakeConsts.MaxPostalCodeLength)]
    public string? PostalCode { get; set; }

    [MaxLength(OrdersConsts.MaxCityLength)]
    public string? City { get; set; }

    [MaxLength(IntakeConsts.MaxCountryCodeLength)]
    public string? CountryCode { get; set; }

    /// <summary>The address as one text when the document does not split it.</summary>
    [MaxLength(IntakeConsts.MaxFullTextLength)]
    public string? FullText { get; set; }
}
