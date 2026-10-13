using System;
using OpenTms.Platform.Localization;
using Volo.Abp;

namespace OpenTms.MasterData.Reference;

/// <summary>ISO 3166-1 country, copied per tenant; alpha-2 and alpha-3 are both natural keys (assessment §3.7).</summary>
public class Country : MasterDataAggregateRoot
{
    public string IsoAlpha2 { get; private set; } = default!;

    public string IsoAlpha3 { get; private set; } = default!;

    public int? IsoNumeric { get; private set; }

    public LocalizedName Name { get; private set; } = default!;

    /// <summary>Real foreign key to the tenant's currency rows; null for territories without a currency (Antarctica).</summary>
    public Guid? DefaultCurrencyId { get; private set; }

    /// <summary>IANA zone id (assessment §2.2: IANA, not Windows); null where tzdata has no zone.</summary>
    public string? PrimaryTimeZoneId { get; private set; }

    /// <summary>The tenant's one default country (PO 2026-10-13): new addresses start with it; the partial unique index enforces one per tenant.</summary>
    public bool IsDefault { get; private set; }

    protected Country()
    {
    }

    public Country(Guid id, Guid? tenantId, string isoAlpha2, string isoAlpha3, LocalizedName name)
        : base(id, tenantId)
    {
        SetIsoCodes(isoAlpha2, isoAlpha3);
        SetName(name);
    }

    public void SetIsoCodes(string isoAlpha2, string isoAlpha3)
    {
        IsoAlpha2 = MasterDataCodes.NormalizeIsoAlpha2(isoAlpha2);
        IsoAlpha3 = MasterDataCodes.NormalizeIsoAlpha3(isoAlpha3, "isoAlpha3");
    }

    public void SetName(LocalizedName name) => Name = Check.NotNull(name, nameof(name));

    public void SetIsoNumeric(int? isoNumeric)
    {
        if (isoNumeric is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(isoNumeric));
        }

        IsoNumeric = isoNumeric;
    }

    public void SetDefaultCurrency(Guid? currencyId) => DefaultCurrencyId = currencyId;

    /// <summary>Only the application service's SetDefault command calls this, after clearing the previous default.</summary>
    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    /// <summary>Validates against the runtime's IANA database (ICU on Linux and macOS, the built-in mapping on Windows).</summary>
    public void SetPrimaryTimeZone(string? timeZoneId)
    {
        var value = timeZoneId?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            PrimaryTimeZoneId = null;
            return;
        }

        if (value.Length > MasterDataConsts.MaxTimeZoneIdLength || !TimeZoneInfo.TryFindSystemTimeZoneById(value, out _))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidTimeZone).WithData("value", value);
        }

        PrimaryTimeZoneId = value;
    }
}
