using System;
using OpenTms.Platform.Localization;
using Volo.Abp;

namespace OpenTms.MasterData.Reference;

/// <summary>ISO 4217 currency, copied per tenant (assessment §3.7); the ISO code is the natural key.</summary>
public class Currency : MasterDataAggregateRoot
{
    public string IsoCode { get; private set; } = default!;

    /// <summary>ISO 4217 numeric code; null for a tenant-created currency.</summary>
    public int? IsoNumeric { get; private set; }

    /// <summary>Decimal places of the minor unit (0–4); null when ISO says "N.A." (precious metals, funds).</summary>
    public int? MinorUnits { get; private set; }

    public string? Symbol { get; private set; }

    public LocalizedName Name { get; private set; } = default!;

    public bool IsActive { get; private set; }

    /// <summary>The tenant's default; exactly one per tenant, enforced by a partial unique index (§3.8).</summary>
    public bool IsDefault { get; private set; }

    protected Currency()
    {
    }

    public Currency(Guid id, Guid? tenantId, string isoCode, LocalizedName name, bool isActive)
        : base(id, tenantId)
    {
        SetIsoCode(isoCode);
        SetName(name);
        IsActive = isActive;
    }

    public void SetIsoCode(string isoCode) => IsoCode = MasterDataCodes.NormalizeIsoAlpha3(isoCode, "isoCode");

    public void SetName(LocalizedName name) => Name = Check.NotNull(name, nameof(name));

    public void SetIsoNumeric(int? isoNumeric)
    {
        if (isoNumeric is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(isoNumeric));
        }

        IsoNumeric = isoNumeric;
    }

    public void SetMinorUnits(int? minorUnits)
    {
        if (minorUnits is < MasterDataConsts.MinMinorUnits or > MasterDataConsts.MaxMinorUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(minorUnits));
        }

        MinorUnits = minorUnits;
    }

    public void SetSymbol(string? symbol)
    {
        var value = symbol?.Trim();
        Symbol = string.IsNullOrEmpty(value) ? null : Check.Length(value, nameof(symbol), MasterDataConsts.MaxSymbolLength);
    }

    public void SetActive(bool isActive)
    {
        if (!isActive && IsDefault)
        {
            throw new BusinessException(MasterDataErrorCodes.DefaultMustBeActive).WithData("isoCode", IsoCode);
        }

        IsActive = isActive;
    }

    /// <summary>Only the application service's SetDefault command calls this, after clearing the previous default.</summary>
    public void MarkDefault()
    {
        if (!IsActive)
        {
            throw new BusinessException(MasterDataErrorCodes.DefaultMustBeActive).WithData("isoCode", IsoCode);
        }

        IsDefault = true;
    }

    public void ClearDefault() => IsDefault = false;
}
