namespace OpenTms.Orders.TransportOrders;

/// <summary>The bounds of the Orders master data (design 51) and of the transport order (design 58, Security quickscan).</summary>
public static class OrdersConsts
{
    /// <summary>The stops a new order of a type starts with.</summary>
    public const int MaxDefaultWaypoints = 10;

    /// <summary>The on-board computer codes of one waypoint type.</summary>
    public const int MaxFleetManagementCodes = 20;

    public const int MaxFleetManagementVendorLength = 64;

    public const int MaxFleetManagementCodeLength = 64;

    /// <summary>The planned time at a stop: at most a day.</summary>
    public const int MaxDefaultDurationMinutes = 1440;

    /* Design 58: the transport order. */

    public const int MaxNumberLength = 32;

    public const int MaxLines = 200;

    public const int MaxStopsPerLine = 50;

    public const int MinStopsPerLine = 2;

    public const int MaxReferences = 50;

    public const int MaxCustomerReferenceLength = 128;

    public const int MaxRemarksLength = 2500;

    public const int MaxStopReferenceLength = 128;

    public const int MaxStopRemarksLength = 1000;

    public const int MaxReferenceTypeLength = 32;

    public const int MaxReferenceValueLength = 128;

    public const int MaxReferenceDescriptionLength = 256;

    public const int MaxLineTextLength = 512;

    public const int MaxContainerFieldLength = 64;

    public const int MaxBarcodeLength = 64;

    public const int MaxMeasurementsPerKind = 3;

    public const int MaxRequiredCharacteristicsPerLine = 20;

    public const int MaxPriceLists = 20;

    public const int MaxDiscountsOrSurcharges = 50;

    public const int MaxTripRequestVehicles = 3;

    public const int MaxTripRequestDrivers = 3;

    public const int MaxTripNumbersLength = 512;

    public const int MaxStopNameLength = 128;

    public const int MaxCityLength = 128;

    public const int MaxCurrencyCodeLength = 3;

    public const int MaxRevisionReasonLength = 1000;

    /* Attributes (design 56, Q12). */

    public const int MaxAttributes = 20;

    public const int MaxAttributeKeyLength = 32;

    public const int MaxAttributeValueLength = 500;

    /* Templates. */

    public const int MaxTemplateNameLength = 128;

    public const int MinScheduleInterval = 1;

    public const int MaxScheduleInterval = 52;

    public const int MinHorizonDays = 1;

    public const int MaxHorizonDays = 60;

    public const int DefaultHorizonDays = 5;

    public const int MaxMaterialisationDates = 60;

    public const int MaxMaterialisationCopies = 50;

    /// <summary>Design 61 wizard "Details": at most one override per generated order (dates × copies).</summary>
    public const int MaxMaterialisationOverrides = MaxMaterialisationDates * MaxMaterialisationCopies;

    /// <summary>UI-GRID-013: the default row cap of a list export (setting <c>Orders:Export:MaxRows</c>).</summary>
    public const int DefaultExportMaxRows = 50_000;

    /// <summary>The columns one export may name.</summary>
    public const int MaxExportColumns = 100;

    /// <summary>The export reads the list in pages of this size (one batch of lookups per page).</summary>
    public const int ExportPageSize = 1_000;

    public const int MaxExportFieldLength = 64;

    public const int MaxExportHeaderLength = 128;

    /// <summary>Planning's reason when a trip request was not honoured (design 59 build note 13).</summary>
    public const int MaxTripRequestWarningLength = 64;

    /// <summary>The target type of the order's stored derived values (design 56, flow 2).</summary>
    public const string OrderSummaryTargetType = "Orders.TransportOrder";
}
