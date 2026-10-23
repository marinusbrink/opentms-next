namespace OpenTms.Financial.Invoicing;

/// <summary>
/// The tax category of a VAT percentage (design 52, assessment §5): accounting connectors map by kind, never by
/// comparing percentages.
/// </summary>
public enum VatKind
{
    Standard = 1,
    Reduced = 2,
    Zero = 3,
    Exempt = 4,
    ReverseCharge = 5,
    IntraCommunity = 6,
    Export = 7
}

/// <summary>What an outbound invoice of the type is; one default per kind (design 52, Assumption 5).</summary>
public enum OutboundInvoiceKind
{
    Invoice = 1,
    CreditNote = 2
}

/// <summary>What an inbound invoice of the type is: a carrier invoice is matched against trips (chartermatch).</summary>
public enum InboundInvoiceKind
{
    CarrierInvoice = 1,
    Other = 2
}
