using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Localization;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Routing;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Maintenance;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.SettingManagement;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// Administration → Services (docs/designs/maintenance-view.md, "Services"), modelled on the host e-mail settings of
/// design 64: global settings in the host database, one configuration of pdfgeneratorapi and routing for every tenant.
/// An empty value falls back to the deployment's configuration. Keys and secrets are write-only: stored encrypted, never
/// returned, kept when the form sends them empty. A save reaches this instance's adapters once its unit of work commits
/// (the overlay is read again) and the others within <see cref="ServiceSettingsOverlay.RefreshPeriod"/>. The test runs
/// the service's own health checks, so the page and the Health tab say the same.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(IntegrationsPermissions.ServiceSettings.Default)]
public class ServiceSettingsAppService : ApplicationService, IServiceSettingsAppService
{
    private readonly ISettingManager _settings;
    private readonly ServiceSettingsOverlay _overlay;
    private readonly RoutingProbeCache _probes;
    private readonly IConfiguration _configuration;

    public ServiceSettingsAppService(
        ISettingManager settings,
        ServiceSettingsOverlay overlay,
        RoutingProbeCache probes,
        IConfiguration configuration)
    {
        _settings = settings;
        _overlay = overlay;
        _probes = probes;
        _configuration = configuration;
        LocalizationResource = typeof(IntegrationsResource);
    }

