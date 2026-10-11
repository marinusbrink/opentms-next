using System;

namespace OpenTms.MasterData.Fleet;

/// <summary>Was "Capability" in OpenTMS; the code is the natural key (assessment §3.2).</summary>
public class TransportCharacteristics : MasterDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    protected TransportCharacteristics()
    {
    }

    public TransportCharacteristics(Guid id, Guid? tenantId, string code, string name)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
    }

    public void SetCode(string code) => Code = MasterDataCodes.NormalizeRequired(code, MasterDataConsts.MaxCodeLength, "code");

    public void SetName(string name) => Name = NormalizeName(name);
}
