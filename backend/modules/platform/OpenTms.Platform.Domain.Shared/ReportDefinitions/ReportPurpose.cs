namespace OpenTms.Platform.ReportDefinitions;

/// <summary>
/// The document a report definition renders (design 54; OpenTMS resolved an entity type name by reflection). The
/// consignment note (CMR) is design 56's addition: OpenTMS drew it programmatically, here it is a template like the others.
/// </summary>
public enum ReportPurpose
{
    TransportOrderConfirmation = 1,
    TransportFile = 2,
    Invoice = 3,
    PurchaseOrder = 4,
    TransportUnitBalance = 5,
    ConsignmentNote = 6
}
