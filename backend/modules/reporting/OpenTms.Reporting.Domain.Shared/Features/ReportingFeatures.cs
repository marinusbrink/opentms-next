namespace OpenTms.Reporting.Features;

/// <summary>The features of Reporting (design 75 "Flag &amp; rollout plan"). Not an app or nav node: no screen belongs to it.</summary>
public static class ReportingFeatures
{
    public const string GroupName = "Reporting";

    /// <summary>
    /// The metric tools and the fact tables behind them. Default off per tenant, set by the host. Off: <c>list_metrics</c> and
    /// <c>query_metrics</c> are absent from <c>tools/list</c>, the handlers skip and no fact is written.
    /// </summary>
    public const string Metrics = GroupName + ".Metrics";
}
