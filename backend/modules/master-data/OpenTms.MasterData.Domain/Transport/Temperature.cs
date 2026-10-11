using System;
using Volo.Abp;

namespace OpenTms.MasterData.Transport;

/// <summary>Was "TemperatureRange" in OpenTMS; the unit is in the property name (assessment §3.5).</summary>
public class Temperature : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public double? MinCelsius { get; private set; }

    public double? MaxCelsius { get; private set; }

    public bool IsArchived { get; set; }

    protected Temperature()
    {
    }

    public Temperature(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetRange(double? minCelsius, double? maxCelsius)
    {
        if (minCelsius != null && maxCelsius != null && minCelsius > maxCelsius)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidTemperatureRange)
                .WithData("min", minCelsius.Value)
                .WithData("max", maxCelsius.Value);
        }

        MinCelsius = minCelsius;
        MaxCelsius = maxCelsius;
    }
}
