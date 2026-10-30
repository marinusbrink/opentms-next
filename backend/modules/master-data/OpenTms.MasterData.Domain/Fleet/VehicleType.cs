using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Fleet;

public class VehicleType : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public VehicleTypeKind Kind { get; private set; }

    public double? MaxEpq { get; set; }

    public double? MaxLoadingMeters { get; set; }

    public double? DefaultLengthCm { get; set; }

    public double? DefaultWidthCm { get; set; }

    public double? DefaultHeightCm { get; set; }

    public double? DefaultGrossVehicleWeightKg { get; set; }

    /// <summary>QP10: the cost price per kilometre of vehicles of this type (the second step of the vehicle → type → tenant cascade); null = unknown.</summary>
    public decimal? CostPricePerKm { get; private set; }

    /// <summary>Join rows to TransportCharacteristics (assessment §3.3: a join table, no delimited string).</summary>
    public virtual ICollection<VehicleTypeTransportCharacteristics> Characteristics { get; private set; } = new List<VehicleTypeTransportCharacteristics>();

    protected VehicleType()
    {
    }

    public VehicleType(Guid id, Guid? tenantId, string name, VehicleTypeKind kind)
        : base(id, tenantId)
    {
        SetName(name);
        SetKind(kind);
    }

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetCostPricePerKm(decimal? costPricePerKm)
    {
        if (costPricePerKm is < 0)
        {
            throw new Volo.Abp.BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "costPricePerKm").WithData("value", costPricePerKm.Value);
        }

        CostPricePerKm = costPricePerKm == null ? null : decimal.Round(costPricePerKm.Value, 4, MidpointRounding.AwayFromZero);
    }

    public void SetKind(VehicleTypeKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    /// <summary>Replaces the set of characteristics: keeps the rows that stay, adds the new, removes the rest.</summary>
    public void SetCharacteristics(IEnumerable<Guid> characteristicsIds)
    {
        var wanted = characteristicsIds.Distinct().ToHashSet();
        foreach (var existing in Characteristics.Where(c => !wanted.Contains(c.TransportCharacteristicsId)).ToList())
        {
            Characteristics.Remove(existing);
        }

        foreach (var id in wanted.Where(id => Characteristics.All(c => c.TransportCharacteristicsId != id)))
        {
            Characteristics.Add(new VehicleTypeTransportCharacteristics(TenantId, Id, id));
        }
    }
}

/// <summary>Join row VehicleType ↔ TransportCharacteristics; composite key, both sides cascade.</summary>
public class VehicleTypeTransportCharacteristics : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid VehicleTypeId { get; private set; }

    public Guid TransportCharacteristicsId { get; private set; }

    protected VehicleTypeTransportCharacteristics()
    {
    }

    public VehicleTypeTransportCharacteristics(Guid? tenantId, Guid vehicleTypeId, Guid transportCharacteristicsId)
    {
        TenantId = tenantId;
        VehicleTypeId = vehicleTypeId;
        TransportCharacteristicsId = transportCharacteristicsId;
    }

    public override object[] GetKeys() => [VehicleTypeId, TransportCharacteristicsId];
}
