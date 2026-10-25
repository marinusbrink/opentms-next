using System;
using OpenTms.Platform.ReferenceData;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// A service an order line can carry besides the transport itself (design 52): what its quantity counts, its VAT
/// category (null = the tenant's default) and ledger accounts. No price: a price is a rate line or a price-list row
/// (AD-08).
/// </summary>
public class AdditionalService : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public ServiceUnit Unit { get; private set; } = ServiceUnit.Piece;

    /// <summary>FK to a VAT percentage of this module (restrict); null = the tenant's default VAT category.</summary>
    public Guid? VatPercentageId { get; set; }

    public string? RevenueLedgerAccount { get; private set; }

    public string? CostLedgerAccount { get; private set; }

    public bool IsArchived { get; set; }

    protected AdditionalService()
    {
    }

    public AdditionalService(Guid id, Guid? tenantId, string code, string name, ServiceUnit unit)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetUnit(unit);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetUnit(ServiceUnit unit) => Unit = FinancialValues.Defined(unit, "unit");

    public void SetLedgerAccounts(string? revenue, string? cost)
    {
        RevenueLedgerAccount = FinancialValues.LedgerAccount(revenue, "revenueLedgerAccount");
        CostLedgerAccount = FinancialValues.LedgerAccount(cost, "costLedgerAccount");
    }
}
