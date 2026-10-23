using System;

namespace OpenTms.Financial.Tariffs;

/// <summary>What the quantity of an additional service on an order line counts (design 52).</summary>
public enum ServiceUnit
{
    Piece = 1,
    Hour = 2,
    Kilometre = 3,
    Kilogram = 4,
    Stop = 5,
    Day = 6
}

public enum DiscountOrSurchargeDirection
{
    Surcharge = 1,
    Discount = 2
}

/// <summary>How the value of a discount or surcharge is read; replaces OpenTMS's two booleans (assessment §5).</summary>
public enum DiscountOrSurchargeValueKind
{
    FixedAmount = 1,
    Percentage = 2,
    PerKilometre = 3
}

/// <summary>Applies to what the customer pays (revenue), what the carrier is paid (cost), or both.</summary>
public enum DiscountOrSurchargeSide
{
    Revenue = 1,
    Cost = 2,
    Both = 3
}

/// <summary>Where the address must lie in a region of a discount or surcharge (design 52, Assumption 4).</summary>
public enum RegionAppliesAt
{
    Loading = 1,
    Unloading = 2,
    Either = 3
}

/// <summary>The days a discount or surcharge applies on; replaces seven columns (assessment §5). Never empty.</summary>
[Flags]
public enum DaysOfWeek
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
}
