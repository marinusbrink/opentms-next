using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// Administration → Services (host only): the stored settings of pdfgeneratorapi and routing, what the deployment's
/// configuration holds where a setting is empty, and a connection test per service.
/// </summary>
public interface IServiceSettingsAppService : IApplicationService
{
    Task<ServiceSettingsDto> GetAsync();

    Task UpdateAsync(UpdateServiceSettingsDto input);

    /// <summary>Runs the health checks of one service (<see cref="ServiceSettingsConsts.DocumentsService"/> or
    /// <see cref="ServiceSettingsConsts.RoutingService"/>) against the effective settings.</summary>
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
}
