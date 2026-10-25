namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// What an e-mail template is for (design 54; OpenTMS's free-text <c>EntityTypeName</c>, assessment §3.10). The module
/// that sends the mail publishes the typed Scriban model of its purpose.
/// </summary>
public enum EmailTemplatePurpose
{
    TransportOrder = 1,
    TransportFile = 2,
    Invoice = 3,
    InvoiceReminder = 4,
    PurchaseOrder = 5,
    EtaNotification = 6,
    DriverInstructions = 7,
    DigitalArchive = 8,
    ProformaOverview = 9
}