    public async Task<ServiceSettingsDto> GetAsync()
    {
        EnsureHostSide();
        var pdf = _configuration.GetSection(PdfGeneratorApiOptions.SectionName).Get<PdfGeneratorApiOptions>() ?? new PdfGeneratorApiOptions();
        var routing = _configuration.GetSection(RoutingIntegrationOptions.SectionName).Get<RoutingIntegrationOptions>() ?? new RoutingIntegrationOptions();

        return new ServiceSettingsDto
        {
            PdfGeneratorApiBaseUrl = await GetAsync(ServiceSettingNames.PdfGeneratorApi.BaseUrl),
            HasPdfGeneratorApiKey = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.PdfGeneratorApi.Key)),
            HasPdfGeneratorApiSecret = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.PdfGeneratorApi.Secret)),
            RoutingProvider = await GetAsync(ServiceSettingNames.Routing.Provider),
            OsrmBaseUrl = await GetAsync(ServiceSettingNames.Routing.OsrmBaseUrl),
            RouteFunctionBaseUrl = await GetAsync(ServiceSettingNames.Routing.RouteFunctionBaseUrl),
            HasRouteFunctionApiKey = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.Routing.RouteFunctionApiKey)),
            VroomBaseUrl = await GetAsync(ServiceSettingNames.Routing.VroomBaseUrl),
            Defaults = new ServiceSettingsDefaultsDto
            {
                PdfGeneratorApiBaseUrl = pdf.BaseUrl,
                HasPdfGeneratorApiKey = !string.IsNullOrWhiteSpace(pdf.Key),
                HasPdfGeneratorApiSecret = !string.IsNullOrWhiteSpace(pdf.Secret),
                RoutingProvider = routing.Provider,
                OsrmBaseUrl = routing.Osrm.BaseUrl,
                RouteFunctionBaseUrl = routing.RouteFunction.BaseUrl,
                HasRouteFunctionApiKey = !string.IsNullOrWhiteSpace(routing.RouteFunction.ApiKey),
                VroomBaseUrl = routing.Vroom.BaseUrl
            }
        };
    }

    [Authorize(IntegrationsPermissions.ServiceSettings.Update)]
    public async Task UpdateAsync(UpdateServiceSettingsDto input)
    {
        EnsureHostSide();
        if (!string.IsNullOrWhiteSpace(input.RoutingProvider) && !ServiceSettingsConsts.RoutingProviders.Contains(input.RoutingProvider.Trim()))
        {
            throw new UserFriendlyException(L["Services:InvalidRoutingProvider"]);
        }

        foreach (var url in new[] { input.PdfGeneratorApiBaseUrl, input.OsrmBaseUrl, input.RouteFunctionBaseUrl, input.VroomBaseUrl })
        {
            if (!string.IsNullOrWhiteSpace(url)
                && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            {
                throw new UserFriendlyException(L["Services:Url.Invalid"]);
            }
        }

        await SetSecretAsync(ServiceSettingNames.PdfGeneratorApi.Key, input.PdfGeneratorApiKey, input.ClearPdfGeneratorApiKey);
        await SetSecretAsync(ServiceSettingNames.PdfGeneratorApi.Secret, input.PdfGeneratorApiSecret, input.ClearPdfGeneratorApiSecret);
        await SetSecretAsync(ServiceSettingNames.Routing.RouteFunctionApiKey, input.RouteFunctionApiKey, input.ClearRouteFunctionApiKey);
        await SetAsync(ServiceSettingNames.PdfGeneratorApi.BaseUrl, input.PdfGeneratorApiBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.Provider, input.RoutingProvider);
        await SetAsync(ServiceSettingNames.Routing.OsrmBaseUrl, input.OsrmBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.RouteFunctionBaseUrl, input.RouteFunctionBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.VroomBaseUrl, input.VroomBaseUrl);

        /* Read back only after the commit: ABP's settings cache is unit-of-work aware. A cached probe of the old address
         * would otherwise answer for a minute. */
        var refresh = async () =>
        {
            await _overlay.RefreshAsync();
            _probes.Clear();
        };
        if (UnitOfWorkManager.Current != null)
        {
            UnitOfWorkManager.Current.OnCompleted(refresh);
        }
        else
        {
            await refresh();
        }

        Logger.LogInformation("Service settings saved by user {UserId}.", CurrentUser.Id);
    }

    [Authorize(IntegrationsPermissions.ServiceSettings.Test)]
    public async Task<List<HealthCheckEntryDto>> TestAsync(string service)
    {
        EnsureHostSide();
        return service switch
        {
            ServiceSettingsConsts.DocumentsService =>
                [await RunAsync<PdfGeneratorApiHealthCheck>(PdfGeneratorApiHealthCheck.Name)],
            ServiceSettingsConsts.RoutingService => await RoutingAsync(),
            _ => throw new UserFriendlyException(L["Services:UnknownService"])
        };
    }

    private async Task<List<HealthCheckEntryDto>> RoutingAsync()
    {
        /* A test asks again: no answer from the probe cache. */
        _probes.Clear();
        return
        [
            await RunAsync<RoutingHealthCheck>(RoutingHealthCheck.Name),
            await RunAsync<RouteFunctionHealthCheck>(RouteFunctionHealthCheck.Name),
            await RunAsync<RouteOptimizerHealthCheck>(RouteOptimizerHealthCheck.Name)
        ];
    }

    private async Task<HealthCheckEntryDto> RunAsync<TCheck>(string name) where TCheck : IHealthCheck
    {
        string[] tags = [HealthCheckFacts.ExternalTag];
        var check = ActivatorUtilities.CreateInstance<TCheck>(LazyServiceProvider.LazyGetRequiredService<IServiceProvider>());
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration(name, check, HealthStatus.Unhealthy, tags) };
        var watch = Stopwatch.StartNew();
        HealthCheckResult result;
        try
        {
            result = await check.CheckHealthAsync(context);
        }
        catch (Exception e)
        {
            result = HealthCheckResult.Unhealthy(e.Message, e, HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing));
        }

        return HealthCheckEntryDto.Create(
            name,
            result.Status.ToString(),
            result.Description,
            watch.Elapsed,
            tags,
            result.Exception?.Message,
            result.Data.ToDictionary(kv => kv.Key, kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty));
    }

    private void EnsureHostSide()
    {
        if (CurrentTenant.IsAvailable)
        {
            throw new AbpAuthorizationException("The service settings are available on the host side only.");
        }
    }

    private Task<string?> GetAsync(string name) => _settings.GetOrNullGlobalAsync(name, fallback: false);

    private Task SetAsync(string name, string? value) =>
        _settings.SetGlobalAsync(name, string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    private async Task SetSecretAsync(string name, string? value, bool clear)
    {
        if (clear)
        {
            await SetAsync(name, null);
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            await SetAsync(name, value);
        }
    }
}
