namespace OpenTms.Financial;

/// <summary>The ABP setting names of the Financial module (design 57).</summary>
public static class FinancialSettings
{
    /// <summary>
    /// The cost price per kilometre of the own fleet (decimal, tenant scope, no default): the last step of the
    /// vehicle → vehicle type → tenant cascade (QP10) that prices the <c>OwnFleetDistance</c> line of a trip.
    /// </summary>
    public const string OwnFleetCostPerKm = "Financial:OwnFleetCostPerKm";
}
