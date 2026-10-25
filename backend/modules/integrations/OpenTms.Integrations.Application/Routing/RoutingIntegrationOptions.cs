using System;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// Configuration section <c>Integrations:Routing</c> (design 60, "Providers"): which provider answers distance and
/// matrix questions, where the OSRM instance and the Valhalla route function live, how long a cached route lives and how
/// long one outbound call may take. No host is ever in code: an unset base URL means "not configured" and every routing
/// call answers null (design 60, "Flag &amp; rollout plan"). The function's key comes from the deployment's secret store.
/// </summary>
public class RoutingIntegrationOptions
{
    public const string SectionName = "Integrations:Routing";

    public const int DefaultCacheDays = 30;

    public const int DefaultTimeoutSeconds = 10;

    /// <summary><see cref="RoutingConsts.OsrmProviderName"/> (default) or <see cref="RoutingConsts.RouteFunctionProviderName"/>.</summary>
    public string Provider { get; set; } = RoutingConsts.OsrmProviderName;

    public OsrmOptions Osrm { get; set; } = new();

    public RouteFunctionOptions RouteFunction { get; set; } = new();

    /// <summary>How long a cached route is served before the provider is asked again.</summary>
    public int CacheDays { get; set; } = DefaultCacheDays;

    /// <summary>The ceiling of one outbound call (design 60: 10 s for the function).</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Max(1, TimeoutSeconds));

    public TimeSpan CacheLifetime => TimeSpan.FromDays(Math.Max(1, CacheDays));

    public class OsrmOptions
    {
        /// <summary>The OSRM instance ("https://host"); the API paths (/route/v1/driving, /table/v1/driving) are appended.</summary>
        public string? BaseUrl { get; set; }

        public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
    }

    public class RouteFunctionOptions
    {
        /// <summary>The route function's URL: the request is POSTed to it as is.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>Optional key sent in <see cref="ApiKeyHeader"/>; from the secret store, never in a file.</summary>
        public string? ApiKey { get; set; }

        public string ApiKeyHeader { get; set; } = "X-API-Key";

        public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);

        /// <summary>The host name alone, for the status endpoint; null when not configured.</summary>
        public string? Host => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}

/// <summary>
/// Configuration section <c>Integrations:CarbonFootprint</c> (design 60, "Carbon footprint"): the accounting carbon
/// performance indicator of the great-circle fallback in grams CO₂e per tonne-kilometre. 62 is OpenTMS's default.
/// </summary>
public class CarbonFootprintOptions
{
    public const string SectionName = "Integrations:CarbonFootprint";

    public const decimal DefaultCpiGramPerTonneKm = 62m;

    public decimal CpiGramPerTonneKm { get; set; } = DefaultCpiGramPerTonneKm;
}
