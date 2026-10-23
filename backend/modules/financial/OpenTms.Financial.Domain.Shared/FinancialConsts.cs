namespace OpenTms.Financial;

/// <summary>Bounds of the Financial master data (design 52, Expected volumes and Security quickscan).</summary>
public static class FinancialConsts
{
    /// <summary>Validity rows of one VAT category.</summary>
    public const int MaxVatRates = 50;

    /// <summary>Validity rows of one discount or surcharge (a monthly diesel surcharge over 15 years).</summary>
    public const int MaxDiscountOrSurchargeValues = 200;

    /// <summary>Regions of one discount or surcharge.</summary>
    public const int MaxDiscountOrSurchargeRegions = 100;

    public const decimal MaxVatPercentage = 100m;

    /// <summary>A percentage discount or surcharge is at most 1,000 %.</summary>
    public const decimal MaxDiscountOrSurchargePercentage = 1000m;

    /// <summary><c>numeric(5,2)</c>: a VAT percentage.</summary>
    public const int VatPercentagePrecision = 5;
    public const int VatPercentageScale = 2;

    /// <summary><c>numeric(18,4)</c>: a discount or surcharge value (money or percentage).</summary>
    public const int ValuePrecision = 18;
    public const int ValueScale = 4;
}
