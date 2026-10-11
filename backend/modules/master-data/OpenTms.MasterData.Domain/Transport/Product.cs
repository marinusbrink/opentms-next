using System;
using Volo.Abp;

namespace OpenTms.MasterData.Transport;

/// <summary>The ADR fields of a product as the application layer hands them in (null and false clear a field).</summary>
public sealed record ProductAdr(
    string? UnNumber,
    string? Class,
    string? PackingGroup,
    string? Description,
    string? TechnicalName = null,
    string? ClassificationCode = null,
    string? HazardLabels = null,
    string? HazardIdentificationNumber = null,
    string? TunnelRestrictionCode = null,
    int? TransportCategory = null,
    bool IsEnvironmentallyHazardous = false,
    bool IsLimitedQuantity = false,
    bool IsWaste = false,
    double? FlashpointCelsius = null,
    string? SpecialProvisions = null);

public class Product : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public bool IsArchived { get; set; }

    public bool IsAdr { get; private set; }

    public string? AdrUnNumber { get; private set; }

    public string? AdrClass { get; private set; }

    public string? AdrPackingGroup { get; private set; }

    /// <summary>The proper shipping name (ADR 5.4.1.1.1); kept under the OpenTMS name.</summary>
    public string? AdrDescription { get; private set; }

    /// <summary>The technical name in brackets for N.O.S. entries.</summary>
    public string? AdrTechnicalName { get; private set; }

    /// <summary>ADR classification code within the class (F1, TF2, …).</summary>
    public string? AdrClassificationCode { get; private set; }

    /// <summary>Hazard label numbers to print and placard, as written on the document ("3", "6.1+8").</summary>
    public string? AdrHazardLabels { get; private set; }

    /// <summary>The Kemler number on the orange plate (tank and bulk transport).</summary>
    public string? AdrHazardIdentificationNumber { get; private set; }

    /// <summary>Tunnel restriction code, e.g. "(D/E)".</summary>
    public string? AdrTunnelRestrictionCode { get; private set; }

    /// <summary>ADR transport category 0–4: the quantity multiplier of the 1.1.3.6 exemption.</summary>
    public int? AdrTransportCategory { get; private set; }

    public bool AdrIsEnvironmentallyHazardous { get; private set; }

    public bool AdrIsLimitedQuantity { get; private set; }

    public bool AdrIsWaste { get; private set; }

    /// <summary>Class 3 only.</summary>
    public double? AdrFlashpointCelsius { get; private set; }

    public string? AdrSpecialProvisions { get; private set; }

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

    /// <summary>The minimal ADR block (UN number, class, packing group, proper shipping name); the other fields are cleared.</summary>
    public void SetAdr(bool isAdr, string? unNumber, string? adrClass, string? packingGroup, string? description)
        => SetAdr(isAdr, new ProductAdr(unNumber, adrClass, packingGroup, description));

    /// <summary>The ADR block (PO option A, 2026-10-11): when the product is dangerous goods, the UN number and class are required.</summary>
    public void SetAdr(bool isAdr, ProductAdr adr)
    {
        if (!isAdr)
        {
            IsAdr = false;
            AdrUnNumber = null;
            AdrClass = null;
            AdrPackingGroup = null;
            AdrDescription = null;
            AdrTechnicalName = null;
            AdrClassificationCode = null;
            AdrHazardLabels = null;
            AdrHazardIdentificationNumber = null;
            AdrTunnelRestrictionCode = null;
            AdrTransportCategory = null;
            AdrIsEnvironmentallyHazardous = false;
            AdrIsLimitedQuantity = false;
            AdrIsWaste = false;
            AdrFlashpointCelsius = null;
            AdrSpecialProvisions = null;
            return;
        }

        var un = adr.UnNumber?.Trim();
        var cls = MasterDataCodes.Normalize(adr.Class);
        if (string.IsNullOrEmpty(un) || !MasterDataCodes.IsUnNumber(un) || cls == null || cls.Length > MasterDataConsts.MaxAdrClassLength)
        {
            throw new BusinessException(MasterDataErrorCodes.AdrFieldsRequired);
        }

        var group = MasterDataCodes.Normalize(adr.PackingGroup);
        if (group != null && group.Length > MasterDataConsts.MaxAdrPackingGroupLength)
        {
            throw new ArgumentOutOfRangeException(nameof(adr.PackingGroup));
        }

        if (adr.TransportCategory is < MasterDataConsts.MinAdrTransportCategory or > MasterDataConsts.MaxAdrTransportCategory)
        {
            throw new ArgumentOutOfRangeException(nameof(adr.TransportCategory));
        }

        IsAdr = true;
        AdrUnNumber = un;
        AdrClass = cls;
        AdrPackingGroup = group;
        AdrDescription = Text(adr.Description, MasterDataConsts.MaxAdrDescriptionLength, nameof(adr.Description));
        AdrTechnicalName = Text(adr.TechnicalName, MasterDataConsts.MaxAdrTechnicalNameLength, nameof(adr.TechnicalName));
        AdrClassificationCode = Code(adr.ClassificationCode, MasterDataConsts.MaxAdrClassificationCodeLength, nameof(adr.ClassificationCode));
        AdrHazardLabels = Code(adr.HazardLabels, MasterDataConsts.MaxAdrHazardLabelsLength, nameof(adr.HazardLabels));
        AdrHazardIdentificationNumber = Code(adr.HazardIdentificationNumber, MasterDataConsts.AdrHazardIdentificationNumberMaxLength, nameof(adr.HazardIdentificationNumber));
        AdrTunnelRestrictionCode = Code(adr.TunnelRestrictionCode, MasterDataConsts.MaxAdrTunnelRestrictionCodeLength, nameof(adr.TunnelRestrictionCode));
        AdrTransportCategory = adr.TransportCategory;
        AdrIsEnvironmentallyHazardous = adr.IsEnvironmentallyHazardous;
        AdrIsLimitedQuantity = adr.IsLimitedQuantity;
        AdrIsWaste = adr.IsWaste;
        AdrFlashpointCelsius = adr.FlashpointCelsius;
        AdrSpecialProvisions = Text(adr.SpecialProvisions, MasterDataConsts.MaxAdrSpecialProvisionsLength, nameof(adr.SpecialProvisions));
    }

    private static string? Text(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, name, maxLength);
    }

    private static string? Code(string? value, int maxLength, string name)
    {
        var code = MasterDataCodes.Normalize(value);
        return code == null ? null : Check.Length(code, name, maxLength);
    }
}
