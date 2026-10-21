namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The pdfgeneratorapi.com account (design 50), configuration section <c>Integrations:PdfGeneratorApi</c>. One FiLogic
/// account for all tenants (PO 2026-10-21): host configuration, not tenant data. <see cref="Key"/> and
/// <see cref="Secret"/> come from the deployment's secret store (environment variables from Secret Manager;
/// <c>dotnet user-secrets</c> locally): never in a database or an appsettings file, never returned, never logged.
/// </summary>
public class PdfGeneratorApiOptions
{
    public const string SectionName = "Integrations:PdfGeneratorApi";

    /// <summary>API v4 in the US region; a trailing slash is added when missing.</summary>
    public string BaseUrl { get; set; } = "https://us1.pdfgeneratorapi.com/api/v4/";

    /// <summary>The host's workspace.</summary>
    public string DefaultWorkspace { get; set; } = "default-workspace@opentms.nl";

    /// <summary>A tenant's workspace: <c>{0}</c> is the tenant id (a GUID, so never one of OpenTMS's numeric workspaces).</summary>
    public string TenantWorkspace { get; set; } = "workspace-tenant-{0}@opentms.nl";

    /// <summary>The API key: the JWT's issuer.</summary>
    public string? Key { get; set; }

    /// <summary>The API secret: signs the JWT.</summary>
    public string? Secret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Secret);
}
