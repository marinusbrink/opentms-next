using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Incompatibilities;

/// <summary>
/// One undirected "do not combine with" pair (assessment §3.3: a join table replaces the delimited string;
/// §5: one rule instead of three). The pair is canonical: Left is the id whose 32-character hex form sorts
/// first ordinally, which equals PostgreSQL's uuid order, so "A with B" and "B with A" are one row.
/// </summary>
public abstract class Incompatibility : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid LeftId { get; private set; }

    public Guid RightId { get; private set; }

    protected Incompatibility()
    {
    }

    protected Incompatibility(Guid? tenantId, Guid a, Guid b)
    {
        var (left, right) = Canonical(a, b);
        TenantId = tenantId;
        LeftId = left;
        RightId = right;
    }

    public override object[] GetKeys() => [LeftId, RightId];

    public Guid OtherThan(Guid id) => LeftId == id ? RightId : LeftId;

    public bool Involves(Guid id) => LeftId == id || RightId == id;

    public static (Guid Left, Guid Right) Canonical(Guid a, Guid b)
    {
        if (a == b)
        {
            throw new BusinessException(MasterDataErrorCodes.SelfIncompatibility).WithData("id", a);
        }

        return string.CompareOrdinal(a.ToString("N"), b.ToString("N")) < 0 ? (a, b) : (b, a);
    }

    /// <summary>The pairs to add and to remove so that <paramref name="id"/> is incompatible with exactly <paramref name="wantedOthers"/>.</summary>
    public static (List<Guid> ToAdd, List<(Guid Left, Guid Right)> ToRemove) Diff(Guid id, IEnumerable<(Guid Left, Guid Right)> existing, IEnumerable<Guid> wantedOthers)
    {
        var wanted = wantedOthers.Where(other => other != id).Distinct().ToHashSet();
        var current = existing.Select(p => p.Left == id ? p.Right : p.Left).ToHashSet();

        var toAdd = wanted.Where(other => !current.Contains(other)).ToList();
        var toRemove = current.Where(other => !wanted.Contains(other)).Select(other => Canonical(id, other)).ToList();
        return (toAdd, toRemove);
    }
}

public class TemperatureIncompatibility : Incompatibility
{
    protected TemperatureIncompatibility()
    {
    }

    public TemperatureIncompatibility(Guid? tenantId, Guid a, Guid b)
        : base(tenantId, a, b)
    {
    }
}

public class ProductIncompatibility : Incompatibility
{
    protected ProductIncompatibility()
    {
    }

    public ProductIncompatibility(Guid? tenantId, Guid a, Guid b)
        : base(tenantId, a, b)
    {
    }
}

public class TransportUnitIncompatibility : Incompatibility
{
    protected TransportUnitIncompatibility()
    {
    }

    public TransportUnitIncompatibility(Guid? tenantId, Guid a, Guid b)
        : base(tenantId, a, b)
    {
    }
}
