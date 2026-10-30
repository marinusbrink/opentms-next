namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>
/// Which party fills a party box of the CMR (boxes 1, 2, 3, 4, 16, 17, 21; design 58 "CMR", PO decision Q11). Replaces
/// OpenTMS's hard-coded field-option GUIDs (docs/input/transport-assessment/routing-co2-cmr.md §3.2).
/// </summary>
public enum CmrPartySource
{
    Customer = 1,
    OriginAddress = 2,
    DestinationAddress = 3,
    OwnCompany = 4,
    Carrier = 5,
    Shipper = 6,
    TripDeparture = 7,
    TripArrival = 8,
    Empty = 9
}

/// <summary>The columns of the goods table (boxes 6–12) a profile shows.</summary>
public enum CmrGoodsColumn
{
    ContainerNumber = 1,
    PinCode = 2,
    SizeOrIso = 3,
    Quantity = 4,
    TransportUnit = 5,
    ProductCode = 6,
    Product = 7,
    Colli = 8,
    WeightKg = 9,
    Dimensions = 10,
    VolumeM3 = 11,
    Barcode = 12,
    Positions = 13,
    Temperature = 14,
    InvoiceReference = 15,
    ColumnNames = 16,
    Totals = 17
}

/// <summary>What box 13 (sender's instructions) is composed of.</summary>
public enum CmrInstructionItem
{
    ShipperInstructions = 1,
    FixedDescriptions = 2,
    SpecialAgreements = 3,
    LineRemarks = 4
}

/// <summary>What box 18 (carrier's remarks) is composed of (the OpenTMS toggles, appendix §3.2).</summary>
public enum CmrRemarkItem
{
    Vehicles = 1,
    OrderNumber = 2,
    CustomerReference = 3,
    TripNumbers = 4,
    LineRemarks = 5,
    StopReferences = 6,
    DriverInstructions = 7,
    InvoiceReference = 8,
    Barcode = 9,
    Temperature = 10,
    SpecialAgreements = 11,
    ShippingCompany = 12,
    ShipperInstructions = 13,
    OriginReference = 14,
    DestinationReference = 15
}
