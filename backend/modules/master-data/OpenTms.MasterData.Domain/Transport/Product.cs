using System;
using Volo.Abp;

namespace OpenTms.MasterData.Transport;

public class Product : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public bool IsArchived { get; set; }

    public bool IsAdr { get; private set; }

    public string? AdrUnNumber { get; private set; }

    public string? AdrClass { get; private set; }

    public string? AdrPackingGroup { get; private set; }

    public string? AdrDescription { get; private set; }

    public double? DefaultQuantity { get; set; }

    public double? DefaultWeightKg { get; set; }

    public double? DefaultColli { get; set; }

    public double? DefaultVolumeM3 { get; set; }

    public double? DefaultLoadingMeters { get; set; }

    public double? DefaultEpq { get; set; }

    public double? DefaultLengthCm { get; set; }

    public double? DefaultWidthCm { get; set; }

    public double? DefaultHeightCm { get; set; }

    protected Product()
    {
    }

    public Product(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public void SetName(string name) => Name = NormalizeName(name);

    /// <summary>The ADR block: when the product is dangerous goods, the UN number and class are required.</summary>
    public void SetAdr(bool isAdr, string? unNumber, string? adrClass, string? packingGroup, string? description)
    {
        if (!isAdr)
        {
            IsAdr = false;
            AdrUnNumber = null;
            AdrClass = null;
            AdrPackingGroup = null;
            AdrDescription = null;
            return;
        }

        var un = unNumber?.Trim();
        var cls = MasterDataCodes.Normalize(adrClass);
        if (string.IsNullOrEmpty(un) || !MasterDataCodes.IsUnNumber(un) || cls == null || cls.Length > MasterDataConsts.MaxAdrClassLength)
        {
            throw new BusinessException(MasterDataErrorCodes.AdrFieldsRequired);
        }

        var group = MasterDataCodes.Normalize(packingGroup);
        if (group != null && group.Length > MasterDataConsts.MaxAdrPackingGroupLength)
        {
            throw new ArgumentOutOfRangeException(nameof(packingGroup));
        }

        var text = description?.Trim();
        IsAdr = true;
        AdrUnNumber = un;
        AdrClass = cls;
        AdrPackingGroup = group;
        AdrDescription = string.IsNullOrEmpty(text) ? null : Check.Length(text, nameof(description), MasterDataConsts.MaxAdrDescriptionLength);
    }
}
