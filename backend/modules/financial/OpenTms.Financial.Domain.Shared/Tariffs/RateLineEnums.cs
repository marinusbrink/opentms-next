namespace OpenTms.Financial.Tariffs;

/*
 * The shared enums of the rate-line model (AD-08, design 57 "Shared enums"). Numeric values are stable: they are stored.
 */

/// <summary>Revenue (what the customer pays) or cost (what the tenant pays a carrier or its own fleet).</summary>
public enum RateSide
{
    Revenue = 1,
    Cost = 2
}

/// <summary>What produced a rate line (design 57 "RateLine").</summary>
public enum RateLineKind
{
    PriceList = 1,
    Manual = 2,
    FixedPrice = 3,
    DiscountOrSurcharge = 4,
    AdditionalService = 5,
    AgreedPrice = 6,
    OwnFleetDistance = 7
}

/// <summary>
/// The quantity a unit price is multiplied with, and the fact it is read from (OpenTMS's twelve "charge by" bases and
/// <c>PriceSpecification</c>, QP5). <see cref="Manual"/> means the user typed the quantity.
/// </summary>
public enum QuantitySource
{
    Manual = 0,
    LineQuantity = 1,
    Positions = 2,
    Colli = 3,
    WeightKg = 4,
    VolumeM3 = 5,
    LoadingMeters = 6,
    LoadFactor = 7,
    Epq = 8,
    DistanceKm = 9,
    TollDistanceKm = 10,
    DurationH = 11,
    VehicleType = 12,
    PerLine = 13,
    TripDistanceKm = 14
}

/// <summary>How a resolved unit price is multiplied (OpenTMS <c>PriceMultiplierOptions</c>, QP3).</summary>
public enum PriceMultiplier
{
    /// <summary>Unit price × the basis quantity of the line.</summary>
    PerUnit = 1,

    /// <summary>Unit price once per line, whatever the quantity.</summary>
    PerLine = 2,

    /// <summary>Unit price × the line's quantity (OpenTMS "Amount"), whatever the basis.</summary>
    PerLineQuantity = 3,

    /// <summary>Unit price once for the first line sharing the last stop's relation; zero for the others.</summary>
    PerLastStop = 4
}

/// <summary>Which quantity selects the column bracket of a price list (OpenTMS <c>GraduatedPriceLookupOptions</c>, QP3).</summary>
public enum ColumnQuantitySource
{
    LineBasis = 1,
    SumOfLaneLines = 2,
    SumByTransportUnit = 3,
    SumByProduct = 4,
    SumByTransportUnitAndProduct = 5,
    LineWeightKg = 6,
    LaneWeightKg = 7,
    LineVolumeM3 = 8,
    LaneVolumeM3 = 9,
    LineLoadingMeters = 10,
    LaneLoadingMeters = 11,
    LineDistanceKm = 12,
    LaneDistanceKm = 13,
    LineDurationH = 14,
    LaneDurationH = 15
}

/// <summary>Which stop(s) of a line a rule's lane must match (OpenTMS <c>RelationLookupOptions</c>).</summary>
public enum StopMatching
{
    First = 1,
    Last = 2,
    Both = 3
}

/// <summary>What happens when several price lists price one line (OpenTMS "only apply highest", QP9).</summary>
public enum MultipleMatches
{
    All = 1,
    HighestWins = 2,
    LowestWins = 3
}

/// <summary>The rounding of a price-list adjustment (design 57 "API contract", adjust).</summary>
public enum PriceRounding
{
    None = 0,
    Cent = 1,
    FiveCents = 2,
    Quarter = 3,
    Half = 4,
    Whole = 5
}
