using System;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.Intake;

/// <summary>
/// A learned alias (design 72 "Review"): the normalised text a document used for a customer, an address, a type, a unit,
/// a product or a temperature, mapped to the row a clerk picked, so the next intake with the same text resolves without
/// a person. Unique per tenant, kind, scope and text. Addresses are scoped to the customer they were picked for; the
/// other kinds are tenant-wide. Not an aggregate root with a concurrency stamp on purpose: the use counter is
/// informational and two intakes resolving with the same alias at once must not refuse each other.
/// </summary>
public class IntakeAlias : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public IntakeAliasKind Kind { get; private set; }

    /// <summary>The customer the alias applies to; null for a tenant-wide alias.</summary>
    public Guid? ScopeRelationId { get; private set; }

    public string NormalizedText { get; private set; } = default!;

    public Guid TargetId { get; private set; }

    /// <summary>The relation of an address alias's target address.</summary>
    public Guid? TargetRelationId { get; private set; }

    public int UseCount { get; private set; }

    protected IntakeAlias()
    {
    }

    public IntakeAlias(Guid id, Guid? tenantId, IntakeAliasKind kind, Guid? scopeRelationId, string text, Guid targetId, Guid? targetRelationId)
        : base(id)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "kind").WithData("value", (int)kind);
        }

        var normalized = IntakeTextNormalizer.Normalize(text);
        if (normalized.Length == 0)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "normalizedText").WithData("value", string.Empty);
        }

        TenantId = tenantId;
        Kind = kind;
        ScopeRelationId = scopeRelationId;
        NormalizedText = normalized;
        Retarget(targetId, targetRelationId);
    }

    /// <summary>A clerk picked another row for the same text: the latest pick wins.</summary>
    public void Retarget(Guid targetId, Guid? targetRelationId)
    {
        if (targetId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "targetId");
        }

        TargetId = targetId;
        TargetRelationId = Kind == IntakeAliasKind.Address ? targetRelationId : null;
    }

    public void Used() => UseCount++;
}
