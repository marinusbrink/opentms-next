using OpenTms.Integrations.Routing;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// The host's settings of the external services in Administration → Services (docs/designs/maintenance-view.md,
/// "Services"), modelled on the e-mail settings of design 64: global values in the host database that apply to every
/// tenant at once; an empty value falls back to the deployment's configuration. Keys and secrets are encrypted and never
/// returned.
/// </summary>
public static class ServiceSettingNames
{
    private const string Prefix = "Integrations.Services.";

    public static class PdfGeneratorApi
    {
        private const string Section = Prefix + "PdfGeneratorApi.";

        public const string BaseUrl = Section + "BaseUrl";

        /// <summary>Encrypted.</summary>
        public const string Key = Section + "Key";

        /// <summary>Encrypted.</summary>
        public const string Secret = Section + "Secret";
    }

    public static class Routing
    {
        private const string Section = Prefix + "Routing.";

        /// <summary>One of <see cref="ServiceSettingsConsts.RoutingProviders"/>.</summary>
        public const string Provider = Section + "Provider";

        public const string OsrmBaseUrl = Section + "OsrmBaseUrl";

        public const string RouteFunctionBaseUrl = Section + "RouteFunctionBaseUrl";

        /// <summary>Encrypted.</summary>
        public const string RouteFunctionApiKey = Section + "RouteFunctionApiKey";

        public const string VroomBaseUrl = Section + "VroomBaseUrl";
    }

    /// <summary>Every setting, for the overlay that reads them in one pass.</summary>
    public static readonly string[] All =
    [
        PdfGeneratorApi.BaseUrl, PdfGeneratorApi.Key, PdfGeneratorApi.Secret,
        Routing.Provider, Routing.OsrmBaseUrl, Routing.RouteFunctionBaseUrl, Routing.RouteFunctionApiKey, Routing.VroomBaseUrl
    ];

    /// <summary>The settings stored encrypted and never returned.</summary>
    public static readonly string[] Encrypted = [PdfGeneratorApi.Key, PdfGeneratorApi.Secret, Routing.RouteFunctionApiKey];
}

public static class ServiceSettingsConsts
{
    public const int MaxUrlLength = 512;

    public const int MaxSecretLength = 256;

    /// <summary>The routing providers the page offers.</summary>
    public static readonly string[] RoutingProviders = [RoutingConsts.OsrmProviderName, RoutingConsts.RouteFunctionProviderName];

    /// <summary>The services the page tests: documents (pdfgeneratorapi) and routing (provider, function, VROOM).</summary>
    public const string DocumentsService = "documents";

    public const string RoutingService = "routing";
}
