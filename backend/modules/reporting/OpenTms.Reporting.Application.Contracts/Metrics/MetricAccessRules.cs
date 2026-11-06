using System.Collections.Generic;
using OpenTms.Financial.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Reporting.Features;

namespace OpenTms.Reporting.Metrics;

/// <summary>
/// What a catalog metric needs (design 75 "The catalog"). The catalog lives in <c>Domain.Shared</c>, which cannot see permission
/// and feature constants; the rule follows from the grain and from whether the metric is money, so a new metric needs nothing here.
/// All permissions have to be granted. Every feature requirement has to hold, and one holds when one of its features is on.
/// </summary>
public static class MetricAccessRules
{
    private static readonly string[] OrderPermissions = [OrdersPermissions.TransportOrders.Default];
    private static readonly string[] OrderMoneyPermissions = [OrdersPermissions.TransportOrders.Default, FinancialPermissions.RateLines.Default];
    private static readonly string[] TripPermissions = [PlanningExecutionPermissions.TransportTrips.Default];
    private static readonly string[] TripMoneyPermissions = [PlanningExecutionPermissions.TransportTrips.Default, FinancialPermissions.RateLines.Default];

    private static readonly string[][] OrderFeatures =
    [
        [ReportingFeatures.Metrics], [AppFeatures.Transport], [AppFeatures.TransportApp.Orders]
    ];

    private static readonly string[][] TripFeatures =
    [
        [ReportingFeatures.Metrics], [AppFeatures.Transport, AppFeatures.Planboard], [AppFeatures.TransportApp.Trips, AppFeatures.Planboard]
    ];

    public static IReadOnlyList<string> PermissionsOf(MetricDefinition metric) => metric.Grain == MetricGrain.Order
        ? metric.IsMoney ? OrderMoneyPermissions : OrderPermissions
        : metric.IsMoney ? TripMoneyPermissions : TripPermissions;

    public static IReadOnlyList<IReadOnlyList<string>> FeaturesOf(MetricDefinition metric)
        => metric.Grain == MetricGrain.Order ? OrderFeatures : TripFeatures;
}
