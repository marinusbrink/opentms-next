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

    /// <summary>Design 72: the language model of the order intake (Anthropic, AD-21).</summary>
    public static class Ai
    {
        private const string Section = Prefix + "Ai.";

        /// <summary>Encrypted.</summary>
        public const string ApiKey = Section + "ApiKey";

        /// <summary>The model that learns a layout and repairs profiles (default <c>OrderIntakeConsts.DefaultLearningModel</c>).</summary>
        public const string LearningModel = Section + "LearningModel";

        /// <summary>The model that extracts without a profile (default <c>OrderIntakeConsts.DefaultExtractionModel</c>).</summary>
        public const string ExtractionModel = Section + "ExtractionModel";
    }

    /// <summary>Design 72: the OAuth client of the Gmail mailbox connections.</summary>
    public static class Google
    {
        private const string Section = Prefix + "Google.";

        public const string ClientId = Section + "ClientId";

        /// <summary>Encrypted.</summary>
        public const string ClientSecret = Section + "ClientSecret";
    }

    /// <summary>Design 72: the multi-tenant Entra app registration of the Microsoft 365 mailbox connections.</summary>
    public static class Microsoft
    {
        private const string Section = Prefix + "Microsoft.";

        public const string ClientId = Section + "ClientId";

        /// <summary>Encrypted.</summary>
        public const string ClientSecret = Section + "ClientSecret";
    }

    /// <summary>Every setting, for the overlay that reads them in one pass.</summary>
    public static readonly string[] All =
    [
        PdfGeneratorApi.BaseUrl, PdfGeneratorApi.Key, PdfGeneratorApi.Secret,
        Routing.Provider, Routing.OsrmBaseUrl, Routing.RouteFunctionBaseUrl, Routing.RouteFunctionApiKey, Routing.VroomBaseUrl,
        Ai.ApiKey, Ai.LearningModel, Ai.ExtractionModel,
        Google.ClientId, Google.ClientSecret,
        Microsoft.ClientId, Microsoft.ClientSecret
    ];

    /// <summary>The settings stored encrypted and never returned.</summary>
    public static readonly string[] Encrypted =
    [
        PdfGeneratorApi.Key, PdfGeneratorApi.Secret, Routing.RouteFunctionApiKey, Ai.ApiKey, Google.ClientSecret, Microsoft.ClientSecret
    ];
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

    /// <summary>Design 72: the Integrations tab (AI, Google, Microsoft 365).</summary>
    public const string IntegrationsService = "integrations";

    public const int MaxModelIdLength = 64;

    /// <summary>An OAuth client id (Google's are about 72 characters, Entra's a GUID).</summary>
    public const int MaxClientIdLength = 256;
}
