using System;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// The type of an outbound invoice (design 52): invoice or credit note, its gapless number format (design 50), and one
/// default per kind per tenant (Assumption 5). The line-creation strategy arrives with the invoicing design.
/// </summary>
public class OutboundInvoiceType : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public OutboundInvoiceKind Kind { get; private set; } = OutboundInvoiceKind.Invoice;

    public bool IsDefault { get; private set; }

    public NumberSequenceFormat Numbering { get; private set; } = default!;

    public bool IsArchived { get; private set; }

    protected OutboundInvoiceType()
    {
    }

    public OutboundInvoiceType(Guid id, Guid? tenantId, string code, LocalizedName name, OutboundInvoiceKind kind, NumberSequenceFormat numbering)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetKind(kind);
        SetNumbering(numbering);
    }

    /// <summary>The key of this type's number sequence (design 50).</summary>
    public static string SequenceKeyOf(Guid id) => "Financial.OutboundInvoiceType:" + id;

    public string SequenceKey => SequenceKeyOf(Id);

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    /// <summary>The default of its kind keeps its kind: make another type the default of the kind first.</summary>
    public void SetKind(OutboundInvoiceKind kind)
    {
        FinancialValues.Defined(kind, "kind");
        if (IsDefault && kind != Kind)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "kind");
        }

        Kind = kind;
    }

    public void SetNumbering(NumberSequenceFormat numbering) => Numbering = Check.NotNull(numbering, nameof(numbering));

    public void SetArchived(bool isArchived)
    {
        if (isArchived && IsDefault)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archive");
        }

        IsArchived = isArchived;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
