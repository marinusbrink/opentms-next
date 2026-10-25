using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using OpenTms.Orders.TransportOrders;
using Volo.Abp;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>A postal address as the consignment note prints it.</summary>
public sealed record CmrAddress(string? Street, string? HouseNumber, string? PostalCode, string? City, string? CountryCode)
{
    public string Line => string.Join(", ", new[] { string.Join(" ", new[] { Street, HouseNumber }.Where(p => !string.IsNullOrEmpty(p))), string.Join(" ", new[] { PostalCode, City }.Where(p => !string.IsNullOrEmpty(p))), CountryCode }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>A party of a box: a relation's name and the address the box shows.</summary>
public sealed record CmrParty(Guid? RelationId, string Name, CmrAddress? Address);

/// <summary>The trip facts the boxes 16/17/18/21 and the trip's parties come from (Planning's lookup).</summary>
public sealed record CmrTripFacts(
    Guid Id, string Number, DateOnly TripDate, string? CarrierName, IReadOnlyList<string> VehicleNames, IReadOnlyList<string> LicencePlates, IReadOnlyList<string> DriverNames);

/// <summary>The names of Master Data rows the goods rows show.</summary>
public sealed record CmrNames(
    IReadOnlyDictionary<Guid, string> TransportUnits,
    IReadOnlyDictionary<Guid, string> Products,
    IReadOnlyDictionary<Guid, string> ProductCodes,
    IReadOnlyDictionary<Guid, string> Temperatures);

/// <summary>The effective layout: the profile with the print's overrides applied.</summary>
public sealed record CmrLayout(
    string ProfileName,
    int Copies,
    bool ShowBorders,
    bool ShowQrCode,
    bool GroupByLeg,
    CmrPartySource Box1Sender,
    CmrPartySource Box2Consignee,
    CmrPartySource Box3DeliveryPlace,
    CmrPartySource Box4TakingOverPlace,
    CmrPartySource Box16Carrier,
    CmrPartySource Box17SuccessiveCarrier,
    CmrPartySource Box21EstablishedBy,
    IReadOnlyList<CmrGoodsColumn> Box6Columns,
    IReadOnlyList<CmrInstructionItem> Box13Items,
    IReadOnlyList<CmrRemarkItem> Box18Items,
    IReadOnlyList<string> FixedDescriptions);

/// <summary>Everything the composer needs for one note; gathered by the application service, resolved here.</summary>
public sealed record CmrSource(
    TransportOrder Order,
    IReadOnlyList<TransportOrderLine> Lines,
    CmrLayout Layout,
    string Number,
    CmrParty? Customer,
    IReadOnlyDictionary<Guid, CmrParty> PartiesByRelation,
    IReadOnlyDictionary<Guid, CmrAddress> AddressesById,
    CmrParty? OwnCompany,
    CmrParty? Carrier,
    CmrParty? Shipper,
    CmrTripFacts? Trip,
    CmrParty? TripDeparture,
    CmrParty? TripArrival,
    CmrNames Names,
    DateTime PrintedAt,
    string? PrintedBy);

/// <summary>
/// The box mapping of the consignment note (design 58 "CMR"; routing-co2-cmr.md §3.2 made explicit per
/// <see cref="CmrPartySource"/>): produces the payload of the report data catalogue's ConsignmentNote purpose, in the
/// OpenTMS envelope. Pure: every party, name and trip fact comes in through <see cref="CmrSource"/>.
/// </summary>
public static class ConsignmentNoteComposer
{
    public const string EntityType = "ConsignmentNote";

    public static JsonObject Compose(CmrSource source)
    {
        Check.NotNull(source, nameof(source));
        var lines = source.Lines.OrderBy(l => l.Position).ToList();
        if (lines.Count == 0)
        {
            throw new BusinessException(OrdersErrorCodes.NothingToPrint);
        }

        var firstStop = lines.First().Stops.OrderBy(s => s.Position).FirstOrDefault();
        var lastStop = lines.Last().Stops.OrderBy(s => s.Position).LastOrDefault();
        var layout = source.Layout;

        var sender = Party(source, layout.Box1Sender, firstStop, lastStop);
        var consignee = Party(source, layout.Box2Consignee, firstStop, lastStop);
        var carrier = Party(source, layout.Box16Carrier, firstStop, lastStop);
        var successive = Party(source, layout.Box17SuccessiveCarrier, firstStop, lastStop);
        var established = Party(source, layout.Box21EstablishedBy, firstStop, lastStop);
        var printedDate = DateOnly.FromDateTime(source.PrintedAt);

        var entity = new JsonObject
        {
            ["EntityNumber"] = source.Number,
            ["Number"] = source.Number,
            ["Barcode"] = source.Number,
            ["Copies"] = layout.Copies,
            ["ShowBorders"] = layout.ShowBorders,
            ["ShowQrCode"] = layout.ShowQrCode,
            ["GroupByLeg"] = layout.GroupByLeg,
            ["ProfileName"] = layout.ProfileName,
            ["Sender"] = PartyNode(sender, layout.Box1Sender),
            ["Consignee"] = PartyNode(consignee, layout.Box2Consignee),
            ["DeliveryPlace"] = PlaceNode(Party(source, layout.Box3DeliveryPlace, firstStop, lastStop), layout.Box3DeliveryPlace, DateOf(layout.Box3DeliveryPlace, firstStop, lastStop)),
            ["TakingOverPlace"] = PlaceNode(Party(source, layout.Box4TakingOverPlace, firstStop, lastStop), layout.Box4TakingOverPlace, firstStop?.Date),
            ["AttachedDocuments"] = Joined(lines.Select(l => l.Freight.AttachedDocuments)),
            ["GoodsColumns"] = new JsonArray(layout.Box6Columns.Select(c => (JsonNode?)JsonValue.Create(c.ToString())).ToArray()),
            ["ShowColumnNames"] = layout.Box6Columns.Contains(CmrGoodsColumn.ColumnNames),
            ["ShowTotals"] = layout.Box6Columns.Contains(CmrGoodsColumn.Totals),
            ["Goods"] = new JsonArray(lines.Select(l => (JsonNode?)GoodsRow(l, source.Names)).ToArray()),
            ["GoodsDescription"] = layout.Box6Columns.Count == 0 ? null : Joined(layout.FixedDescriptions),
            ["Totals"] = new JsonObject
            {
                ["Quantity"] = Dec(lines.Sum(l => l.Freight.Quantity)),
                ["Colli"] = Dec(lines.Sum(l => l.Freight.Colli ?? 0m)),
                ["WeightKg"] = lines.Sum(l => l.Freight.WeightKg ?? 0),
                ["VolumeM3"] = lines.Sum(l => l.Freight.VolumeM3 ?? 0),
                ["Positions"] = Dec(lines.Sum(l => l.Freight.Positions ?? 0m))
            },
            ["SenderInstructionItems"] = ItemsNode(layout.Box13Items.Select(i => (i.ToString(), InstructionText(i, lines, layout.FixedDescriptions)))),
            ["SenderInstructions"] = Joined(layout.Box13Items.Select(i => InstructionText(i, lines, layout.FixedDescriptions))),
            ["PaymentInstruction"] = lines.Select(l => l.Freight.PaymentInstruction).FirstOrDefault(p => p != PaymentInstruction.NotSet) is var payment && payment != PaymentInstruction.NotSet ? payment.ToString() : null,
            ["Carrier"] = PartyNode(carrier, layout.Box16Carrier),
            ["SuccessiveCarrier"] = PartyNode(successive, layout.Box17SuccessiveCarrier),
            ["CarrierRemarkItems"] = ItemsNode(layout.Box18Items.Select(i => (i.ToString(), RemarkText(i, source, lines, firstStop, lastStop)))),
            ["CarrierRemarks"] = Joined(layout.Box18Items.Select(i => RemarkText(i, source, lines, firstStop, lastStop))),
            ["SpecialAgreements"] = Joined(lines.Select(l => l.Freight.SpecialAgreements)),
            ["EstablishedIn"] = PlaceNode(established, layout.Box21EstablishedBy, printedDate),
            ["SenderSignature"] = SignatureNode(sender, null, null),
            ["CarrierSignature"] = SignatureNode(carrier, null, null),
            ["ConsigneeSignature"] = SignatureNode(consignee, null, null),
            ["Order"] = new JsonObject
            {
                ["Id"] = source.Order.Id.ToString("D"),
                ["Number"] = source.Order.Number,
                ["CustomerReference"] = source.Order.CustomerReference,
                ["OrderDate"] = Day(source.Order.OrderDate),
                ["CustomerName"] = source.Order.CustomerName
            },
            ["Trip"] = source.Trip == null ? null : new JsonObject
            {
                ["Id"] = source.Trip.Id.ToString("D"),
                ["Number"] = source.Trip.Number,
                ["TripDate"] = Day(source.Trip.TripDate),
                ["CarrierName"] = source.Trip.CarrierName,
                ["VehicleNames"] = Strings(source.Trip.VehicleNames),
                ["LicencePlates"] = Strings(source.Trip.LicencePlates),
                ["DriverNames"] = Strings(source.Trip.DriverNames)
            },
            ["OrderLineIds"] = new JsonArray(lines.Select(l => (JsonNode?)JsonValue.Create(l.Id.ToString("D"))).ToArray()),
            ["PrintedAt"] = source.PrintedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["PrintedBy"] = source.PrintedBy
        };

        return new JsonObject
        {
            ["CompanyLogo"] = null,
            ["Me"] = MeNode(source.OwnCompany),
            ["Entity"] = entity,
            ["EntityType"] = EntityType
        };
    }

    /// <summary>The party a box source names (routing-co2-cmr.md §3.2, the option list), or null for Empty and unknown parties.</summary>
    public static CmrParty? Party(CmrSource source, CmrPartySource box, TransportOrderStop? firstStop, TransportOrderStop? lastStop)
    {
        switch (box)
        {
            case CmrPartySource.Customer:
                return source.Customer;
            case CmrPartySource.OriginAddress:
                return StopParty(source, firstStop);
            case CmrPartySource.DestinationAddress:
                return StopParty(source, lastStop);
            case CmrPartySource.OwnCompany:
                return source.OwnCompany;
            case CmrPartySource.Carrier:
                return source.Carrier;
            case CmrPartySource.Shipper:
                return source.Shipper;
            case CmrPartySource.TripDeparture:
                return source.TripDeparture;
            case CmrPartySource.TripArrival:
                return source.TripArrival;
            case CmrPartySource.Empty:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(box), box, "Unknown party source.");
        }
    }

    private static CmrParty? StopParty(CmrSource source, TransportOrderStop? stop)
    {
        if (stop?.Details.RelationId == null)
        {
            return null;
        }

        source.PartiesByRelation.TryGetValue(stop.Details.RelationId.Value, out var relation);
        var address = stop.Details.RelationAddressId != null && source.AddressesById.TryGetValue(stop.Details.RelationAddressId.Value, out var found) ? found : relation?.Address;
        return relation == null && address == null ? null : new CmrParty(stop.Details.RelationId, relation?.Name ?? string.Empty, address);
    }

    private static DateOnly? DateOf(CmrPartySource box, TransportOrderStop? firstStop, TransportOrderStop? lastStop) => box switch
    {
        CmrPartySource.OriginAddress => firstStop?.Date,
        CmrPartySource.DestinationAddress => lastStop?.Date,
        _ => null
    };

    private static string? InstructionText(CmrInstructionItem item, IReadOnlyList<TransportOrderLine> lines, IReadOnlyList<string> fixedDescriptions) => item switch
    {
        CmrInstructionItem.ShipperInstructions => Joined(lines.Select(l => l.Freight.ShipperInstructions)),
        CmrInstructionItem.FixedDescriptions => Joined(fixedDescriptions),
        CmrInstructionItem.SpecialAgreements => Joined(lines.Select(l => l.Freight.SpecialAgreements)),
        CmrInstructionItem.LineRemarks => Joined(lines.Select(l => l.Freight.Remarks)),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown instruction item.")
    };

    private static string? RemarkText(CmrRemarkItem item, CmrSource source, IReadOnlyList<TransportOrderLine> lines, TransportOrderStop? firstStop, TransportOrderStop? lastStop)
    {
        var stops = lines.SelectMany(l => l.Stops.OrderBy(s => s.Position)).ToList();
        return item switch
        {
            CmrRemarkItem.Vehicles => source.Trip == null ? null : Joined(source.Trip.VehicleNames.Zip(source.Trip.LicencePlates.Concat(Enumerable.Repeat<string>(string.Empty, source.Trip.VehicleNames.Count)), (name, plate) => string.IsNullOrEmpty(plate) ? name : $"{name} ({plate})")),
            CmrRemarkItem.OrderNumber => source.Order.Number,
            CmrRemarkItem.CustomerReference => source.Order.CustomerReference,
            CmrRemarkItem.TripNumbers => source.Trip?.Number ?? source.Order.TripNumbers,
            CmrRemarkItem.LineRemarks => Joined(lines.Select(l => l.Freight.Remarks)),
            CmrRemarkItem.StopReferences => Joined(stops.Select(s => s.Details.Reference)),
            CmrRemarkItem.DriverInstructions => Joined(stops.Select(s => s.Details.Remarks)),
            CmrRemarkItem.InvoiceReference => Joined(lines.Select(l => l.Freight.InvoiceReference)),
            CmrRemarkItem.Barcode => Joined(lines.Select(l => l.Freight.Barcode)),
            CmrRemarkItem.Temperature => Joined(lines.Select(l => l.Freight.TemperatureId == null ? null : source.Names.Temperatures.GetValueOrDefault(l.Freight.TemperatureId.Value))),
            CmrRemarkItem.SpecialAgreements => Joined(lines.Select(l => l.Freight.SpecialAgreements)),
            CmrRemarkItem.ShippingCompany => Joined(lines.Select(l => l.Freight.ShippingCompany)),
            CmrRemarkItem.ShipperInstructions => Joined(lines.Select(l => l.Freight.ShipperInstructions)),
            CmrRemarkItem.OriginReference => firstStop?.Details.Reference,
            CmrRemarkItem.DestinationReference => lastStop?.Details.Reference,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown remark item.")
        };
    }

    private static JsonObject GoodsRow(TransportOrderLine line, CmrNames names)
    {
        var f = line.Freight;
        var dimensions = f.LengthCm == null && f.WidthCm == null && f.HeightCm == null
            ? null
            : string.Join(" × ", new[] { f.LengthCm, f.WidthCm, f.HeightCm }.Select(d => d?.ToString("0.#", CultureInfo.InvariantCulture) ?? "-")) + " cm";
        return new JsonObject
        {
            ["OrderLineId"] = line.Id.ToString("D"),
            ["Position"] = line.Position,
            ["ContainerNumber"] = f.ContainerNumber,
            ["PinCode"] = f.PinCode,
            ["SizeOrIso"] = f.SizeOrIso,
            ["Quantity"] = Dec(f.Quantity),
            ["TransportUnit"] = names.TransportUnits.GetValueOrDefault(f.TransportUnitId),
            ["ProductCode"] = f.ProductRelationCodeId == null ? null : names.ProductCodes.GetValueOrDefault(f.ProductRelationCodeId.Value),
            ["Product"] = f.ProductId == null ? null : names.Products.GetValueOrDefault(f.ProductId.Value),
            ["Colli"] = f.Colli == null ? null : Dec(f.Colli.Value),
            ["WeightKg"] = f.WeightKg,
            ["Dimensions"] = dimensions,
            ["VolumeM3"] = f.VolumeM3,
            ["Barcode"] = f.Barcode,
            ["Positions"] = f.Positions == null ? null : Dec(f.Positions.Value),
            ["Temperature"] = f.TemperatureId == null ? null : names.Temperatures.GetValueOrDefault(f.TemperatureId.Value),
            ["InvoiceReference"] = f.InvoiceReference,
            ["Description"] = f.Remarks
        };
    }

    private static JsonObject? PartyNode(CmrParty? party, CmrPartySource source) => party == null ? null : new JsonObject
    {
        ["RelationId"] = party.RelationId?.ToString("D"),
        ["Name"] = party.Name,
        ["Address"] = AddressNode(party.Address),
        ["Source"] = source.ToString()
    };

    private static JsonObject? PlaceNode(CmrParty? party, CmrPartySource source, DateOnly? date) => party == null ? null : new JsonObject
    {
        ["Name"] = party.Name,
        ["City"] = party.Address?.City,
        ["CountryCode"] = party.Address?.CountryCode,
        ["Address"] = party.Address?.Line,
        ["Date"] = date == null ? null : Day(date.Value),
        ["Source"] = source.ToString()
    };

    private static JsonObject? SignatureNode(CmrParty? party, string? place, DateOnly? date) => party == null ? null : new JsonObject
    {
        ["Name"] = party.Name,
        ["Place"] = place ?? party.Address?.City,
        ["Date"] = date == null ? null : Day(date.Value)
    };

    private static JsonObject? AddressNode(CmrAddress? address) => address == null ? null : new JsonObject
    {
        ["Street"] = address.Street,
        ["HouseNumber"] = address.HouseNumber,
        ["PostalCode"] = address.PostalCode,
        ["City"] = address.City,
        ["CountryCode"] = address.CountryCode,
        ["Line"] = address.Line
    };

    /// <summary>The envelope's Me in the OpenTMS Relation shape the other purposes share (only what the CMR knows).</summary>
    private static JsonObject? MeNode(CmrParty? ownCompany)
    {
        if (ownCompany == null)
        {
            return null;
        }

        var address = ownCompany.Address == null ? null : new JsonObject
        {
            ["IsPostalAddress"] = false,
            ["IsVisitingAddress"] = true,
            ["Country"] = ownCompany.Address.CountryCode == null ? null : new JsonObject { ["Iso3166_2"] = ownCompany.Address.CountryCode },
            ["PostalCode"] = ownCompany.Address.PostalCode,
            ["City"] = ownCompany.Address.City,
            ["AddressLine1"] = ownCompany.Address.Street,
            ["NumberSuffix"] = ownCompany.Address.HouseNumber
        };
        return new JsonObject
        {
            ["Id"] = ownCompany.RelationId?.ToString("D"),
            ["Name"] = ownCompany.Name,
            ["FullName"] = ownCompany.Name,
            ["Addresses"] = address == null ? new JsonArray() : new JsonArray(address),
            ["VisitingAddress"] = address?.DeepClone()
        };
    }

    private static JsonArray ItemsNode(IEnumerable<(string Item, string? Text)> items)
        => new(items.Where(i => !string.IsNullOrEmpty(i.Text)).Select(i => (JsonNode?)new JsonObject { ["Item"] = i.Item, ["Text"] = i.Text }).ToArray());

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static string? Joined(IEnumerable<string?> parts)
    {
        var texts = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        return texts.Count == 0 ? null : string.Join("\n", texts);
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static JsonValue Dec(decimal value) => JsonValue.Create(decimal.ToDouble(value));
}
