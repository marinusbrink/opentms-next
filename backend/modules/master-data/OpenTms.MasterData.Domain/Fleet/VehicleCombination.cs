using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Fleet;

/// <summary>
/// A standing coupling of a motorised vehicle with its trailers (design 48, assessment §5): the unit a planner uses
/// by default. The combination that actually ran a trip is Planning's fact. A vehicle is in at most one active
/// combination: the members carry a denormalised <see cref="VehicleCombinationMember.IsActive"/> under a partial
/// unique index on (TenantId, VehicleId).
/// </summary>
public class VehicleCombination : MasterDataAggregateRoot
{
    public static readonly VehicleTypeKind[] MotorisedKinds = [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van];

    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key, so "Night shift" and "NIGHT SHIFT" are one name (PO 2026-10-16).</summary>
    public string NormalizedName { get; private set; } = default!;

    public bool IsArchived { get; private set; }

    public virtual ICollection<VehicleCombinationMember> Members { get; private set; } = new List<VehicleCombinationMember>();

    protected VehicleCombination()
    {
    }

    public VehicleCombination(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public static string NormalizeCombinationName(string name)
        => Check.NotNullOrWhiteSpace(name, nameof(name), MasterDataConsts.MaxVehicleCombinationNameLength).Trim();

    /// <summary>The unique key of a name: trimmed and upper case.</summary>
    public static string NormalizedNameOf(string name) => NormalizeCombinationName(name).ToUpperInvariant();

    public void SetName(string name)
    {
        Name = NormalizeCombinationName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    /// <summary>
    /// Replaces the members in the given order (positions 1, 2, …): 2 to 4 vehicles, each once, the first motorised
    /// (Tractor, Rigid or Van) and every later one a trailer. The kinds come from the vehicles' types.
    /// </summary>
    public void SetMembers(IReadOnlyList<(Guid VehicleId, VehicleTypeKind Kind)> ordered)
    {
        if (ordered.Count < MasterDataConsts.MinVehicleCombinationMembers || ordered.Count > MasterDataConsts.MaxVehicleCombinationMembers)
        {
            throw new BusinessException(MasterDataErrorCodes.CombinationMemberCount)
                .WithData("min", MasterDataConsts.MinVehicleCombinationMembers)
                .WithData("max", MasterDataConsts.MaxVehicleCombinationMembers);
        }

        if (ordered.Select(m => m.VehicleId).Distinct().Count() != ordered.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "members").WithData("value", string.Empty);
        }

        if (!MotorisedKinds.Contains(ordered[0].Kind) || ordered.Skip(1).Any(m => m.Kind != VehicleTypeKind.Trailer))
        {
            throw new BusinessException(MasterDataErrorCodes.CombinationMotorisedFirst);
        }

        var wanted = ordered.Select(m => m.VehicleId).ToHashSet();
        foreach (var stale in Members.Where(m => !wanted.Contains(m.VehicleId)).ToList())
        {
            Members.Remove(stale);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var vehicleId = ordered[i].VehicleId;
            var member = Members.FirstOrDefault(m => m.VehicleId == vehicleId);
            if (member == null)
            {
                member = new VehicleCombinationMember(TenantId, Id, vehicleId);
                Members.Add(member);
            }

            member.Set(i + 1, !IsArchived);
        }
    }

    /// <summary>An archived combination frees its vehicles: its members leave the active-member index.</summary>
    public void Archive()
    {
        IsArchived = true;
        foreach (var member in Members)
        {
            member.Set(member.Position, false);
        }
    }

    /// <summary>The service checks first that no member is in another active combination.</summary>
    public void Unarchive()
    {
        IsArchived = false;
        foreach (var member in Members)
        {
            member.Set(member.Position, true);
        }
    }

    /// <summary>Before a soft delete: the member rows go, so nothing points at a deleted combination.</summary>
    public void ClearMembers() => Members.Clear();
}

/// <summary>A vehicle at a position (1-based) in a combination; composite key (combination, vehicle).</summary>
public class VehicleCombinationMember : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleCombinationId { get; private set; }

    public Guid VehicleId { get; private set; }

    public int Position { get; private set; }

    /// <summary>Denormalised from the combination: true while it is neither archived nor deleted.</summary>
    public bool IsActive { get; private set; }

    protected VehicleCombinationMember()
    {
    }

    public VehicleCombinationMember(Guid? tenantId, Guid vehicleCombinationId, Guid vehicleId)
    {
        TenantId = tenantId;
        VehicleCombinationId = vehicleCombinationId;
        VehicleId = vehicleId;
    }

    public void Set(int position, bool isActive)
    {
        Position = position;
        IsActive = isActive;
    }

    public override object[] GetKeys() => [VehicleCombinationId, VehicleId];
}
