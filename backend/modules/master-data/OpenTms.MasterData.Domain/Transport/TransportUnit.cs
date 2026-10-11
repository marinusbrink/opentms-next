using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Transport;

/// <summary>Packaging type only (assessment §2): balances and transactions are Financial, transaction times Orders.</summary>
public class TransportUnit : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public bool IsArchived { get; set; }

    public double? LengthCm { get; set; }

    public double? WidthCm { get; set; }

    public double? HeightCm { get; set; }

    /// <summary>The weight of the empty unit; with the goods it gives the gross weight on the document.</summary>
    public double? TareWeightKg { get; set; }

    /// <summary>The payload the unit may carry (a Europallet about 1,500 kg).</summary>
    public double? MaxLoadKg { get; set; }

    /// <summary>Planning fills a trailer by floor positions; carriers price a non-stackable unit differently.</summary>
    public bool IsStackable { get; set; }

    /// <summary>How many of these units stack on top of each other when stackable.</summary>
    public int? MaxStackCount { get; set; }

    public double? LoadingMeters { get; set; }

    public double? EpqFactor { get; set; }

    /// <summary>The pallet-exchange flag: this unit is exchanged at the dock (europallet yes, one-way pallet no).</summary>
    public bool IsExchangeable { get; set; }

    public virtual ICollection<TransportUnitLoadFactor> LoadFactors { get; private set; } = new List<TransportUnitLoadFactor>();

    protected TransportUnit()
    {
    }

    public TransportUnit(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public void SetName(string name) => Name = NormalizeName(name);

    /// <summary>Replaces the load factors: rows with a known id are updated, new rows added, the rest removed.</summary>
    public void SetLoadFactors(IEnumerable<(Guid? Id, Guid VehicleTypeId, double? LoadingCapacityFactor, double? MaxCapacity)> factors, Func<Guid> newId)
    {
        var wanted = factors.ToList();
        if (wanted.Select(f => f.VehicleTypeId).Distinct().Count() != wanted.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateLoadFactor);
        }

        var keep = new HashSet<Guid>();
        foreach (var factor in wanted)
        {
            var existing = factor.Id != null
                ? LoadFactors.FirstOrDefault(f => f.Id == factor.Id.Value)
                : LoadFactors.FirstOrDefault(f => f.VehicleTypeId == factor.VehicleTypeId);

            if (existing == null)
            {
                existing = new TransportUnitLoadFactor(newId(), TenantId, Id, factor.VehicleTypeId);
                LoadFactors.Add(existing);
            }

            existing.Set(factor.VehicleTypeId, factor.LoadingCapacityFactor, factor.MaxCapacity);
            keep.Add(existing.Id);
        }

        foreach (var stale in LoadFactors.Where(f => !keep.Contains(f.Id)).ToList())
        {
            LoadFactors.Remove(stale);
        }
    }
}

/// <summary>Child of TransportUnit: how many of this unit a vehicle type carries. Unique per (unit, vehicle type).</summary>
public class TransportUnitLoadFactor : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TransportUnitId { get; private set; }

    public Guid VehicleTypeId { get; private set; }

    public double? LoadingCapacityFactor { get; private set; }

    public double? MaxCapacity { get; private set; }

    protected TransportUnitLoadFactor()
    {
    }

    public TransportUnitLoadFactor(Guid id, Guid? tenantId, Guid transportUnitId, Guid vehicleTypeId)
        : base(id)
    {
        TenantId = tenantId;
        TransportUnitId = transportUnitId;
        VehicleTypeId = vehicleTypeId;
    }

    public void Set(Guid vehicleTypeId, double? loadingCapacityFactor, double? maxCapacity)
    {
        VehicleTypeId = vehicleTypeId;
        LoadingCapacityFactor = loadingCapacityFactor;
        MaxCapacity = maxCapacity;
    }
}
