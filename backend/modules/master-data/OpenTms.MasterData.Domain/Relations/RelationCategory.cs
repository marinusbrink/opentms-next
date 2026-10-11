using System;

namespace OpenTms.MasterData.Relations;

/// <summary>Free classification of relations; no system flags (assessment §3.1: roles live on Relation later).</summary>
public class RelationCategory : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    protected RelationCategory()
    {
    }

    public RelationCategory(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public void SetName(string name) => Name = NormalizeName(name);
}
