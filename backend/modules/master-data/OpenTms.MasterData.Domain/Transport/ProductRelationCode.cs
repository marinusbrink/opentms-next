using System;
using Volo.Abp;

namespace OpenTms.MasterData.Transport;

/// <summary>
/// A customer's article code mapped to our product, transport unit and destination (design 48, assessment §2 row 1).
/// The code is stored as typed (trimmed); the normalised code (upper case) is unique per tenant and is what imports
/// match on. It is never created on the fly (§3.15): an unknown code is a proposal in the intake flow.
/// </summary>
public class ProductRelationCode : MasterDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string NormalizedCode { get; private set; } = default!;

    /// <summary>The destination (OpenTMS "Destination", design 48 assumption 4).</summary>
    public Guid? RelationId { get; private set; }

    public Guid? ProductId { get; private set; }

    public Guid? TransportUnitId { get; private set; }

    public bool IsArchived { get; private set; }

    protected ProductRelationCode()
    {
    }

    public ProductRelationCode(Guid id, Guid? tenantId, string code)
        : base(id, tenantId)
    {
        SetCode(code);
    }

    /// <summary>The form a code is matched in: trimmed, upper case; null when empty.</summary>
    public static string? Normalize(string? code)
    {
        var text = code?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.ToUpperInvariant();
    }

    public void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MasterDataConsts.MaxProductRelationCodeLength).Trim();
        NormalizedCode = Normalize(Code)!;
    }

    /// <summary>At least one of relation, product and transport unit: a code that maps to nothing is noise.</summary>
    public void SetTargets(Guid? relationId, Guid? productId, Guid? transportUnitId)
    {
        if (relationId == null && productId == null && transportUnitId == null)
        {
            throw new BusinessException(MasterDataErrorCodes.ProductRelationCodeTargetRequired);
        }

        RelationId = relationId;
        ProductId = productId;
        TransportUnitId = transportUnitId;
    }

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
