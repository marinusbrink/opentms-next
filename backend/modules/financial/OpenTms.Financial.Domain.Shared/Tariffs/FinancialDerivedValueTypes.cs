namespace OpenTms.Financial.Tariffs;

/// <summary>
/// The derived-value target types Financial registers (docs/designs/49-derived-values-and-live-updates.md), published so
/// another module can request a recalculation without referencing Financial's Application project.
/// </summary>
public static class FinancialDerivedValueTypes
{
    public const string OrderRateLines = "Financial.OrderRateLines";

    public const string TripRateLines = "Financial.TripRateLines";
}
