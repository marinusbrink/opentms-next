using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// Administration → Services (host only): the stored settings of pdfgeneratorapi, routing and (design 72) the Integrations
/// tab (Anthropic, Google, Microsoft 365), what the deployment's configuration holds where a setting is empty, and a
/// connection test per service.
/// </summary>
public interface IServiceSettingsAppService : IApplicationService
{
    Task<ServiceSettingsDto> GetAsync();

    Task UpdateAsync(UpdateServiceSettingsDto input);

    /// <summary>Runs the health checks of one service (<see cref="ServiceSettingsConsts.DocumentsService"/>,
    /// <see cref="ServiceSettingsConsts.RoutingService"/> or <see cref="ServiceSettingsConsts.IntegrationsService"/>) against
    /// the effective settings.</summary>
    Task<List<HealthCheckEntryDto>> TestAsync(string service);
}

/// <summary>The stored values (null = the configuration decides). Secrets are never returned: only whether one is stored.</summary>
public record ServiceSettingsDto
{
    public string? PdfGeneratorApiBaseUrl { get; init; }

    public bool HasPdfGeneratorApiKey { get; init; }

    public bool HasPdfGeneratorApiSecret { get; init; }

    public string? RoutingProvider { get; init; }

    public string? OsrmBaseUrl { get; init; }

    public string? RouteFunctionBaseUrl { get; init; }

    public bool HasRouteFunctionApiKey { get; init; }

    public string? VroomBaseUrl { get; init; }

    /* Design 72: the Integrations tab. */

    public bool HasAiApiKey { get; init; }

    public string? AiLearningModel { get; init; }

    public string? AiExtractionModel { get; init; }

    public string? GoogleClientId { get; init; }

    public bool HasGoogleClientSecret { get; init; }

    public string? MicrosoftClientId { get; init; }

    public bool HasMicrosoftClientSecret { get; init; }

    /// <summary>Read-only: the redirect URI to register at Google and Microsoft, <c>{App:SelfUrl}/api/integrations/mailbox-oauth/callback</c>.</summary>
    public string? MailboxRedirectUri { get; init; }

    /// <summary>What applies where a field is empty: the deployment's configuration.</summary>
    public ServiceSettingsDefaultsDto Defaults { get; init; } = new();
}

/// <summary>The deployment's configuration, shown as the hint of an empty field. Secrets as "set or not" only.</summary>
public record ServiceSettingsDefaultsDto
{
    public string? PdfGeneratorApiBaseUrl { get; init; }

    public bool HasPdfGeneratorApiKey { get; init; }

    public bool HasPdfGeneratorApiSecret { get; init; }

    public string? RoutingProvider { get; init; }

    public string? OsrmBaseUrl { get; init; }

    public string? RouteFunctionBaseUrl { get; init; }

    public bool HasRouteFunctionApiKey { get; init; }

    public string? VroomBaseUrl { get; init; }

    public bool HasAiApiKey { get; init; }

    public string? AiLearningModel { get; init; }

    public string? AiExtractionModel { get; init; }

    public string? GoogleClientId { get; init; }

    public bool HasGoogleClientSecret { get; init; }

    public string? MicrosoftClientId { get; init; }

    public bool HasMicrosoftClientSecret { get; init; }
}

/// <summary>
/// One save of the page. An empty field clears the stored value (the configuration decides again); a secret left empty
/// keeps the stored one, and its Clear flag removes it.
/// </summary>
public record UpdateServiceSettingsDto
{
    [Url]
    [StringLength(ServiceSettingsConsts.MaxUrlLength)]
    public string? PdfGeneratorApiBaseUrl { get; init; }

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? PdfGeneratorApiKey { get; init; }

    public bool ClearPdfGeneratorApiKey { get; init; }

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? PdfGeneratorApiSecret { get; init; }

    public bool ClearPdfGeneratorApiSecret { get; init; }

    public string? RoutingProvider { get; init; }

    [Url]
    [StringLength(ServiceSettingsConsts.MaxUrlLength)]
    public string? OsrmBaseUrl { get; init; }

    [Url]
    [StringLength(ServiceSettingsConsts.MaxUrlLength)]
    public string? RouteFunctionBaseUrl { get; init; }

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? RouteFunctionApiKey { get; init; }

    public bool ClearRouteFunctionApiKey { get; init; }

    [Url]
    [StringLength(ServiceSettingsConsts.MaxUrlLength)]
    public string? VroomBaseUrl { get; init; }

    /* Design 72: the Integrations tab. */

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? AiApiKey { get; init; }

    public bool ClearAiApiKey { get; init; }

    [StringLength(ServiceSettingsConsts.MaxModelIdLength)]
    public string? AiLearningModel { get; init; }

    [StringLength(ServiceSettingsConsts.MaxModelIdLength)]
    public string? AiExtractionModel { get; init; }

    [StringLength(ServiceSettingsConsts.MaxClientIdLength)]
    public string? GoogleClientId { get; init; }

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? GoogleClientSecret { get; init; }

    public bool ClearGoogleClientSecret { get; init; }

    [StringLength(ServiceSettingsConsts.MaxClientIdLength)]
    public string? MicrosoftClientId { get; init; }

    [StringLength(ServiceSettingsConsts.MaxSecretLength)]
    public string? MicrosoftClientSecret { get; init; }

    public bool ClearMicrosoftClientSecret { get; init; }
}
