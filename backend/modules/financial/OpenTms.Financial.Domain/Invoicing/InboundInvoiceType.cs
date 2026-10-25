using System;
using OpenTms.Platform.ReferenceData;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// The type of an inbound (purchase) invoice (design 52). Not numbered: an inbound invoice keeps the supplier's number
/// (Assumption 3). A carrier invoice is matched against trips (chartermatch).
/// </summary>
public class InboundInvoiceType : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public InboundInvoiceKind Kind { get; private set; } = InboundInvoiceKind.CarrierInvoice;

    public string? CostLedgerAccount { get; private set; }

    public bool IsArchived { get; set; }

    protected InboundInvoiceType()
    {
    }

    public InboundInvoiceType(Guid id, Guid? tenantId, string code, string name, InboundInvoiceKind kind)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetKind(kind);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetKind(InboundInvoiceKind kind) => Kind = FinancialValues.Defined(kind, "kind");

    public void SetCostLedgerAccount(string? account) => CostLedgerAccount = FinancialValues.LedgerAccount(account, "costLedgerAccount");
}
