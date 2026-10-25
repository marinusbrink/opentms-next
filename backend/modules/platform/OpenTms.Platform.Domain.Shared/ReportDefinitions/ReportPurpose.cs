namespace OpenTms.Platform.ReportDefinitions;

/// <summary>The document a report definition renders (design 54; OpenTMS resolved an entity type name by reflection).</summary>
public enum ReportPurpose
{
    TransportOrderConfirmation = 1,
    TransportFile = 2,
    Invoice = 3,
    PurchaseOrder = 4,
    TransportUnitBalance = 5
}
