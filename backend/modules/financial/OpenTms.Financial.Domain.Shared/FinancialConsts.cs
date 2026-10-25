namespace OpenTms.Financial;

/// <summary>Bounds of the Financial master data (design 52) and of the price lists and rate lines (design 57, Security quickscan).</summary>
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

    /// <summary><c>numeric(18,4)</c>: a discount or surcharge value (money or percentage), a unit price, a quantity, a bracket bound.</summary>
    public const int ValuePrecision = 18;
    public const int ValueScale = 4;

    /// <summary><c>numeric(18,2)</c>: a line amount (design 56 "Money").</summary>
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;

    /// <summary><c>numeric(18,6)</c>: the unit conversion factor of a price list.</summary>
    public const int FactorPrecision = 18;
    public const int FactorScale = 6;

    /* Design 57: price lists. */

    /// <summary>The horizontal brackets of one price list.</summary>
    public const int MaxPriceListColumns = 50;

    /// <summary>The vertical brackets of one rule.</summary>
    public const int MaxPriceListBrackets = 30;

    /// <summary>The rules of one price list.</summary>
    public const int MaxPriceListRules = 10_000;

    /// <summary>The rules one import or rule page handles at once; the rule list pages by the grid contract.</summary>
    public const int MaxRulePageSize = 200;

    /// <summary>An import file is at most this many bytes (5 MB).</summary>
    public const int MaxImportBytes = 5 * 1024 * 1024;

    /* Design 57: rate lines. */

    /// <summary>Rate lines under one anchor tree (an order with its lines, a trip with its legs).</summary>
    public const int MaxRateLinesPerAnchor = 500;

    /// <summary>A localisation key of a calculated line ("Financial:RateLine.PriceList").</summary>
    public const int MaxDescriptionKeyLength = 64;

    /// <summary>The free text of a manual line.</summary>
    public const int MaxRateLineDescriptionLength = 256;

    /* Design 57: relation financial settings. */

    /// <summary>Price lists of one relation (per side).</summary>
    public const int MaxRelationPriceLists = 50;

    /// <summary>Default discounts and surcharges of one relation.</summary>
    public const int MaxRelationDiscountsOrSurcharges = 100;

    /// <summary>The billable share of the kilometres of a per-kilometre discount or surcharge, in percent.</summary>
    public const decimal MaxBillableKilometrePercentage = 100m;
}
